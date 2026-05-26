namespace WhisperBenchmark.Domain;

/// <summary>
/// Metadane pliku WAV przygotowane przez InputScanner i AudioMetadataReader.
/// Trzymamy tu tylko to, co potrzebne do zbudowania kolejki jobów benchmarku.
/// </summary>
public sealed class AudioFileInfo
{
    public required string FullPath { get; init; }
    public required string FileName { get; init; }
    public required string CallId { get; init; }
    public required string ParticipantId { get; init; }

    public int SampleRate { get; init; }
    public int Channels { get; init; }
    public int BitsPerSample { get; init; }
    public double DurationSeconds { get; init; }
    public long FileSizeBytes { get; init; }

    public bool IsValid { get; init; }
    public string? ValidationError { get; init; }
}
