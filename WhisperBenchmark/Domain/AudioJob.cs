namespace WhisperBenchmark.Domain;

/// <summary>
/// Pojedyncze zadanie transkrypcji = jeden plik WAV.
/// Job jest tworzony w schedulerze i przekazywany do WhisperTranscribera.
/// </summary>
public sealed class AudioJob
{
    public required AudioFileInfo File { get; init; }

    /// <summary>
    /// Moment zakolejkowania jobu (do liczenia queue wait).
    /// </summary>
    public DateTime EnqueuedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Numer porządkowy joba w obrębie benchmarku (przy zapętlaniu datasetu rośnie cały czas).
    /// </summary>
    public required long Sequence { get; init; }

    /// <summary>
    /// Czy ten job jest częścią fazy warmup. Warmupowe joby nie liczą się do podsumowania.
    /// </summary>
    public bool IsWarmup { get; init; }
}
