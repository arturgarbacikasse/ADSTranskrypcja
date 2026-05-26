using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WhisperBenchmark.Configuration;
using WhisperBenchmark.Domain;

namespace WhisperBenchmark.Services;

/// <summary>
/// Skanuje katalog wejściowy, parsuje nazwy plików wg regexa (callId/participantId)
/// i waliduje nagłówki WAV.
/// </summary>
public sealed class InputScanner
{
    private readonly ILogger<InputScanner> _logger;

    public InputScanner(ILogger<InputScanner> logger)
    {
        _logger = logger;
    }

    public (IReadOnlyList<AudioFileInfo> Valid, IReadOnlyList<BenchmarkError> Errors) Scan(BenchmarkSettings settings)
    {
        var valid = new List<AudioFileInfo>();
        var errors = new List<BenchmarkError>();

        if (!Directory.Exists(settings.InputDirectory))
        {
            errors.Add(new BenchmarkError
            {
                Stage = "Scan",
                Message = $"Katalog wejściowy nie istnieje: {settings.InputDirectory}"
            });
            return (valid, errors);
        }

        Regex regex;
        try
        {
            regex = new Regex(settings.FileNameRegex, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        catch (Exception ex)
        {
            errors.Add(new BenchmarkError
            {
                Stage = "Scan",
                Message = $"Nieprawidłowy regex nazw plików: {ex.Message}",
                Exception = ex.ToString()
            });
            return (valid, errors);
        }

        var files = Directory
            .EnumerateFiles(settings.InputDirectory, settings.Pattern, SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _logger.LogInformation("Skan katalogu {Dir}: znaleziono {Count} plików pasujących do wzorca {Pattern}.",
            settings.InputDirectory, files.Length, settings.Pattern);

        foreach (var fullPath in files)
        {
            var fileName = Path.GetFileName(fullPath);
            var match = regex.Match(fileName);
            if (!match.Success)
            {
                errors.Add(new BenchmarkError
                {
                    File = fileName,
                    Stage = "Scan",
                    Message = $"Nazwa pliku nie pasuje do wzorca: {settings.FileNameRegex}"
                });
                continue;
            }

            var callId = match.Groups["callId"].Value;
            var participantId = match.Groups["participantId"].Value;

            if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(participantId))
            {
                errors.Add(new BenchmarkError
                {
                    File = fileName,
                    Stage = "Scan",
                    Message = "Nie udało się wyznaczyć callId/participantId z nazwy pliku."
                });
                continue;
            }

            var info = AudioMetadataReader.Read(fullPath, callId, participantId);

            if (!info.IsValid)
            {
                errors.Add(new BenchmarkError
                {
                    File = fileName,
                    CallId = callId,
                    ParticipantId = participantId,
                    Stage = "Validation",
                    Message = info.ValidationError ?? "Plik WAV odrzucony w walidacji."
                });
                continue;
            }

            valid.Add(info);
        }

        _logger.LogInformation(
            "Walidacja zakończona: poprawnych plików {Valid}, odrzuconych/błędnych {Errors}.",
            valid.Count, errors.Count);

        return (valid, errors);
    }
}
