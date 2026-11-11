using System.Windows;
using System.Windows.Media.Media3D;
using EarthWatch3D.App.Rendering;

namespace EarthWatch3D.Tests;

public class DirectionIndicatorTests
{   // Camera at (0,-5,0) looking +Y, up +Z, FOV 60, viewport 800x600, inset 40
    private static Projector P(double w = 800, double h = 600) =>
        new(new PerspectiveCamera(new Point3D(0, -5, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, 1), 60), w, h);

    [Fact] public void AheadIsOnScreenAtCentre()
    { var r = DirectionIndicator.Place(new(0, 1, 0), P(), 40); Assert.True(r.OnScreen); Assert.Equal(400, r.Position.X, 6); Assert.Equal(300, r.Position.Y, 6); }

    [Fact] public void RightIsArrowOnRightEdge()
    { var r = DirectionIndicator.Place(new(1, 0, 0), P(), 40); Assert.False(r.OnScreen); Assert.Equal(760, r.Position.X, 6); Assert.Equal(300, r.Position.Y, 6); Assert.Equal(0, r.AngleDeg, 6); }

    [Fact] public void UpIsArrowOnTopEdge()
    { var r = DirectionIndicator.Place(new(0, 0, 1), P(), 40); Assert.Equal(400, r.Position.X, 6); Assert.Equal(40, r.Position.Y, 6); Assert.Equal(-90, r.AngleDeg, 6); }

    [Fact] public void BehindLeftIsArrowOnLeftEdge()
    { var r = DirectionIndicator.Place(new(-1, -1, 0), P(), 40); Assert.Equal(40, r.Position.X, 6); Assert.Equal(300, r.Position.Y, 6); Assert.Equal(180, Math.Abs(r.AngleDeg), 6); }

    [Fact] public void StraightBehindPointsDown()
    { var r = DirectionIndicator.Place(new(0, -1, 0), P(), 40); Assert.Equal(400, r.Position.X, 6); Assert.Equal(560, r.Position.Y, 6); Assert.Equal(90, r.AngleDeg, 6); }

    [Fact] public void NearlyBehindPointsDown()
    { var r = DirectionIndicator.Place(new(0.05, -1, 0), P(), 40); Assert.Equal(400, r.Position.X, 6); Assert.Equal(560, r.Position.Y, 6); Assert.Equal(90, r.AngleDeg, 6); Assert.True(r.Behind); }

    [Fact] public void SideIsNotBehind()
    { var r = DirectionIndicator.Place(new(1, 0, 0), P(), 40); Assert.False(r.Behind); }

    [Fact] public void BehindLeftIsBehind()
    { var r = DirectionIndicator.Place(new(-1, -1, 0), P(), 40); Assert.True(r.Behind); Assert.Equal(40, r.Position.X, 6); }

    [Fact] public void FarCornerClampsToInsetRectangle()
    { var r = DirectionIndicator.Place(new(1, 0.01, 1), P(), 40); Assert.InRange(r.Position.X, 40, 760); Assert.InRange(r.Position.Y, 40, 560); }

    [Theory] // Review Focus 4
    [InlineData(0, 0)] [InlineData(50, 50)]
    public void TinyViewportHidesWithoutNaN(double w, double h)
    { var r = DirectionIndicator.Place(new(1, 0, 0), P(w, h), 40); Assert.False(r.Visible); }
}
