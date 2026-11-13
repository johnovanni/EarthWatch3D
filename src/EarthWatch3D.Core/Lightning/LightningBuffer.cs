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
    private List<LightningFlash> _flashes = [];                        // ascending by TimeUtc; replaced whole by a merge
    private readonly HashSet<(LightningSensor, long, long, long)> _keys = [];
    private readonly int _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));

    /// <summary>Raised (outside the lock, on the adding thread) after an <see cref="Add"/> that added at least one flash.</summary>
    public event EventHandler? Changed;

    public int Count { get { lock (_lock) return _flashes.Count; } }

    private static (LightningSensor, long, long, long) KeyOf(in LightningFlash f) =>
        (f.Sensor, f.TimeUtc.Ticks / TimeSpan.TicksPerMillisecond, (long)Math.Round(f.Lat * 1e4), (long)Math.Round(f.Lon * 1e4));

    /// <summary>
    /// Adds the flashes not already held and returns how many were new and are still held (a flash that the cap evicts at
    /// once does not count). Linear in the flashes held plus the batch: a batch older than what is held (the newest-first
    /// catch-up) is merged in one pass rather than inserted flash by flash.
    /// </summary>
    public int Add(IEnumerable<LightningFlash> flashes)
    {
        ArgumentNullException.ThrowIfNull(flashes);
        // Materialised and keyed before taking the lock, so a slow or lazy caller sequence never holds up the readers.
        var incoming = flashes.Select(f => (Flash: f, Key: KeyOf(f))).ToList();
        if (incoming.Count == 0) return 0;
        int added;
        lock (_lock)
        {
            bool full = _flashes.Count >= _capacity;
            var oldestHeld = _flashes.Count > 0 ? _flashes[0].TimeUtc : DateTime.MinValue;
            var batch = new HashSet<(LightningSensor, long, long, long)>();
            var fresh = new List<LightningFlash>(incoming.Count);
            foreach (var (f, key) in incoming)
            {
                if (full && f.TimeUtc < oldestHeld) continue;   // would be evicted at once
                if (_keys.Contains(key) || !batch.Add(key)) continue;
                fresh.Add(f);
            }
            if (fresh.Count == 0) return 0;
            foreach (var k in batch) _keys.Add(k);
            // Stable, so flashes with equal times keep their arrival order (as inserting each after its equals did).
            if (!IsSorted(fresh)) fresh = [.. fresh.OrderBy(f => f.TimeUtc)];

            if (_flashes.Count == 0 || fresh[0].TimeUtc >= _flashes[^1].TimeUtc) _flashes.AddRange(fresh);
            else _flashes = Merge(_flashes, fresh);
            added = fresh.Count;
            int excess = _flashes.Count - _capacity;
            if (excess > 0) added -= RemoveFront(excess, batch);
        }
        if (added > 0) Changed?.Invoke(this, EventArgs.Empty);
        return added;
    }

    private static bool IsSorted(List<LightningFlash> list)
    {
        for (int i = 1; i < list.Count; i++)
            if (list[i].TimeUtc < list[i - 1].TimeUtc) return false;
        return true;
    }

    /// <summary>Merges two time-ordered lists into a new one; among equal times the flashes already held come first.</summary>
    private static List<LightningFlash> Merge(List<LightningFlash> held, List<LightningFlash> fresh)
    {
        var merged = new List<LightningFlash>(held.Count + fresh.Count);
        int i = 0, j = 0;
        while (i < held.Count && j < fresh.Count)
            merged.Add(fresh[j].TimeUtc < held[i].TimeUtc ? fresh[j++] : held[i++]);
        for (; i < held.Count; i++) merged.Add(held[i]);
        for (; j < fresh.Count; j++) merged.Add(fresh[j]);
        return merged;
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

    /// <summary>
    /// Removes every flash for which <paramref name="match"/> is true and returns how many there were. Raises
    /// <see cref="Changed"/> once (outside the lock) when any were removed.
    /// </summary>
    public int RemoveWhere(Func<LightningFlash, bool> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        int removed;
        lock (_lock)
        {
            var kept = new List<LightningFlash>(_flashes.Count);
            foreach (var f in _flashes)
            {
                if (match(f)) _keys.Remove(KeyOf(f));
                else kept.Add(f);
            }
            removed = _flashes.Count - kept.Count;
            if (removed > 0) _flashes = kept;
        }
        if (removed > 0) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
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

    /// <summary>Removes the n oldest flashes; returns how many of them were in <paramref name="batch"/>.</summary>
    private int RemoveFront(int n, HashSet<(LightningSensor, long, long, long)>? batch = null)
    {
        if (n <= 0) return 0;
        int fromBatch = 0;
        for (int i = 0; i < n; i++)
        {
            var k = KeyOf(_flashes[i]);
            _keys.Remove(k);
            if (batch is not null && batch.Contains(k)) fromBatch++;
        }
        _flashes.RemoveRange(0, n);
        return fromBatch;
    }

    /// <summary>Index of the first flash with time >= t.</summary>
    private int LowerBound(DateTime t)
    {
        int lo = 0, hi = _flashes.Count;
        while (lo < hi) { int mid = (lo + hi) >>> 1; if (_flashes[mid].TimeUtc < t) lo = mid + 1; else hi = mid; }
        return lo;
    }
}
