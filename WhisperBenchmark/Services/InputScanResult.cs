using WhisperBenchmark.Domain;

namespace WhisperBenchmark.Services;

public sealed record InputScanResult(
    IReadOnlyList<AudioFileInfo> Valid,
    IReadOnlyList<BenchmarkError> Errors,
    int InteractionsDiscovered,
    int FilesDiscovered,
    IReadOnlyDictionary<string, int> FilesPerInteractionDiscovered);
