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

    [Fact]
    public void OrbitStepHitsEndpoints()
    {
        var a = new Vector3D(3, 0, 1); var b = new Vector3D(-1, 2, 2);
        var p0 = CameraRules.OrbitStep(a, b, 0).Position; var p1 = CameraRules.OrbitStep(a, b, 1).Position;
        Assert.True(((Vector3D)p0 - a).Length < 1e-9);
        Assert.True(((Vector3D)p1 - b).Length < 1e-9);
    }

    [Fact]
    public void OrbitStepOppositeDirectionsNeverPassThroughGlobe()
    {
        var a = new Vector3D(0, -3, 0); var b = new Vector3D(0, 5, 0);
        var (p, look, _) = CameraRules.OrbitStep(a, b, 0.5);
        Assert.Equal(4.0, ((Vector3D)p).Length, 9);   // smoothstep(0.5) = 0.5 -> distance 3 + 2*0.5
        Assert.True(((Vector3D)p).Length >= 1.047);
        Assert.True((look + (Vector3D)p).Length < 1e-9);
        for (double t = 0; t <= 1.0001; t += 0.05)
            Assert.True(((Vector3D)CameraRules.OrbitStep(a, b, t).Position).Length >= 3 - 1e-9);
    }

    [Fact]
    public void OrbitStepHasNoNaNAtPoles()
    {
        var pole = new Vector3D(0, 0, 3); var south = new Vector3D(0, 0, -3); var eq = new Vector3D(3, 0, 0);
        foreach (var (f, g) in new[] { (pole, eq), (eq, pole), (pole, south), (south, pole), (pole, pole) })
            for (double t = 0; t <= 1.0001; t += 0.1)
            {
                var (p, l, u) = CameraRules.OrbitStep(f, g, t);
                foreach (var v in new[] { p.X, p.Y, p.Z, l.X, l.Y, l.Z, u.X, u.Y, u.Z }) Assert.False(double.IsNaN(v));
            }
    }
}
