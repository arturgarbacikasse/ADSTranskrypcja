namespace WhisperBenchmark.Domain;

/// <summary>
/// Agregat per interakcja (interactionId / callId).
/// </summary>
public sealed class CallBenchmarkResult
{
    public required string CallId { get; init; }

    /// <summary>Alias CallId – ta sama wartość co interactionId w CSV dataset.</summary>
    public string InteractionId => CallId;

    public required IReadOnlyList<string> Participants { get; init; }
    public required IReadOnlyList<string> Files { get; init; }

    public double TotalAudioSeconds { get; init; }
    public int CompletedFiles { get; init; }
    public int FailedFiles { get; init; }
    public double TotalProcessingSeconds { get; init; }
    public double MaxFileProcessingSeconds { get; init; }

    /// <summary>lastFileFinishedAt − firstFileStartedAt (sekundy).</summary>
    public double InteractionProcessingSeconds { get; init; }

    public DateTime? FirstFileStartedAt { get; init; }
    public DateTime? LastFileFinishedAt { get; init; }

    public bool Completed { get; init; }
}
