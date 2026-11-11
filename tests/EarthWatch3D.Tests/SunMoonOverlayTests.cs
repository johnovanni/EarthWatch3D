using System.Windows;
using EarthWatch3D.App.Rendering;

namespace EarthWatch3D.Tests;

public class SunMoonOverlayTests
{   // 1100x700 overlay; the card at the top right, as in the main window (12 px margin, 230 px wide).
    private const double W = 1100, H = 700;
    private static readonly Rect Card = new(W - 12 - 230, 12, 230, 140);

    private static Rect KeepOut()
    {
        var r = Card;
        r.Inflate(SunMoonOverlay.LabelWidth / 2 + SunMoonOverlay.ArrowSize / 2, SunMoonOverlay.LabelHeight / 2 + SunMoonOverlay.ArrowSize / 2);
        return r;
    }

    [Fact]
    public void ArrowAwayFromTheCardIsUnchanged()
    {
        var p = new Point(SunMoonOverlay.Inset, 300);
        Assert.Equal(p, SunMoonOverlay.AvoidCard(p, Card, W, H));
    }

    [Fact]
    public void ArrowBelowTheCardIsUnchanged()
    {
        var p = new Point(W - SunMoonOverlay.Inset, 400);
        Assert.Equal(p, SunMoonOverlay.AvoidCard(p, Card, W, H));
    }

    [Theory]
    [InlineData(W - SunMoonOverlay.Inset, 60)]    // right edge, beside the card
    [InlineData(W - 150, SunMoonOverlay.Inset)]   // top edge, above the card
    [InlineData(W - 290, SunMoonOverlay.Inset)]   // top edge, left of the card but close enough for the label to overlap
    public void ArrowInTheCardMovesDownTheRightEdgeToJustBelowIt(double x, double y)
    {
        var r = SunMoonOverlay.AvoidCard(new Point(x, y), Card, W, H);
        Assert.Equal(W - SunMoonOverlay.Inset, r.X, 9);
        Assert.False(KeepOut().Contains(r));
        Assert.InRange(r.Y, KeepOut().Bottom, KeepOut().Bottom + 2);
    }

    [Fact]
    public void ShortWindowClampsToTheBottomOfTheEdge()
    {
        var r = SunMoonOverlay.AvoidCard(new Point(W - SunMoonOverlay.Inset, 60), Card, W, 150);
        Assert.Equal(new Point(W - SunMoonOverlay.Inset, 150 - SunMoonOverlay.Inset), r);
    }

    [Fact]
    public void EmptyCardChangesNothing()
    {
        var p = new Point(W - SunMoonOverlay.Inset, 60);
        Assert.Equal(p, SunMoonOverlay.AvoidCard(p, Rect.Empty, W, H));
    }
}
