using AstroCore.Earth;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Tests;

public class EventStoreTests
{
    private sealed class FixedClock(DateTime t) : IClock { public DateTime UtcNow { get; set; } = t; }

    private static readonly DateTime T = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly GeoPosition Here = new(10, 20);

    private static Earthquake Quake(string id, DateTime time, double mag = 4, DateTime? updated = null) =>
        new(id, time, Here, "q " + id, null, mag, 10, "somewhere", updated ?? time);

    private static CycloneAdvisory Advisory(string id, DateTime time) =>
        new(EventSource.Nhc, id, time, Here, "t", null, "N", 70, 990, [], [], [], []);

    private static WeatherAlert Alert(string id, DateTime ends, DateTime? cancelled = null) =>
        new(id, ends.AddHours(-3), Here, "a", null, "Flood Watch", "Minor", ends.AddHours(-2), ends, cancelled, [], [], "d");

    private static EonetStorm Eonet(string id, DateTime lastFix, DateTime? closed = null) =>
        new(id, lastFix.AddDays(-1), Here, "e", null,
            [new StormFix(lastFix.AddDays(-1), Here, 50), new StormFix(lastFix, Here, 60)], closed);

    [Fact]
    public void UpsertReplacesRevisedQuake()
    {
        var store = new EventStore(new FixedClock(T));
        store.UpsertEarthquakes([Quake("a", T.AddHours(-1), 4, T.AddMinutes(-30))]);
        store.UpsertEarthquakes([Quake("a", T.AddHours(-1), 5, T.AddMinutes(-10))]);
        Assert.Equal(5, Assert.Single(store.Snapshot.Earthquakes).Magnitude);
        store.UpsertEarthquakes([Quake("a", T.AddHours(-1), 3, T.AddMinutes(-20))]);
        Assert.Equal(5, Assert.Single(store.Snapshot.Earthquakes).Magnitude);
    }

    [Fact]
    public void WeekRefreshRemovesDeletedQuakes()
    {
        var store = new EventStore(new FixedClock(T));
        var weekStart = T.AddDays(-7);
        store.UpsertEarthquakes([Quake("old", weekStart.AddHours(-1)), Quake("gone", weekStart.AddHours(5)), Quake("keep", weekStart.AddHours(6))]);
        store.ReplaceEarthquakeWeek([Quake("keep", weekStart.AddHours(6)), Quake("new", weekStart.AddHours(7))], weekStart);
        Assert.Equal(["keep", "new", "old"], store.Snapshot.Earthquakes.Select(q => q.Id).Order().ToArray());
    }

    [Fact]
    public void AdvisoriesKeyedByStormAndTime()
    {
        var store = new EventStore(new FixedClock(T));
        store.AddCycloneAdvisories([Advisory("nhc:1", T.AddHours(-6)), Advisory("nhc:1", T.AddHours(-3))]);
        Assert.Equal(2, store.Snapshot.Cyclones.Count);
        store.AddCycloneAdvisories([Advisory("nhc:1", T.AddHours(-3)) with { Name = "Replaced" }]);
        Assert.Equal(2, store.Snapshot.Cyclones.Count);
        Assert.Contains(store.Snapshot.Cyclones, c => c.Name == "Replaced");
    }

    [Fact]
    public void PruneAppliesRetention()
    {
        var store = new EventStore(new FixedClock(T));
        store.UpsertEarthquakes([Quake("oldq", T.AddDays(-8)), Quake("edgeq", T.AddDays(-7)), Quake("newq", T.AddDays(-6))]);
        store.AddCycloneAdvisories([Advisory("oldc", T.AddHours(-25)), Advisory("edgec", T.AddHours(-24)), Advisory("newc", T.AddHours(-23))]);
        store.UpsertAlerts([Alert("olda", T.AddHours(-25)), Alert("edgea", T.AddHours(-24)), Alert("livea", T.AddHours(5)),
            Alert("cancelled", T.AddHours(5), T.AddHours(-25))]);
        store.UpsertEonet([Eonet("olde", T.AddDays(-8)), Eonet("edgee", T.AddDays(-7)), Eonet("newe", T.AddDays(-1)),
            Eonet("closed", T.AddDays(-1), T.AddDays(-8))]);

        store.Prune();

        Assert.Equal(["edgeq", "newq"], store.Snapshot.Earthquakes.Select(q => q.Id).Order().ToArray());
        Assert.Equal(["edgec", "newc"], store.Snapshot.Cyclones.Select(q => q.Id).Order().ToArray());
        Assert.Equal(["edgea", "livea"], store.Snapshot.Alerts.Select(q => q.Id).Order().ToArray());
        Assert.Equal(["edgee", "newe"], store.Snapshot.Eonet.Select(q => q.Id).Order().ToArray());
    }

    [Fact]
    public void ChangedRaisedAndVersionIncrements()
    {
        var store = new EventStore(new FixedClock(T));
        var raised = 0;
        long versionSeen = -1;
        store.Changed += (_, _) => { raised++; versionSeen = store.Snapshot.Version; };
        var v0 = store.Snapshot.Version;
        store.UpsertEarthquakes([Quake("a", T)]);
        Assert.Equal(1, raised);
        Assert.Equal(v0 + 1, store.Snapshot.Version);
        Assert.Equal(store.Snapshot.Version, versionSeen);
        store.UpsertEarthquakes([Quake("a", T)]); // identical: no change
        Assert.Equal(1, raised);
        store.Prune(); // nothing to remove
        Assert.Equal(1, raised);
    }

    private static GeoPolygon Poly() => new([new List<GeoPosition> { new(0, 0), new(0, 5), new(5, 5), new(0, 0) }]);
    private static GeoLine Line() => new([new GeoPosition(1, 1), new GeoPosition(2, 2)]);

    private static WeatherAlert FullAlert() => Alert("x", T.AddHours(5)) with
    {
        Areas = new List<GeoPolygon> { Poly() }, ZoneIds = new List<string> { "TXZ001", "TXZ002" }
    };

    private static EonetStorm FullStorm(double wind = 60) => Eonet("s", T.AddDays(-1)) with
    {
        Fixes = new List<StormFix> { new(T.AddDays(-2), Here, 50), new(T.AddDays(-1), Here, wind) }
    };

    private static CycloneAdvisory FullAdvisory() => Advisory("nhc:9", T.AddHours(-1)) with
    {
        PastTrack = new List<GeoLine> { Line() }, ForecastTrack = new List<GeoLine> { Line() },
        Cone = new List<GeoPolygon> { Poly() }, WindAreas = new List<WindArea> { new(63, Poly()) }
    };

    private static (EventStore Store, Func<int> Raised) Counting()
    {
        var store = new EventStore(new FixedClock(T));
        var n = 0;
        store.Changed += (_, _) => n++;
        return (store, () => n);
    }

    [Fact]
    public void ReUpsertIdenticalAlertIsNoChange()
    {
        var (store, raised) = Counting();
        store.UpsertAlerts([FullAlert()]);
        var v = store.Snapshot.Version;
        store.UpsertAlerts([FullAlert()]);
        Assert.Equal(1, raised());
        Assert.Equal(v, store.Snapshot.Version);
    }

    [Fact]
    public void ReUpsertIdenticalEonetIsNoChange()
    {
        var (store, raised) = Counting();
        store.UpsertEonet([FullStorm()]);
        var v = store.Snapshot.Version;
        store.UpsertEonet([FullStorm()]);
        Assert.Equal(1, raised());
        Assert.Equal(v, store.Snapshot.Version);
    }

    [Fact]
    public void ReAddIdenticalAdvisoryIsNoChange()
    {
        var (store, raised) = Counting();
        store.AddCycloneAdvisories([FullAdvisory()]);
        var v = store.Snapshot.Version;
        store.AddCycloneAdvisories([FullAdvisory()]);
        Assert.Equal(1, raised());
        Assert.Equal(v, store.Snapshot.Version);
    }

    [Fact]
    public void ChangedFixInEonetIsAChange()
    {
        var (store, raised) = Counting();
        store.UpsertEonet([FullStorm()]);
        store.UpsertEonet([FullStorm(wind: 75)]);
        Assert.Equal(2, raised());
        Assert.Equal(75, store.Snapshot.Eonet[0].Fixes[1].WindKt);
    }

    [Fact]
    public void ConcurrentWritersDoNotLoseUpdates()
    {
        var store = new EventStore(new FixedClock(T));
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, t =>
        {
            for (var i = 0; i < 1000; i++) store.UpsertEarthquakes([Quake($"{t}-{i}", T)]);
        });
        Assert.Equal(8000, store.Snapshot.Earthquakes.Count);
    }
}
