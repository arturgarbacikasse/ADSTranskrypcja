using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WhisperBenchmark.Cli;
using WhisperBenchmark.Configuration;
using WhisperBenchmark.Services;

namespace WhisperBenchmark;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        CliOptions cli;
        try
        {
            cli = CliOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine("Błąd argumentów: " + ex.Message);
            CliOptions.PrintHelp();
            return 1;
        }

        var builder = Host.CreateApplicationBuilder(args);

        var basePath = AppContext.BaseDirectory;
        builder.Configuration
            .SetBasePath(basePath)
            .AddJsonFile(Path.Combine(basePath, "appsettings.json"), optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        builder.Services.Configure<BenchmarkSettings>(builder.Configuration.GetSection("Benchmark"));
        builder.Services.Configure<TranscriptionSettings>(builder.Configuration.GetSection("Transcription"));

        builder.Services.AddSingleton<InputScanner>();
        builder.Services.AddSingleton<WhisperModelDownloader>();
        builder.Services.AddSingleton<WhisperTranscriber>(sp =>
        {
            var trans = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TranscriptionSettings>>().Value;
            var dl = sp.GetRequiredService<WhisperModelDownloader>();
            var log = sp.GetRequiredService<ILogger<WhisperTranscriber>>();
            return new WhisperTranscriber(trans, dl, log);
        });
        builder.Services.AddSingleton<BenchmarkRunner>();
        builder.Services.AddSingleton<SweepBenchmarkRunner>();
        builder.Services.AddSingleton<ReportPublisher>();

        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILogger<HostMarker>>();

        var benchmarkSettings = ApplyCliOverrides(
            host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BenchmarkSettings>>().Value, cli);
        var transcriptionSettings = ApplyTranscriptionCliOverrides(
            host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<TranscriptionSettings>>().Value, cli);

        Directory.CreateDirectory(benchmarkSettings.OutputDirectory);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            try
            {
                logger.LogWarning("Otrzymano Ctrl+C – zatrzymuję benchmark i zapisuję raport częściowy.");
            }
            catch { /* logger może być już zwinięty */ }
            try { cts.Cancel(); } catch { }
        };
        AppDomain.CurrentDomain.ProcessExit += (_, __) =>
        {
            try { if (!cts.IsCancellationRequested) cts.Cancel(); } catch { }
        };

        try
        {
            return cli.Mode switch
            {
                BenchmarkMode.Single => await RunSingleAsync(host, benchmarkSettings, transcriptionSettings, cli, cts.Token),
                BenchmarkMode.Soak => await RunSoakAsync(host, benchmarkSettings, transcriptionSettings, cts.Token),
                BenchmarkMode.Sweep => await RunSweepAsync(host, benchmarkSettings, transcriptionSettings, cli, cts.Token),
                BenchmarkMode.Dataset => await RunDatasetAsync(host, benchmarkSettings, transcriptionSettings, cts.Token),
                _ => 1
            };
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Benchmark anulowany przez użytkownika.");
            return 130;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Benchmark zakończył się błędem: {Msg}", ex.Message);
            return 2;
        }
        finally
        {
            var transcriber = host.Services.GetRequiredService<WhisperTranscriber>();
            await transcriber.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<int> RunSingleAsync(
        IHost host,
        BenchmarkSettings benchmark,
        TranscriptionSettings transcription,
        CliOptions cli,
        CancellationToken token)
    {
        var logger = host.Services.GetRequiredService<ILogger<HostMarker>>();
        var runner = host.Services.GetRequiredService<BenchmarkRunner>();
        var publisher = host.Services.GetRequiredService<ReportPublisher>();

        var file = cli.File ?? benchmark.SingleSampleFile;
        if (string.IsNullOrWhiteSpace(file))
        {
            throw new ArgumentException(
                "Tryb 'single' wymaga --file <ścieżka> lub ustawienia Benchmark.SingleSampleFile w appsettings.json " +
                "(np. ./Data/Input/100/100_1.wav).");
        }

        logger.LogInformation("Tryb SINGLE – plik {File}.", file);

        var result = await runner.RunSingleAsync(file, benchmark, transcription, token).ConfigureAwait(false);

        logger.LogInformation(
            "Plik: {File}, audio={Audio:F2}s, processing={Proc:F2}s, rtf={Rtf:F2}x, segments={Seg}.",
            result.File, result.AudioSeconds, result.ProcessingSeconds, result.Rtf, result.SegmentCount);

        var execution = new BenchmarkRunner.BenchmarkExecutionResult(
            Summary: new Domain.BenchmarkSummary
            {
                StartedAt = result.StartedAt,
                FinishedAt = result.FinishedAt,
                DurationSeconds = result.ProcessingSeconds,
                Model = transcription.ModelFileName,
                Language = transcription.Language,
                UseGpu = transcription.UseGpu,
                GpuDevice = transcription.GpuDevice,
                GpuConcurrency = 1,
                InputDirectory = benchmark.InputDirectory,
                FileCountDiscovered = 1,
                ProcessedFiles = 1,
                ProcessedCalls = 1,
                ProcessedAudioSeconds = result.AudioSeconds,
                AudioHoursProcessed = result.AudioSeconds / 3600.0,
                Rtf = result.Rtf,
                FilesPerHour = 3600.0 / Math.Max(0.001, result.ProcessingSeconds),
                CallsPerHour = 3600.0 / Math.Max(0.001, result.ProcessingSeconds),
                AvgFileProcessingSeconds = result.ProcessingSeconds,
                P50FileProcessingSeconds = result.ProcessingSeconds,
                P95FileProcessingSeconds = result.ProcessingSeconds,
                P99FileProcessingSeconds = result.ProcessingSeconds,
                AvgFileRtf = result.Rtf,
                P50FileRtf = result.Rtf,
                P95FileRtf = result.Rtf,
                Errors = 0
            },
            Files: new[] { result },
            Calls: Array.Empty<Domain.CallBenchmarkResult>(),
            Errors: Array.Empty<Domain.BenchmarkError>());

        await publisher.PublishSoakAsync(benchmark, execution, gpuSamples: null,
            gpuCsvHeader: string.Empty, token).ConfigureAwait(false);

        return 0;
    }

    private static async Task<int> RunSoakAsync(
        IHost host,
        BenchmarkSettings benchmark,
        TranscriptionSettings transcription,
        CancellationToken token)
    {
        var logger = host.Services.GetRequiredService<ILogger<HostMarker>>();
        var runner = host.Services.GetRequiredService<BenchmarkRunner>();
        var publisher = host.Services.GetRequiredService<ReportPublisher>();

        logger.LogInformation(
            "Tryb SOAK – input={Input}, output={Output}, duration={Duration}min, concurrency={C}.",
            benchmark.InputDirectory, benchmark.OutputDirectory,
            benchmark.DurationMinutes, benchmark.GpuConcurrency);

        var gpuLogger = host.Services.GetRequiredService<ILogger<GpuMetricsCollector>>();
        await using var gpu = new GpuMetricsCollector(benchmark, gpuLogger);
        gpu.Start();

        var execution = await runner.RunSoakAsync(benchmark, transcription, token).ConfigureAwait(false);

        await gpu.DisposeAsync().ConfigureAwait(false);
        var samples = gpu.SnapshotSamples();

        await publisher.PublishSoakAsync(benchmark, execution, samples,
            benchmark.CollectGpuMetrics ? gpu.CsvHeader : string.Empty, token).ConfigureAwait(false);

        PrintSummary(logger, execution.Summary);
        return 0;
    }

    private static async Task<int> RunDatasetAsync(
        IHost host,
        BenchmarkSettings benchmark,
        TranscriptionSettings transcription,
        CancellationToken token)
    {
        var logger = host.Services.GetRequiredService<ILogger<HostMarker>>();
        var runner = host.Services.GetRequiredService<BenchmarkRunner>();
        var publisher = host.Services.GetRequiredService<ReportPublisher>();

        logger.LogInformation(
            "Tryb DATASET – input={Input}, output={Output}, concurrency={C}, shuffle={Shuffle}.",
            benchmark.InputDirectory, benchmark.OutputDirectory,
            benchmark.GpuConcurrency, benchmark.ShuffleInput);

        GpuStartupCheck.WarnIfGpuLooksBusy(logger, transcription.UseGpu);

        IReadOnlyList<string> samples = Array.Empty<string>();
        var gpuHeader = string.Empty;

        if (benchmark.CollectGpuMetrics)
        {
            var gpuLogger = host.Services.GetRequiredService<ILogger<GpuMetricsCollector>>();
            await using var gpu = new GpuMetricsCollector(benchmark, gpuLogger);
            gpu.Start();

            var execution = await runner.RunDatasetAsync(benchmark, transcription, token).ConfigureAwait(false);

            await gpu.DisposeAsync().ConfigureAwait(false);
            samples = gpu.SnapshotSamples();
            gpuHeader = gpu.CsvHeader;

            await publisher.PublishDatasetAsync(benchmark, execution, samples, gpuHeader, token)
                .ConfigureAwait(false);

            PrintDatasetSummary(execution.Summary, benchmark.OutputDirectory);
            return 0;
        }

        var result = await runner.RunDatasetAsync(benchmark, transcription, token).ConfigureAwait(false);
        await publisher.PublishDatasetAsync(benchmark, result, samples, gpuHeader, token).ConfigureAwait(false);
        PrintDatasetSummary(result.Summary, benchmark.OutputDirectory);
        return 0;
    }

    private static async Task<int> RunSweepAsync(
        IHost host,
        BenchmarkSettings benchmark,
        TranscriptionSettings transcription,
        CliOptions cli,
        CancellationToken token)
    {
        var logger = host.Services.GetRequiredService<ILogger<HostMarker>>();
        var sweepRunner = host.Services.GetRequiredService<SweepBenchmarkRunner>();
        var publisher = host.Services.GetRequiredService<ReportPublisher>();

        logger.LogInformation(
            "Tryb SWEEP – concurrencies=[{C}], duration={Duration}min.",
            string.Join(',', cli.ConcurrencySweep!), benchmark.DurationMinutes);

        var steps = await sweepRunner.RunAsync(cli.ConcurrencySweep!, benchmark, transcription, token)
            .ConfigureAwait(false);

        await publisher.PublishSweepAsync(benchmark, steps, token).ConfigureAwait(false);

        foreach (var step in steps)
        {
            logger.LogInformation(
                "Sweep krok c={C}: files={Files}, rtf={Rtf:F2}x, audioHours/h={Audio:F2}, p95Proc={P95:F2}s.",
                step.Concurrency,
                step.Execution.Summary.ProcessedFiles,
                step.Execution.Summary.Rtf,
                step.Execution.Summary.AudioHoursProcessed,
                step.Execution.Summary.P95FileProcessingSeconds);
        }
        return 0;
    }

    private static void PrintSummary(ILogger logger, Domain.BenchmarkSummary s)
    {
        logger.LogInformation(
            "SUMMARY: files={Files}, calls={Calls}, audioH={AudioH:F2}, rtf={Rtf:F2}x, " +
            "filesPerHour={FH:F1}, callsPerHour={CH:F1}, avgProc={Avg:F2}s, p95Proc={P95:F2}s, errors={Errs}.",
            s.ProcessedFiles, s.ProcessedCalls, s.AudioHoursProcessed, s.Rtf,
            s.FilesPerHour, s.CallsPerHour, s.AvgFileProcessingSeconds, s.P95FileProcessingSeconds, s.Errors);
    }

    private static void PrintDatasetSummary(Domain.BenchmarkSummary s, string outputDirectory)
    {
        var cap = s.CapacityPrediction;
        Console.WriteLine();
        Console.WriteLine("DATASET BENCHMARK FINISHED");
        Console.WriteLine($"Input: {s.InputDirectory}");
        Console.WriteLine($"Model: {s.Model}");
        Console.WriteLine($"GPU concurrency: {s.GpuConcurrency}");
        Console.WriteLine();
        Console.WriteLine($"Interactions: {s.ProcessedInteractions} processed, {s.FailedInteractions} failed");
        Console.WriteLine($"Files: {s.ProcessedFiles} processed, {s.FailedFiles} failed");
        Console.WriteLine($"Audio: {s.DatasetAudioHours:F2} h");
        Console.WriteLine($"Wall-clock: {s.WallClockMinutes:F2} min");
        Console.WriteLine($"RTF: {s.Rtf:F2}x");
        Console.WriteLine($"Capacity: {s.AudioHoursPerHour:F2} h audio / h");
        if (cap is not null)
        {
            Console.WriteLine($"Estimated 8h audio processing time: {cap.ProcessingTimeFor8AudioHoursMinutes:F2} min");
            Console.WriteLine($"Estimated 24h audio processing time: {cap.ProcessingTimeFor24AudioHoursMinutes:F2} min");
            Console.WriteLine($"Estimated 100h audio processing time: {cap.ProcessingTimeFor100AudioHoursMinutes:F2} min");
        }
        Console.WriteLine();
        Console.WriteLine("Output:");
        Console.WriteLine($"  {Path.Combine(outputDirectory, "benchmark-summary.json")}");
        Console.WriteLine($"  {Path.Combine(outputDirectory, "benchmark-files.csv")}");
        Console.WriteLine($"  {Path.Combine(outputDirectory, "benchmark-calls.csv")}");
        Console.WriteLine($"  {Path.Combine(outputDirectory, "errors.json")}");
        Console.WriteLine();
    }

    private static BenchmarkSettings ApplyCliOverrides(BenchmarkSettings src, CliOptions cli)
    {
        if (!string.IsNullOrWhiteSpace(cli.Input)) src.InputDirectory = cli.Input!;
        if (!string.IsNullOrWhiteSpace(cli.Output)) src.OutputDirectory = cli.Output!;
        if (cli.DurationMinutes is int d && d > 0 && cli.Mode != BenchmarkMode.Dataset) src.DurationMinutes = d;
        if (cli.GpuConcurrency is int c && c > 0) src.GpuConcurrency = c;
        if (cli.MaxFiles is int m && m > 0) src.MaxFiles = m;
        if (cli.WriteTranscriptionJson is bool w) src.WriteTranscriptionJson = w;
        if (cli.WritePerFileJson is bool pf) src.WritePerFileJson = pf;
        if (cli.WriteMergedCallJson is bool mc) src.WriteMergedCallJson = mc;
        if (cli.CollectGpuMetrics is bool g) src.CollectGpuMetrics = g;
        if (cli.Shuffle is bool sh) src.ShuffleInput = sh;
        if (cli.MetricsIntervalSeconds is int mis && mis > 0) src.MetricsIntervalSeconds = mis;
        if (cli.GpuMetricsIntervalSeconds is int gmis && gmis > 0) src.GpuMetricsIntervalSeconds = gmis;

        if (cli.Mode == BenchmarkMode.Dataset)
        {
            src.WarmupFiles = 0;
            src.RepeatInputUntilDurationEnds = false;
        }
        else if (cli.WarmupFiles is int wf && wf >= 0)
        {
            src.WarmupFiles = wf;
        }

        return src;
    }

    private static TranscriptionSettings ApplyTranscriptionCliOverrides(TranscriptionSettings src, CliOptions cli)
    {
        if (!string.IsNullOrWhiteSpace(cli.Model))
        {
            src.ModelFileName = NormalizeModelFileName(cli.Model!);
        }

        if (!string.IsNullOrWhiteSpace(cli.Language))
        {
            src.Language = cli.Language.Trim();
        }

        if (cli.Threads is int t)
        {
            if (t <= 0)
            {
                throw new ArgumentException("Parametr --threads musi być większy od zera.");
            }

            src.Threads = t;
        }

        if (cli.UseGpu is bool ug) src.UseGpu = ug;
        if (cli.GpuDevice is int gd && gd >= 0) src.GpuDevice = gd;
        if (!string.IsNullOrWhiteSpace(cli.ModelsDirectory)) src.ModelsDirectory = cli.ModelsDirectory!;
        if (cli.AutoDownloadModel is bool adm) src.AutoDownloadModel = adm;

        if (!string.IsNullOrWhiteSpace(cli.SamplingStrategy))
        {
            var strategy = cli.SamplingStrategy.Trim();
            if (!strategy.Equals("Greedy", StringComparison.OrdinalIgnoreCase)
                && !strategy.Equals("BeamSearch", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Parametr --sampling-strategy musi być Greedy lub BeamSearch.");
            }

            src.Whisper.SamplingStrategy = strategy.Equals("BeamSearch", StringComparison.OrdinalIgnoreCase)
                ? "BeamSearch"
                : "Greedy";
        }

        if (cli.BeamSize is int bs)
        {
            if (bs <= 0)
            {
                throw new ArgumentException("Parametr --beam-size musi być większy od zera.");
            }

            src.Whisper.BeamSize = bs;
        }

        if (cli.InitialPrompt is not null) src.Whisper.InitialPrompt = cli.InitialPrompt;
        if (cli.Temperature is float temp && temp >= 0) src.Whisper.Temperature = temp;
        if (cli.Translate is bool tr) src.Whisper.Translate = tr;

        return src;
    }

    private static string NormalizeModelFileName(string raw)
    {
        var name = raw.Trim();
        if (name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        if (!name.StartsWith("ggml-", StringComparison.OrdinalIgnoreCase))
        {
            name = "ggml-" + name;
        }

        return name + ".bin";
    }

    private sealed class HostMarker { }
}
