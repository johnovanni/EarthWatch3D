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
/// Disk file: ZonesDir/&lt;last URL segment&gt;.json.
/// </summary>
public sealed class ZoneCache(FeedHttp http, AppPaths paths, IClock clock, IAppLog log, int maxConcurrent = 4)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private readonly ConcurrentDictionary<string, IReadOnlyList<GeoPolygon>> _mem = new(StringComparer.Ordinal);
    private readonly HashSet<string> _queued = new(StringComparer.Ordinal);
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

    /// <summary>Completes when no downloads are queued or in flight.</summary>
    public Task WhenIdleAsync() { lock (_gate) return _idle.Task; }

    public bool TryGet(string zoneUrl, out IReadOnlyList<GeoPolygon> areas)
    {
        if (_mem.TryGetValue(zoneUrl, out var m)) { areas = m; return true; }
        var loaded = TryLoadFromDisk(zoneUrl);
        if (loaded is not null) { _mem[zoneUrl] = loaded; areas = loaded; return true; }
        areas = [];
        return false;
    }

    /// <summary>Queues the distinct URLs that are neither cached nor already queued. Never blocks.</summary>
    public void Request(IEnumerable<string> zoneUrls)
    {
        var start = new List<string>();
        foreach (var url in zoneUrls.Distinct(StringComparer.Ordinal))
        {
            if (TryGet(url, out _)) continue;
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
                    Save(url, polys);
                    ZonesLoaded?.Invoke(this, EventArgs.Empty);
                }
                finally { Interlocked.Decrement(ref _inFlight); }
            }
            finally { _slots.Release(); }
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or JsonException or InvalidOperationException
                                       or FormatException or ArgumentException or KeyNotFoundException or IOException or UnauthorizedAccessException)
        {
            log.Warn($"NWS: zone {url} failed; will retry when requested again", ex);
        }
        finally
        {
            TaskCompletionSource? done = null;
            lock (_gate)
            {
                _queued.Remove(url);
                if (--_pending == 0) done = _idle;
            }
            done?.TrySetResult();
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
        var seg = url.TrimEnd('/');
        seg = seg[(seg.LastIndexOf('/') + 1)..];
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
