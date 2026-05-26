namespace WhisperBenchmark.Domain;

public sealed class BenchmarkError
{
    public string? File { get; init; }
    public string? FullPath { get; init; }
    public string? CallId { get; init; }

    /// <summary>Alias CallId.</summary>
    public string? InteractionId => CallId;

    public string? ParticipantId { get; init; }
    public required string Stage { get; init; }
    public required string Message { get; init; }
    public string? Exception { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
