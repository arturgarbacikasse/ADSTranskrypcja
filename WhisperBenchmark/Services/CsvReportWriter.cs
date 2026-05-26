using System.Globalization;
using System.Text;
using WhisperBenchmark.Domain;

namespace WhisperBenchmark.Services;

/// <summary>
/// Prosty writer CSV bez zewnętrznych zależności. CSV w stylu RFC 4180:
/// pola z przecinkiem/cudzysłowem/nową linią escapujemy podwójnym cudzysłowem.
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
            "file,fullPath,callId,participantId,audioSeconds,processingSeconds,queueWaitSeconds,rtf,startedAt,finishedAt,segmentCount,success,errorMessage")
            .ConfigureAwait(false);

        foreach (var r in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = string.Join(',', new[]
            {
                Escape(r.File),
                Escape(r.FullPath),
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
            "callId,participants,files,totalAudioSeconds,completedFiles,failedFiles,totalProcessingSeconds,maxFileProcessingSeconds,completed")
            .ConfigureAwait(false);

        foreach (var r in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = string.Join(',', new[]
            {
                Escape(r.CallId),
                Escape(string.Join('|', r.Participants)),
                Escape(string.Join('|', r.Files)),
                F(r.TotalAudioSeconds),
                r.CompletedFiles.ToString(Inv),
                r.FailedFiles.ToString(Inv),
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
        await writer.WriteLineAsync($"startedAt,{summary.StartedAt:o}").ConfigureAwait(false);
        await writer.WriteLineAsync($"finishedAt,{summary.FinishedAt:o}").ConfigureAwait(false);
        await writer.WriteLineAsync($"durationSeconds,{F(summary.DurationSeconds)}").ConfigureAwait(false);

        await writer.WriteLineAsync($"model,{Escape(summary.Model)}").ConfigureAwait(false);
        await writer.WriteLineAsync($"language,{Escape(summary.Language)}").ConfigureAwait(false);
        await writer.WriteLineAsync($"useGpu,{summary.UseGpu}").ConfigureAwait(false);
        await writer.WriteLineAsync($"gpuDevice,{summary.GpuDevice}").ConfigureAwait(false);
        await writer.WriteLineAsync($"gpuConcurrency,{summary.GpuConcurrency}").ConfigureAwait(false);

        await writer.WriteLineAsync($"inputDirectory,{Escape(summary.InputDirectory)}").ConfigureAwait(false);
        await writer.WriteLineAsync($"fileCountDiscovered,{summary.FileCountDiscovered}").ConfigureAwait(false);
        await writer.WriteLineAsync($"processedFiles,{summary.ProcessedFiles}").ConfigureAwait(false);
        await writer.WriteLineAsync($"processedCalls,{summary.ProcessedCalls}").ConfigureAwait(false);

        await writer.WriteLineAsync($"processedAudioSeconds,{F(summary.ProcessedAudioSeconds)}").ConfigureAwait(false);
        await writer.WriteLineAsync($"audioHoursProcessed,{F(summary.AudioHoursProcessed)}").ConfigureAwait(false);
        await writer.WriteLineAsync($"rtf,{F(summary.Rtf)}").ConfigureAwait(false);
        await writer.WriteLineAsync($"filesPerHour,{F(summary.FilesPerHour)}").ConfigureAwait(false);
        await writer.WriteLineAsync($"callsPerHour,{F(summary.CallsPerHour)}").ConfigureAwait(false);

        await writer.WriteLineAsync($"avgFileProcessingSeconds,{F(summary.AvgFileProcessingSeconds)}").ConfigureAwait(false);
        await writer.WriteLineAsync($"p50FileProcessingSeconds,{F(summary.P50FileProcessingSeconds)}").ConfigureAwait(false);
        await writer.WriteLineAsync($"p95FileProcessingSeconds,{F(summary.P95FileProcessingSeconds)}").ConfigureAwait(false);
        await writer.WriteLineAsync($"p99FileProcessingSeconds,{F(summary.P99FileProcessingSeconds)}").ConfigureAwait(false);

        await writer.WriteLineAsync($"avgFileRtf,{F(summary.AvgFileRtf)}").ConfigureAwait(false);
        await writer.WriteLineAsync($"p50FileRtf,{F(summary.P50FileRtf)}").ConfigureAwait(false);
        await writer.WriteLineAsync($"p95FileRtf,{F(summary.P95FileRtf)}").ConfigureAwait(false);

        await writer.WriteLineAsync($"errors,{summary.Errors}").ConfigureAwait(false);

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

    private static string F(double value) => value.ToString("0.######", Inv);

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var needsQuoting = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!needsQuoting) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
