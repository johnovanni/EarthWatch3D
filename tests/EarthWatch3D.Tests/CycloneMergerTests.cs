using AstroCore.Earth;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Geo;

namespace EarthWatch3D.Tests;

public class CycloneMergerTests
{
    private static readonly DateTime T = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    private static CycloneAdvisory Adv(EventSource src, string id, string name, GeoPosition pos, IReadOnlyList<WindArea>? wind = null) =>
        new(src, id, T, pos, name, null, name, 70, 990, [], [], [], wind ?? []);

    private static WindArea Wind(double kmh) =>
        new(kmh, new GeoPolygon([[new(0, 0), new(0, 1), new(1, 1)]]));

    [Fact]
    public void NormalizeNameDropsGdacsYearSuffix()
    {
        Assert.Equal(CycloneMerger.NormalizeName("Pena"), CycloneMerger.NormalizeName("PENA-26"));
        Assert.Equal("PENA", CycloneMerger.NormalizeName("  Pena-26 "));
    }

    [Fact]
    public void NormalizeNameKeepsRsmcIdSuffix()
    {
        Assert.Equal("02-20262027-27", CycloneMerger.NormalizeName("02-20262027-27"));
    }

    [Fact]
    public void MergesSameStormAdoptingGdacsWindAreas()
    {
        var pos = new GeoPosition(15, -50);
        var nhc = Adv(EventSource.Nhc, "nhc:al01", "Pena", pos);
        var gdacs = Adv(EventSource.Gdacs, "gdacs:1001", "PENA-26", pos, [Wind(63), Wind(93)]);
        var merged = CycloneMerger.Merge([gdacs, nhc]);
        var m = Assert.Single(merged);
        Assert.Equal(EventSource.Nhc, m.Source);
        Assert.Equal(2, m.WindAreas.Count);
    }

    [Fact]
    public void KeepsNhcWindAreasWhenPresent()
    {
        var pos = new GeoPosition(15, -50);
        var nhc = Adv(EventSource.Nhc, "nhc:al01", "Pena", pos, [Wind(63)]);
        var gdacs = Adv(EventSource.Gdacs, "gdacs:1001", "PENA-26", pos, [Wind(63), Wind(93)]);
        Assert.Single(Assert.Single(CycloneMerger.Merge([nhc, gdacs])).WindAreas);
    }

    [Fact] // Task 17 snapshot: NHC's feed has no past track, so the merged Pena showed none
    public void MergedStormAdoptsGdacsPastTrackWhenNhcHasNone()
    {
        var pos = new GeoPosition(9.9, -167.7);
        GeoLine past = new([new(11.3, -150.1), new(9.9, -167.7)]);
        GeoLine forecast = new([new(9.9, -167.7), new(10.2, -172.6)]);
        var nhc = Adv(EventSource.Nhc, "nhc:cp04", "Pena", pos) with { ForecastTrack = [forecast] };
        var gdacs = Adv(EventSource.Gdacs, "gdacs:1001348", "PENA-26", pos) with { PastTrack = [past], ForecastTrack = [past] };
        var m = Assert.Single(CycloneMerger.Merge([nhc, gdacs]));
        Assert.Equal(EventSource.Nhc, m.Source);
        Assert.Equal(past, Assert.Single(m.PastTrack));
        Assert.Equal(forecast, Assert.Single(m.ForecastTrack));   // NHC's forecast is kept

        GeoLine own = new([new(9, -160), new(9.9, -167.7)]);
        var withOwn = Assert.Single(CycloneMerger.Merge([nhc with { PastTrack = [own] }, gdacs]));
        Assert.Equal(own, Assert.Single(withOwn.PastTrack));
    }

    [Fact]
    public void SameNamesFarApartAreTwoStorms()
    {
        var a = new GeoPosition(15, -50);
        var b = new GeoPosition(15 + 500 / 111.19, -50);
        var merged = CycloneMerger.Merge([Adv(EventSource.Nhc, "nhc:al01", "Pena", a), Adv(EventSource.Gdacs, "gdacs:1", "PENA-26", b)]);
        Assert.Equal(2, merged.Count);
    }
}
