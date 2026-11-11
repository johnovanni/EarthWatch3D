using System.Windows;
using System.Windows.Media;
using EarthWatch3D.App.Rendering;

namespace EarthWatch3D.Tests;

public class MoonPhaseGlyphTests
{
    [Theory] [InlineData(0.0)] [InlineData(0.25)] [InlineData(0.5)] [InlineData(0.9)] [InlineData(1.0)]
    public void LitAreaMatchesFraction(double k)
    {
        var g = MoonPhaseGlyph.CreateGeometry(k, waxing: true, radius: 100);
        double disc = Math.PI * 100 * 100;
        Assert.InRange(g.GetArea(0.01, ToleranceType.Absolute) - k * disc, -0.005 * disc, 0.005 * disc);
    }

    [Fact]
    public void WaningLightsLeftSide()
    {
        var g = MoonPhaseGlyph.CreateGeometry(0.3, waxing: false, radius: 100);
        Assert.True(g.FillContains(new Point(-90, 0))); Assert.False(g.FillContains(new Point(90, 0)));
    }

    [Fact]
    public void WaningLitAreaMatchesFraction()
    {
        var g = MoonPhaseGlyph.CreateGeometry(0.3, waxing: false, radius: 100);
        double disc = Math.PI * 100 * 100;
        Assert.InRange(g.GetArea(0.01, ToleranceType.Absolute) - 0.3 * disc, -0.005 * disc, 0.005 * disc);
    }

    [Fact]
    public void WaxingLightsRightSide()
    {
        var g = MoonPhaseGlyph.CreateGeometry(0.3, waxing: true, radius: 100);
        Assert.True(g.FillContains(new Point(90, 0))); Assert.False(g.FillContains(new Point(-90, 0)));
    }
}
