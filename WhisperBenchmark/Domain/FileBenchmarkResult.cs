namespace WhisperBenchmark.Domain;

/// <summary>
/// Wynik benchmarku dla pojedynczego pliku WAV.
/// </summary>
public sealed class FileBenchmarkResult
{
    public required string File { get; init; }
    public required string FullPath { get; init; }
    public required string CallId { get; init; }
    public required string ParticipantId { get; init; }

    public double AudioSeconds { get; init; }
    public double ProcessingSeconds { get; init; }
    public double QueueWaitSeconds { get; init; }
    public double Rtf { get; init; }

    public DateTime StartedAt { get; init; }
    public DateTime FinishedAt { get; init; }

    public int SegmentCount { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public bool IsWarmup { get; init; }

    /// <summary>
    /// Segmenty transkrypcji – wypełnione tylko, jeśli włączony jest zapis JSON-a.
    /// W przeciwnym wypadku trzymamy null, żeby nie obciążać pamięci.
    /// </summary>
    public IReadOnlyList<TranscriptionSegment>? Segments { get; init; }
}
