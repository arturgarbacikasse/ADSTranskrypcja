using WhisperBenchmark.Domain;

namespace WhisperBenchmark.Services;

/// <summary>
/// Bardzo lekki parser nagłówka WAV (RIFF/WAVE/fmt /data) – wystarczy nam wyłącznie do
/// odczytu sample rate, kanałów, bits per sample i długości audio.
/// Nie polegamy na zewnętrznych zależnościach (ffmpeg/naudio) – walidacja jest częścią
/// założeń POC: pliki wejściowe to gotowe WAV mono 16 kHz PCM 16-bit.
/// </summary>
public static class AudioMetadataReader
{
    private const int ExpectedSampleRate = 16_000;
    private const int ExpectedChannels = 1;
    private const int ExpectedBits = 16;
    private const ushort PcmFormat = 1;
    private const ushort WaveFormatExtensible = 0xFFFE;

    public static AudioFileInfo Read(string fullPath, string callId, string participantId)
    {
        var fileName = Path.GetFileName(fullPath);

        try
        {
            using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192);
            using var br = new BinaryReader(fs);

            var fileSize = fs.Length;

            var riff = new string(br.ReadChars(4));
            br.ReadUInt32();
            var wave = new string(br.ReadChars(4));
            if (riff != "RIFF" || wave != "WAVE")
            {
                return Invalid(fullPath, fileName, callId, participantId, fileSize,
                    "Plik nie jest poprawnym kontenerem RIFF/WAVE.");
            }

            ushort audioFormat = 0;
            ushort channels = 0;
            uint sampleRate = 0;
            ushort bitsPerSample = 0;
            uint dataSize = 0;
            bool dataChunkFound = false;

            while (fs.Position + 8 <= fs.Length)
            {
                var chunkId = new string(br.ReadChars(4));
                var chunkSize = br.ReadUInt32();

                if (chunkId == "fmt ")
                {
                    var fmtStart = fs.Position;
                    audioFormat = br.ReadUInt16();
                    channels = br.ReadUInt16();
                    sampleRate = br.ReadUInt32();
                    br.ReadUInt32();
                    br.ReadUInt16();
                    bitsPerSample = br.ReadUInt16();

                    if (audioFormat == WaveFormatExtensible && chunkSize >= 40)
                    {
                        br.ReadUInt16();
                        br.ReadUInt16();
                        br.ReadUInt16();
                        br.ReadUInt32();
                        audioFormat = br.ReadUInt16();
                    }

                    var consumed = fs.Position - fmtStart;
                    if (chunkSize > consumed)
                    {
                        fs.Seek(chunkSize - consumed, SeekOrigin.Current);
                    }
                }
                else if (chunkId == "data")
                {
                    dataSize = chunkSize;
                    dataChunkFound = true;
                    break;
                }
                else
                {
                    fs.Seek(chunkSize, SeekOrigin.Current);
                    if ((chunkSize & 1) == 1 && fs.Position < fs.Length)
                    {
                        fs.Seek(1, SeekOrigin.Current);
                    }
                }
            }

            if (channels == 0 || sampleRate == 0 || bitsPerSample == 0)
            {
                return Invalid(fullPath, fileName, callId, participantId, fileSize,
                    "Brak poprawnego chunku 'fmt ' w pliku WAV.");
            }

            if (!dataChunkFound)
            {
                return Invalid(fullPath, fileName, callId, participantId, fileSize,
                    "Brak chunku 'data' w pliku WAV.");
            }

            var bytesPerSecond = sampleRate * channels * (bitsPerSample / 8u);
            var duration = bytesPerSecond > 0 ? (double)dataSize / bytesPerSecond : 0.0;

            string? validationError = null;
            if (audioFormat != PcmFormat)
            {
                validationError = $"Plik nie jest PCM (audioFormat={audioFormat}).";
            }
            else if (channels != ExpectedChannels)
            {
                validationError = $"Liczba kanałów = {channels}, oczekiwane mono ({ExpectedChannels}).";
            }
            else if (sampleRate != ExpectedSampleRate)
            {
                validationError = $"Sample rate = {sampleRate} Hz, oczekiwane {ExpectedSampleRate} Hz.";
            }
            else if (bitsPerSample != ExpectedBits)
            {
                validationError = $"Bits per sample = {bitsPerSample}, oczekiwane {ExpectedBits}.";
            }

            return new AudioFileInfo
            {
                FullPath = fullPath,
                FileName = fileName,
                CallId = callId,
                ParticipantId = participantId,
                Channels = channels,
                SampleRate = (int)sampleRate,
                BitsPerSample = bitsPerSample,
                DurationSeconds = duration,
                FileSizeBytes = fileSize,
                IsValid = validationError is null,
                ValidationError = validationError
            };
        }
        catch (Exception ex)
        {
            return Invalid(fullPath, fileName, callId, participantId, 0,
                $"Błąd odczytu nagłówka WAV: {ex.Message}");
        }
    }

    private static AudioFileInfo Invalid(
        string fullPath,
        string fileName,
        string callId,
        string participantId,
        long fileSize,
        string error) =>
        new()
        {
            FullPath = fullPath,
            FileName = fileName,
            CallId = callId,
            ParticipantId = participantId,
            FileSizeBytes = fileSize,
            IsValid = false,
            ValidationError = error
        };
}
