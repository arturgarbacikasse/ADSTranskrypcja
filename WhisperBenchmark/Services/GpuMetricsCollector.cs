using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WhisperBenchmark.Configuration;

namespace WhisperBenchmark.Services;

/// <summary>
/// Best-effort kolektor metryk GPU oparty o <c>nvidia-smi</c>.
/// Co N sekund odpalamy nvidia-smi w trybie batch i zapisujemy linie do CSV.
/// Jeżeli komenda nie istnieje (brak NVIDII albo Windows bez sterowników w PATH),
/// kolektor po prostu kończy działanie z ostrzeżeniem – benchmark nie jest blokowany.
/// </summary>
public sealed class GpuMetricsCollector : IAsyncDisposable
{
    private readonly ILogger<GpuMetricsCollector> _logger;
    private readonly BenchmarkSettings _settings;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<string> _samples = new();
    private readonly object _samplesGate = new();
    private Task? _loop;
    private bool _disposed;

    public string CsvHeader => "timestamp,index,name,utilization_gpu_pct,memory_used_mb,memory_total_mb,power_draw_w,temperature_c";

    public GpuMetricsCollector(BenchmarkSettings settings, ILogger<GpuMetricsCollector> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public void Start()
    {
        if (!_settings.CollectGpuMetrics)
        {
            _logger.LogInformation("Zbieranie metryk GPU wyłączone w konfiguracji.");
            return;
        }
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public IReadOnlyList<string> SnapshotSamples()
    {
        lock (_samplesGate) return _samples.ToArray();
    }

    private async Task LoopAsync(CancellationToken token)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.GpuMetricsIntervalSeconds));
        var nvidiaSmiAvailable = true;

        while (!token.IsCancellationRequested)
        {
            if (nvidiaSmiAvailable)
            {
                try
                {
                    await CollectOnceAsync(token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    nvidiaSmiAvailable = false;
                    _logger.LogWarning(ex,
                        "Nie udało się uruchomić nvidia-smi. Dalsze próby zostaną pominięte – benchmark biegnie dalej.");
                }
            }

            try
            {
                await Task.Delay(interval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CollectOnceAsync(CancellationToken token)
    {
        const string args =
            "--query-gpu=timestamp,index,name,utilization.gpu,memory.used,memory.total,power.draw,temperature.gpu " +
            "--format=csv,noheader,nounits";

        var psi = new ProcessStartInfo
        {
            FileName = "nvidia-smi",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Nie udało się uruchomić nvidia-smi.");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(token);
        var stderrTask = proc.StandardError.ReadToEndAsync(token);

        await proc.WaitForExitAsync(token).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"nvidia-smi exit code {proc.ExitCode}. stderr: {stderr.Trim()}");
        }

        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return;

        lock (_samplesGate)
        {
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                _samples.Add(line);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Pętla GPU metrics zakończona z wyjątkiem (pomijam).");
            }
        }
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
