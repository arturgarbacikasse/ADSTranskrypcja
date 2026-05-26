using WhisperBenchmark.Domain;

namespace WhisperBenchmark.Services;

/// <summary>
/// Trzyma stan agregowanych metryk benchmarku i potrafi:
/// - thread-safe dodać wynik pliku,
/// - zwrócić snapshot do logowania,
/// - zbudować końcowe podsumowanie i agregaty per callId.
/// Warmupowe joby NIE są wliczane do snapshotów ani do summary.
/// </summary>
public sealed class MetricsAggregator
{
    private readonly object _gate = new();
    private readonly List<FileBenchmarkResult> _results = new();
    private readonly List<BenchmarkError> _errors = new();

    private int _activeWorkers;
    private int _queueDepth;

    public DateTime BenchmarkStartedAt { get; private set; }
    public DateTime BenchmarkFinishedAt { get; private set; }

    public void MarkStarted() => BenchmarkStartedAt = DateTime.UtcNow;
    public void MarkFinished() => BenchmarkFinishedAt = DateTime.UtcNow;

    public void SetQueueDepth(int queueDepth) => Interlocked.Exchange(ref _queueDepth, queueDepth);

    public void IncrementActive() => Interlocked.Increment(ref _activeWorkers);
    public void DecrementActive() => Interlocked.Decrement(ref _activeWorkers);

    public void Add(FileBenchmarkResult result)
    {
        lock (_gate) _results.Add(result);
    }

    public void Add(BenchmarkError error)
    {
        lock (_gate) _errors.Add(error);
    }

    public IReadOnlyList<FileBenchmarkResult> SnapshotResults()
    {
        lock (_gate) return _results.ToArray();
    }

    public IReadOnlyList<BenchmarkError> SnapshotErrors()
    {
        lock (_gate) return _errors.ToArray();
    }

    public LiveSnapshot Snapshot(DateTime now)
    {
        FileBenchmarkResult[] snapshot;
        BenchmarkError[] errors;
        lock (_gate)
        {
            snapshot = _results.Where(r => !r.IsWarmup && r.Success).ToArray();
            errors = _errors.ToArray();
        }

        var elapsed = now - BenchmarkStartedAt;
        var elapsedSeconds = Math.Max(0.001, elapsed.TotalSeconds);

        var audioSeconds = snapshot.Sum(r => r.AudioSeconds);
        var rtf = audioSeconds / elapsedSeconds;
        var processedFiles = snapshot.Length;
        var callsCompleted = CountCompletedCalls(snapshot);

        return new LiveSnapshot(
            Elapsed: elapsed,
            ProcessedFiles: processedFiles,
            ProcessedCalls: callsCompleted,
            AudioHours: audioSeconds / 3600.0,
            Rtf: rtf,
            Active: Volatile.Read(ref _activeWorkers),
            Queue: Volatile.Read(ref _queueDepth),
            Errors: errors.Length);
    }

    public BenchmarkSummary BuildSummary(
        string inputDirectory,
        int discovered,
        string model,
        string language,
        bool useGpu,
        int gpuDevice,
        int gpuConcurrency)
    {
        FileBenchmarkResult[] all;
        BenchmarkError[] errs;
        lock (_gate)
        {
            all = _results.ToArray();
            errs = _errors.ToArray();
        }

        var measured = all.Where(r => !r.IsWarmup && r.Success).ToArray();

        var startedAt = BenchmarkStartedAt;
        var finishedAt = BenchmarkFinishedAt == default ? DateTime.UtcNow : BenchmarkFinishedAt;
        var duration = (finishedAt - startedAt).TotalSeconds;
        if (duration <= 0) duration = 0.001;

        var audioSeconds = measured.Sum(r => r.AudioSeconds);
        var processingSecondsList = measured.Select(r => r.ProcessingSeconds).ToArray();
        var rtfList = measured.Select(r => r.Rtf).ToArray();

        var calls = AggregateCalls(measured);

        return new BenchmarkSummary
        {
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            DurationSeconds = duration,

            Model = model,
            Language = language,
            UseGpu = useGpu,
            GpuDevice = gpuDevice,
            GpuConcurrency = gpuConcurrency,

            InputDirectory = inputDirectory,
            FileCountDiscovered = discovered,
            ProcessedFiles = measured.Length,
            ProcessedCalls = calls.Count(c => c.Completed),

            ProcessedAudioSeconds = audioSeconds,
            AudioHoursProcessed = audioSeconds / 3600.0,
            Rtf = audioSeconds / duration,

            FilesPerHour = measured.Length / (duration / 3600.0),
            CallsPerHour = calls.Count(c => c.Completed) / (duration / 3600.0),

            AvgFileProcessingSeconds = Avg(processingSecondsList),
            P50FileProcessingSeconds = Percentile(processingSecondsList, 50),
            P95FileProcessingSeconds = Percentile(processingSecondsList, 95),
            P99FileProcessingSeconds = Percentile(processingSecondsList, 99),

            AvgFileRtf = Avg(rtfList),
            P50FileRtf = Percentile(rtfList, 50),
            P95FileRtf = Percentile(rtfList, 95),

            Errors = errs.Length
        };
    }

    public IReadOnlyList<CallBenchmarkResult> AggregateCalls()
    {
        FileBenchmarkResult[] measured;
        lock (_gate)
        {
            measured = _results.Where(r => !r.IsWarmup).ToArray();
        }
        return AggregateCalls(measured);
    }

    private static IReadOnlyList<CallBenchmarkResult> AggregateCalls(IReadOnlyList<FileBenchmarkResult> measured)
    {
        return measured
            .GroupBy(r => r.CallId)
            .Select(g =>
            {
                var files = g.ToArray();
                var completed = files.All(f => f.Success);
                return new CallBenchmarkResult
                {
                    CallId = g.Key,
                    Participants = files.Select(f => f.ParticipantId).Distinct().OrderBy(p => p).ToArray(),
                    Files = files.Select(f => f.File).Distinct().OrderBy(f => f).ToArray(),
                    TotalAudioSeconds = files.Sum(f => f.AudioSeconds),
                    CompletedFiles = files.Count(f => f.Success),
                    FailedFiles = files.Count(f => !f.Success),
                    TotalProcessingSeconds = files.Sum(f => f.ProcessingSeconds),
                    MaxFileProcessingSeconds = files.Length > 0 ? files.Max(f => f.ProcessingSeconds) : 0,
                    Completed = completed
                };
            })
            .OrderBy(c => c.CallId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int CountCompletedCalls(IReadOnlyList<FileBenchmarkResult> results)
    {
        return results
            .GroupBy(r => r.CallId)
            .Count(g => g.All(f => f.Success));
    }

    private static double Avg(IReadOnlyList<double> values) =>
        values.Count == 0 ? 0.0 : values.Average();

    private static double Percentile(IReadOnlyList<double> values, double p)
    {
        if (values.Count == 0) return 0.0;
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 1) return sorted[0];
        var rank = (p / 100.0) * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper) return sorted[lower];
        var weight = rank - lower;
        return sorted[lower] * (1 - weight) + sorted[upper] * weight;
    }

    public sealed record LiveSnapshot(
        TimeSpan Elapsed,
        int ProcessedFiles,
        int ProcessedCalls,
        double AudioHours,
        double Rtf,
        int Active,
        int Queue,
        int Errors);
}
