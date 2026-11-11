using AstroCore.Earth;
using EarthWatch3D.Core.Geo;

namespace EarthWatch3D.Tests;

public class GeoFormatTests
{
    [Theory] [InlineData(-12.34, -45.0, "12.3°S 45.0°W")] [InlineData(0, 180, "0.0°N 180.0°E")] [InlineData(18.05, 120.0, "18.1°N 120.0°E")]
    [InlineData(-0.04, -0.04, "0.0°N 0.0°E")]
    public void Formats(double lat, double lon, string expected) => Assert.Equal(expected, GeoFormat.LatLon(new(lat, lon)));
}
