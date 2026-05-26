using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace WhisperBenchmark.Services;

/// <summary>
/// Sprawdza przed benchmarkiem, czy GPU nie jest zajęte przez wiszący proces (częsta przyczyna CUDA error po crashu).
/// </summary>
public static class GpuStartupCheck
{
    public static void WarnIfGpuLooksBusy(ILogger logger, bool useGpu)
    {
        if (!useGpu)
        {
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-compute-apps=pid,process_name,used_gpu_memory --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return;
            }

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!line.Contains("WhisperBenchmark", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                logger.LogWarning(
                    "Wykryto proces WhisperBenchmark na GPU ({Line}). " +
                    "Po wcześniejszym crashu CUDA zabij go: pkill -f WhisperBenchmark, potem uruchom benchmark ponownie.",
                    line.Trim());
            }

            psi.Arguments = "--query-gpu=memory.used,memory.total --format=csv,noheader,nounits";
            using var memProc = Process.Start(psi);
            if (memProc is null)
            {
                return;
            }

            var memLine = memProc.StandardOutput.ReadToEnd().Trim();
            memProc.WaitForExit(2000);

            var parts = memLine.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length >= 2 &&
                double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var used) &&
                double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var total) &&
                total > 0 && used / total > 0.85)
            {
                logger.LogWarning(
                    "GPU pamięć zajęta w {Pct:P0} ({Used:F0}/{Total:F0} MiB). " +
                    "Na RTX 3050 4GB używaj GpuConcurrency=1 i zamknij wiszące procesy przed dataset.",
                    used / total, used, total);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "GpuStartupCheck pominięty (nvidia-smi niedostępne).");
        }
    }
}
