using System.Globalization;

namespace WhisperBenchmark.Cli;

public enum BenchmarkMode
{
    Single,
    Soak,
    Sweep,
    Dataset
}

public sealed class CliOptions
{
    public BenchmarkMode Mode { get; init; }
    public string? File { get; init; }
    public string? Input { get; init; }
    public string? Output { get; init; }
    public int? DurationMinutes { get; init; }
    public int? GpuConcurrency { get; init; }
    public int? MaxFiles { get; init; }
    public IReadOnlyList<int>? ConcurrencySweep { get; init; }
    public bool? WriteTranscriptionJson { get; init; }
    public bool? WritePerFileJson { get; init; }
    public bool? WriteMergedCallJson { get; init; }
    public bool? CollectGpuMetrics { get; init; }
    public bool? Shuffle { get; init; }
    public int? WarmupFiles { get; init; }
    public int? MetricsIntervalSeconds { get; init; }
    public int? GpuMetricsIntervalSeconds { get; init; }

    /// <summary>Nazwa pliku modelu GGML (np. ggml-large-v3-turbo.bin lub large-v3-turbo).</summary>
    public string? Model { get; init; }
    /// <summary>Język transkrypcji ISO 639-1 (np. pl, en) lub auto.</summary>
    public string? Language { get; init; }
    public int? Threads { get; init; }
    public bool? UseGpu { get; init; }
    public int? GpuDevice { get; init; }
    public string? ModelsDirectory { get; init; }
    public bool? AutoDownloadModel { get; init; }
    public string? SamplingStrategy { get; init; }
    public int? BeamSize { get; init; }
    public string? InitialPrompt { get; init; }
    public float? Temperature { get; init; }
    public bool? Translate { get; init; }

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException(
                "Brakuje trybu pracy. Użyj: WhisperBenchmark <single|soak|sweep|dataset> [opcje].");
        }

        var modeArg = args[0].ToLowerInvariant();
        if (modeArg is "--help" or "-h" or "help" or "/?")
        {
            PrintHelp();
            Environment.Exit(0);
        }

        var mode = modeArg switch
        {
            "single" => BenchmarkMode.Single,
            "soak" => BenchmarkMode.Soak,
            "sweep" => BenchmarkMode.Sweep,
            "dataset" => BenchmarkMode.Dataset,
            _ => throw new ArgumentException(
                $"Nieznany tryb '{args[0]}'. Dostępne: single | soak | sweep | dataset.")
        };

        string? file = null;
        string? input = null;
        string? output = null;
        int? duration = null;
        int? gpu = null;
        int? maxFiles = null;
        IReadOnlyList<int>? sweep = null;
        bool? writeTranscription = null;
        bool? writePerFile = null;
        bool? writeMergedCall = null;
        bool? collectGpu = null;
        bool? shuffle = null;
        int? warmupFiles = null;
        int? metricsInterval = null;
        int? gpuMetricsInterval = null;
        string? model = null;
        string? language = null;
        int? threads = null;
        bool? useGpu = null;
        int? gpuDevice = null;
        string? modelsDirectory = null;
        bool? autoDownloadModel = null;
        string? samplingStrategy = null;
        int? beamSize = null;
        string? initialPrompt = null;
        float? temperature = null;
        bool? translate = null;

        for (int i = 1; i < args.Length; i++)
        {
            var key = args[i];
            string? GetValue()
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException($"Brak wartości dla parametru {key}.");
                }
                return args[++i];
            }

            switch (key.ToLowerInvariant())
            {
                case "--file":
                    file = GetValue();
                    break;
                case "--input":
                    input = GetValue();
                    break;
                case "--output":
                    output = GetValue();
                    break;
                case "--duration-minutes":
                    duration = int.Parse(GetValue()!, CultureInfo.InvariantCulture);
                    break;
                case "--gpu-concurrency":
                    gpu = int.Parse(GetValue()!, CultureInfo.InvariantCulture);
                    break;
                case "--max-files":
                    maxFiles = int.Parse(GetValue()!, CultureInfo.InvariantCulture);
                    break;
                case "--concurrency":
                    sweep = (GetValue() ?? string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(s => int.Parse(s, CultureInfo.InvariantCulture))
                        .Where(c => c > 0)
                        .ToArray();
                    if (sweep.Count == 0)
                    {
                        throw new ArgumentException("Parametr --concurrency wymaga listy np. 1,2,4,8.");
                    }
                    break;
                case "--write-transcription-json":
                    writeTranscription = ParseBool(GetValue()!);
                    break;
                case "--write-per-file-json":
                    writePerFile = ParseBool(GetValue()!);
                    break;
                case "--write-merged-call-json":
                    writeMergedCall = ParseBool(GetValue()!);
                    break;
                case "--collect-gpu-metrics":
                    collectGpu = ParseBool(GetValue()!);
                    break;
                case "--shuffle":
                    shuffle = ParseBool(GetValue()!);
                    break;
                case "--warmup-files":
                    warmupFiles = int.Parse(GetValue()!, CultureInfo.InvariantCulture);
                    break;
                case "--metrics-interval-seconds":
                    metricsInterval = int.Parse(GetValue()!, CultureInfo.InvariantCulture);
                    break;
                case "--gpu-metrics-interval-seconds":
                    gpuMetricsInterval = int.Parse(GetValue()!, CultureInfo.InvariantCulture);
                    break;
                case "--model":
                    model = GetValue();
                    break;
                case "--language":
                    language = GetValue();
                    break;
                case "--threads":
                    threads = int.Parse(GetValue()!, CultureInfo.InvariantCulture);
                    break;
                case "--use-gpu":
                    useGpu = ParseBool(GetValue()!);
                    break;
                case "--gpu-device":
                    gpuDevice = int.Parse(GetValue()!, CultureInfo.InvariantCulture);
                    break;
                case "--models-directory":
                    modelsDirectory = GetValue();
                    break;
                case "--auto-download-model":
                    autoDownloadModel = ParseBool(GetValue()!);
                    break;
                case "--sampling-strategy":
                    samplingStrategy = GetValue();
                    break;
                case "--beam-size":
                    beamSize = int.Parse(GetValue()!, CultureInfo.InvariantCulture);
                    break;
                case "--initial-prompt":
                    initialPrompt = GetValue();
                    break;
                case "--temperature":
                    temperature = float.Parse(GetValue()!, CultureInfo.InvariantCulture);
                    break;
                case "--translate":
                    translate = ParseBool(GetValue()!);
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Nieznany parametr: {key}");
            }
        }

        if (mode == BenchmarkMode.Sweep && (sweep is null || sweep.Count == 0))
        {
            throw new ArgumentException("Tryb 'sweep' wymaga parametru --concurrency np. 1,2,4,8.");
        }

        return new CliOptions
        {
            Mode = mode,
            File = file,
            Input = input,
            Output = output,
            DurationMinutes = duration,
            GpuConcurrency = gpu,
            MaxFiles = maxFiles,
            ConcurrencySweep = sweep,
            WriteTranscriptionJson = writeTranscription,
            WritePerFileJson = writePerFile,
            WriteMergedCallJson = writeMergedCall,
            CollectGpuMetrics = collectGpu,
            Shuffle = shuffle,
            WarmupFiles = warmupFiles,
            MetricsIntervalSeconds = metricsInterval,
            GpuMetricsIntervalSeconds = gpuMetricsInterval,
            Model = model,
            Language = language,
            Threads = threads,
            UseGpu = useGpu,
            GpuDevice = gpuDevice,
            ModelsDirectory = modelsDirectory,
            AutoDownloadModel = autoDownloadModel,
            SamplingStrategy = samplingStrategy,
            BeamSize = beamSize,
            InitialPrompt = initialPrompt,
            Temperature = temperature,
            Translate = translate
        };
    }

    public static void PrintHelp()
    {
        Console.WriteLine();
        Console.WriteLine("WhisperBenchmark – POC benchmark wydajności Whisper.net / whisper.cpp.");
        Console.WriteLine();
        Console.WriteLine("Tryby pracy:");
        Console.WriteLine("  single  – transkrybuje pojedynczy plik WAV i drukuje wynik.");
        Console.WriteLine("  soak    – test obciążeniowy przez zadany czas (DurationMinutes).");
        Console.WriteLine("  sweep   – porównanie wielu wartości GpuConcurrency.");
        Console.WriteLine("  dataset – przetwarza cały InputDirectory dokładnie raz (rekomendowany POC).");
        Console.WriteLine();
        Console.WriteLine("Opcje benchmarku:");
        Console.WriteLine("  --file <path>                     plik WAV (tryb single)");
        Console.WriteLine("  --input <dir>                     katalog główny: {interactionId}/*.wav");
        Console.WriteLine("  --output <dir>                    katalog raportów");
        Console.WriteLine("  --duration-minutes <int>          długość fazy pomiarowej (soak/sweep; ignorowane w dataset)");
        Console.WriteLine("  --gpu-concurrency <int>           równoległość transkrypcji na GPU");
        Console.WriteLine("  --concurrency 1,2,4,8             lista concurrencies dla trybu sweep");
        Console.WriteLine("  --max-files <int>                 twardy limit liczby plików");
        Console.WriteLine("  --warmup-files <int>              pliki warmup przed pomiarem (soak; dataset=0)");
        Console.WriteLine("  --shuffle true/false              losowa kolejność plików (dataset/soak)");
        Console.WriteLine("  --write-transcription-json true   zapis transkrypcji per plik");
        Console.WriteLine("  --write-per-file-json true        zapis metryk per plik (debug)");
        Console.WriteLine("  --write-merged-call-json true     scalony JSON per interactionId");
        Console.WriteLine("  --collect-gpu-metrics true/false  zbieranie nvidia-smi");
        Console.WriteLine("  --metrics-interval-seconds <int>  log postępu na konsoli");
        Console.WriteLine("  --gpu-metrics-interval-seconds    odstęp próbek nvidia-smi");
        Console.WriteLine();
        Console.WriteLine("Opcje transkrypcji:");
        Console.WriteLine("  --model <name>                    plik modelu GGML (np. large-v3-turbo)");
        Console.WriteLine("  --models-directory <dir>          katalog z modelami (domyślnie ./Models)");
        Console.WriteLine("  --language <code>                 język ISO 639-1 (pl, en, tr) lub auto");
        Console.WriteLine("  --threads <int>                   wątki CPU whisper.cpp");
        Console.WriteLine("  --use-gpu true/false              włącz/wyłącz CUDA");
        Console.WriteLine("  --gpu-device <int>                indeks GPU (zwykle 0)");
        Console.WriteLine("  --auto-download-model true/false  pobieranie modelu z Hugging Face");
        Console.WriteLine("  --sampling-strategy Greedy|BeamSearch");
        Console.WriteLine("  --beam-size <int>                 rozmiar wiązki (BeamSearch)");
        Console.WriteLine("  --initial-prompt <text>           prompt początkowy Whispera");
        Console.WriteLine("  --temperature <float>             temperatura próbkowania");
        Console.WriteLine("  --translate true/false            tłumaczenie na angielski");
        Console.WriteLine();
        Console.WriteLine("Przykład (dataset, wszystkie kluczowe parametry):");
        Console.WriteLine("  WhisperBenchmark dataset \\");
        Console.WriteLine("    --input ./data/bench-37 --output ./data/output-run \\");
        Console.WriteLine("    --gpu-concurrency 2 --shuffle false \\");
        Console.WriteLine("    --write-transcription-json true --write-merged-call-json true --write-per-file-json false \\");
        Console.WriteLine("    --collect-gpu-metrics true --metrics-interval-seconds 10 --gpu-metrics-interval-seconds 10 \\");
        Console.WriteLine("    --model large-v3-turbo --models-directory ./Models --language pl --threads 4 \\");
        Console.WriteLine("    --use-gpu true --gpu-device 0 --auto-download-model false \\");
        Console.WriteLine("    --sampling-strategy BeamSearch --beam-size 5 --temperature 0 --translate false");
        Console.WriteLine();
    }

    private static bool ParseBool(string raw) =>
        raw.ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "y" or "on" => true,
            "0" or "false" or "no" or "n" or "off" => false,
            _ => throw new ArgumentException($"Nieprawidłowa wartość bool: '{raw}'.")
        };
}
