namespace WhisperBenchmark.Domain;

/// <summary>
/// Pojedynczy segment transkrypcji zwrócony przez Whispera.
/// </summary>
public sealed class TranscriptionSegment
{
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public string Text { get; init; } = string.Empty;
}
