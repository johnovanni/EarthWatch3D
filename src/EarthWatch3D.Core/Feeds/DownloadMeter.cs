using System.Globalization;
using System.Text.Json;
using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Storage;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Core.Feeds;

/// <summary>
/// Counts the bytes downloaded today (the local calendar day in <c>zone</c>), and says when a daily limit is reached.
/// Thread-safe. With a <c>file</c>, <see cref="Save"/> keeps the count across restarts on the same local day.
/// </summary>
public sealed class DownloadMeter
{
    private const string DateFormat = "yyyy-MM-dd";

    private readonly IClock _clock;
    private readonly TimeZoneInfo _zone;
    private readonly string? _file;
    private readonly IAppLog _log;
    private readonly object _gate = new();
    private string _date;
    private long _bytes;
    private long? _limit;

    public DownloadMeter(IClock clock, TimeZoneInfo zone, string? file, IAppLog log)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _zone = zone ?? throw new ArgumentNullException(nameof(zone));
        _file = file;
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _date = LocalDate();
        Load();
    }

    /// <summary>Adds downloaded bytes to today's total.</summary>
    public void Add(long bytes)
    {
        lock (_gate)
        {
            Roll();
            _bytes += bytes;
        }
    }

    public long TodayBytes
    {
        get { lock (_gate) { Roll(); return _bytes; } }
    }

    /// <summary>The daily limit in bytes; null means no limit.</summary>
    public long? LimitBytes
    {
        get { lock (_gate) return _limit; }
        set { lock (_gate) _limit = value; }
    }

    public bool LimitReached
    {
        get { lock (_gate) { Roll(); return _limit is { } l && _bytes >= l; } }
    }

    /// <summary>Writes today's count to the file (if any). A failed write is logged, not thrown.</summary>
    public void Save()
    {
        if (_file is null) return;
        string json;
        lock (_gate)
        {
            Roll();
            json = JsonSerializer.Serialize(new { localDate = _date, bytes = _bytes });
        }
        if (!AtomicFile.TryWriteAllText(_file, json, out var error))
            _log.Warn("Could not save the download count", error);
    }

    /// <summary>Today's count for the status bar, saying when the daily limit is reached (the feeds then pause).</summary>
    public string Text
    {
        get { lock (_gate) { Roll(); return Format(_bytes, _limit is { } l && _bytes >= l); } }
    }

    /// <summary>
    /// "Today: 0.4 MB", "Today: 123 MB" or "Today: 1.23 GB" (decimal units), followed by " (limit reached)" when
    /// <paramref name="limitReached"/>.
    /// </summary>
    public static string Format(long bytes, bool limitReached = false)
    {
        var inv = CultureInfo.InvariantCulture;
        string suffix = limitReached ? " (limit reached)" : "";
        if (bytes >= 1_000_000_000L) return "Today: " + (bytes / 1e9).ToString("0.00", inv) + " GB" + suffix;
        var mb = bytes / 1e6;
        return "Today: " + (Math.Round(mb, 1) < 100 ? mb.ToString("0.0", inv) : mb.ToString("0", inv)) + " MB" + suffix;
    }

    private string LocalDate() =>
        TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, _zone).ToString(DateFormat, CultureInfo.InvariantCulture);

    // Starts a new day's count when the local date has changed. Called under the lock.
    private void Roll()
    {
        var today = LocalDate();
        if (today == _date) return;
        _date = today;
        _bytes = 0;
    }

    private void Load()
    {
        if (_file is null || !File.Exists(_file)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_file));
            var date = doc.RootElement.GetProperty("localDate").GetString();
            var bytes = doc.RootElement.GetProperty("bytes").GetInt64();
            if (date == _date && bytes >= 0) _bytes = bytes;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException
                                       or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            _log.Warn("The download count file is unreadable; starting from 0", ex);
        }
    }
}
