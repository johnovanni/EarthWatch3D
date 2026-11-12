using System.Windows.Media;
using EarthWatch3D.App.Rendering;

namespace EarthWatch3D.Tests;

public class EventStyleTests
{
    [Fact]
    public void QuakeRadiusScalesAndClamps()
    {
        Assert.Equal(0.004, QuakeStyle.RadiusEarthRadii(2.5), 12);
        Assert.Equal(0.004 * Math.Pow(1.5, 4.5), QuakeStyle.RadiusEarthRadii(7), 12);
        Assert.Equal(0.0015, QuakeStyle.RadiusEarthRadii(0), 12);
        Assert.Equal(0.05, QuakeStyle.RadiusEarthRadii(9.5), 12);
    }

    [Fact]
    public void ColorBucketBoundaries()
    {
        Assert.Equal(0, QuakeStyle.ColorBucket(0.09));
        Assert.Equal(1, QuakeStyle.ColorBucket(0.1));
        Assert.Equal(2, QuakeStyle.ColorBucket(0.69));
        Assert.Equal(3, QuakeStyle.ColorBucket(0.7));
    }

    [Fact]
    public void BucketColors()
    {
        Assert.Equal(Color.FromRgb(0xFF, 0x3B, 0x30), QuakeStyle.BucketColor(0));
        Assert.Equal(Color.FromRgb(0xFF, 0x95, 0x00), QuakeStyle.BucketColor(1));
        Assert.Equal(Color.FromRgb(0xFF, 0xD6, 0x0A), QuakeStyle.BucketColor(2));
        Assert.Equal(Color.FromRgb(0x9C, 0x8F, 0x5A), QuakeStyle.BucketColor(3));
    }

    [Fact]
    public void MinRadiusForView()
    {
        double expected = 3 * 2 * 2 * Math.Tan(22.5 * Math.PI / 180) / 1000;
        Assert.Equal(expected, QuakeStyle.MinRadiusForView(3, 45, 1000), 12);
    }

    [Fact]
    public void CategoryColors()
    {
        Assert.Equal(Color.FromRgb(0x5E, 0xBA, 0xFF), StormStyle.CategoryColor(-1));
        Assert.Equal(Color.FromRgb(0x00, 0xFA, 0xF4), StormStyle.CategoryColor(0));
        Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xCC), StormStyle.CategoryColor(1));
        Assert.Equal(Color.FromRgb(0xFF, 0xE7, 0x75), StormStyle.CategoryColor(2));
        Assert.Equal(Color.FromRgb(0xFF, 0xC1, 0x40), StormStyle.CategoryColor(3));
        Assert.Equal(Color.FromRgb(0xFF, 0x8F, 0x20), StormStyle.CategoryColor(4));
        Assert.Equal(Color.FromRgb(0xFF, 0x60, 0x60), StormStyle.CategoryColor(5));
    }

    [Theory]
    [InlineData("Pena", 40.0, "PENA TS")]
    [InlineData("CEMPAKA-26", 95.0, "CEMPAKA C2")]
    [InlineData("Ten", 30.0, "TEN TD")]
    [InlineData("Max", 140.0, "MAX C5")]
    public void StormLabelIsNameAndCategory(string name, double windKt, string expected) =>
        Assert.Equal(expected, StormStyle.Label(name, EarthWatch3D.Core.Events.SaffirSimpson.Category(windKt)));

    [Theory]
    [InlineData("Tropical Cyclone 03S", "03S")]
    [InlineData("Super Typhoon In-fa", "IN-FA")]
    [InlineData("Tropical Storm Pena", "PENA")]
    [InlineData("Hurricane Melissa", "MELISSA")]
    [InlineData("Invest 98L", "INVEST 98L")]
    public void EonetNameDropsTheStormTypeWords(string title, string expected) =>
        Assert.Equal(expected, StormStyle.EonetName(title));

    [Fact]
    public void QuakeLabelOnlyForMagnitudeFiveAndUp()
    {
        Assert.Null(QuakeStyle.Label(4.99));
        Assert.Equal("M5.0", QuakeStyle.Label(5.0));
        Assert.Equal("M6.1", QuakeStyle.Label(6.14));
    }
}
