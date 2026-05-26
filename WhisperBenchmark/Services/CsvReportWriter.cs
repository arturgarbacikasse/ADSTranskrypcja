using System.Globalization;
using System.Text;
using WhisperBenchmark.Domain;

namespace WhisperBenchmark.Services;

/// <summary>
/// Prosty writer CSV bez zewnętrznych zależności. CSV w stylu RFC 4180.
/// </summary>
public static class CsvReportWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static async Task WriteFilesAsync(
        string path,
        IEnumerable<FileBenchmarkResult> rows,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        await writer.WriteLineAsync(
            "file,fullPath,interactionId,callId,participantId,audioSeconds,processingSeconds,queueWaitSeconds,rtf,startedAt,finishedAt,segmentCount,success,errorMessage")
            .ConfigureAwait(false);

        foreach (var r in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = string.Join(',', new[]
            {
                Escape(r.File),
                Escape(r.FullPath),
                Escape(r.CallId),
                Escape(r.CallId),
                Escape(r.ParticipantId),
                F(r.AudioSeconds),
                F(r.ProcessingSeconds),
                F(r.QueueWaitSeconds),
                F(r.Rtf),
                r.StartedAt.ToString("o", Inv),
                r.FinishedAt.ToString("o", Inv),
                r.SegmentCount.ToString(Inv),
                r.Success ? "true" : "false",
                Escape(r.ErrorMessage ?? string.Empty)
            });
            await writer.WriteLineAsync(line).ConfigureAwait(false);
        }
    }

    public static async Task WriteCallsAsync(
        string path,
        IEnumerable<CallBenchmarkResult> rows,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        await writer.WriteLineAsync(
            "interactionId,callId,participants,files,totalAudioSeconds,completedFiles,failedFiles,interactionProcessingSeconds,firstFileStartedAt,lastFileFinishedAt,totalProcessingSeconds,maxFileProcessingSeconds,completed")
            .ConfigureAwait(false);

        foreach (var r in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = string.Join(',', new[]
            {
                Escape(r.InteractionId),
                Escape(r.CallId),
                Escape(string.Join('|', r.Participants)),
                Escape(string.Join('|', r.Files)),
                F(r.TotalAudioSeconds),
                r.CompletedFiles.ToString(Inv),
                r.FailedFiles.ToString(Inv),
                F(r.InteractionProcessingSeconds),
                r.FirstFileStartedAt?.ToString("o", Inv) ?? string.Empty,
                r.LastFileFinishedAt?.ToString("o", Inv) ?? string.Empty,
                F(r.TotalProcessingSeconds),
                F(r.MaxFileProcessingSeconds),
                r.Completed ? "true" : "false"
            });
            await writer.WriteLineAsync(line).ConfigureAwait(false);
        }
    }

    public static async Task WriteSummaryAsync(
        string path,
        BenchmarkSummary summary,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        await writer.WriteLineAsync("metric,value").ConfigureAwait(false);

        await WriteRow(writer, "mode", summary.Mode ?? "soak").ConfigureAwait(false);
        await WriteRow(writer, "startedAt", summary.StartedAt.ToString("o", Inv)).ConfigureAwait(false);
        await WriteRow(writer, "finishedAt", summary.FinishedAt.ToString("o", Inv)).ConfigureAwait(false);
        await WriteRow(writer, "durationSeconds", F(summary.DurationSeconds)).ConfigureAwait(false);

        await WriteRow(writer, "model", summary.Model).ConfigureAwait(false);
        await WriteRow(writer, "language", summary.Language).ConfigureAwait(false);
        await WriteRow(writer, "useGpu", summary.UseGpu.ToString()).ConfigureAwait(false);
        await WriteRow(writer, "gpuDevice", summary.GpuDevice.ToString(Inv)).ConfigureAwait(false);
        await WriteRow(writer, "gpuConcurrency", summary.GpuConcurrency.ToString(Inv)).ConfigureAwait(false);
        await WriteRow(writer, "inputDirectory", summary.InputDirectory).ConfigureAwait(false);

        if (summary.Mode == "dataset")
        {
            await WriteRow(writer, "interactionsDiscovered", summary.InteractionsDiscovered.ToString(Inv)).ConfigureAwait(false);
            await WriteRow(writer, "filesDiscovered", summary.FilesDiscovered.ToString(Inv)).ConfigureAwait(false);
            await WriteRow(writer, "processedInteractions", summary.ProcessedInteractions.ToString(Inv)).ConfigureAwait(false);
            await WriteRow(writer, "processedFiles", summary.ProcessedFiles.ToString(Inv)).ConfigureAwait(false);
            await WriteRow(writer, "failedInteractions", summary.FailedInteractions.ToString(Inv)).ConfigureAwait(false);
            await WriteRow(writer, "failedFiles", summary.FailedFiles.ToString(Inv)).ConfigureAwait(false);
            await WriteRow(writer, "datasetAudioSeconds", F(summary.DatasetAudioSeconds)).ConfigureAwait(false);
            await WriteRow(writer, "datasetAudioHours", F(summary.DatasetAudioHours)).ConfigureAwait(false);
            await WriteRow(writer, "wallClockSeconds", F(summary.WallClockSeconds)).ConfigureAwait(false);
            await WriteRow(writer, "wallClockMinutes", F(summary.WallClockMinutes)).ConfigureAwait(false);
            await WriteRow(writer, "rtf", F(summary.Rtf)).ConfigureAwait(false);
            await WriteRow(writer, "audioHoursPerHour", F(summary.AudioHoursPerHour)).ConfigureAwait(false);
            await WriteRow(writer, "processingTimePercentOfAudioDuration", F(summary.ProcessingTimePercentOfAudioDuration)).ConfigureAwait(false);
            await WriteRow(writer, "filesPerHour", F(summary.FilesPerHour)).ConfigureAwait(false);
            await WriteRow(writer, "interactionsPerHour", F(summary.InteractionsPerHour)).ConfigureAwait(false);
            await WriteRow(writer, "averageFileAudioSeconds", F(summary.AverageFileAudioSeconds)).ConfigureAwait(false);
            await WriteRow(writer, "averageInteractionAudioSeconds", F(summary.AverageInteractionAudioSeconds)).ConfigureAwait(false);
            await WriteRow(writer, "avgFileProcessingSeconds", F(summary.AvgFileProcessingSeconds)).ConfigureAwait(false);
            await WriteRow(writer, "p50FileProcessingSeconds", F(summary.P50FileProcessingSeconds)).ConfigureAwait(false);
            await WriteRow(writer, "p95FileProcessingSeconds", F(summary.P95FileProcessingSeconds)).ConfigureAwait(false);
            await WriteRow(writer, "p99FileProcessingSeconds", F(summary.P99FileProcessingSeconds)).ConfigureAwait(false);
            await WriteRow(writer, "avgInteractionProcessingSeconds", F(summary.AvgInteractionProcessingSeconds)).ConfigureAwait(false);
            await WriteRow(writer, "p50InteractionProcessingSeconds", F(summary.P50InteractionProcessingSeconds)).ConfigureAwait(false);
            await WriteRow(writer, "p95InteractionProcessingSeconds", F(summary.P95InteractionProcessingSeconds)).ConfigureAwait(false);
            await WriteRow(writer, "p99InteractionProcessingSeconds", F(summary.P99InteractionProcessingSeconds)).ConfigureAwait(false);
            await WriteRow(writer, "avgFileRtf", F(summary.AvgFileRtf)).ConfigureAwait(false);
            await WriteRow(writer, "p50FileRtf", F(summary.P50FileRtf)).ConfigureAwait(false);
            await WriteRow(writer, "p95FileRtf", F(summary.P95FileRtf)).ConfigureAwait(false);
            await WriteRow(writer, "p99FileRtf", F(summary.P99FileRtf)).ConfigureAwait(false);

            if (summary.CapacityPrediction is { } cap)
            {
                await WriteRow(writer, "capacityPrediction.audioHoursPerHour", F(cap.AudioHoursPerHour)).ConfigureAwait(false);
                await WriteRow(writer, "capacityPrediction.estimatedAudioHoursPer8HourShift", F(cap.EstimatedAudioHoursPer8HourShift)).ConfigureAwait(false);
                await WriteRow(writer, "capacityPrediction.estimatedInteractionsPerHourByAverageDuration", F(cap.EstimatedInteractionsPerHourByAverageDuration)).ConfigureAwait(false);
                await WriteRow(writer, "capacityPrediction.estimatedInteractionsPer8HourShiftByAverageDuration", F(cap.EstimatedInteractionsPer8HourShiftByAverageDuration)).ConfigureAwait(false);
                await WriteRow(writer, "capacityPrediction.processingTimeFor2AudioHoursMinutes", F(cap.ProcessingTimeFor2AudioHoursMinutes)).ConfigureAwait(false);
                await WriteRow(writer, "capacityPrediction.processingTimeFor8AudioHoursMinutes", F(cap.ProcessingTimeFor8AudioHoursMinutes)).ConfigureAwait(false);
                await WriteRow(writer, "capacityPrediction.processingTimeFor24AudioHoursMinutes", F(cap.ProcessingTimeFor24AudioHoursMinutes)).ConfigureAwait(false);
                await WriteRow(writer, "capacityPrediction.processingTimeFor100AudioHoursMinutes", F(cap.ProcessingTimeFor100AudioHoursMinutes)).ConfigureAwait(false);
            }
        }
        else
        {
            await WriteRow(writer, "fileCountDiscovered", summary.FileCountDiscovered.ToString(Inv)).ConfigureAwait(false);
            await WriteRow(writer, "processedFiles", summary.ProcessedFiles.ToString(Inv)).ConfigureAwait(false);
            await WriteRow(writer, "processedCalls", summary.ProcessedCalls.ToString(Inv)).ConfigureAwait(false);
            await WriteRow(writer, "processedAudioSeconds", F(summary.ProcessedAudioSeconds)).ConfigureAwait(false);
            await WriteRow(writer, "audioHoursProcessed", F(summary.AudioHoursProcessed)).ConfigureAwait(false);
            await WriteRow(writer, "rtf", F(summary.Rtf)).ConfigureAwait(false);
            await WriteRow(writer, "filesPerHour", F(summary.FilesPerHour)).ConfigureAwait(false);
            await WriteRow(writer, "callsPerHour", F(summary.CallsPerHour)).ConfigureAwait(false);
            await WriteRow(writer, "avgFileProcessingSeconds", F(summary.AvgFileProcessingSeconds)).ConfigureAwait(false);
            await WriteRow(writer, "p50FileProcessingSeconds", F(summary.P50FileProcessingSeconds)).ConfigureAwait(false);
            await WriteRow(writer, "p95FileProcessingSeconds", F(summary.P95FileProcessingSeconds)).ConfigureAwait(false);
            await WriteRow(writer, "p99FileProcessingSeconds", F(summary.P99FileProcessingSeconds)).ConfigureAwait(false);
            await WriteRow(writer, "avgFileRtf", F(summary.AvgFileRtf)).ConfigureAwait(false);
            await WriteRow(writer, "p50FileRtf", F(summary.P50FileRtf)).ConfigureAwait(false);
            await WriteRow(writer, "p95FileRtf", F(summary.P95FileRtf)).ConfigureAwait(false);
        }

        await WriteRow(writer, "errors", summary.Errors.ToString(Inv)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public static async Task WriteSweepAsync(
        string path,
        IEnumerable<(int Concurrency, BenchmarkSummary Summary)> rows,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        await writer.WriteLineAsync(
            "gpuConcurrency,durationSeconds,processedFiles,processedCalls,processedAudioSeconds,audioHoursProcessed,rtf,filesPerHour,callsPerHour,avgFileProcessingSeconds,p95FileProcessingSeconds,errors")
            .ConfigureAwait(false);

        foreach (var (concurrency, s) in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = string.Join(',', new[]
            {
                concurrency.ToString(Inv),
                F(s.DurationSeconds),
                s.ProcessedFiles.ToString(Inv),
                s.ProcessedCalls.ToString(Inv),
                F(s.ProcessedAudioSeconds),
                F(s.AudioHoursProcessed),
                F(s.Rtf),
                F(s.FilesPerHour),
                F(s.CallsPerHour),
                F(s.AvgFileProcessingSeconds),
                F(s.P95FileProcessingSeconds),
                s.Errors.ToString(Inv)
            });
            await writer.WriteLineAsync(line).ConfigureAwait(false);
        }
    }

    public static async Task WriteGpuSamplesAsync(
        string path,
        string header,
        IEnumerable<string> samples,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        await writer.WriteLineAsync(header).ConfigureAwait(false);
        foreach (var line in samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(line).ConfigureAwait(false);
        }
    }

    private static async Task WriteRow(StreamWriter writer, string metric, string value) =>
        await writer.WriteLineAsync($"{metric},{Escape(value)}").ConfigureAwait(false);

    private static string F(double value) => value.ToString("0.######", Inv);

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var needsQuoting = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!needsQuoting) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
