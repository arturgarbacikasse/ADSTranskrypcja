using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Whisper.net.Ggml;

namespace WhisperBenchmark.Services;

/// <summary>
/// Auto-downloader modelu GGML.
/// Mapuje nazwę pliku (np. <c>ggml-large-v3-turbo.bin</c>, <c>ggml-medium-q5_0.bin</c>)
/// na pary <see cref="GgmlType"/> + <see cref="QuantizationType"/> i pobiera plik z Hugging Face
/// za pomocą wbudowanego w Whisper.net <c>WhisperGgmlDownloader.Default</c>.
///
/// Zapis jest atomowy – stream leci do pliku <c>*.tmp</c>, dopiero po sukcesie następuje
/// <c>File.Move</c> na docelową nazwę.
/// </summary>
public sealed class WhisperModelDownloader
{
    private static readonly Dictionary<string, GgmlType> ModelMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tiny"] = GgmlType.Tiny,
        ["tiny.en"] = GgmlType.TinyEn,
        ["base"] = GgmlType.Base,
        ["base.en"] = GgmlType.BaseEn,
        ["small"] = GgmlType.Small,
        ["small.en"] = GgmlType.SmallEn,
        ["medium"] = GgmlType.Medium,
        ["medium.en"] = GgmlType.MediumEn,
        ["large-v1"] = GgmlType.LargeV1,
        ["large-v2"] = GgmlType.LargeV2,
        ["large-v3"] = GgmlType.LargeV3,
        ["large-v3-turbo"] = GgmlType.LargeV3Turbo
    };

    private static readonly Dictionary<string, QuantizationType> QuantMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["q4_0"] = QuantizationType.Q4_0,
        ["q4_1"] = QuantizationType.Q4_1,
        ["q5_0"] = QuantizationType.Q5_0,
        ["q5_1"] = QuantizationType.Q5_1,
        ["q8_0"] = QuantizationType.Q8_0
    };

    private readonly ILogger<WhisperModelDownloader> _logger;

    public WhisperModelDownloader(ILogger<WhisperModelDownloader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Upewnia się, że plik <paramref name="modelPath"/> istnieje. Jeżeli go nie ma, mapuje nazwę
    /// pliku na typ modelu i pobiera go z Hugging Face.
    /// </summary>
    public async Task EnsureModelAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        if (File.Exists(modelPath))
        {
            _logger.LogDebug("Model już istnieje w {Path}.", modelPath);
            return;
        }

        var fileName = Path.GetFileName(modelPath);
        if (!TryParseModelName(fileName, out var type, out var quantization, out var parseError))
        {
            throw new InvalidOperationException(
                $"Nie udało się rozpoznać typu modelu z nazwy pliku '{fileName}': {parseError}. " +
                $"Wgraj plik ręcznie do {Path.GetDirectoryName(modelPath)} albo użyj jednej " +
                $"z obsługiwanych nazw, np. ggml-large-v3-turbo.bin lub ggml-medium-q5_0.bin.");
        }

        var directory = Path.GetDirectoryName(modelPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tmpPath = modelPath + ".tmp";
        if (File.Exists(tmpPath))
        {
            File.Delete(tmpPath);
        }

        _logger.LogInformation(
            "Brak modelu w {Path}. Pobieram {Type} (kwantyzacja: {Quant}) z Hugging Face...",
            modelPath, type, quantization);

        var sw = Stopwatch.StartNew();
        long downloadedBytes = 0;
        const long progressEveryBytes = 50L * 1024 * 1024;
        long nextProgressMark = progressEveryBytes;

        try
        {
            await using var source = await WhisperGgmlDownloader.Default
                .GetGgmlModelAsync(type, quantization, cancellationToken)
                .ConfigureAwait(false);

            await using (var target = new FileStream(
                tmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 1024 * 1024, useAsync: true))
            {
                var buffer = new byte[1024 * 1024];
                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0) break;

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    downloadedBytes += read;

                    if (downloadedBytes >= nextProgressMark)
                    {
                        _logger.LogInformation(
                            "Pobrano {Mb} MB modelu ({Speed:F1} MB/s, czas: {Elapsed:hh\\:mm\\:ss}).",
                            downloadedBytes / (1024 * 1024),
                            (downloadedBytes / (1024.0 * 1024.0)) / Math.Max(0.001, sw.Elapsed.TotalSeconds),
                            sw.Elapsed);
                        nextProgressMark += progressEveryBytes;
                    }
                }
            }

            File.Move(tmpPath, modelPath);

            sw.Stop();
            _logger.LogInformation(
                "Pobrano model {File} ({Mb} MB) w {Elapsed:hh\\:mm\\:ss} – zapisano do {Path}.",
                fileName, downloadedBytes / (1024 * 1024), sw.Elapsed, modelPath);
        }
        catch (OperationCanceledException)
        {
            SafeDeleteTmp(tmpPath);
            throw;
        }
        catch (Exception ex)
        {
            SafeDeleteTmp(tmpPath);
            throw new InvalidOperationException(
                $"Nie udało się pobrać modelu {fileName}: {ex.Message}", ex);
        }
    }

    private static void SafeDeleteTmp(string tmpPath)
    {
        try { if (File.Exists(tmpPath)) File.Delete(tmpPath); }
        catch { /* best-effort cleanup */ }
    }

    /// <summary>
    /// Parser nazw plików ggml. Obsługuje schemat
    ///   <c>ggml-{model}[-{quant}].bin</c>
    /// gdzie model = tiny/base/small/medium/large-v1/large-v2/large-v3/large-v3-turbo (opcjonalnie z ".en"),
    /// a quant ∈ { q4_0, q4_1, q5_0, q5_1, q8_0 }.
    /// </summary>
    private static bool TryParseModelName(
        string fileName,
        out GgmlType type,
        out QuantizationType quantization,
        out string? error)
    {
        type = default;
        quantization = QuantizationType.NoQuantization;
        error = null;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            error = "pusta nazwa";
            return false;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        const string prefix = "ggml-";
        if (!stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = $"oczekiwany prefiks '{prefix}'";
            return false;
        }

        var core = stem[prefix.Length..];

        var lastDash = core.LastIndexOf('-');
        if (lastDash > 0)
        {
            var maybeQuant = core[(lastDash + 1)..];
            if (QuantMap.TryGetValue(maybeQuant, out var q))
            {
                quantization = q;
                core = core[..lastDash];
            }
        }

        if (!ModelMap.TryGetValue(core, out type))
        {
            error = $"nieznany model '{core}'";
            return false;
        }

        return true;
    }
}
