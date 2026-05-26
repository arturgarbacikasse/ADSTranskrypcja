using Microsoft.Extensions.Logging;
using WhisperBenchmark.Configuration;
using WhisperBenchmark.Domain;

namespace WhisperBenchmark.Services;

/// <summary>
/// Wypisuje wszystkie pliki wynikowe (JSON/CSV/transkrypcje) do OutputDirectory.
/// Wyodrębnione od runnerów żeby trzymać reguły I/O i nazw plików w jednym miejscu.
/// </summary>
public sealed class ReportPublisher
{
    private readonly ILogger<ReportPublisher> _logger;

    public ReportPublisher(ILogger<ReportPublisher> logger)
    {
        _logger = logger;
    }

    public async Task PublishSoakAsync(
        BenchmarkSettings benchmark,
        BenchmarkRunner.BenchmarkExecutionResult result,
        IReadOnlyList<string>? gpuSamples,
        string gpuCsvHeader,
        CancellationToken cancellationToken)
    {
        var outDir = benchmark.OutputDirectory;
        Directory.CreateDirectory(outDir);

        var summaryJson = Path.Combine(outDir, "benchmark-summary.json");
        var summaryCsv = Path.Combine(outDir, "benchmark-summary.csv");
        var filesCsv = Path.Combine(outDir, "benchmark-files.csv");
        var callsCsv = Path.Combine(outDir, "benchmark-calls.csv");
        var errorsJson = Path.Combine(outDir, "errors.json");
        var gpuCsv = Path.Combine(outDir, "gpu-metrics.csv");

        await JsonReportWriter.WriteSummaryAsync(summaryJson, result.Summary, cancellationToken).ConfigureAwait(false);
        await CsvReportWriter.WriteSummaryAsync(summaryCsv, result.Summary, cancellationToken).ConfigureAwait(false);
        await CsvReportWriter.WriteFilesAsync(filesCsv, result.Files, cancellationToken).ConfigureAwait(false);
        await CsvReportWriter.WriteCallsAsync(callsCsv, result.Calls, cancellationToken).ConfigureAwait(false);
        await JsonReportWriter.WriteErrorsAsync(errorsJson, result.Errors, cancellationToken).ConfigureAwait(false);

        if (gpuSamples is { Count: > 0 })
        {
            await CsvReportWriter.WriteGpuSamplesAsync(gpuCsv, gpuCsvHeader, gpuSamples, cancellationToken)
                .ConfigureAwait(false);
        }

        if (benchmark.WritePerFileJson)
        {
            var perFileDir = Path.Combine(outDir, "files");
            Directory.CreateDirectory(perFileDir);
            foreach (var file in result.Files)
            {
                if (file.IsWarmup) continue;
                cancellationToken.ThrowIfCancellationRequested();
                var stem = Path.GetFileNameWithoutExtension(file.File);
                var jsonPath = Path.Combine(perFileDir, $"{stem}.benchmark.json");
                await JsonReportWriter.WritePerFileAsync(jsonPath, file, cancellationToken).ConfigureAwait(false);
            }
        }

        await WriteTranscriptionOutputsAsync(benchmark, result, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Raport zapisany w {Dir}.", outDir);
    }

    public async Task PublishSweepAsync(
        BenchmarkSettings baseBenchmark,
        IReadOnlyList<SweepBenchmarkRunner.SweepStepResult> steps,
        CancellationToken cancellationToken)
    {
        var rootOut = baseBenchmark.OutputDirectory;
        Directory.CreateDirectory(rootOut);

        foreach (var step in steps)
        {
            var stepDir = Path.Combine(rootOut, $"sweep-c{step.Concurrency}");
            Directory.CreateDirectory(stepDir);

            var stepSettings = CloneSettings(step.Settings);
            stepSettings.OutputDirectory = stepDir;

            await PublishSoakAsync(stepSettings, step.Execution, gpuSamples: null,
                    gpuCsvHeader: string.Empty, cancellationToken)
                .ConfigureAwait(false);
        }

        var sweepCsv = Path.Combine(rootOut, "benchmark-sweep.csv");
        await CsvReportWriter.WriteSweepAsync(
                sweepCsv,
                steps.Select(s => (s.Concurrency, s.Execution.Summary)),
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Sweep zapisany w {Dir}.", rootOut);
    }

    private async Task WriteTranscriptionOutputsAsync(
        BenchmarkSettings benchmark,
        BenchmarkRunner.BenchmarkExecutionResult result,
        CancellationToken cancellationToken)
    {
        var filesWithSegments = result.Files
            .Where(f => !f.IsWarmup && f.Segments is { Count: > 0 })
            .ToArray();

        if (!benchmark.WriteTranscriptionJson && filesWithSegments.Length == 0)
        {
            return;
        }

        var transcriptionDir = Path.Combine(benchmark.OutputDirectory, "transcriptions");
        Directory.CreateDirectory(transcriptionDir);

        var model = result.Summary.Model;
        var language = result.Summary.Language;

        foreach (var file in filesWithSegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stem = Path.GetFileNameWithoutExtension(file.File);
            var jsonPath = Path.Combine(transcriptionDir, $"{stem}.json");

            var dto = new JsonReportWriter.TranscriptionFile
            {
                CallId = file.CallId,
                ParticipantId = file.ParticipantId,
                File = file.File,
                Model = model,
                Language = language,
                AudioSeconds = file.AudioSeconds,
                ProcessingSeconds = file.ProcessingSeconds,
                Rtf = file.Rtf,
                Segments = JsonReportWriter.ToSegmentDtos(file.Segments!)
            };

            await JsonReportWriter.WriteTranscriptionAsync(jsonPath, dto, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Transkrypcja zapisana: {Path}.", jsonPath);
        }

        if (!benchmark.WriteMergedCallJson)
        {
            return;
        }

        foreach (var callGroup in filesWithSegments.GroupBy(f => f.CallId, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mergedPath = Path.Combine(transcriptionDir, $"{callGroup.Key}.json");

            var merged = new JsonReportWriter.MergedCallTranscriptionFile
            {
                CallId = callGroup.Key,
                Model = model,
                Language = language,
                Participants = callGroup
                    .OrderBy(f => f.ParticipantId, StringComparer.Ordinal)
                    .Select(f => new JsonReportWriter.TranscriptionParticipantDto
                    {
                        ParticipantId = f.ParticipantId,
                        File = f.File,
                        AudioSeconds = f.AudioSeconds,
                        Segments = JsonReportWriter.ToSegmentDtos(f.Segments!)
                    })
                    .ToArray()
            };

            await JsonReportWriter.WriteMergedCallAsync(mergedPath, merged, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Scalona transkrypcja callId zapisana: {Path}.", mergedPath);
        }
    }

    private static BenchmarkSettings CloneSettings(BenchmarkSettings src) => new()
    {
        InputDirectory = src.InputDirectory,
        OutputDirectory = src.OutputDirectory,
        Pattern = src.Pattern,
        FileNameRegex = src.FileNameRegex,
        DurationMinutes = src.DurationMinutes,
        WarmupFiles = src.WarmupFiles,
        GpuConcurrency = src.GpuConcurrency,
        MaxFiles = src.MaxFiles,
        ShuffleInput = src.ShuffleInput,
        RepeatInputUntilDurationEnds = src.RepeatInputUntilDurationEnds,
        WriteTranscriptionJson = src.WriteTranscriptionJson,
        WritePerFileJson = src.WritePerFileJson,
        WriteMergedCallJson = src.WriteMergedCallJson,
        MetricsIntervalSeconds = src.MetricsIntervalSeconds,
        CollectGpuMetrics = src.CollectGpuMetrics,
        GpuMetricsIntervalSeconds = src.GpuMetricsIntervalSeconds
    };
}
