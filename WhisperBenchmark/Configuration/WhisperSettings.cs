namespace WhisperBenchmark.Configuration;

/// <summary>
/// Parametry Whispera, mapowane bezpośrednio na opcje WhisperFactory/Processor.
/// Pozwalają eksperymentować z trybem próbkowania, beam size, temperaturą itd.
/// </summary>
public sealed class WhisperSettings
{
    /// <summary>
    /// Tekstowy prompt podawany Whisperowi na początku każdej transkrypcji.
    /// Pomaga modelowi rozpoznawać domenę językową, np. rozmowy konsultanta.
    /// </summary>
    public string? InitialPrompt { get; set; }

    /// <summary>
    /// Strategia próbkowania: Greedy lub BeamSearch.
    /// </summary>
    public string SamplingStrategy { get; set; } = "Greedy";

    /// <summary>
    /// Wielkość wiązki dla BeamSearch. Ignorowane przy Greedy.
    /// </summary>
    public int BeamSize { get; set; } = 5;

    /// <summary>
    /// Liczba best-of dla Greedy (poniżej 1 = domyślna).
    /// </summary>
    public int BestOf { get; set; } = 0;

    /// <summary>
    /// Temperatura próbkowania, zwykle 0.0 dla deterministycznych transkrypcji.
    /// </summary>
    public float Temperature { get; set; } = 0.0f;

    /// <summary>
    /// Próg detekcji ciszy / "no speech".
    /// </summary>
    public float NoSpeechThreshold { get; set; } = 0.6f;

    /// <summary>
    /// Jeżeli true, Whisper będzie tłumaczyć na angielski zamiast transkrybować.
    /// </summary>
    public bool Translate { get; set; } = false;
}
