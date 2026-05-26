using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WhisperBenchmark.Configuration;
using WhisperBenchmark.Domain;

namespace WhisperBenchmark.Services;

/// <summary>
/// Skanuje katalog wejściowy: podkatalogi <c>{interactionId}/</c> z plikami
/// <c>{interactionId}_{participantId}.wav</c>, parsuje nazwy wg regexa (callId/participantId)
/// i waliduje nagłówki WAV.
/// </summary>
public sealed class InputScanner
{
    private readonly ILogger<InputScanner> _logger;

    public InputScanner(ILogger<InputScanner> logger)
    {
        _logger = logger;
    }

    public InputScanResult Scan(BenchmarkSettings settings)
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
            return new InputScanResult(valid, errors, 0, 0, new Dictionary<string, int>());
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
            return new InputScanResult(valid, errors, 0, 0, new Dictionary<string, int>());
        }

        var interactionDirs = Directory
            .EnumerateDirectories(settings.InputDirectory)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rootWavFiles = Directory
            .EnumerateFiles(settings.InputDirectory, settings.Pattern, SearchOption.TopDirectoryOnly)
            .ToArray();

        if (rootWavFiles.Length > 0)
        {
            foreach (var fullPath in rootWavFiles)
            {
                errors.Add(new BenchmarkError
                {
                    File = Path.GetFileName(fullPath),
                    FullPath = fullPath,
                    Stage = "Scan",
                    Message =
                        "Plik WAV w katalogu głównym InputDirectory jest nieobsługiwany. " +
                        "Oczekiwana struktura: {interactionId}/{interactionId}_{participantId}.wav"
                });
            }
        }

        if (interactionDirs.Length == 0)
        {
            _logger.LogWarning(
                "Skan katalogu {Dir}: brak podkatalogów interactionId (znaleziono {RootWav} plików WAV w katalogu głównym).",
                settings.InputDirectory, rootWavFiles.Length);
            return new InputScanResult(valid, errors, 0, rootWavFiles.Length, new Dictionary<string, int>());
        }

        var totalFiles = 0;
        var filesPerInteraction = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var interactionDir in interactionDirs)
        {
            var folderInteractionId = Path.GetFileName(interactionDir);
            var files = Directory
                .EnumerateFiles(interactionDir, settings.Pattern, SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            filesPerInteraction[folderInteractionId] = files.Length;
            totalFiles += files.Length;

            foreach (var fullPath in files)
            {
                ProcessFile(fullPath, folderInteractionId, regex, settings, valid, errors);
            }
        }

        _logger.LogInformation(
            "Skan katalogu {Dir}: {InteractionCount} katalogów interactionId, {Count} plików pasujących do wzorca {Pattern}.",
            settings.InputDirectory, interactionDirs.Length, totalFiles, settings.Pattern);

        _logger.LogInformation(
            "Walidacja zakończona: poprawnych plików {Valid}, odrzuconych/błędnych {Errors}.",
            valid.Count, errors.Count);

        return new InputScanResult(valid, errors, interactionDirs.Length, totalFiles, filesPerInteraction);
    }

    private void ProcessFile(
        string fullPath,
        string folderInteractionId,
        Regex regex,
        BenchmarkSettings settings,
        List<AudioFileInfo> valid,
        List<BenchmarkError> errors)
    {
        var fileName = Path.GetFileName(fullPath);
        var match = regex.Match(fileName);
        if (!match.Success)
        {
            errors.Add(new BenchmarkError
            {
                File = fileName,
                FullPath = fullPath,
                CallId = folderInteractionId,
                Stage = "Scan",
                Message = $"Nazwa pliku nie pasuje do wzorca: {settings.FileNameRegex}"
            });
            return;
        }

        var callId = match.Groups["callId"].Value;
        var participantId = match.Groups["participantId"].Value;

        if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(participantId))
        {
            errors.Add(new BenchmarkError
            {
                File = fileName,
                FullPath = fullPath,
                CallId = folderInteractionId,
                Stage = "Scan",
                Message = "Nie udało się wyznaczyć callId/participantId z nazwy pliku."
            });
            return;
        }

        if (!string.Equals(callId, folderInteractionId, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new BenchmarkError
            {
                File = fileName,
                FullPath = fullPath,
                CallId = callId,
                ParticipantId = participantId,
                Stage = "Scan",
                Message =
                    $"Prefiks w nazwie pliku ({callId}) nie zgadza się z nazwą katalogu ({folderInteractionId})."
            });
            return;
        }

        var info = AudioMetadataReader.Read(fullPath, callId, participantId);

        if (!info.IsValid)
        {
            errors.Add(new BenchmarkError
            {
                File = fileName,
                FullPath = fullPath,
                CallId = callId,
                ParticipantId = participantId,
                Stage = "Validation",
                Message = info.ValidationError ?? "Plik WAV odrzucony w walidacji."
            });
            return;
        }

        valid.Add(info);
    }
}
