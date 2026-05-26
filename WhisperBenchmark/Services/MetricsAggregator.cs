using WhisperBenchmark.Domain;

namespace WhisperBenchmark.Services;

/// <summary>
/// Trzyma stan agregowanych metryk benchmarku i potrafi:
/// - thread-safe dodać wynik pliku,
/// - zwrócić snapshot do logowania,
/// - zbudować końcowe podsumowanie (soak lub dataset).
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

    public LiveSnapshot Snapshot(DateTime now, int filesDiscovered = 0, int interactionsDiscovered = 0)
    {
        FileBenchmarkResult[] measuredSuccess;
        BenchmarkError[] errors;
        lock (_gate)
        {
            measuredSuccess = _results.Where(r => !r.IsWarmup && r.Success).ToArray();
            errors = _errors.ToArray();
        }

        var elapsed = now - BenchmarkStartedAt;
        var elapsedSeconds = Math.Max(0.001, elapsed.TotalSeconds);

        var audioSeconds = measuredSuccess.Sum(r => r.AudioSeconds);
        var rtf = audioSeconds / elapsedSeconds;
        var processedFiles = measuredSuccess.Length;
        var processedInteractions = CountCompletedCalls(measuredSuccess);

        return new LiveSnapshot(
            Elapsed: elapsed,
            ProcessedFiles: processedFiles,
            FilesDiscovered: filesDiscovered,
            ProcessedInteractions: processedInteractions,
            InteractionsDiscovered: interactionsDiscovered,
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
            Mode = "soak",
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
            P99FileRtf = Percentile(rtfList, 99),

            Errors = errs.Length
        };
    }

    public BenchmarkSummary BuildDatasetSummary(
        string inputDirectory,
        int interactionsDiscovered,
        int filesDiscovered,
        IReadOnlyDictionary<string, int> expectedFilesPerInteraction,
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

        var measuredSuccess = all.Where(r => !r.IsWarmup && r.Success).ToArray();
        var measuredAll = all.Where(r => !r.IsWarmup).ToArray();
        var failedTranscriptionFiles = measuredAll.Count(r => !r.Success);

        var validationFailedFiles = errs.Count(e =>
            e.Stage is "Scan" or "Validation");

        var failedFiles = validationFailedFiles + failedTranscriptionFiles;
        var processedFiles = measuredSuccess.Length;

        var startedAt = BenchmarkStartedAt;
        var finishedAt = BenchmarkFinishedAt == default ? DateTime.UtcNow : BenchmarkFinishedAt;
        var wallClockSeconds = (finishedAt - startedAt).TotalSeconds;
        if (wallClockSeconds <= 0) wallClockSeconds = 0.001;

        var datasetAudioSeconds = measuredSuccess.Sum(r => r.AudioSeconds);
        var rtf = datasetAudioSeconds / wallClockSeconds;

        var processingSecondsList = measuredSuccess.Select(r => r.ProcessingSeconds).ToArray();
        var rtfList = measuredSuccess.Select(r => r.Rtf).ToArray();

        var calls = AggregateCalls(measuredAll);
        var interactionProcessingTimes = calls
            .Where(c => c.FirstFileStartedAt.HasValue && c.LastFileFinishedAt.HasValue)
            .Select(c => c.InteractionProcessingSeconds)
            .ToArray();

        var processedInteractions = CountProcessedInteractions(
            expectedFilesPerInteraction, measuredSuccess, errs);
        var failedInteractions = CountFailedInteractions(
            interactionsDiscovered, expectedFilesPerInteraction, measuredSuccess, errs, calls);

        var averageFileAudioSeconds = processedFiles > 0
            ? datasetAudioSeconds / processedFiles
            : 0.0;

        var interactionAudioTotals = calls
            .Where(c => c.Completed)
            .Select(c => c.TotalAudioSeconds)
            .ToArray();
        var averageInteractionAudioSeconds = interactionAudioTotals.Length > 0
            ? interactionAudioTotals.Average()
            : (processedInteractions > 0 ? datasetAudioSeconds / processedInteractions : 0.0);

        var capacity = CapacityPrediction.FromRtf(rtf, averageInteractionAudioSeconds);

        return new BenchmarkSummary
        {
            Mode = "dataset",
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            DurationSeconds = wallClockSeconds,

            Model = model,
            Language = language,
            UseGpu = useGpu,
            GpuDevice = gpuDevice,
            GpuConcurrency = gpuConcurrency,

            InputDirectory = inputDirectory,
            InteractionsDiscovered = interactionsDiscovered,
            FilesDiscovered = filesDiscovered,
            FileCountDiscovered = filesDiscovered,

            ProcessedFiles = processedFiles,
            ProcessedCalls = processedInteractions,
            ProcessedInteractions = processedInteractions,
            FailedInteractions = failedInteractions,
            FailedFiles = failedFiles,

            ProcessedAudioSeconds = datasetAudioSeconds,
            AudioHoursProcessed = datasetAudioSeconds / 3600.0,
            DatasetAudioSeconds = datasetAudioSeconds,
            DatasetAudioHours = datasetAudioSeconds / 3600.0,

            WallClockSeconds = wallClockSeconds,
            WallClockMinutes = wallClockSeconds / 60.0,

            Rtf = rtf,
            AudioHoursPerHour = rtf,
            ProcessingTimePercentOfAudioDuration =
                datasetAudioSeconds > 0 ? (wallClockSeconds / datasetAudioSeconds) * 100.0 : 0.0,

            FilesPerHour = processedFiles / (wallClockSeconds / 3600.0),
            CallsPerHour = processedInteractions / (wallClockSeconds / 3600.0),
            InteractionsPerHour = processedInteractions / (wallClockSeconds / 3600.0),

            AverageFileAudioSeconds = averageFileAudioSeconds,
            AverageInteractionAudioSeconds = averageInteractionAudioSeconds,

            AvgFileProcessingSeconds = Avg(processingSecondsList),
            P50FileProcessingSeconds = Percentile(processingSecondsList, 50),
            P95FileProcessingSeconds = Percentile(processingSecondsList, 95),
            P99FileProcessingSeconds = Percentile(processingSecondsList, 99),

            AvgInteractionProcessingSeconds = Avg(interactionProcessingTimes),
            P50InteractionProcessingSeconds = Percentile(interactionProcessingTimes, 50),
            P95InteractionProcessingSeconds = Percentile(interactionProcessingTimes, 95),
            P99InteractionProcessingSeconds = Percentile(interactionProcessingTimes, 99),

            AvgFileRtf = Avg(rtfList),
            P50FileRtf = Percentile(rtfList, 50),
            P95FileRtf = Percentile(rtfList, 95),
            P99FileRtf = Percentile(rtfList, 99),

            CapacityPrediction = capacity,
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
            .GroupBy(r => r.CallId, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var files = g.ToArray();
                var completed = files.All(f => f.Success);
                var started = files.Min(f => f.StartedAt);
                var finished = files.Max(f => f.FinishedAt);
                var interactionProcessing = (finished - started).TotalSeconds;
                if (interactionProcessing < 0) interactionProcessing = 0;

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
                    InteractionProcessingSeconds = interactionProcessing,
                    FirstFileStartedAt = started,
                    LastFileFinishedAt = finished,
                    Completed = completed
                };
            })
            .OrderBy(c => c.CallId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int CountCompletedCalls(IReadOnlyList<FileBenchmarkResult> results)
    {
        return results
            .GroupBy(r => r.CallId, StringComparer.OrdinalIgnoreCase)
            .Count(g => g.All(f => f.Success));
    }

    private static int CountProcessedInteractions(
        IReadOnlyDictionary<string, int> expectedFilesPerInteraction,
        IReadOnlyList<FileBenchmarkResult> measuredSuccess,
        IReadOnlyList<BenchmarkError> errors)
    {
        var successByInteraction = measuredSuccess
            .GroupBy(r => r.CallId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var count = 0;
        foreach (var (interactionId, expectedCount) in expectedFilesPerInteraction)
        {
            if (successByInteraction.TryGetValue(interactionId, out var okCount) && okCount >= expectedCount)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountFailedInteractions(
        int interactionsDiscovered,
        IReadOnlyDictionary<string, int> expectedFilesPerInteraction,
        IReadOnlyList<FileBenchmarkResult> measuredSuccess,
        IReadOnlyList<BenchmarkError> errors,
        IReadOnlyList<CallBenchmarkResult> calls)
    {
        var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var err in errors)
        {
            if (!string.IsNullOrWhiteSpace(err.CallId))
            {
                failed.Add(err.CallId);
            }
        }

        foreach (var call in calls.Where(c => !c.Completed))
        {
            failed.Add(call.CallId);
        }

        var successByInteraction = measuredSuccess
            .GroupBy(r => r.CallId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var (interactionId, expectedCount) in expectedFilesPerInteraction)
        {
            if (!successByInteraction.TryGetValue(interactionId, out var okCount) || okCount < expectedCount)
            {
                failed.Add(interactionId);
            }
        }

        foreach (var interactionId in expectedFilesPerInteraction.Keys)
        {
            if (!successByInteraction.ContainsKey(interactionId))
            {
                failed.Add(interactionId);
            }
        }

        return failed.Count;
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
        int FilesDiscovered,
        int ProcessedInteractions,
        int InteractionsDiscovered,
        double AudioHours,
        double Rtf,
        int Active,
        int Queue,
        int Errors);
}
