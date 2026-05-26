namespace WhisperBenchmark.Domain;

/// <summary>
/// Podsumowanie przebiegu benchmarku (soak, dataset lub jeden krok sweepu).
/// Pola soak/single używają nazw historycznych (ProcessedCalls, CallsPerHour).
/// Tryb dataset wypełnia dodatkowo pola interactionId / datasetAudio* / capacityPrediction.
/// </summary>
public sealed class BenchmarkSummary
{
    /// <summary>soak | dataset | single | sweep</summary>
    public string? Mode { get; init; }

    public DateTime StartedAt { get; init; }
    public DateTime FinishedAt { get; init; }

    /// <summary>Czas fazy pomiarowej (soak/dataset). Dla dataset = wallClockSeconds.</summary>
    public double DurationSeconds { get; init; }

    public string Model { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public bool UseGpu { get; init; }
    public int GpuDevice { get; init; }
    public int GpuConcurrency { get; init; }

    public string InputDirectory { get; init; } = string.Empty;

    // --- odkrycie datasetu (tryb dataset) ---
    public int InteractionsDiscovered { get; init; }
    public int FilesDiscovered { get; init; }

    // --- soak (kompatybilność) ---
    public int FileCountDiscovered { get; init; }

    public int ProcessedFiles { get; init; }

    /// <summary>Soak: domknięte callId. Dataset: processedInteractions.</summary>
    public int ProcessedCalls { get; init; }

    // --- dataset ---
    public int ProcessedInteractions { get; init; }
    public int FailedInteractions { get; init; }
    public int FailedFiles { get; init; }

    public double ProcessedAudioSeconds { get; init; }
    public double AudioHoursProcessed { get; init; }

    public double DatasetAudioSeconds { get; init; }
    public double DatasetAudioHours { get; init; }

    public double WallClockSeconds { get; init; }
    public double WallClockMinutes { get; init; }

    public double Rtf { get; init; }
    public double AudioHoursPerHour { get; init; }
    public double ProcessingTimePercentOfAudioDuration { get; init; }

    public double FilesPerHour { get; init; }
    public double CallsPerHour { get; init; }
    public double InteractionsPerHour { get; init; }

    public double AverageFileAudioSeconds { get; init; }
    public double AverageInteractionAudioSeconds { get; init; }

    public double AvgFileProcessingSeconds { get; init; }
    public double P50FileProcessingSeconds { get; init; }
    public double P95FileProcessingSeconds { get; init; }
    public double P99FileProcessingSeconds { get; init; }

    public double AvgInteractionProcessingSeconds { get; init; }
    public double P50InteractionProcessingSeconds { get; init; }
    public double P95InteractionProcessingSeconds { get; init; }
    public double P99InteractionProcessingSeconds { get; init; }

    public double AvgFileRtf { get; init; }
    public double P50FileRtf { get; init; }
    public double P95FileRtf { get; init; }
    public double P99FileRtf { get; init; }

    public CapacityPrediction? CapacityPrediction { get; init; }

    public int Errors { get; init; }
}
