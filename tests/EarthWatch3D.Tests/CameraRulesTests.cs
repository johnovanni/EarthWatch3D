using System.Windows.Media.Media3D;
using AstroCore.Earth;
using EarthWatch3D.App.Rendering;

namespace EarthWatch3D.Tests;

public class CameraRulesTests
{
    [Theory] [InlineData(0.5, 1.047)] [InlineData(5, 5)] [InlineData(100, 20)]
    public void ClampsDistanceKeepingDirection(double d, double expected)
    {
        var p = CameraRules.ClampDistance(new Point3D(0, -d, 0));
        Assert.Equal(expected, ((Vector3D)p).Length, 9); Assert.Equal(0, p.X, 12);
    }

    [Theory] [InlineData(3, 1.0)] [InlineData(1.047, 0.0235)] [InlineData(1.0, 0.02)]
    public void SensitivityScalesWithAltitude(double d, double expected) =>
        Assert.Equal(expected, CameraRules.RotationSensitivity(d), 4);

    [Fact]
    public void FitDistanceFitsGlobeInVerticalHalfFov()
    {
        double d = CameraRules.FitDistance(45, 16.0 / 9);
        double vHalf = Math.Atan(Math.Tan(22.5 * Math.PI / 180) / (16.0 / 9));
        Assert.True(Math.Asin(1 / d) <= 0.9 * vHalf + 1e-9);
    }

    [Fact]
    public void FitDistanceUsesHorizontalHalfForTallWindow()
    {
        double d = CameraRules.FitDistance(45, 0.5);
        Assert.Equal(1 / Math.Sin(0.9 * 22.5 * Math.PI / 180), d, 9);
    }

    [Theory] [InlineData(45, 0.01)] [InlineData(45, 100)] [InlineData(120, 1)] [InlineData(5, 1)]
    public void FitDistanceStaysWithinLimits(double fov, double aspect)
    {
        double d = CameraRules.FitDistance(fov, aspect);
        Assert.InRange(d, CameraRules.MinDistance, CameraRules.MaxDistance);
    }

    [Theory] // Review Focus 3: poles must not degenerate
    [InlineData(0, 0)] [InlineData(90, 0)] [InlineData(-90, 45)] [InlineData(23.44, 179.9)]
    public void LookDownOnPointsAtCentreWithValidUp(double lat, double lon)
    {
        var (pos, look, up) = CameraRules.LookDownOn(new GeoPosition(lat, lon), 3);
        var expected = new GeoPosition(lat, lon).ToUnitVector() * 3;
        Assert.Equal(expected.X, pos.X, 9); Assert.Equal(expected.Y, pos.Y, 9); Assert.Equal(expected.Z, pos.Z, 9);
        Assert.Equal(-3, Vector3D.DotProduct(look, (Vector3D)pos) / 3, 9);
        Assert.False(double.IsNaN(up.X + up.Y + up.Z));
        Assert.True(Vector3D.CrossProduct(look, up).Length > 0.1);
    }
}
