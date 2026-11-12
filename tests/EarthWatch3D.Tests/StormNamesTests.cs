using AstroCore.Earth;
using EarthWatch3D.Core.Events;

namespace EarthWatch3D.Tests;

public sealed class StormNamesTests
{
    [Theory]
    [InlineData(25, -70, 3, "Category 3 hurricane")]                                  // North Atlantic
    [InlineData(12, -95, 1, "Category 1 hurricane")]                                  // NE Pacific off Mexico (lon -100..0 rule)
    [InlineData(15, -150, 2, "Category 2 hurricane")]                                 // Central Pacific
    [InlineData(15, -100, 4, "Category 4 hurricane")]                                 // the edge between the two
    [InlineData(15, 140, 4, "Category 4 typhoon")]                                    // NW Pacific
    [InlineData(20, 180, 5, "Category 5 typhoon")]                                    // on the date line: still west of it
    [InlineData(10, 100, 1, "Category 1 typhoon")]
    [InlineData(-15, 140, 2, "Category 2 tropical cyclone (Saffir–Simpson)")]   // Australian region
    [InlineData(15, 88, 3, "Category 3 tropical cyclone (Saffir–Simpson)")]     // Bay of Bengal
    [InlineData(-20, -150, 1, "Category 1 tropical cyclone (Saffir–Simpson)")]  // South Pacific
    [InlineData(-18, 50, 5, "Category 5 tropical cyclone (Saffir–Simpson)")]    // South-West Indian Ocean
    [InlineData(25, 10, 1, "Category 1 tropical cyclone (Saffir–Simpson)")]     // east of the Atlantic box (lon > 0)
    public void CategoryByBasin(double lat, double lon, int category, string expected) =>
        Assert.Equal(expected, StormNames.CategoryText(category, new GeoPosition(lat, lon)));

    [Theory]
    [InlineData(25, -70)]
    [InlineData(15, 140)]
    [InlineData(-15, 140)]
    public void WeakerStormsAreNamedTheSameEverywhere(double lat, double lon)
    {
        var p = new GeoPosition(lat, lon);
        Assert.Equal("Tropical storm", StormNames.CategoryText(0, p));
        Assert.Equal("Tropical depression", StormNames.CategoryText(-1, p));
    }
}
