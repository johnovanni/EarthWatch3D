using AstroCore.Earth;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Lightning;

namespace EarthWatch3D.Tests;

public class ViewStateTests
{
    [Fact]
    public void FacesUsesTheGreatCircleAngle()
    {
        var cam = new GeoPosition(0, -75.2);
        Assert.True(ViewState.Faces(cam, -75.2));    // GOES-East, 0 deg
        Assert.True(ViewState.Faces(cam, -137.2));   // GOES-West, 62 deg
        Assert.True(ViewState.Faces(cam, 0.0));      // MTG, 75.2 deg
        Assert.False(ViewState.Faces(new GeoPosition(0, 100), -75.2));  // 175.2 deg
        Assert.False(ViewState.Faces(new GeoPosition(0, 24.8), -75.2)); // exactly 100 deg
        Assert.True(ViewState.Faces(new GeoPosition(0, 24.7), -75.2));
    }

    [Fact]
    public void FacesAccountsForLatitudeAndTheDateLine()
    {
        Assert.False(ViewState.Faces(new GeoPosition(90, 0), -75.2, 80)); // the pole is 90 deg from the equator
        Assert.True(ViewState.Faces(new GeoPosition(0, 179), -179));      // 2 deg across the antimeridian
    }

    [Fact]
    public void CameraSubPointDefaultsToOriginAndRoundTrips()
    {
        var v = new ViewState();
        Assert.Equal(new GeoPosition(0, 0), v.CameraSubPoint);
        v.CameraSubPoint = new GeoPosition(12.5, -99.25);
        Assert.Equal(new GeoPosition(12.5, -99.25), v.CameraSubPoint);
    }

    private static Task LongTask(Action a) => Task.Factory.StartNew(a, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    [Fact]
    public async Task CameraSubPointIsNeverTorn()
    {
        var v = new ViewState();
        bool torn = false;
        var writer = LongTask(() => { for (int i = 0; i < 300_000; i++) v.CameraSubPoint = (i & 1) == 0 ? new GeoPosition(1, 1) : new GeoPosition(2, 2); });
        var reader = LongTask(() =>
        {
            for (int i = 0; i < 300_000; i++) { var p = v.CameraSubPoint; if (p.LatitudeDeg != p.LongitudeDeg && p.LatitudeDeg != 0) torn = true; }
        });
        await Task.WhenAll(writer, reader);
        Assert.False(torn);
    }
}

public class LiveLightningSourceTests
{
    private static readonly DateTime T0 = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void LiveLightningSourceDoesNotSupportHistory()
        => Assert.False(new LiveLightningSource(new LightningBuffer(), () => false).SupportsHistory);

    [Fact]
    public void CoverageReportsNewestPerSensorAndMtgSetup()
    {
        var b = new LightningBuffer();
        bool mtg = false;
        var src = new LiveLightningSource(b, () => mtg);
        var c0 = src.Coverage;
        Assert.Null(c0.NewestGoesEastUtc); Assert.Null(c0.NewestGoesWestUtc); Assert.Null(c0.NewestMtgUtc); Assert.False(c0.MtgSetUp);
        b.Add(new[] { new LightningFlash(T0, 1, -80, 0, LightningSensor.GoesEast), new LightningFlash(T0.AddSeconds(3), 1, -130, 0, LightningSensor.GoesWest) });
        mtg = true;
        var c = src.Coverage;
        Assert.Equal(T0, c.NewestGoesEastUtc); Assert.Equal(T0.AddSeconds(3), c.NewestGoesWestUtc); Assert.Null(c.NewestMtgUtc); Assert.True(c.MtgSetUp);
    }

    [Fact]
    public void GetFlashesDelegatesAndFlashesChangedForwards()
    {
        var b = new LightningBuffer();
        var src = new LiveLightningSource(b, () => false);
        int n = 0;
        src.FlashesChanged += (_, _) => n++;
        b.Add(new[] { new LightningFlash(T0, 1, -80, 0, LightningSensor.GoesEast) });
        Assert.Equal(1, n);
        Assert.Single(src.GetFlashes(T0, T0.AddSeconds(1)));
        Assert.Empty(src.GetFlashes(T0.AddSeconds(1), T0.AddSeconds(2)));
    }
}
