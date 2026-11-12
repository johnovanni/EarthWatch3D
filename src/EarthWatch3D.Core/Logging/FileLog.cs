using System.Globalization;
using System.Text;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Core.Logging;

/// <summary>
/// Rolling log: one file per local day (<c>earthwatch3d-yyyyMMdd.log</c>), UTC timestamps inside the lines.
/// Thread-safe. Files last written more than 7 days ago are deleted when the log is created.
/// Logging never throws.
/// </summary>
public sealed class FileLog : IAppLog, IDisposable
{
    private const string Prefix = "earthwatch3d-";
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private readonly string _dir;
    private readonly IClock _clock;
    private readonly object _gate = new();
    private bool _disposed;

    public FileLog(string logsDir, IClock clock)
    {
        _dir = logsDir;
        _clock = clock;
        try
        {
            Directory.CreateDirectory(_dir);
            Prune();
        }
        catch (Exception) { /* logging must not stop the app */ }
    }

    public void Info(string m) => Write("INFO", m, null);
    public void Warn(string m, Exception? e = null) => Write("WARN", m, e);
    public void Error(string m, Exception? e = null) => Write("ERROR", m, e);
    public void Dispose() { lock (_gate) _disposed = true; }

    private void Prune()
    {
        var cutoff = _clock.UtcNow - Retention;
        foreach (var f in Directory.EnumerateFiles(_dir, Prefix + "*.log"))
        {
            try { if (File.GetLastWriteTimeUtc(f) < cutoff) File.Delete(f); }
            catch (Exception) { }
        }
    }

    private void Write(string level, string message, Exception? e)
    {
        var now = _clock.UtcNow;
        var sb = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))
            .Append(' ').Append(level).Append(' ').Append(message);
        if (e is not null) sb.Append(" | ").Append(e.ToString());
        // Keep each entry on one line so the file stays greppable.
        var line = sb.ToString().ReplaceLineEndings(" ");
        var file = Path.Combine(_dir, Prefix + now.ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
        lock (_gate)
        {
            if (_disposed) return;
            try { File.AppendAllText(file, line + Environment.NewLine); }
            catch (Exception) { }
        }
    }
}
