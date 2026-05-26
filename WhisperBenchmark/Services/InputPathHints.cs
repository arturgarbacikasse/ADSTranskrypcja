using System.Text.RegularExpressions;

namespace WhisperBenchmark.Services;

internal static class InputPathHints
{
    private static readonly Regex FileNamePattern =
        new(@"^(?<interactionId>.+)_(?<participantId>\d+)\.wav$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string ForMissingSingleFile(string filePath, string inputDirectory)
    {
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName))
        {
            return " Oczekiwana ścieżka: {InputDirectory}/{interactionId}/{interactionId}_{participantId}.wav";
        }

        if (Directory.Exists(inputDirectory))
        {
            var sameName = Directory
                .EnumerateFiles(inputDirectory, fileName, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(sameName))
            {
                return $" Plik o tej nazwie jest w: {sameName}";
            }

            var match = FileNamePattern.Match(fileName);
            if (match.Success)
            {
                var interactionId = match.Groups["interactionId"].Value;
                var suggested = Path.Combine(inputDirectory, interactionId, fileName);
                if (File.Exists(suggested))
                {
                    return $" Czy chodziło o: {suggested}?";
                }
            }
        }

        return " Oczekiwana ścieżka: {InputDirectory}/{interactionId}/{interactionId}_{participantId}.wav " +
               "(np. ./Data/Input/100/100_1.wav).";
    }
}
