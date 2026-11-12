using System.IO;
using AstroCore.Earth;
using EarthWatch3D.App.Diagnostics;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Feeds.Eonet;
using EarthWatch3D.Core.Feeds.Gdacs;
using EarthWatch3D.Core.Feeds.Gibs;
using EarthWatch3D.Core.Feeds.Nhc;
using EarthWatch3D.Core.Feeds.Nws;
using EarthWatch3D.Core.Feeds.Usgs;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Lightning;
using EarthWatch3D.Core.Lightning.Glm;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.Tests;

/// <summary>Runs every feed once through the --offline-samples handler, as the app does.</summary>
public class OfflineSamplesTests
{
    private static readonly DateTime SampleTime = new(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);
    private static string SamplesDir => Path.Combine(AppContext.BaseDirectory, "Samples");

    [Fact]
    public void CloudsAreWaitedForWhenSavedOnOrNamedInLayers()
    {
        Assert.False(OfflineSamples.CloudsWanted(false, null));
        Assert.False(OfflineSamples.CloudsWanted(false, ["plates"]));
        Assert.True(OfflineSamples.CloudsWanted(false, ["plates", "Clouds"]));
        Assert.True(OfflineSamples.CloudsWanted(true, null));
    }

    [Fact]
    public void LightningIsWaitedForWhenSavedOnOrNamedInLayers()
    {
        Assert.False(OfflineSamples.LightningWanted(false, null));
        Assert.False(OfflineSamples.LightningWanted(false, ["clouds"]));
        Assert.True(OfflineSamples.LightningWanted(false, ["Lightning"]));
        Assert.True(OfflineSamples.LightningWanted(true, null));
    }

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
                new GibsCloudsFeed(http, paths, clock, () => true, log),
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

    [Fact]
    public async Task OfflineRunHasLightningAndClouds()
    {
        var handler = OfflineSamples.CreateHandler(SamplesDir);
        using var http = new FeedHttp(handler);
        var clock = new TestClock(SampleTime);
        var log = new RecordingLog();
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "ew3d-offline-" + Guid.NewGuid().ToString("N")));
        try
        {
            paths.EnsureCreated();
            var view = new ViewState { CameraSubPoint = new GeoPosition(0, -75) };
            var buffer = new LightningBuffer();
            var east = new GlmFeed(http, buffer, LightningSensor.GoesEast, clock, view, () => TimeSpan.FromMinutes(15), () => false, () => false, () => true, log);
            Assert.Equal(FeedRunResult.Ok, await east.RunOnceAsync(CancellationToken.None));
            // The 15 min window (19:45-20:00) holds up to 45 of the listing's files, and every one is answered with the
            // same sample .nc (757 flashes). The buffer keeps one flash per sensor, millisecond and 1e-4 degree position,
            // so the repeated files add nothing, and the feed drops flashes more than an hour old (the sample file is from
            // the start of 19 h). What is left is the sample's flashes from 19:00 on, however many files were read.
            List<LightningFlash> sampleFlashes;
            using (var sample = File.OpenRead(Path.Combine(SamplesDir, "glm_g19_sample.nc")))
                sampleFlashes = GlmReader.Read(sample, LightningSensor.GoesEast).ToList();
            Assert.Equal(757, sampleFlashes.Count);
            int expected = sampleFlashes.Count(f => f.TimeUtc >= SampleTime - TimeSpan.FromMinutes(60));
            Assert.InRange(expected, 1, 757);
            Assert.Equal(expected, buffer.Count);
            Assert.Contains(handler.Requests, r => r.StartsWith("https://noaa-goes19.s3.amazonaws.com/GLM-L2-LCFA/", StringComparison.Ordinal));

            // The West bucket answers too: the same listing, with the G18 sample for its files.
            view.CameraSubPoint = new GeoPosition(0, -137);
            var westBuffer = new LightningBuffer();
            var west = new GlmFeed(http, westBuffer, LightningSensor.GoesWest, clock, view, () => TimeSpan.FromMinutes(15), () => false, () => false, () => true, log);
            Assert.Equal(FeedRunResult.Ok, await west.RunOnceAsync(CancellationToken.None));
            Assert.True(westBuffer.Count > 0);
            Assert.Contains(handler.Requests, r => r.StartsWith("https://noaa-goes18.s3.amazonaws.com/GLM-L2-LCFA/", StringComparison.Ordinal));

            CloudImage? ready = null;
            var clouds = new GibsCloudsFeed(http, paths, clock, () => true, log);
            clouds.ImageReady += (_, image) => ready = image;
            Assert.Equal(FeedRunResult.Ok, await clouds.RunOnceAsync(CancellationToken.None));
            Assert.NotNull(ready);
            Assert.Empty(log.Errors);
            Assert.DoesNotContain(log.Warnings, w => w.Contains("skipped", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(paths.Root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void StaleOfflineFoldersAreSwept()
    {
        var temp = Path.Combine(Path.GetTempPath(), "ew3d-sweep-" + Guid.NewGuid().ToString("N"));
        var now = new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            string Make(string name, TimeSpan age)
            {
                var d = Directory.CreateDirectory(Path.Combine(temp, name)).FullName;
                File.WriteAllText(Path.Combine(d, "settings.json"), "{}");
                Directory.SetLastWriteTimeUtc(d, now - age);
                return d;
            }
            var stale = Make("EarthWatch3D-offline-aaa", TimeSpan.FromDays(2));
            var fresh = Make("EarthWatch3D-offline-bbb", TimeSpan.FromHours(3));   // maybe another instance running
            var other = Make("SomethingElse-offline-ccc", TimeSpan.FromDays(9));
            var log = new RecordingLog();

            Assert.Equal(1, OfflineSamples.SweepStaleFolders(temp, now, log));

            Assert.False(Directory.Exists(stale));
            Assert.True(Directory.Exists(fresh));
            Assert.True(Directory.Exists(other));
            Assert.Equal(0, OfflineSamples.SweepStaleFolders(Path.Combine(temp, "missing"), now, log));   // never throws
        }
        finally { try { Directory.Delete(temp, true); } catch (IOException) { } }
    }
}
