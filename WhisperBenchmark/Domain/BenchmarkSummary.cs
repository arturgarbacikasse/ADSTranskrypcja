namespace WhisperBenchmark.Domain;

/// <summary>
/// Podsumowanie pojedynczego przebiegu benchmarku (soak lub jeden krok sweepu).
/// Pola odpowiadają strukturze benchmark-summary.json i benchmark-summary.csv.
/// </summary>
public sealed class BenchmarkSummary
{
    public DateTime StartedAt { get; init; }
    public DateTime FinishedAt { get; init; }
    public double DurationSeconds { get; init; }

    public string Model { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public bool UseGpu { get; init; }
    public int GpuDevice { get; init; }
    public int GpuConcurrency { get; init; }

    public string InputDirectory { get; init; } = string.Empty;
    public int FileCountDiscovered { get; init; }
    public int ProcessedFiles { get; init; }
    public int ProcessedCalls { get; init; }

    public double ProcessedAudioSeconds { get; init; }
    public double AudioHoursProcessed { get; init; }
    public double Rtf { get; init; }

    public double FilesPerHour { get; init; }
    public double CallsPerHour { get; init; }

    public double AvgFileProcessingSeconds { get; init; }
    public double P50FileProcessingSeconds { get; init; }
    public double P95FileProcessingSeconds { get; init; }
    public double P99FileProcessingSeconds { get; init; }

    public double AvgFileRtf { get; init; }
    public double P50FileRtf { get; init; }
    public double P95FileRtf { get; init; }

    public int Errors { get; init; }
}
