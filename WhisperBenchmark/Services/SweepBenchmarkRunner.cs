using Microsoft.Extensions.Logging;
using WhisperBenchmark.Configuration;
using WhisperBenchmark.Domain;

namespace WhisperBenchmark.Services;

/// <summary>
/// Wykonuje sekwencyjne przebiegi benchmarku dla różnych wartości GpuConcurrency.
/// Model jest ładowany tylko raz – <see cref="WhisperTranscriber"/> żyje przez wszystkie kroki sweepu.
/// </summary>
public sealed class SweepBenchmarkRunner
{
    private readonly ILogger<SweepBenchmarkRunner> _logger;
    private readonly BenchmarkRunner _runner;

    public SweepBenchmarkRunner(ILogger<SweepBenchmarkRunner> logger, BenchmarkRunner runner)
    {
        _logger = logger;
        _runner = runner;
    }

    public async Task<IReadOnlyList<SweepStepResult>> RunAsync(
        IReadOnlyList<int> concurrencies,
        BenchmarkSettings baseBenchmark,
        TranscriptionSettings transcription,
        CancellationToken cancellationToken)
    {
        var results = new List<SweepStepResult>(concurrencies.Count);

        foreach (var concurrency in concurrencies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("=== Sweep krok: GpuConcurrency = {C} ===", concurrency);

            var stepSettings = Clone(baseBenchmark);
            stepSettings.GpuConcurrency = concurrency;

            stepSettings.WarmupFiles = results.Count == 0 ? baseBenchmark.WarmupFiles : 0;

            var stepResult = await _runner
                .RunSoakAsync(stepSettings, transcription, cancellationToken)
                .ConfigureAwait(false);

            results.Add(new SweepStepResult(concurrency, stepSettings, stepResult));
        }

        return results;
    }

    private static BenchmarkSettings Clone(BenchmarkSettings src) => new()
    {
        DefaultMode = src.DefaultMode,
        InputDirectory = src.InputDirectory,
        OutputDirectory = src.OutputDirectory,
        SingleSampleFile = src.SingleSampleFile,
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

    public sealed record SweepStepResult(
        int Concurrency,
        BenchmarkSettings Settings,
        BenchmarkRunner.BenchmarkExecutionResult Execution);
}
