namespace WhisperBenchmark.Configuration;

/// <summary>
/// Ustawienia silnika transkrypcji Whisper.net.
/// </summary>
public sealed class TranscriptionSettings
{
    /// <summary>
    /// Jeżeli true, silnik próbuje załadować runtime CUDA. Jeśli się nie powiedzie,
    /// nastąpi fallback na CPU.
    /// </summary>
    public bool UseGpu { get; set; } = true;

    /// <summary>
    /// Indeks GPU dla CUDA (zwykle 0). Wykorzystywany przez WhisperFactory.
    /// </summary>
    public int GpuDevice { get; set; } = 0;

    /// <summary>
    /// Nazwa pliku modelu ggml (np. ggml-large-v3-turbo.bin) w katalogu modeli.
    /// </summary>
    public string ModelFileName { get; set; } = "ggml-large-v3-turbo.bin";

    /// <summary>
    /// Katalog z modelami Whispera.
    /// </summary>
    public string ModelsDirectory { get; set; } = "./Models";

    /// <summary>
    /// Język transkrypcji (ISO 639-1, np. "pl"). "auto" włącza autodetekcję.
    /// </summary>
    public string Language { get; set; } = "auto";

    /// <summary>
    /// Liczba wątków CPU używanych przez whisper.cpp (także przy GPU – do pre/post processingu).
    /// </summary>
    public int Threads { get; set; } = 4;

    /// <summary>
    /// Jeżeli true i pliku modelu nie ma w <see cref="ModelsDirectory"/>, aplikacja ściągnie go
    /// z Hugging Face za pomocą wbudowanego <c>WhisperGgmlDownloader</c>. Domyślnie włączone –
    /// wygodne w developmencie, na L40 produkcyjnym można wyłączyć i wgrać model ręcznie.
    /// </summary>
    public bool AutoDownloadModel { get; set; } = true;

    /// <summary>
    /// Parametry szczegółowe Whispera.
    /// </summary>
    public WhisperSettings Whisper { get; set; } = new();
}
