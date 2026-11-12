using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using AstroCore.Earth;
using EarthWatch3D.App.Rendering;
using HelixToolkit.Wpf;

namespace EarthWatch3D.Tests;

public class SurfacePickTests
{   // Camera at (0,-5,0) looking +Y (at the Earth's centre), up +Z, FOV 60, viewport 800x600.
    private static PerspectiveCamera Camera() => new(new Point3D(0, -5, 0), new Vector3D(0, 5, 0), new Vector3D(0, 0, 1), 60);
    private static Projector P() => new(Camera(), 800, 600);

    [Fact]
    public void RaySphereHitMatchesFindNearestAtCentre()
    {
        Assert.True(P().TryHitUnitSphere(new Point(400, 300), out var hit));
        Assert.Equal(0, hit.X, 9);
        Assert.Equal(-1, hit.Y, 9);   // the near side, facing the camera
        Assert.Equal(0, hit.Z, 9);

        // The same point from Helix's FindNearest against the real globe mesh, laid out at 800x600.
        Point3D nearest = default;
        bool found = false;
        Sta.Run(() =>
        {
            var viewport = new Viewport3D { Camera = Camera(), Width = 800, Height = 600 };
            viewport.Children.Add(new ModelVisual3D { Content = new GeometryModel3D(GlobeMesh.Create(), new DiffuseMaterial(Brushes.White)) });
            viewport.Children.Add(new ModelVisual3D { Content = new AmbientLight(Colors.White) });
            viewport.Measure(new Size(800, 600));
            viewport.Arrange(new Rect(0, 0, 800, 600));
            viewport.UpdateLayout();
            found = viewport.FindNearest(new Point(400, 300), out nearest, out _, out _);
        });
        Assert.True(found);
        Assert.True((hit - nearest).Length < 1e-3, $"analytic {hit} vs mesh {nearest}");
    }

    [Theory]
    [InlineData(400, 300)]
    [InlineData(450, 260)]
    [InlineData(330, 350)]
    public void HitProjectsBackToTheMouseAndIsOnTheNearSide(double x, double y)
    {
        var p = P();
        Assert.True(p.TryHitUnitSphere(new Point(x, y), out var hit));
        Assert.Equal(1, ((Vector3D)hit).Length, 9);
        Assert.True(p.TryProject(hit, out var back));
        Assert.Equal(x, back.X, 6);
        Assert.Equal(y, back.Y, 6);
        Assert.True(MarkerHover.IsOnVisibleSide(p.Eye, GeoPosition.FromVector(new AstroCore.Astronomy.Vector3d(hit.X, hit.Y, hit.Z))));
    }

    [Fact]
    public void MissOutsideTheGlobe()
    {
        // The globe spans asin(1/5) = 11.5 degrees around the centre; the corner is about 37 degrees off.
        Assert.False(P().TryHitUnitSphere(new Point(5, 5), out _));
        Assert.False(P().TryHitUnitSphere(new Point(400, 50), out _));
    }
}
