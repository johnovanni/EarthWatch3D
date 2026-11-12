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

    [Fact]
    public void SameNamesFarApartAreTwoStorms()
    {
        var a = new GeoPosition(15, -50);
        var b = new GeoPosition(15 + 500 / 111.19, -50);
        var merged = CycloneMerger.Merge([Adv(EventSource.Nhc, "nhc:al01", "Pena", a), Adv(EventSource.Gdacs, "gdacs:1", "PENA-26", b)]);
        Assert.Equal(2, merged.Count);
    }
}
