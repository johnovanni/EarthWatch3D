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
        Assert.True(File.Exists(Path.Combine(paths.ZonesDir, "PZZ150.json")));

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
    public async Task FailedDownloadIsLoggedAndCanBeRequestedAgain()
    {
        var handler = new SampleHttpHandler();
        var clock = new TestClock(new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc));
        var log = new RecordingLog();
        using var http = new FeedHttp(handler);
        var zc = new ZoneCache(http, TempPaths(), clock, log);
        zc.Request([Pzz]);
        await zc.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(log.Warnings);
        Assert.False(zc.TryGet(Pzz, out _));
        zc.Request([Pzz]);
        await zc.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(2, handler.Requests.Count);
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
}
