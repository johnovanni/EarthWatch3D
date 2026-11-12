using System.IO;
using EarthWatch3D.App.Diagnostics;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Feeds.Eonet;
using EarthWatch3D.Core.Feeds.Gdacs;
using EarthWatch3D.Core.Feeds.Nhc;
using EarthWatch3D.Core.Feeds.Nws;
using EarthWatch3D.Core.Feeds.Usgs;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.Tests;

/// <summary>Runs every feed once through the --offline-samples handler, as the app does.</summary>
public class OfflineSamplesTests
{
    private static readonly DateTime SampleTime = new(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);
    private static string SamplesDir => Path.Combine(AppContext.BaseDirectory, "Samples");

    [Fact]
    public void EverySampleFileExists()
    {
        foreach (var f in OfflineSamples.Files) Assert.True(File.Exists(Path.Combine(SamplesDir, f)), f);
    }

    [Fact]
    public async Task EveryFeedRunsFromTheSamples()
    {
        var handler = OfflineSamples.CreateHandler(SamplesDir);
        using var http = new FeedHttp(handler);
        var clock = new TestClock(SampleTime);
        var log = new RecordingLog();
        var store = new EventStore(clock);
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "ew3d-offline-" + Guid.NewGuid().ToString("N")));
        try
        {
            paths.EnsureCreated();
            var zones = new ZoneCache(http, paths, clock, log);
            IEventFeed[] feeds =
            [
                new UsgsFeed(http, store, clock, log),
                new GdacsFeed(http, store, clock, log),
                new NhcFeed(http, store, log),
                new NwsFeed(http, store, zones, () => AlertTypes.Warnings, clock, log),
                new EonetFeed(http, store, log),
            ];
            foreach (var f in feeds) await f.RunOnceAsync(CancellationToken.None);   // throws if a request 404s
            await zones.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(20));

            var s = store.Snapshot;
            Assert.NotEmpty(s.Earthquakes);
            var nhc = Assert.Single(s.Cyclones, c => c.Id == "nhc:cp042026");
            Assert.NotEmpty(nhc.ForecastTrack);
            Assert.NotEmpty(nhc.Cone);
            var gdacs = Assert.Single(s.Cyclones, c => c.Id == "gdacs:" + OfflineSamples.GdacsSampleEventId);
            Assert.NotEmpty(gdacs.PastTrack);
            // Other GDACS storms keep their own position (empty geometry), not Pena's.
            Assert.All(s.Cyclones.Where(c => c.Source == EventSource.Gdacs && c.Id != gdacs.Id),
                c => { Assert.Empty(c.PastTrack); Assert.NotEqual(gdacs.Location, c.Location); });
            Assert.NotEmpty(s.Alerts);
            Assert.NotEmpty(s.Eonet);
            Assert.True(AnyZoneLoaded(s, zones), "no zone of an active warning was loaded");
            Assert.Contains(handler.Requests, r => r.StartsWith("https://api.weather.gov/zones/", StringComparison.Ordinal));
            Assert.Empty(log.Errors);
            Assert.DoesNotContain(log.Warnings, w => w.Contains("failed", StringComparison.OrdinalIgnoreCase));   // no 404s
        }
        finally
        {
            try { Directory.Delete(paths.Root, true); } catch (IOException) { }
        }
    }

    private static bool AnyZoneLoaded(EventSnapshot s, ZoneCache zones) =>
        s.Alerts.SelectMany(a => a.ZoneIds).Any(z => zones.TryGetCached(z, out var p) && p.Count > 0);
}
