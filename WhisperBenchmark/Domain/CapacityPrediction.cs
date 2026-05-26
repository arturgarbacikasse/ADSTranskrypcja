namespace WhisperBenchmark.Domain;

/// <summary>
/// Predykcja przepustowości na podstawie RTF z trybu dataset.
/// </summary>
public sealed class CapacityPrediction
{
    public double AudioHoursPerHour { get; init; }
    public double EstimatedAudioHoursPer8HourShift { get; init; }
    public double EstimatedInteractionsPerHourByAverageDuration { get; init; }
    public double EstimatedInteractionsPer8HourShiftByAverageDuration { get; init; }
    public double ProcessingTimeFor2AudioHoursMinutes { get; init; }
    public double ProcessingTimeFor8AudioHoursMinutes { get; init; }
    public double ProcessingTimeFor24AudioHoursMinutes { get; init; }
    public double ProcessingTimeFor100AudioHoursMinutes { get; init; }

    public static CapacityPrediction FromRtf(double rtf, double averageInteractionAudioSeconds)
    {
        if (rtf <= 0) rtf = 0.0001;
        if (averageInteractionAudioSeconds <= 0) averageInteractionAudioSeconds = 0.0001;

        var interactionsPerHour = (rtf * 3600.0) / averageInteractionAudioSeconds;

        return new CapacityPrediction
        {
            AudioHoursPerHour = rtf,
            EstimatedAudioHoursPer8HourShift = rtf * 8.0,
            EstimatedInteractionsPerHourByAverageDuration = interactionsPerHour,
            EstimatedInteractionsPer8HourShiftByAverageDuration = interactionsPerHour * 8.0,
            ProcessingTimeFor2AudioHoursMinutes = (2.0 / rtf) * 60.0,
            ProcessingTimeFor8AudioHoursMinutes = (8.0 / rtf) * 60.0,
            ProcessingTimeFor24AudioHoursMinutes = (24.0 / rtf) * 60.0,
            ProcessingTimeFor100AudioHoursMinutes = (100.0 / rtf) * 60.0
        };
    }
}
