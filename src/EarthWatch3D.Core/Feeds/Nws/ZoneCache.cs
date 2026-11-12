using System.Collections.Concurrent;
using System.Text.Json;
using AstroCore.Earth;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Storage;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Core.Feeds.Nws;

/// <summary>
/// NWS zone outlines, downloaded on demand (at most <c>maxConcurrent</c> at a time) and kept on disk for 30 days.
/// Disk file: ZonesDir/&lt;type&gt;_&lt;id&gt;.json. A failed download (an HTTP error, no geometry, a parse error) is not
/// tried again for 10 min, doubling after each further failure up to 6 h.
/// </summary>
public sealed class ZoneCache(FeedHttp http, AppPaths paths, IClock clock, IAppLog log, int maxConcurrent = 4)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    private static readonly TimeSpan MissMemory = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, DateTime> _missUntil = new(StringComparer.Ordinal);
    private static readonly TimeSpan FirstRetry = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MaxRetry = TimeSpan.FromHours(6);
    // Failed downloads: how many in a row, and when the zone may be tried again. Cleared by a success.
    private readonly ConcurrentDictionary<string, (int Failures, DateTime RetryAt)> _failed = new(StringComparer.Ordinal);
    // Zones whose failure has been logged with its stack trace (only the first failure per zone is).
    private readonly ConcurrentDictionary<string, byte> _traced = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, IReadOnlyList<GeoPolygon>> _mem = new(StringComparer.Ordinal);
    private readonly HashSet<string> _queued = new(StringComparer.Ordinal);
    private readonly HashSet<string> _diskQueued = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _slots = new(maxConcurrent);
    private readonly object _gate = new();
    private int _pending;
    private int _inFlight;
    private int _peak;
    private TaskCompletionSource _idle = NewIdle(done: true);

    public event EventHandler? ZonesLoaded;

    /// <summary>The highest number of downloads that ran at once (for tests).</summary>
    public int InFlightPeak => Volatile.Read(ref _peak);

    private static TaskCompletionSource NewIdle(bool done)
    {
        var t = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (done) t.SetResult();
        return t;
    }

    /// <summary>Completes when no downloads or background disk reads are queued or in flight.</summary>
    public Task WhenIdleAsync() { lock (_gate) return _idle.Task; }

    /// <summary>Memory only: never touches the disk, so it is safe on a hot UI path such as hover.</summary>
    public bool TryGetCached(string zoneUrl, out IReadOnlyList<GeoPolygon> areas)
    {
        if (_mem.TryGetValue(zoneUrl, out var m)) { areas = m; return true; }
        areas = [];
        return false;
    }

    public bool TryGet(string zoneUrl, out IReadOnlyList<GeoPolygon> areas)
    {
        if (_mem.TryGetValue(zoneUrl, out var m)) { areas = m; return true; }
        // A disk miss is remembered briefly so repeated lookups do not probe the file system.
        if (_missUntil.TryGetValue(zoneUrl, out var until) && clock.UtcNow < until) { areas = []; return false; }
        var loaded = TryLoadFromDisk(zoneUrl);
        if (loaded is not null) { _mem[zoneUrl] = loaded; _missUntil.TryRemove(zoneUrl, out _); areas = loaded; return true; }
        _missUntil[zoneUrl] = clock.UtcNow + MissMemory;
        areas = [];
        return false;
    }

    /// <summary>
    /// Reads from disk, on a background thread, the zones that are not in memory, and raises <see cref="ZonesLoaded"/>
    /// if any were found (or had reached memory meanwhile). Never downloads: the NWS feed downloads the zones of the
    /// alerts it wants. Never blocks and never touches the disk on the calling thread, so it is safe on the UI thread.
    /// A zone missing from disk is not probed again for 30 s.
    /// </summary>
    public void LoadInBackground(IEnumerable<string> zoneUrls)
    {
        var now = clock.UtcNow;
        var probe = new List<string>();
        lock (_gate)
        {
            foreach (var url in zoneUrls)
            {
                if (_missUntil.TryGetValue(url, out var until) && now < until) continue;
                if (!_diskQueued.Add(url)) continue;
                probe.Add(url);
            }
            if (probe.Count == 0) return;
            if (_pending++ == 0) _idle = NewIdle(done: false);
        }
        _ = Task.Run(() => ReadFromDisk(probe));
    }

    private void ReadFromDisk(List<string> urls)
    {
        var found = false;
        try
        {
            foreach (var url in urls) if (TryGet(url, out _)) found = true;   // TryGet logs and survives unreadable files
        }
        catch (Exception ex) { log.Warn("NWS: reading cached zones failed", ex); }
        finally
        {
            lock (_gate) foreach (var url in urls) _diskQueued.Remove(url);
            if (found) RaiseZonesLoaded();
            WorkDone();
        }
    }

    private void RaiseZonesLoaded()
    {
        try { ZonesLoaded?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { log.Warn("NWS: a ZonesLoaded subscriber threw", ex); }
    }

    private void WorkDone()
    {
        TaskCompletionSource? done = null;
        lock (_gate) if (--_pending == 0) done = _idle;
        done?.TrySetResult();
    }

    /// <summary>How long a zone waits after its <paramref name="failures"/>-th failed download in a row: 10 min, doubling, at most 6 h.</summary>
    public static TimeSpan RetryDelay(int failures)
    {
        var minutes = FirstRetry.TotalMinutes * Math.Pow(2, Math.Clamp(failures, 1, 30) - 1);
        return minutes >= MaxRetry.TotalMinutes ? MaxRetry : TimeSpan.FromMinutes(minutes);
    }

    /// <summary>Queues the distinct URLs that are neither cached, already queued, nor waiting after a failure. Never blocks.</summary>
    public void Request(IEnumerable<string> zoneUrls)
    {
        var start = new List<string>();
        var now = clock.UtcNow;
        foreach (var url in zoneUrls.Distinct(StringComparer.Ordinal))
        {
            if (TryGet(url, out _)) continue;
            if (_failed.TryGetValue(url, out var failed) && now < failed.RetryAt) continue;
            lock (_gate)
            {
                if (!_queued.Add(url)) continue;
                if (_pending++ == 0) _idle = NewIdle(done: false);
            }
            start.Add(url);
        }
        foreach (var url in start) _ = Task.Run(() => DownloadAsync(url));
    }

    private async Task DownloadAsync(string url)
    {
        var loaded = false;
        try
        {
            await _slots.WaitAsync().ConfigureAwait(false);
            try
            {
                var now = Interlocked.Increment(ref _inFlight);
                int p;
                while (now > (p = Volatile.Read(ref _peak))) Interlocked.CompareExchange(ref _peak, now, p);
                try
                {
                    var bytes = await http.GetBytesAsync(new Uri(url), CancellationToken.None).ConfigureAwait(false);
                    var polys = ParseZone(bytes);
                    _mem[url] = polys;
                    _missUntil.TryRemove(url, out _);
                    _failed.TryRemove(url, out _);
                    Save(url, polys);
                    loaded = true;
                }
                finally { Interlocked.Decrement(ref _inFlight); }
            }
            finally { _slots.Release(); }
        }
        catch (Exception ex)
        {
            // Background task: nothing may escape as an unobserved exception.
            var now = clock.UtcNow;
            var entry = _failed.AddOrUpdate(url, _ => (1, now + RetryDelay(1)),
                (_, old) => (old.Failures + 1, now + RetryDelay(old.Failures + 1)));
            var wait = RetryDelay(entry.Failures);
            if (_traced.TryAdd(url, 0))
                log.Warn($"NWS: zone {url} failed; next try in {wait.TotalMinutes:0} min at the earliest", ex);
            else
                log.Warn($"NWS: zone {url} failed again ({entry.Failures} in a row): {ex.GetType().Name}: {ex.Message}; next try in {wait.TotalMinutes:0} min");
        }
        finally
        {
            // Raised before the idle signal, and outside the download's try so a throwing subscriber is not a download failure.
            if (loaded) RaiseZonesLoaded();
            lock (_gate) _queued.Remove(url);
            WorkDone();
        }
    }

    private static IReadOnlyList<GeoPolygon> ParseZone(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        if (!doc.RootElement.TryGetProperty("geometry", out var g) || g.ValueKind != JsonValueKind.Object)
            throw new FormatException("zone has no geometry");
        return NwsReader.ReadGeometry(g);
    }

    private string FileFor(string url)
    {
        // Zone ids repeat across types (zones/forecast/X, zones/county/X), so the file is <type>_<id>.json.
        var parts = url.TrimEnd('/').Split('/');
        var seg = parts.Length >= 2 ? parts[^2] + "_" + parts[^1] : parts[^1];
        foreach (var c in Path.GetInvalidFileNameChars()) seg = seg.Replace(c, '_');
        return Path.Combine(paths.ZonesDir, seg + ".json");
    }

    private IReadOnlyList<GeoPolygon>? TryLoadFromDisk(string url)
    {
        try
        {
            var file = FileFor(url);
            if (!File.Exists(file)) return null;
            if (clock.UtcNow - File.GetLastWriteTimeUtc(file) > MaxAge) return null;
            var dto = JsonSerializer.Deserialize<List<List<List<double[]>>>>(File.ReadAllText(file));
            if (dto is null) return null;
            return [.. dto.Select(poly => new GeoPolygon([.. poly.Select(ring =>
                (IReadOnlyList<GeoPosition>)[.. ring.Select(pt => new GeoPosition(pt[1], pt[0]))])]))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or IndexOutOfRangeException or ArgumentException)
        {
            log.Warn($"NWS: cached zone {url} unreadable; will download again", ex);
            return null;
        }
    }

    private void Save(string url, IReadOnlyList<GeoPolygon> polys)
    {
        try
        {
            Directory.CreateDirectory(paths.ZonesDir);
            var dto = polys.Select(p => p.Rings.Select(r => r.Select(pt => new[] { pt.LongitudeDeg, pt.LatitudeDeg }).ToList()).ToList()).ToList();
            var file = FileFor(url);
            File.WriteAllText(file, JsonSerializer.Serialize(dto));
            File.SetLastWriteTimeUtc(file, clock.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"NWS: could not cache zone {url} on disk", ex);
        }
    }
}
