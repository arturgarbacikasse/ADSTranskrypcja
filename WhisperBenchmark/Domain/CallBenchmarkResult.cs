namespace WhisperBenchmark.Domain;

/// <summary>
/// Agregat per rozmowa (callId).
/// </summary>
public sealed class CallBenchmarkResult
{
    public required string CallId { get; init; }
    public required IReadOnlyList<string> Participants { get; init; }
    public required IReadOnlyList<string> Files { get; init; }

    public double TotalAudioSeconds { get; init; }
    public int CompletedFiles { get; init; }
    public int FailedFiles { get; init; }
    public double TotalProcessingSeconds { get; init; }
    public double MaxFileProcessingSeconds { get; init; }

    /// <summary>
    /// True jeżeli wszystkie pliki tego callu zostały przetworzone z sukcesem.
    /// </summary>
    public bool Completed { get; init; }
}
