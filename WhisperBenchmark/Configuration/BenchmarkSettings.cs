namespace WhisperBenchmark.Configuration;

/// <summary>
/// Ustawienia samego benchmarku: gdzie szukać plików, jak długo trwa test,
/// ile transkrypcji może iść równolegle na GPU itp.
/// </summary>
public sealed class BenchmarkSettings
{
    /// <summary>Domyślny tryb CLI, gdy aplikacja uruchamiana bez argumentów (informacyjnie).</summary>
    public string DefaultMode { get; set; } = "dataset";

    public string InputDirectory { get; set; } = "./Data/Input";
    public string OutputDirectory { get; set; } = "./Data/Output";

    /// <summary>
    /// Domyślny plik WAV dla trybu <c>single</c>, gdy nie podano <c>--file</c>.
    /// Ścieżka względna do katalogu projektu, np. ./Data/Input/100/100_1.wav.
    /// </summary>
    public string? SingleSampleFile { get; set; } = "./Data/Input/100/100_1.wav";

    public string Pattern { get; set; } = "*.wav";

    /// <summary>
    /// Wzorzec nazewnictwa plików w podkatalogu interactionId.
    /// Domyślnie {interactionId}_{participantId}.wav (grupa regex: callId).
    /// </summary>
    public string FileNameRegex { get; set; } = @"^(?<callId>.+)_(?<participantId>\d+)\.wav$";

    /// <summary>
    /// Jak długo (w minutach) trwa fazę pomiarowa benchmarku.
    /// </summary>
    public int DurationMinutes { get; set; } = 60;

    /// <summary>
    /// Ile plików ma zostać przetworzone w fazie warmup. Wyniki warmupu nie wliczają się do summary.
    /// </summary>
    public int WarmupFiles { get; set; } = 5;

    /// <summary>
    /// Maksymalna liczba równoległych transkrypcji – ogranicznik obciążenia GPU.
    /// </summary>
    public int GpuConcurrency { get; set; } = 1;

    /// <summary>
    /// Twardy limit liczby plików (np. do szybkich testów). null = bez limitu.
    /// </summary>
    public int? MaxFiles { get; set; } = null;

    /// <summary>
    /// Losowa kolejność wejścia (utrudnia caching dyskowy i daje miarodajne wyniki).
    /// </summary>
    public bool ShuffleInput { get; set; } = true;

    /// <summary>
    /// Jeśli plików jest mniej niż czasu testu – zapętlaj dataset aż minie czas.
    /// </summary>
    public bool RepeatInputUntilDurationEnds { get; set; } = true;

    /// <summary>
    /// Zapis pełnej transkrypcji do JSON per plik. Domyślnie wyłączone, żeby mierzyć tylko GPU.
    /// </summary>
    public bool WriteTranscriptionJson { get; set; } = false;

    /// <summary>
    /// Zapis metryk per plik do osobnych plików JSON (debug).
    /// </summary>
    public bool WritePerFileJson { get; set; } = false;

    /// <summary>
    /// Scalanie segmentów per callId do jednego JSON-a. POC: domyślnie false.
    /// </summary>
    public bool WriteMergedCallJson { get; set; } = false;

    /// <summary>
    /// Co ile sekund logować postęp benchmarku na konsolę.
    /// </summary>
    public int MetricsIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// Czy zbierać metryki GPU za pomocą nvidia-smi.
    /// </summary>
    public bool CollectGpuMetrics { get; set; } = true;

    /// <summary>
    /// Co ile sekund odpytywać nvidia-smi.
    /// </summary>
    public int GpuMetricsIntervalSeconds { get; set; } = 10;
}
