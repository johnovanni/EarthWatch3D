using AstroCore.Earth;
using EarthWatch3D.Core.Events;

namespace EarthWatch3D.Tests;

public class EventVisibilityTests
{
    private static readonly DateTime T = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly GeoPosition Here = new(10, 20);

    private static EventSnapshot Snap(Earthquake[]? q = null, CycloneAdvisory[]? c = null, WeatherAlert[]? a = null, EonetStorm[]? e = null) =>
        new(q ?? [], c ?? [], a ?? [], e ?? [], 1);

    private static Earthquake Quake(string id, DateTime time, double mag) =>
        new(id, time, Here, id, null, mag, 10, "p", time);

    private static CycloneAdvisory Adv(string id, DateTime time, GeoPosition? pos = null, EventSource src = EventSource.Nhc, string name = "Pena") =>
        new(src, id, time, pos ?? Here, name, null, name, 70, 990, [], [], [], []);

    private static WeatherAlert Alert(string type, DateTime onset, DateTime ends, DateTime? cancelled = null) =>
        new("a1", onset, Here, "a", null, type, "Minor", onset, ends, cancelled, [], [], "d");

    private static EonetStorm Eonet(GeoPosition pos, DateTime fixTime, DateTime? closed = null) =>
        new("e1", fixTime, pos, "e", null, [new StormFix(fixTime, pos, 50)], closed);

    [Fact]
    public void QuakeWindowAndMagnitudeFilter()
    {
        var s = Snap(q:
        [
            Quake("half", T.AddHours(-12), 4),
            Quake("old", T.AddHours(-25), 4),
            Quake("future", T.AddMinutes(1), 4),
            Quake("small", T.AddHours(-1), 2.0),
            Quake("now", T, 3),
        ]);
        var v = EventVisibility.At(s, T, DisplayFilter.Default);
        Assert.Equal(["half", "now"], v.Quakes.Select(x => x.Quake.Id).OrderBy(x => x).ToArray());
        Assert.Equal(0.5, v.Quakes.Single(x => x.Quake.Id == "half").AgeFraction, 9);
        Assert.Equal(0, v.Quakes.Single(x => x.Quake.Id == "now").AgeFraction, 9);
        var all = EventVisibility.At(s, T, DisplayFilter.Default with { MinMagnitude = double.NegativeInfinity });
        Assert.Contains(all.Quakes, x => x.Quake.Id == "small");
    }

    [Fact]
    public void LatestAdvisoryAtTimeIsUsed()
    {
        var s = Snap(c: [Adv("nhc:1", T.AddHours(-12)), Adv("nhc:1", T.AddHours(-6))]);
        Assert.Equal(T.AddHours(-6), Assert.Single(EventVisibility.At(s, T, DisplayFilter.Default).Cyclones).TimeUtc);
        Assert.Empty(EventVisibility.At(s, T.AddHours(-13), DisplayFilter.Default).Cyclones);
        Assert.Equal(T.AddHours(-12), Assert.Single(EventVisibility.At(s, T.AddHours(-8), DisplayFilter.Default).Cyclones).TimeUtc);
    }

    [Fact]
    public void StormHiddenAfter24hWithoutAdvisory()
    {
        var s = Snap(c: [Adv("nhc:1", T.AddHours(-12))]);
        Assert.Single(EventVisibility.At(s, T.AddHours(12), DisplayFilter.Default).Cyclones);
        Assert.Empty(EventVisibility.At(s, T.AddHours(12).AddMinutes(1), DisplayFilter.Default).Cyclones);
    }

    [Fact]
    public void AlertActiveWindowAndTypeFilter()
    {
        var watch = Snap(a: [Alert("Flood Watch", T.AddHours(-1), T.AddHours(3))]);
        Assert.Empty(EventVisibility.At(watch, T, DisplayFilter.Default).Alerts);
        Assert.Single(EventVisibility.At(watch, T, DisplayFilter.Default with { AlertTypes = AlertTypes.Watches }).Alerts);

        var warn = Snap(a: [Alert("Tornado Warning", T.AddHours(-1), T.AddHours(3), T.AddHours(1))]);
        Assert.Single(EventVisibility.At(warn, T, DisplayFilter.Default).Alerts);
        Assert.Empty(EventVisibility.At(warn, T.AddHours(1), DisplayFilter.Default).Alerts);
        Assert.Empty(EventVisibility.At(warn, T.AddHours(-2), DisplayFilter.Default).Alerts);
    }

    [Fact]
    public void EonetHiddenNearMergedCyclone()
    {
        var near = new GeoPosition(10 + 100 / 111.19, 20);
        var s = Snap(c: [Adv("nhc:1", T.AddHours(-1))], e: [Eonet(near, T.AddHours(-2))]);
        Assert.Empty(EventVisibility.At(s, T, DisplayFilter.Default).Eonet);
    }

    [Fact]
    public void EonetShownWhenAlone()
    {
        var far = new GeoPosition(10 + 1000 / 111.19, 20);
        var s = Snap(c: [Adv("nhc:1", T.AddHours(-1))], e: [Eonet(far, T.AddHours(-2))]);
        var ve = Assert.Single(EventVisibility.At(s, T, DisplayFilter.Default).Eonet);
        Assert.Equal(far, ve.Fix.Position);
        Assert.Empty(EventVisibility.At(s, T.AddHours(-3), DisplayFilter.Default).Eonet);
        var closed = Snap(e: [Eonet(far, T.AddHours(-2), T.AddHours(-1))]);
        Assert.Empty(EventVisibility.At(closed, T, DisplayFilter.Default).Eonet);
    }

    [Fact]
    public void NothingVisibleBeforeAllEvents()
    {
        var s = Snap(
            q: [Quake("q", T.AddHours(-3), 5)],
            c: [Adv("nhc:1", T.AddHours(-2))],
            a: [Alert("Tornado Warning", T.AddHours(-1), T.AddHours(2))],
            e: [Eonet(new GeoPosition(-40, 100), T.AddHours(-4))]);
        var v = EventVisibility.At(s, T.AddHours(-5), DisplayFilter.Default);
        Assert.Empty(v.Quakes); Assert.Empty(v.Cyclones); Assert.Empty(v.Alerts); Assert.Empty(v.Eonet);
    }

    [Fact]
    public void ShowFlagsHideCategories()
    {
        var s = Snap(q: [Quake("q", T, 5)], c: [Adv("nhc:1", T)]);
        var f = DisplayFilter.Default with { ShowQuakes = false, ShowCyclones = false };
        var v = EventVisibility.At(s, T, f);
        Assert.Empty(v.Quakes); Assert.Empty(v.Cyclones);
    }
}
