using AstroCore.Earth;
using EarthWatch3D.Core.Events;
using GlobeCore.Earth;
using GlobeCore.Geo;

namespace EarthWatch3D.Tests;

public sealed class CycloneReplayTests
{
    private static DateTime H(double hours) => new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc).AddHours(hours);

    private static CycloneAdvisory Adv(double hour, GeoPosition pos, double? wind = 50, EventSource src = EventSource.Nhc,
        params StormFix[] history) =>
        new(src, "nhc:1", H(hour), pos, "Tropical Storm Pena", null, "Pena", wind, 1000, [], [new GeoLine([pos, new GeoPosition(pos.LatitudeDeg + 1, pos.LongitudeDeg)])],
            [new GeoPolygon([[pos, new GeoPosition(1, 1), new GeoPosition(2, 2)]])], [])
        { History = history };

    private static StormFix Fix(double hour, GeoPosition pos, double? wind = null) => new(H(hour), pos, wind);

    [Fact]
    public void NewestAdvisoryIsUnchanged()
    {
        var a = Adv(0, new(10, -150));
        var b = Adv(6, new(10, -152));
        var r = CycloneReplay.At([a, b], H(7));
        Assert.Same(b, r);
        Assert.Null(r!.Replay);
        Assert.Same(b, CycloneReplay.At([a, b], H(6)));
    }

    [Fact]
    public void InterpolatesBetweenFixes()
    {
        var r = CycloneReplay.At([Adv(0, new(10, -150)), Adv(6, new(10, -152))], H(3))!;
        var u = (new GeoPosition(10, -150).ToUnitVector() + new GeoPosition(10, -152).ToUnitVector()).Normalized();
        var mid = GeoPosition.FromVector(u);
        Assert.InRange(Math.Abs(r.Location.LatitudeDeg - mid.LatitudeDeg), 0, 0.01);
        Assert.InRange(Math.Abs(r.Location.LongitudeDeg - mid.LongitudeDeg), 0, 0.01);
        Assert.Equal(H(0), r.TimeUtc);
        Assert.Equal(H(0), r.Replay!.EarlierFixUtc);
        Assert.Equal(H(6), r.Replay.LaterFixUtc);
        Assert.Equal("NHC advisories", r.Replay.HistorySource);
    }

    [Fact]
    public void InterpolatesAcrossTheDateLine()
    {
        var r = CycloneReplay.At([Adv(0, new(10, 179)), Adv(6, new(10, -179))], H(3))!;
        Assert.Equal(180, Math.Abs(r.Location.LongitudeDeg), 2);
    }

    [Fact]
    public void HiddenBeforeEarliestFix() =>
        Assert.Null(CycloneReplay.At([Adv(6, new(10, -150)), Adv(12, new(10, -152))], H(5)));

    [Fact]
    public void HiddenAfter24hGap()
    {
        var storm = new[] { Adv(0, new(10, -150)), Adv(40, new(10, -160)) };
        Assert.NotNull(CycloneReplay.At(storm, H(24)));
        Assert.Null(CycloneReplay.At(storm, H(24.1)));
    }

    [Fact]
    public void WindInterpolatedWhenBothKnown()
    {
        var r = CycloneReplay.At([Adv(0, new(10, -150), 40), Adv(6, new(10, -152), 60)], H(3))!;
        Assert.Equal(50, r.WindKt!.Value, 6);
        Assert.True(r.Replay!.IntensityKnown);
    }

    [Fact]
    public void UnknownWindIsGrey()
    {
        var first = Adv(12, new(11, -155), 45, EventSource.Gdacs, Fix(0, new(10, -150)), Fix(6, new(10.5, -152)));
        var r = CycloneReplay.At([first], H(3))!;
        Assert.False(r.Replay!.IntensityKnown);
        Assert.Null(r.WindKt);
        Assert.Equal("GDACS track", r.Replay.HistorySource);
        Assert.Equal(H(6), r.Replay.LaterFixUtc);
    }

    [Fact]
    public void NoForecastBeforeFirstAdvisory()
    {
        var first = Adv(12, new(11, -155), 45, EventSource.Gdacs, Fix(0, new(10, -150)), Fix(6, new(10.5, -152)));
        var r = CycloneReplay.At([first], H(3))!;
        Assert.Empty(r.ForecastTrack);
        Assert.Empty(r.Cone);
        Assert.Empty(r.WindAreas);
    }

    [Fact]
    public void ForecastComesFromNewestAdvisoryAtOrBeforeT()
    {
        var a = Adv(0, new(10, -150));
        var r = CycloneReplay.At([a, Adv(6, new(10, -152))], H(3))!;
        Assert.Equal(a.ForecastTrack, r.ForecastTrack);
        Assert.Equal(a.Cone, r.Cone);
    }

    [Fact]
    public void DuplicateFixTimesCollapse()
    {
        // The older advisory's history says (10,-151) at 03:00; the newer advisory's own fix at 03:00 wins.
        var older = Adv(0, new(10, -150), 40, EventSource.Nhc, Fix(3, new(10, -151), 40));
        var newer = Adv(3, new(10, -152), 50, EventSource.Nhc);
        var r = CycloneReplay.At([older, newer, Adv(9, new(10, -156), 60)], H(3))!;
        Assert.Equal(-152, r.Location.LongitudeDeg, 6);
        Assert.Equal(50, r.WindKt!.Value, 6);
        Assert.Equal(H(3), r.Replay!.EarlierFixUtc);
    }

    [Fact]
    public void PastTrackRunsThroughFixesUpToThePosition()
    {
        var a = Adv(0, new(10, -150), 40, EventSource.Nhc, Fix(-6, new(10, -148), 35));
        var r = CycloneReplay.At([a, Adv(6, new(10, -152), 60)], H(3))!;
        var pts = r.PastTrack.SelectMany(l => l.Points).ToList();
        Assert.Equal(3, pts.Count);
        Assert.Equal(-148, pts[0].LongitudeDeg, 6);
        Assert.Equal(-150, pts[1].LongitudeDeg, 6);
        Assert.Equal(r.Location, pts[2]);
    }

    [Fact]
    public void SourceLabelIsBestTrackBetweenTwoBestTrackFixes()
    {
        var a = Adv(0, new(10, -150), 40, EventSource.Nhc, Fix(1, new(10, -150.5), 38), Fix(3, new(10, -151), 38));
        Assert.Equal("NHC best track", CycloneReplay.At([a, Adv(9, new(10, -156), 60)], H(2))!.Replay!.HistorySource);
    }

    [Fact]
    public void SourceLabelMixesAdvisoryAndBestTrack()
    {
        var a = Adv(0, new(10, -150), 40, EventSource.Nhc, Fix(3, new(10, -151), 38));
        var b = Adv(9, new(10, -156), 60);
        Assert.Equal("NHC advisories and best track", CycloneReplay.At([a, b], H(1))!.Replay!.HistorySource);
        Assert.Equal("NHC advisories and best track", CycloneReplay.At([a, b], H(4))!.Replay!.HistorySource);
        var gd = Adv(12, new(11, -155), 45, EventSource.Gdacs, Fix(0, new(10, -150)), Fix(6, new(10.5, -152)));
        Assert.Equal("GDACS track", CycloneReplay.At([gd], H(3))!.Replay!.HistorySource);
        var g1 = Adv(0, new(10, -150), 45, EventSource.Gdacs);
        var g2 = Adv(6, new(10, -152), 45, EventSource.Gdacs);
        Assert.Equal("GDACS advisories", CycloneReplay.At([g1, g2], H(3))!.Replay!.HistorySource);
    }

    [Fact]
    public void GdacsAdvisoryFixIsAtItsHistoryEnd()
    {
        // The advisory is stored at 09:00 but its position applies at the todate fix (06:00, last in History).
        var gd = Adv(9, new(10, -152), 45, EventSource.Gdacs, Fix(0, new(10, -150)), Fix(6, new(10, -152), 45));
        var r = CycloneReplay.At([gd], H(3))!;
        Assert.Equal(H(0), r.Replay!.EarlierFixUtc);
        Assert.Equal(H(6), r.Replay.LaterFixUtc);
    }

    [Fact]
    public void PressureAndTitleDoNotComeFromALaterAdvisory()
    {
        var first = Adv(12, new(11, -155), 45, EventSource.Gdacs, Fix(0, new(10, -150)), Fix(6, new(10.5, -152))) with { Title = "Tropical Storm Pena", PressureHpa = 995 };
        var before = CycloneReplay.At([first], H(3))!;
        Assert.Null(before.PressureHpa);
        Assert.Equal("Tropical cyclone PENA", before.Title);
        var after = CycloneReplay.At([first, Adv(18, new(11, -158), 45, EventSource.Gdacs)], H(14))!;
        Assert.Equal(995, after.PressureHpa);
        Assert.Equal("Tropical Storm Pena", after.Title);
    }

    [Fact]
    public void WindFallsBackToTheAdvisoryWhenOnlyOneNeighbourHasWind()
    {
        // e (03:00) has no wind, l (06:00) has: use the advisory's (40 kt), not l's 60.
        var a = Adv(0, new(10, -150), 40, EventSource.Nhc, Fix(3, new(10, -151), null));
        var r = CycloneReplay.At([a, Adv(6, new(10, -152), 60)], H(4))!;
        Assert.Equal(40, r.WindKt!.Value, 6);
        Assert.True(r.Replay!.IntensityKnown);
        // e has wind, l has none: again the advisory's.
        var b = Adv(0, new(10, -150), 40, EventSource.Nhc, Fix(3, new(10, -151), 45), Fix(5, new(10, -151.5), null));
        var r2 = CycloneReplay.At([b, Adv(6, new(10, -152), 60)], H(4))!;
        Assert.Equal(40, r2.WindKt!.Value, 6);
    }

    [Fact]
    public void PastTrackIsSplitAtTheDateLine()
    {
        var r = CycloneReplay.At([Adv(0, new(10, 179), 40), Adv(6, new(10, -179), 40)], H(5))!;
        Assert.Equal(2, r.PastTrack.Count);
        var all = r.PastTrack.SelectMany(l => l.Points).ToList();
        Assert.Contains(all, p => p.LongitudeDeg == 180);
        Assert.Contains(all, p => p.LongitudeDeg == -180);
        Assert.All(r.PastTrack, l => Assert.All(l.Points.Zip(l.Points.Skip(1)), q => Assert.True(Math.Abs(q.First.LongitudeDeg - q.Second.LongitudeDeg) < 180)));
    }
}
