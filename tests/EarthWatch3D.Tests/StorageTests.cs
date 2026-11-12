using System.IO;
using AstroCore.Earth;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ew3d-storage-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;

    public StorageTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private sealed class ListLog : IAppLog
    {
        public List<string> Warnings { get; } = [];
        public void Info(string m) { }
        public void Warn(string m, Exception? e = null) => Warnings.Add(m);
        public void Error(string m, Exception? e = null) => Warnings.Add(m);
    }

    [Fact]
    public void PathsAreUnderRoot()
    {
        Assert.Equal(Path.Combine(_root, "cache", "events.json"), _paths.EventsFile);
        Assert.True(Directory.Exists(_paths.CacheDir));
        Assert.True(Directory.Exists(_paths.ZonesDir));
        Assert.True(Directory.Exists(_paths.LogsDir));
    }

    [Fact]
    public void SettingsRoundTripAndDefaults()
    {
        var store = new SettingsStore(_paths, new NullLog());
        Assert.Equal(new AppSettings(), store.Load());

        var s = new AppSettings
        {
            ShowQuakes = false, ShowCoastlines = false, MinMagnitude = double.NaN, QuakeWindowDays = 7,
            AlertTypes = AlertTypes.Warnings | AlertTypes.Watches, NightStrength = 0.4, ShowTerminator = false,
            LayerStripExpanded = true,
        };
        store.Save(s);
        var back = store.Load();
        Assert.Equal(s.ShowQuakes, back.ShowQuakes);
        Assert.True(double.IsNaN(back.MinMagnitude));
        Assert.Equal(7, back.QuakeWindowDays);
        Assert.Equal(AlertTypes.Warnings | AlertTypes.Watches, back.AlertTypes);
        Assert.Equal(0.4, back.NightStrength);
        Assert.True(back.LayerStripExpanded);
        Assert.False(File.Exists(_paths.SettingsFile + ".tmp"));

        var f = back.ToFilter();
        Assert.Equal(double.NegativeInfinity, f.MinMagnitude);
        Assert.Equal(TimeSpan.FromDays(7), f.QuakeWindow);
        Assert.False(f.ShowQuakes);
    }

    [Fact]
    public void UnknownKeysIgnoredAndMissingKeysDefault()
    {
        File.WriteAllText(_paths.SettingsFile, "{\"ShowQuakes\":false,\"FutureThing\":42}");
        var s = new SettingsStore(_paths, new NullLog()).Load();
        Assert.False(s.ShowQuakes);
        Assert.Equal(2.5, s.MinMagnitude);
    }

    [Fact]
    public void CorruptSettingsGiveDefaults()
    {
        File.WriteAllText(_paths.SettingsFile, "{ not json");
        var log = new ListLog();
        Assert.Equal(new AppSettings(), new SettingsStore(_paths, log).Load());
        Assert.Single(log.Warnings);
    }

    private static DateTime U(int d, int h) => new(2026, 11, d, h, 0, 0, DateTimeKind.Utc);

    private static EventSnapshot Sample()
    {
        var p = new GeoPosition(10, 20);
        var q = new Earthquake("q1", new DateTime(2026, 11, 11, 1, 2, 3, DateTimeKind.Utc), p, "M5", new Uri("https://x.test/q"),
            5.1, 12, "somewhere", U(11, 2));
        var line = new GeoLine([new GeoPosition(1, 2), new GeoPosition(3, 4)]);
        var poly = new GeoPolygon([[new GeoPosition(0, 0), new GeoPosition(0, 5), new GeoPosition(5, 5), new GeoPosition(0, 0)],
            [new GeoPosition(1, 1), new GeoPosition(1, 2), new GeoPosition(2, 2), new GeoPosition(1, 1)]]);
        var c = new CycloneAdvisory(EventSource.Nhc, "nhc:al01", U(11, 3), p, "Storm", null,
            "ONE", 85, 975, [line], [line], [poly], [new WindArea(63, poly)]);
        var a = new WeatherAlert("a1", U(11, 4), p, "Flood", null, "Flood Watch", "Minor",
            U(11, 5), U(11, 9), null, [poly], ["TXZ001"], "desc");
        var e = new EonetStorm("e1", U(10, 0), p, "Eonet", null,
            [new StormFix(U(10, 0), p, 50), new StormFix(U(11, 0), p, null)], U(12, 0));
        return new EventSnapshot([q], [c], [a], [e], 7);
    }

    [Fact]
    public void EventCacheRoundTrip()
    {
        var cache = new EventCache(_paths, new NullLog());
        Assert.Null(cache.Load());
        var s = Sample();
        cache.Save(s);
        var back = cache.Load();
        Assert.NotNull(back);
        Assert.Equal(s.Earthquakes, back!.Earthquakes);
        Assert.Equal(s.Cyclones, back.Cyclones);
        Assert.Equal(s.Alerts, back.Alerts);
        Assert.Equal(s.Eonet, back.Eonet);
        Assert.Equal(DateTimeKind.Utc, back.Earthquakes[0].TimeUtc.Kind);
        Assert.False(File.Exists(_paths.EventsFile + ".tmp"));
    }

    [Fact]
    public void SaveWhileDestinationLockedLogsAndDoesNotThrow()
    {
        var log = new ListLog();
        var settings = new SettingsStore(_paths, log);
        var cache = new EventCache(_paths, log);
        settings.Save(new AppSettings());
        cache.Save(Sample());

        using (new FileStream(_paths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None))
        using (new FileStream(_paths.EventsFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(settings.Save(new AppSettings { ShowQuakes = false }));
            Assert.False(cache.Save(Sample()));
            Assert.Equal(2, log.Warnings.Count);
            Assert.False(File.Exists(_paths.SettingsFile + ".tmp"));
            Assert.False(File.Exists(_paths.EventsFile + ".tmp"));
        }

        Assert.True(settings.Save(new AppSettings { ShowQuakes = false }));
        Assert.True(cache.Save(Sample()));
        Assert.False(settings.Load().ShowQuakes);
    }

    [Fact]
    public void CorruptCacheIsDeletedAndIgnored()
    {
        File.WriteAllText(_paths.EventsFile, "{ truncated");
        var log = new ListLog();
        Assert.Null(new EventCache(_paths, log).Load());
        Assert.False(File.Exists(_paths.EventsFile));
        Assert.Single(log.Warnings);
    }
}
