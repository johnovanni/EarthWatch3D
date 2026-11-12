using System.Net.Http;
using System.IO;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Feeds.Nws;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.Tests;

public sealed class ZoneCacheTests
{
    private static string Sample(string f) => Path.Combine(AppContext.BaseDirectory, "Samples", f);
    private const string Pzz = "https://api.weather.gov/zones/forecast/PZZ150";

    private static AppPaths TempPaths()
    {
        var p = new AppPaths(Path.Combine(Path.GetTempPath(), "ew3d-zc-" + Guid.NewGuid().ToString("N")));
        p.EnsureCreated();
        return p;
    }

    [Fact]
    public async Task ZoneParsedAndCachedToDisk()
    {
        var handler = new SampleHttpHandler();
        handler.Map(Pzz, Sample("nws_zone.json"));
        var clock = new TestClock(new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc));
        var paths = TempPaths();
        using var http = new FeedHttp(handler);
        var zc = new ZoneCache(http, paths, clock, new RecordingLog());
        var loaded = 0;
        zc.ZonesLoaded += (_, _) => Interlocked.Increment(ref loaded);
        Assert.False(zc.TryGet(Pzz, out _));

        zc.Request([Pzz]);
        await zc.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(zc.TryGet(Pzz, out var areas));
        Assert.Single(areas);
        Assert.True(loaded >= 1);
        Assert.True(File.Exists(Path.Combine(paths.ZonesDir, "forecast_PZZ150.json")));

        var zc2 = new ZoneCache(http, paths, clock, new RecordingLog());
        Assert.True(zc2.TryGet(Pzz, out var again));
        Assert.Single(again);
        Assert.Single(handler.Requests);

        clock.UtcNow = clock.UtcNow.AddDays(31);
        var zc3 = new ZoneCache(http, paths, clock, new RecordingLog());
        Assert.False(zc3.TryGet(Pzz, out _));
        zc3.Request([Pzz]);
        await zc3.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(2, handler.Requests.Count);
        Assert.True(zc3.TryGet(Pzz, out _));
    }

    [Fact]
    public async Task TryGetCachedNeverTouchesDisk()
    {
        var handler = new SampleHttpHandler();
        handler.Map(Pzz, Sample("nws_zone.json"));
        var clock = new TestClock(new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc));
        var paths = TempPaths();
        using var http = new FeedHttp(handler);
        var zc = new ZoneCache(http, paths, clock, new RecordingLog());
        zc.Request([Pzz]);
        await zc.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));

        var fresh = new ZoneCache(http, paths, clock, new RecordingLog());   // the file is on disk, memory is empty
        Assert.False(fresh.TryGetCached(Pzz, out _));
        Assert.True(fresh.TryGet(Pzz, out _));
        Assert.True(fresh.TryGetCached(Pzz, out var areas));
        Assert.Single(areas);
    }

    [Fact]
    public async Task NegativeLookupIsCachedBriefly()
    {
        var handler = new SampleHttpHandler();
        handler.Map(Pzz, Sample("nws_zone.json"));
        var clock = new TestClock(new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc));
        var paths = TempPaths();
        using var http = new FeedHttp(handler);
        var writer = new ZoneCache(http, paths, clock, new RecordingLog());
        var reader = new ZoneCache(http, paths, clock, new RecordingLog());
        Assert.False(reader.TryGet(Pzz, out _));                 // miss, remembered

        writer.Request([Pzz]);                                    // the file appears on disk
        await writer.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(reader.TryGet(Pzz, out _));                 // still the remembered miss: no disk probe
        clock.UtcNow = clock.UtcNow.AddSeconds(31);
        Assert.True(reader.TryGet(Pzz, out _));                  // memory expired: found
    }

    [Fact]
    public async Task SameIdInDifferentZoneTypesIsCachedSeparately()
    {
        var handler = new SampleHttpHandler();
        handler.Map("https://api.weather.gov/zones/", Sample("nws_zone.json"));
        var clock = new TestClock(new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc));
        var paths = TempPaths();
        using var http = new FeedHttp(handler);
        var zc = new ZoneCache(http, paths, clock, new RecordingLog());
        zc.Request(["https://api.weather.gov/zones/forecast/X", "https://api.weather.gov/zones/county/X"]);
        await zc.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(File.Exists(Path.Combine(paths.ZonesDir, "forecast_X.json")));
        Assert.True(File.Exists(Path.Combine(paths.ZonesDir, "county_X.json")));
        Assert.Equal(2, handler.Requests.Count);
        var zc2 = new ZoneCache(http, paths, clock, new RecordingLog());
        Assert.True(zc2.TryGet("https://api.weather.gov/zones/county/X", out _));
        Assert.True(zc2.TryGet("https://api.weather.gov/zones/forecast/X", out _));
    }

    /// <summary>Records each warning and whether it carried an exception (and so a stack trace).</summary>
    private sealed class TraceLog : EarthWatch3D.Core.Logging.IAppLog
    {
        private readonly object _gate = new();
        public List<(string Message, bool HasException)> Warnings { get; } = [];
        public void Info(string m) { }
        public void Warn(string m, Exception? e = null) { lock (_gate) Warnings.Add((m, e is not null)); }
        public void Error(string m, Exception? e = null) { lock (_gate) Warnings.Add((m, e is not null)); }
    }

    [Fact]
    public async Task FailedDownloadBacksOffAndLogsTheTraceOnce()
    {
        var handler = new SampleHttpHandler();   // nothing mapped: every zone is a 404
        var clock = new TestClock(new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc));
        var log = new TraceLog();
        using var http = new FeedHttp(handler);
        var zc = new ZoneCache(http, TempPaths(), clock, log);

        async Task RequestAt(TimeSpan sinceStart)
        {
            clock.UtcNow = new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc) + sinceStart;
            zc.Request([Pzz]);
            await zc.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }

        await RequestAt(TimeSpan.Zero);
        Assert.Single(handler.Requests);
        Assert.True(Assert.Single(log.Warnings).HasException);   // the first failure logs the stack trace

        await RequestAt(TimeSpan.FromMinutes(9));                 // within the 10 min backoff: not retried
        Assert.Single(handler.Requests);

        await RequestAt(TimeSpan.FromMinutes(10));                // retried; fails again: one line, no trace
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(2, log.Warnings.Count);
        Assert.False(log.Warnings[1].HasException);

        await RequestAt(TimeSpan.FromMinutes(29));                // the backoff doubled to 20 min (due at 30)
        Assert.Equal(2, handler.Requests.Count);
        await RequestAt(TimeSpan.FromMinutes(30));
        Assert.Equal(3, handler.Requests.Count);

        // 40, 80, 160, 320 min, then capped at 6 h.
        var due = TimeSpan.FromMinutes(30);
        foreach (var backoff in new[] { 40, 80, 160, 320, 360, 360 })
        {
            due += TimeSpan.FromMinutes(backoff);
            int before = handler.Requests.Count;
            await RequestAt(due - TimeSpan.FromSeconds(1));
            Assert.Equal(before, handler.Requests.Count);
            await RequestAt(due);
            Assert.Equal(before + 1, handler.Requests.Count);
        }
        Assert.Equal(1, log.Warnings.Count(w => w.HasException));
        Assert.False(zc.TryGet(Pzz, out _));
    }

    [Fact]
    public async Task ZoneWithoutGeometryIsAFailureToo()
    {
        var handler = new SampleHttpHandler();
        handler.MapText(Pzz, """{"id":"x","geometry":null}""");
        var clock = new TestClock(new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc));
        using var http = new FeedHttp(handler);
        var zc = new ZoneCache(http, TempPaths(), clock, new RecordingLog());
        zc.Request([Pzz]);
        await zc.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
        zc.Request([Pzz]);
        await zc.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(handler.Requests);

        handler.Map(Pzz, Sample("nws_zone.json"));   // fixed upstream: loads once the backoff has passed
        clock.UtcNow = clock.UtcNow.AddMinutes(10);
        zc.Request([Pzz]);
        await zc.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(zc.TryGet(Pzz, out _));
    }

    private sealed class DelayHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private int _now, _peak;
        public int Peak => _peak;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var n = Interlocked.Increment(ref _now);
            int p;
            while (n > (p = Volatile.Read(ref _peak))) Interlocked.CompareExchange(ref _peak, n, p);
            try
            {
                await Task.Delay(50, ct);
                return await base.SendAsync(request, ct);
            }
            finally { Interlocked.Decrement(ref _now); }
        }
    }

    [Fact]
    public async Task SameZoneRequestedOnceAndConcurrencyCapped()
    {
        var inner = new SampleHttpHandler();
        inner.Map("https://api.weather.gov/zones/forecast/", Sample("nws_zone.json"));
        var delay = new DelayHandler(inner);
        var clock = new TestClock(new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc));
        using var http = new FeedHttp(delay);
        var zc = new ZoneCache(http, TempPaths(), clock, new RecordingLog());
        var urls = Enumerable.Range(0, 50).Select(i => "https://api.weather.gov/zones/forecast/Z" + (i % 10)).ToList();
        zc.Request(urls);
        zc.Request(urls);
        await zc.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
        zc.Request(urls);
        await zc.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(10, inner.Requests.Count);
        Assert.InRange(zc.InFlightPeak, 1, 4);
        Assert.InRange(delay.Peak, 1, 4);
    }

    [Fact]
    public async Task LoadInBackgroundReadsTheDiskAndRaisesZonesLoaded()
    {
        var handler = new SampleHttpHandler();
        handler.Map(Pzz, Sample("nws_zone.json"));
        var clock = new TestClock(new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc));
        var paths = TempPaths();
        using var http = new FeedHttp(handler);
        var writer = new ZoneCache(http, paths, clock, new RecordingLog());
        writer.Request([Pzz]);
        await writer.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));

        var zc = new ZoneCache(http, paths, clock, new RecordingLog());   // on disk, not in memory
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        zc.ZonesLoaded += (_, _) => loaded.TrySetResult();
        zc.LoadInBackground([Pzz]);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(zc.TryGetCached(Pzz, out var areas));
        Assert.Single(areas);
        Assert.Single(handler.Requests);   // only the writer's download
    }

    [Fact]
    public async Task LoadInBackgroundNeverDownloads()
    {
        var handler = new SampleHttpHandler();
        handler.Map(Pzz, Sample("nws_zone.json"));
        var clock = new TestClock(new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc));
        using var http = new FeedHttp(handler);
        var zc = new ZoneCache(http, TempPaths(), clock, new RecordingLog());
        int raised = 0;
        zc.ZonesLoaded += (_, _) => Interlocked.Increment(ref raised);
        zc.LoadInBackground([Pzz]);
        await zc.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Empty(handler.Requests);
        Assert.False(zc.TryGetCached(Pzz, out _));
        Assert.Equal(0, raised);
    }
}
