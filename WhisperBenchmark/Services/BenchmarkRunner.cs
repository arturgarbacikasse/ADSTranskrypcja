using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using WhisperBenchmark.Configuration;
using WhisperBenchmark.Domain;

namespace WhisperBenchmark.Services;

/// <summary>
/// Główny runner trybów <c>single</c> i <c>soak</c>.
/// Strategia:
/// 1. Skanujemy katalog i walidujemy pliki.
/// 2. Opcjonalnie wykonujemy fazę warmup (kilka plików, wyniki nie liczą się do summary).
/// 3. Uruchamiamy worker pool o wielkości <see cref="BenchmarkSettings.GpuConcurrency"/>.
/// 4. Producer wrzuca joby do kanału (z opcjonalnym zapętlaniem datasetu), workerzy konsumują
///    i wywołują <see cref="WhisperTranscriber"/>. Limit współbieżności wymuszony jest przez liczbę
///    workerów + bounded channel, więc nigdy nie tworzymy nieograniczonej liczby tasków.
/// 5. Kanał zamyka się po upływie DurationMinutes albo po wyczerpaniu datasetu (bez zapętlania).
/// </summary>
public sealed class BenchmarkRunner
{
    private readonly ILogger<BenchmarkRunner> _logger;
    private readonly InputScanner _inputScanner;
    private readonly WhisperTranscriber _transcriber;

    public BenchmarkRunner(
        ILogger<BenchmarkRunner> logger,
        InputScanner inputScanner,
        WhisperTranscriber transcriber)
    {
        _logger = logger;
        _inputScanner = inputScanner;
        _transcriber = transcriber;
    }

    public async Task<BenchmarkExecutionResult> RunSoakAsync(
        BenchmarkSettings benchmark,
        TranscriptionSettings transcription,
        CancellationToken cancellationToken)
    {
        var aggregator = new MetricsAggregator();

        var (validFiles, scanErrors) = _inputScanner.Scan(benchmark);
        foreach (var err in scanErrors)
        {
            aggregator.Add(err);
        }

        if (validFiles.Count == 0)
        {
            _logger.LogError("Brak poprawnych plików WAV w {Dir}. Benchmark zakończony bez pomiaru.",
                benchmark.InputDirectory);

            aggregator.MarkStarted();
            aggregator.MarkFinished();
            return BuildResult(aggregator, benchmark, transcription, discovered: 0);
        }

        await _transcriber.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var queue = PrepareInitialQueue(validFiles, benchmark);

        if (benchmark.WarmupFiles > 0)
        {
            _logger.LogInformation("Faza warmup: planuję {Count} plików.", Math.Min(benchmark.WarmupFiles, queue.Count));
            await RunWarmupAsync(queue.Take(benchmark.WarmupFiles).ToArray(), aggregator, cancellationToken)
                .ConfigureAwait(false);
        }

        aggregator.MarkStarted();
        _logger.LogInformation(
            "Start fazy pomiarowej: durationMinutes={Duration}, gpuConcurrency={Concurrency}, dataset={Files}, repeat={Repeat}.",
            benchmark.DurationMinutes, benchmark.GpuConcurrency, queue.Count, benchmark.RepeatInputUntilDurationEnds);

        await RunMeasuredAsync(queue, aggregator, benchmark, cancellationToken).ConfigureAwait(false);

        aggregator.MarkFinished();
        return BuildResult(aggregator, benchmark, transcription, discovered: validFiles.Count);
    }

    /// <summary>
    /// Tryb single: jeden konkretny plik, raport prosty.
    /// </summary>
    public async Task<FileBenchmarkResult> RunSingleAsync(
        string filePath,
        BenchmarkSettings benchmark,
        TranscriptionSettings transcription,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Plik wejściowy nie istnieje: {filePath}", filePath);
        }

        var fileName = Path.GetFileName(filePath);
        var (callId, participantId) = ParseCallParticipant(benchmark, fileName);
        var info = AudioMetadataReader.Read(filePath, callId, participantId);
        if (!info.IsValid)
        {
            throw new InvalidOperationException(
                $"Plik nie spełnia wymagań (mono/16 kHz/PCM16): {info.ValidationError}");
        }

        await _transcriber.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var job = new AudioJob { File = info, Sequence = 0, IsWarmup = false };
        return await TranscribeAsync(job, captureSegments: true, cancellationToken).ConfigureAwait(false);
    }

    private (string callId, string participantId) ParseCallParticipant(BenchmarkSettings benchmark, string fileName)
    {
        var regex = new System.Text.RegularExpressions.Regex(benchmark.FileNameRegex,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var match = regex.Match(fileName);
        if (!match.Success)
        {
            _logger.LogWarning("Nazwa pliku {File} nie pasuje do wzorca {Pattern} – zapisuję jako single/0.",
                fileName, benchmark.FileNameRegex);
            return ("single", "0");
        }
        return (match.Groups["callId"].Value, match.Groups["participantId"].Value);
    }

    private static List<AudioFileInfo> PrepareInitialQueue(
        IReadOnlyList<AudioFileInfo> validFiles,
        BenchmarkSettings benchmark)
    {
        IEnumerable<AudioFileInfo> seq = validFiles;
        if (benchmark.ShuffleInput)
        {
            var rng = Random.Shared;
            seq = validFiles.OrderBy(_ => rng.Next());
        }
        var list = seq.ToList();
        if (benchmark.MaxFiles is int max && max > 0 && list.Count > max)
        {
            list = list.Take(max).ToList();
        }
        return list;
    }

    private async Task RunWarmupAsync(
        IReadOnlyList<AudioFileInfo> warmupFiles,
        MetricsAggregator aggregator,
        CancellationToken cancellationToken)
    {
        long seq = 0;
        foreach (var file in warmupFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var job = new AudioJob
            {
                File = file,
                Sequence = seq++,
                IsWarmup = true
            };

            try
            {
                var result = await TranscribeAsync(job, captureSegments: false, cancellationToken).ConfigureAwait(false);
                aggregator.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Błąd w warmupie pliku {File}: {Msg}", file.FileName, ex.Message);
                aggregator.Add(new BenchmarkError
                {
                    File = file.FileName,
                    CallId = file.CallId,
                    ParticipantId = file.ParticipantId,
                    Stage = "Warmup",
                    Message = ex.Message,
                    Exception = ex.ToString()
                });
            }
        }
        _logger.LogInformation("Warmup zakończony.");
    }

    private async Task RunMeasuredAsync(
        IReadOnlyList<AudioFileInfo> queue,
        MetricsAggregator aggregator,
        BenchmarkSettings benchmark,
        CancellationToken externalToken)
    {
        var duration = TimeSpan.FromMinutes(Math.Max(0, benchmark.DurationMinutes));
        using var durationCts = new CancellationTokenSource(duration);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(externalToken, durationCts.Token);
        var stopToken = linked.Token;

        // Producer-consumer z bounded channel zapewnia, że nigdy nie mamy nieograniczonej liczby
        // tasków w pamięci. Pojemność = 4 * concurrency, żeby workerzy się nie głodzili.
        var capacity = Math.Max(4, benchmark.GpuConcurrency * 4);
        var channel = Channel.CreateBounded<AudioJob>(new BoundedChannelOptions(capacity)
        {
            SingleReader = false,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var producer = Task.Run(async () => await ProduceJobsAsync(channel, queue, benchmark, aggregator, stopToken)
            .ConfigureAwait(false));

        var consumers = new Task[Math.Max(1, benchmark.GpuConcurrency)];
        for (int i = 0; i < consumers.Length; i++)
        {
            consumers[i] = Task.Run(() => ConsumeAsync(channel, aggregator, benchmark, stopToken));
        }

        var metricsLogger = Task.Run(() => LogMetricsLoopAsync(aggregator, benchmark, stopToken));

        try
        {
            await producer.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected on duration timeout / Ctrl+C
        }
        finally
        {
            channel.Writer.TryComplete();
        }

        try
        {
            await Task.WhenAll(consumers).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        try
        {
            await metricsLogger.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    private async Task ProduceJobsAsync(
        Channel<AudioJob> channel,
        IReadOnlyList<AudioFileInfo> queue,
        BenchmarkSettings benchmark,
        MetricsAggregator aggregator,
        CancellationToken stopToken)
    {
        long sequence = 0;
        int index = 0;
        int loops = 0;

        while (!stopToken.IsCancellationRequested)
        {
            if (queue.Count == 0) break;

            var file = queue[index % queue.Count];
            index++;

            var job = new AudioJob
            {
                File = file,
                Sequence = sequence++,
                IsWarmup = false
            };

            try
            {
                await channel.Writer.WriteAsync(job, stopToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            aggregator.SetQueueDepth(channel.Reader.Count);

            if (index >= queue.Count)
            {
                loops++;
                if (!benchmark.RepeatInputUntilDurationEnds)
                {
                    _logger.LogInformation("Dataset wyczerpany po {Loops} pętli – producer kończy.", loops);
                    break;
                }
                index = 0;
            }
        }

        channel.Writer.TryComplete();
    }

    private async Task ConsumeAsync(
        Channel<AudioJob> channel,
        MetricsAggregator aggregator,
        BenchmarkSettings benchmark,
        CancellationToken stopToken)
    {
        try
        {
            await foreach (var job in channel.Reader.ReadAllAsync(stopToken).ConfigureAwait(false))
            {
                aggregator.SetQueueDepth(channel.Reader.Count);
                aggregator.IncrementActive();
                try
                {
                    var result = await TranscribeAsync(
                            job,
                            captureSegments: benchmark.WriteTranscriptionJson,
                            stopToken)
                        .ConfigureAwait(false);

                    aggregator.Add(result);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Błąd transkrypcji pliku {File}: {Msg}", job.File.FileName, ex.Message);
                    aggregator.Add(new BenchmarkError
                    {
                        File = job.File.FileName,
                        CallId = job.File.CallId,
                        ParticipantId = job.File.ParticipantId,
                        Stage = "Transcription",
                        Message = ex.Message,
                        Exception = ex.ToString()
                    });

                    aggregator.Add(new FileBenchmarkResult
                    {
                        File = job.File.FileName,
                        FullPath = job.File.FullPath,
                        CallId = job.File.CallId,
                        ParticipantId = job.File.ParticipantId,
                        AudioSeconds = job.File.DurationSeconds,
                        ProcessingSeconds = 0,
                        QueueWaitSeconds = (DateTime.UtcNow - job.EnqueuedAt).TotalSeconds,
                        Rtf = 0,
                        StartedAt = job.EnqueuedAt,
                        FinishedAt = DateTime.UtcNow,
                        SegmentCount = 0,
                        Success = false,
                        ErrorMessage = ex.Message,
                        IsWarmup = job.IsWarmup
                    });
                }
                finally
                {
                    aggregator.DecrementActive();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }

    private async Task<FileBenchmarkResult> TranscribeAsync(
        AudioJob job,
        bool captureSegments,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var queueWait = (startedAt - job.EnqueuedAt).TotalSeconds;
        var sw = Stopwatch.StartNew();
        var result = await _transcriber.TranscribeAsync(job.File.FullPath, cancellationToken).ConfigureAwait(false);
        sw.Stop();

        var finishedAt = DateTime.UtcNow;
        var processingSeconds = sw.Elapsed.TotalSeconds;
        var rtf = processingSeconds > 0 ? job.File.DurationSeconds / processingSeconds : 0;

        return new FileBenchmarkResult
        {
            File = job.File.FileName,
            FullPath = job.File.FullPath,
            CallId = job.File.CallId,
            ParticipantId = job.File.ParticipantId,
            AudioSeconds = job.File.DurationSeconds,
            ProcessingSeconds = processingSeconds,
            QueueWaitSeconds = queueWait,
            Rtf = rtf,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            SegmentCount = result.Segments.Count,
            Success = true,
            ErrorMessage = null,
            IsWarmup = job.IsWarmup,
            Segments = captureSegments ? result.Segments : null
        };
    }

    private async Task LogMetricsLoopAsync(
        MetricsAggregator aggregator,
        BenchmarkSettings benchmark,
        CancellationToken stopToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, benchmark.MetricsIntervalSeconds));
        while (!stopToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stopToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            LogSnapshot(aggregator);
        }
        LogSnapshot(aggregator);
    }

    private void LogSnapshot(MetricsAggregator aggregator)
    {
        var snap = aggregator.Snapshot(DateTime.UtcNow);
        _logger.LogInformation(
            "[{Elapsed:hh\\:mm\\:ss}] files={Files} calls={Calls} audio={Audio:F2}h rtf={Rtf:F1}x active={Active} queue={Queue} errors={Errors}",
            snap.Elapsed,
            snap.ProcessedFiles,
            snap.ProcessedCalls,
            snap.AudioHours,
            snap.Rtf,
            snap.Active,
            snap.Queue,
            snap.Errors);
    }

    private BenchmarkExecutionResult BuildResult(
        MetricsAggregator aggregator,
        BenchmarkSettings benchmark,
        TranscriptionSettings transcription,
        int discovered)
    {
        var summary = aggregator.BuildSummary(
            inputDirectory: benchmark.InputDirectory,
            discovered: discovered,
            model: transcription.ModelFileName,
            language: transcription.Language,
            useGpu: transcription.UseGpu,
            gpuDevice: transcription.GpuDevice,
            gpuConcurrency: benchmark.GpuConcurrency);

        return new BenchmarkExecutionResult(
            Summary: summary,
            Files: aggregator.SnapshotResults(),
            Calls: aggregator.AggregateCalls(),
            Errors: aggregator.SnapshotErrors());
    }

    public sealed record BenchmarkExecutionResult(
        BenchmarkSummary Summary,
        IReadOnlyList<FileBenchmarkResult> Files,
        IReadOnlyList<CallBenchmarkResult> Calls,
        IReadOnlyList<BenchmarkError> Errors);
}
