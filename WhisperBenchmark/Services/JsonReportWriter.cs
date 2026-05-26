using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using WhisperBenchmark.Domain;

namespace WhisperBenchmark.Services;

public static class JsonReportWriter
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public static Task WriteSummaryAsync(string path, BenchmarkSummary summary, CancellationToken ct) =>
        WriteAsync(path, summary, ct);

    public static Task WriteErrorsAsync(string path, IEnumerable<BenchmarkError> errors, CancellationToken ct) =>
        WriteAsync(path, errors, ct);

    public static Task WritePerFileAsync(string path, FileBenchmarkResult result, CancellationToken ct) =>
        WriteAsync(path, result, ct);

    public static Task WriteTranscriptionAsync(string path, TranscriptionFile transcription, CancellationToken ct) =>
        WriteAsync(path, transcription, ct);

    public static Task WriteMergedCallAsync(string path, MergedCallTranscriptionFile transcription, CancellationToken ct) =>
        WriteAsync(path, transcription, ct);

    private static async Task WriteAsync<T>(string path, T value, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, value, Options, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Format zapisu transkrypcji per plik WAV (np. transcriptions/call001_1.json).
    /// </summary>
    public sealed class TranscriptionFile
    {
        public required string CallId { get; init; }
        public required string ParticipantId { get; init; }
        public required string File { get; init; }
        public required string Model { get; init; }
        public required string Language { get; init; }
        public double AudioSeconds { get; init; }
        public double ProcessingSeconds { get; init; }
        public double Rtf { get; init; }
        public required IReadOnlyList<TranscriptionSegmentDto> Segments { get; init; }
    }

    /// <summary>
    /// Scalona transkrypcja wszystkich nóg rozmowy (np. transcriptions/call001.json).
    /// </summary>
    public sealed class MergedCallTranscriptionFile
    {
        public required string CallId { get; init; }
        public required string Model { get; init; }
        public required string Language { get; init; }
        public required IReadOnlyList<TranscriptionParticipantDto> Participants { get; init; }
    }

    public sealed class TranscriptionParticipantDto
    {
        public required string ParticipantId { get; init; }
        public required string File { get; init; }
        public double AudioSeconds { get; init; }
        public required IReadOnlyList<TranscriptionSegmentDto> Segments { get; init; }
    }

    public sealed class TranscriptionSegmentDto
    {
        public double Start { get; init; }
        public double End { get; init; }
        public required string Text { get; init; }
    }

    public static IReadOnlyList<TranscriptionSegmentDto> ToSegmentDtos(
        IReadOnlyList<TranscriptionSegment> segments) =>
        segments.Select(s => new TranscriptionSegmentDto
        {
            Start = s.Start.TotalSeconds,
            End = s.End.TotalSeconds,
            Text = s.Text
        }).ToArray();
}
