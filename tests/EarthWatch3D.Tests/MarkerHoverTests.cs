using System.Windows;
using System.Windows.Media.Media3D;
using AstroCore.Earth;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.Core.Geo;

namespace EarthWatch3D.Tests;

public class MarkerHoverTests
{   // Camera at (0,-5,0) looking +Y (at the Earth's centre), up +Z, FOV 60, viewport 800x600.
    private static Projector P() =>
        new(new PerspectiveCamera(new Point3D(0, -5, 0), new Vector3D(0, 5, 0), new Vector3D(0, 0, 1), 60), 800, 600);

    private static readonly GeoPosition FacingCamera = new(0, -90);   // unit vector (0,-1,0): the middle of the visible side
    private static readonly GeoPosition FarSide = new(0, 90);         // (0,1,0): directly behind the Earth

    [Fact]
    public void MarkerFacingCameraIsVisibleFarSideIsNot()
    {
        Assert.True(MarkerHover.IsOnVisibleSide(new Point3D(0, -5, 0), FacingCamera));
        Assert.False(MarkerHover.IsOnVisibleSide(new Point3D(0, -5, 0), FarSide));
    }

    [Fact]
    public void HitWithinRadiusMissOutside()
    {
        Assert.True(MarkerHover.Hits(new Point(405, 300), P(), FacingCamera));
        Assert.False(MarkerHover.Hits(new Point(420, 300), P(), FacingCamera));
    }

    [Fact] // the far-side marker projects to the screen centre too, but it is hidden by the globe
    public void FarSideMarkerNeverHits() =>
        Assert.False(MarkerHover.Hits(new Point(400, 300), P(), FarSide));

    [Fact]
    public void TooltipTextsNameThePointAndExplainIt()
    {
        var p = new GeoPosition(-17.6, -147.1);
        string sun = MarkerHover.SunText(p), moon = MarkerHover.MoonText(p);
        Assert.StartsWith("Sun overhead", sun);
        Assert.Contains(GeoFormat.LatLon(p), sun);
        Assert.Contains("directly overhead", sun);
        Assert.StartsWith("Moon overhead", moon);
        Assert.Contains("closest point on Earth to the Moon", moon);
    }
}
