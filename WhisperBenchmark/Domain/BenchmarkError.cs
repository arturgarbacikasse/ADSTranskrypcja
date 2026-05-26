namespace WhisperBenchmark.Domain;

public sealed class BenchmarkError
{
    public string? File { get; init; }
    public string? CallId { get; init; }
    public string? ParticipantId { get; init; }
    public required string Stage { get; init; }
    public required string Message { get; init; }
    public string? Exception { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
