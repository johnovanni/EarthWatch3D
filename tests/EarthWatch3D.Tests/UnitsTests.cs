using EarthWatch3D.Core.Geo;

namespace EarthWatch3D.Tests;

public class UnitsTests
{
    [Fact]
    public void FormatsBothSystems()
    {
        Assert.Equal("35 km", Units.Distance(35, DistanceUnits.Kilometres));
        Assert.Equal("22 mi", Units.Distance(35, DistanceUnits.Miles));
        Assert.Equal("65 kt (120 km/h)", Units.WindFromKnots(65, DistanceUnits.Kilometres));
        Assert.Equal("65 kt (75 mph)", Units.WindFromKnots(65, DistanceUnits.Miles));
        Assert.Equal("148.1 million km", Units.Large(148_100_000, DistanceUnits.Kilometres));
        Assert.Equal("92.0 million mi", Units.Large(148_100_000, DistanceUnits.Miles));
        Assert.Equal("404,855 km", Units.Distance(404_855.2, DistanceUnits.Kilometres));
    }
}
