using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Whisper.net;
using Whisper.net.LibraryLoader;
using Whisper.net.SamplingStrategy;
using WhisperBenchmark.Configuration;
using WhisperBenchmark.Domain;

namespace WhisperBenchmark.Services;

/// <summary>
/// Otacza Whisper.net: ładuje <paramref name="parallelSlots"/> kopii modelu (po jednej na slot GPU),
/// żeby równoległe transkrypcje nie współdzieliły jednego native context (unika crashy CUDA).
/// </summary>
public sealed class WhisperTranscriber : IAsyncDisposable, IDisposable
{
    private readonly ILogger<WhisperTranscriber> _logger;
    private readonly TranscriptionSettings _settings;
    private readonly WhisperModelDownloader _modelDownloader;
    private WhisperFactory[]? _factories;
    private SemaphoreSlim? _concurrencyLimit;
    private int _roundRobin;
    private bool _disposed;

    public string Model => _settings.ModelFileName;
    public string Language => _settings.Language;
    public bool UseGpu => _settings.UseGpu;
    public int GpuDevice => _settings.GpuDevice;
    public string LoadedRuntime { get; private set; } = "Unknown";
    public int LoadedFactoryCount { get; private set; }

    public WhisperTranscriber(
        TranscriptionSettings settings,
        WhisperModelDownloader modelDownloader,
        ILogger<WhisperTranscriber> logger)
    {
        _settings = settings;
        _modelDownloader = modelDownloader;
        _logger = logger;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        InitializeAsync(parallelSlots: 1, cancellationToken);

    /// <summary>
    /// Ładuje <paramref name="parallelSlots"/> niezależnych instancji modelu (zwykle = GpuConcurrency).
    /// </summary>
    public async Task InitializeAsync(int parallelSlots, CancellationToken cancellationToken = default)
    {
        if (_factories is not null)
        {
            return;
        }

        parallelSlots = Math.Max(1, parallelSlots);
        ConfigureRuntime();

        var modelPath = Path.Combine(_settings.ModelsDirectory, _settings.ModelFileName);
        if (!File.Exists(modelPath))
        {
            if (!_settings.AutoDownloadModel)
            {
                throw new FileNotFoundException(
                    $"Nie znaleziono pliku modelu Whispera: {modelPath}. " +
                    $"Skopiuj plik {_settings.ModelFileName} do katalogu {_settings.ModelsDirectory} " +
                    $"albo włącz Transcription.AutoDownloadModel=true w appsettings.json.",
                    modelPath);
            }

            await _modelDownloader.EnsureModelAsync(modelPath, cancellationToken).ConfigureAwait(false);
        }

        var options = WhisperFactoryOptions.Default;
        options.UseGpu = _settings.UseGpu;
        options.GpuDevice = _settings.GpuDevice;

        _factories = new WhisperFactory[parallelSlots];
        _concurrencyLimit = new SemaphoreSlim(parallelSlots, parallelSlots);

        var totalSw = Stopwatch.StartNew();
        for (var i = 0; i < parallelSlots; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            _factories[i] = WhisperFactory.FromPath(modelPath, options);
            sw.Stop();
            _logger.LogInformation(
                "Załadowano instancję modelu {Index}/{Total} w {Elapsed} ms.",
                i + 1, parallelSlots, sw.ElapsedMilliseconds);
        }
        totalSw.Stop();

        LoadedFactoryCount = parallelSlots;
        LoadedRuntime = RuntimeOptions.LoadedLibrary?.ToString() ?? "Unknown";

        _logger.LogInformation(
            "Gotowe: {Slots} instancji modelu {Model} (łącznie {Total} ms, runtime: {Runtime}, useGpu={UseGpu}, gpuDevice={Device}).",
            parallelSlots, _settings.ModelFileName, totalSw.ElapsedMilliseconds, LoadedRuntime, _settings.UseGpu,
            _settings.GpuDevice);

        if (_settings.UseGpu &&
            LoadedRuntime is not "Cuda" and not "Cuda12" and not "Vulkan" and not "CoreML" and not "OpenVino")
        {
            _logger.LogWarning(
                "Skonfigurowano UseGpu=true, ale Whisper.net załadował runtime CPU ({Runtime}). " +
                "Sprawdź sterowniki NVIDIA i pakiety Whisper.net.Runtime.Cuda(12).",
                LoadedRuntime);
        }
    }

    public async Task<TranscriptionResult> TranscribeAsync(string audioFilePath, CancellationToken cancellationToken)
    {
        if (_factories is null || _concurrencyLimit is null)
        {
            throw new InvalidOperationException("Transcriber nie został zainicjalizowany. Wywołaj InitializeAsync().");
        }

        await _concurrencyLimit.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var idx = Interlocked.Increment(ref _roundRobin);
            var factory = _factories[(idx - 1) % _factories.Length];

            using var processor = BuildProcessor(factory);

            await using var fs = new FileStream(
                audioFilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, useAsync: true);

            var segments = new List<TranscriptionSegment>(capacity: 64);
            var sw = Stopwatch.StartNew();

            await foreach (var seg in processor.ProcessAsync(fs, cancellationToken).ConfigureAwait(false))
            {
                segments.Add(new TranscriptionSegment
                {
                    Start = seg.Start,
                    End = seg.End,
                    Text = seg.Text ?? string.Empty
                });
            }

            sw.Stop();
            return new TranscriptionResult(segments, sw.Elapsed);
        }
        finally
        {
            _concurrencyLimit.Release();
        }
    }

    private WhisperProcessor BuildProcessor(WhisperFactory factory)
    {
        var builder = factory.CreateBuilder()
            .WithThreads(_settings.Threads)
            .WithNoContext();

        var lang = string.IsNullOrWhiteSpace(_settings.Language) ? "auto" : _settings.Language.Trim();
        if (lang.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            builder = builder.WithLanguageDetection();
        }
        else
        {
            builder = builder.WithLanguage(lang);
        }

        var w = _settings.Whisper;

        if (!string.IsNullOrWhiteSpace(w.InitialPrompt))
        {
            builder = builder.WithPrompt(w.InitialPrompt!);
        }

        builder = builder
            .WithTemperature(w.Temperature)
            .WithNoSpeechThreshold(w.NoSpeechThreshold);

        if (w.Translate)
        {
            builder = builder.WithTranslate();
        }

        if (string.Equals(w.SamplingStrategy, "BeamSearch", StringComparison.OrdinalIgnoreCase))
        {
            builder = builder.WithBeamSearchSamplingStrategy(b =>
            {
                if (w.BeamSize > 0)
                {
                    b.WithBeamSize(w.BeamSize);
                }
            });
        }
        else
        {
            builder = builder.WithGreedySamplingStrategy(b =>
            {
                if (w.BestOf > 0)
                {
                    b.WithBestOf(w.BestOf);
                }
            });
        }

        return builder.Build();
    }

    private void ConfigureRuntime()
    {
        if (_settings.UseGpu)
        {
            RuntimeOptions.RuntimeLibraryOrder =
            [
                RuntimeLibrary.Cuda,
                RuntimeLibrary.Cuda12,
                RuntimeLibrary.Vulkan,
                RuntimeLibrary.CoreML,
                RuntimeLibrary.OpenVino,
                RuntimeLibrary.Cpu,
                RuntimeLibrary.CpuNoAvx
            ];
        }
        else
        {
            RuntimeOptions.RuntimeLibraryOrder =
            [
                RuntimeLibrary.Cpu,
                RuntimeLibrary.CpuNoAvx
            ];
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_factories is not null)
        {
            foreach (var f in _factories)
            {
                f.Dispose();
            }
        }

        _factories = null;
        _concurrencyLimit?.Dispose();
        _concurrencyLimit = null;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        Dispose();
    }

    public sealed record TranscriptionResult(IReadOnlyList<TranscriptionSegment> Segments, TimeSpan Elapsed);
}
