namespace EarthWatch3D.Core.Lightning;

/// <summary>
/// Thread-safe, time-ordered buffer of lightning flashes. Feeds <see cref="Add"/> and <see cref="PruneBefore"/> from background
/// threads; the UI reads with <see cref="Between"/>, <see cref="Newest"/> and <see cref="Count"/>.
/// De-duplicates by sensor, time to the millisecond and lat/lon to 1e-4 degrees (adjacent GLM files overlap by about a second).
/// When over capacity the oldest flashes (by flash time) are dropped.
/// </summary>
public sealed class LightningBuffer(int capacity = 100_000)
{
    private readonly object _lock = new();
    private readonly List<LightningFlash> _flashes = [];               // ascending by TimeUtc
    private readonly HashSet<(LightningSensor, long, long, long)> _keys = [];
    private readonly int _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));

    /// <summary>Raised (outside the lock, on the adding thread) after an <see cref="Add"/> that added at least one flash.</summary>
    public event EventHandler? Changed;

    public int Count { get { lock (_lock) return _flashes.Count; } }

    private static (LightningSensor, long, long, long) KeyOf(in LightningFlash f) =>
        (f.Sensor, f.TimeUtc.Ticks / TimeSpan.TicksPerMillisecond, (long)Math.Round(f.Lat * 1e4), (long)Math.Round(f.Lon * 1e4));

    /// <summary>Adds the flashes not already held and returns how many were new.</summary>
    public int Add(IEnumerable<LightningFlash> flashes)
    {
        ArgumentNullException.ThrowIfNull(flashes);
        int added = 0;
        lock (_lock)
        {
            foreach (var f in flashes)
            {
                if (!_keys.Add(KeyOf(f))) continue;
                _flashes.Insert(UpperBound(f.TimeUtc), f);   // mostly at the end
                added++;
            }
            int excess = _flashes.Count - _capacity;
            if (excess > 0) RemoveFront(excess);
        }
        if (added > 0) Changed?.Invoke(this, EventArgs.Empty);
        return added;
    }

    /// <summary>Flashes with <paramref name="fromUtc"/> &lt;= time &lt; <paramref name="toUtc"/>, in time order (a copy).</summary>
    public IReadOnlyList<LightningFlash> Between(DateTime fromUtc, DateTime toUtc)
    {
        lock (_lock)
        {
            int i = LowerBound(fromUtc);
            var result = new List<LightningFlash>();
            for (; i < _flashes.Count && _flashes[i].TimeUtc < toUtc; i++) result.Add(_flashes[i]);
            return result;
        }
    }

    /// <summary>Removes flashes older than <paramref name="utc"/>.</summary>
    public void PruneBefore(DateTime utc)
    {
        lock (_lock) RemoveFront(LowerBound(utc));
    }

    /// <summary>Time of the newest flash from <paramref name="s"/>, or null if none is held.</summary>
    public DateTime? Newest(LightningSensor s)
    {
        lock (_lock)
        {
            for (int i = _flashes.Count - 1; i >= 0; i--)
                if (_flashes[i].Sensor == s) return _flashes[i].TimeUtc;
            return null;
        }
    }

    private void RemoveFront(int n)
    {
        if (n <= 0) return;
        for (int i = 0; i < n; i++) _keys.Remove(KeyOf(_flashes[i]));
        _flashes.RemoveRange(0, n);
    }

    /// <summary>Index of the first flash with time >= t.</summary>
    private int LowerBound(DateTime t)
    {
        int lo = 0, hi = _flashes.Count;
        while (lo < hi) { int mid = (lo + hi) >>> 1; if (_flashes[mid].TimeUtc < t) lo = mid + 1; else hi = mid; }
        return lo;
    }

    /// <summary>Index after the last flash with time <= t.</summary>
    private int UpperBound(DateTime t)
    {
        int lo = 0, hi = _flashes.Count;
        while (lo < hi) { int mid = (lo + hi) >>> 1; if (_flashes[mid].TimeUtc <= t) lo = mid + 1; else hi = mid; }
        return lo;
    }
}
