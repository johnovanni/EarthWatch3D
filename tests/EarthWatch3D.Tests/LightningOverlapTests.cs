using EarthWatch3D.Core.Lightning;

namespace EarthWatch3D.Tests;

public class LightningOverlapTests
{
    private static LightningFlash F(double lon, LightningSensor s) => new(new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc), 10, lon, 0, s);

    [Theory]
    [InlineData(-106.21)] [InlineData(-106.2)] [InlineData(-106.19)]
    [InlineData(-37.61)] [InlineData(-37.6)] [InlineData(-37.59)]
    public void EachFlashKeptExactlyOnce(double lon)
    {
        int kept = 0;
        foreach (var s in new[] { LightningSensor.GoesEast, LightningSensor.GoesWest, LightningSensor.Mtg })
            if (LightningOverlap.Keep(F(lon, s), mtgActive: true)) kept++;
        Assert.Equal(1, kept);
    }

    [Fact]
    public void WithMtgActiveEachSensorKeepsItsOwnBand()
    {
        Assert.True(LightningOverlap.Keep(F(-106.21, LightningSensor.GoesWest), true));
        Assert.True(LightningOverlap.Keep(F(-106.2, LightningSensor.GoesEast), true));
        Assert.True(LightningOverlap.Keep(F(-37.61, LightningSensor.GoesEast), true));
        Assert.True(LightningOverlap.Keep(F(-37.6, LightningSensor.Mtg), true));
    }

    [Theory] [InlineData(-37.6)] [InlineData(-37.59)] [InlineData(0)] [InlineData(150)]
    public void WithoutMtgGoesEastKeepsEverythingEastOfTheBoundary(double lon)
    {
        Assert.True(LightningOverlap.Keep(F(lon, LightningSensor.GoesEast), mtgActive: false));
        Assert.False(LightningOverlap.Keep(F(lon, LightningSensor.GoesWest), mtgActive: false));
    }
}
