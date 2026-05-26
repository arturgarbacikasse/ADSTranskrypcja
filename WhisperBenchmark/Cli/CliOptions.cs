using System.Globalization;

namespace WhisperBenchmark.Cli;

/// <summary>
/// Tryb pracy aplikacji.
/// </summary>
public enum BenchmarkMode
{
    Single,
    Soak,
    Sweep
}

/// <summary>
/// Sparsowane opcje z linii poleceń. Wszystko jest opcjonalne – brakujące pola pochodzą z appsettings.json.
/// </summary>
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

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException(
                "Brakuje trybu pracy. Użyj: WhisperBenchmark <single|soak|sweep> [opcje].");
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
            _ => throw new ArgumentException(
                $"Nieznany tryb '{args[0]}'. Dostępne: single | soak | sweep.")
        };

        string? file = null;
        string? input = null;
        string? output = null;
        int? duration = null;
        int? gpu = null;
        int? maxFiles = null;
        IReadOnlyList<int>? sweep = null;
        bool? writeTranscription = null;

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
                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Nieznany parametr: {key}");
            }
        }

        if (mode == BenchmarkMode.Single && string.IsNullOrWhiteSpace(file))
        {
            throw new ArgumentException("Tryb 'single' wymaga parametru --file <ścieżka do WAV>.");
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
            WriteTranscriptionJson = writeTranscription
        };
    }

    public static void PrintHelp()
    {
        Console.WriteLine();
        Console.WriteLine("WhisperBenchmark – POC benchmark wydajności Whisper.net / whisper.cpp.");
        Console.WriteLine();
        Console.WriteLine("Tryby pracy:");
        Console.WriteLine("  single  – transkrybuje pojedynczy plik WAV i drukuje wynik.");
        Console.WriteLine("  soak    – godzinny+ test obciążeniowy dla jednej wartości GpuConcurrency.");
        Console.WriteLine("  sweep   – test wielu wartości GpuConcurrency (np. 1,2,4,8) w jednej sesji.");
        Console.WriteLine();
        Console.WriteLine("Wspólne opcje:");
        Console.WriteLine("  --input <dir>                   katalog z plikami WAV (override appsettings)");
        Console.WriteLine("  --output <dir>                  katalog raportów (override appsettings)");
        Console.WriteLine("  --duration-minutes <int>        długość fazy pomiarowej");
        Console.WriteLine("  --gpu-concurrency <int>         równoległość transkrypcji na GPU");
        Console.WriteLine("  --concurrency 1,2,4,8           lista concurrencies dla trybu sweep");
        Console.WriteLine("  --max-files <int>               twardy limit liczby plików");
        Console.WriteLine("  --write-transcription-json true zapis pełnej transkrypcji per plik");
        Console.WriteLine();
        Console.WriteLine("Przykłady:");
        Console.WriteLine("  WhisperBenchmark single --file /data/input/call001_1.wav");
        Console.WriteLine("  WhisperBenchmark soak --input /data/input --output /data/output --duration-minutes 60 --gpu-concurrency 4");
        Console.WriteLine("  WhisperBenchmark sweep --input /data/input --output /data/output --duration-minutes 10 --concurrency 1,2,4,8,16");
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
