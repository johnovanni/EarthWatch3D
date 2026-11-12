using System.Windows.Media.Media3D;
using AstroCore.Earth;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.Core.Geo;

namespace EarthWatch3D.Tests;

public class SphereLinesTests
{
    private static double AngleDeg(Point3D a, Point3D b)
    {
        var u = (Vector3D)a; var v = (Vector3D)b;
        u.Normalize(); v.Normalize();
        return Vector3D.AngleBetween(u, v);
    }

    [Fact]
    public void DensifyStaysOnSphereAndStepsAreSmall()
    {
        var line = new GeoLine([new GeoPosition(0, 0), new GeoPosition(40, 60), new GeoPosition(-10, 100), new GeoPosition(-10, 100.3)]);
        var pts = SphereLines.Densify(line, 1.003);

        Assert.True(pts.Count > 100);   // about 70° + 60° of arc at ≤ 1° per step
        foreach (var p in pts) Assert.Equal(1.003, ((Vector3D)p).Length, 9);
        for (int i = 1; i < pts.Count; i++) Assert.True(AngleDeg(pts[i - 1], pts[i]) <= 1.0 + 1e-9);

        // It passes through every input point, in order.
        var first = new GeoPosition(0, 0).ToUnitVector();
        Assert.Equal(new Point3D(first.X * 1.003, first.Y * 1.003, first.Z * 1.003), pts[0]);
        var last = new GeoPosition(-10, 100.3).ToUnitVector();
        Assert.True((pts[^1] - new Point3D(last.X * 1.003, last.Y * 1.003, last.Z * 1.003)).Length < 1e-12);
    }

    [Fact]
    public void DensifyOfOnePointIsThatPoint()
    {
        var pts = SphereLines.Densify(new GeoLine([new GeoPosition(10, 20)]), 1.0);
        Assert.Single(pts);
    }

    [Fact]
    public void DashesAlternate()
    {
        // 10° along the equator: dash 1.5°, gap 1°, so dashes at 0–1.5, 2.5–4, 5–6.5 and 7.5–9.
        var path = SphereLines.Densify(new GeoLine([new GeoPosition(0, 0), new GeoPosition(0, 10)]), 1.0);
        var dashes = SphereLines.Dashes(path);

        Assert.Equal(4, dashes.Count);
        double[] starts = [0, 2.5, 5, 7.5];
        for (int i = 0; i < 4; i++)
        {
            var d = dashes[i];
            Assert.True(d.Count >= 2);
            var origin = new Point3D(1, 0, 0);
            Assert.Equal(starts[i], AngleDeg(origin, d[0]), 6);
            Assert.Equal(starts[i] + 1.5, AngleDeg(origin, d[^1]), 6);
            foreach (var p in d) Assert.Equal(1.0, ((Vector3D)p).Length, 9);
        }
    }

    [Fact]
    public void DashesOfAnEmptyOrSinglePointPathAreNone()
    {
        Assert.Empty(SphereLines.Dashes([]));
        Assert.Empty(SphereLines.Dashes([new Point3D(1, 0, 0)]));
    }
}
