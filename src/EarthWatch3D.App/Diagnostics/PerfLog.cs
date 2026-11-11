#if PERF_LOG
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace EarthWatch3D.App.Diagnostics;

/// <summary>PERF_LOG builds only: appends timestamped lines to %TEMP%\EarthWatch3D-perf.log.</summary>
internal static class PerfLog
{
    public static readonly string FilePath = Path.Combine(Path.GetTempPath(), "EarthWatch3D-perf.log");
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        string line = string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} {message}{Environment.NewLine}");
        lock (Gate) File.AppendAllText(FilePath, line);
    }

    public static string Memory()
    {
        using var p = Process.GetCurrentProcess();
        return string.Create(CultureInfo.InvariantCulture,
            $"ws={p.WorkingSet64 / 1048576.0:F0} MB private={p.PrivateMemorySize64 / 1048576.0:F0} MB managed={GC.GetTotalMemory(false) / 1048576.0:F0} MB");
    }
}
#endif
