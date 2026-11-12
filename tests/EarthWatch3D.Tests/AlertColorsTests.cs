using System.Windows.Media;
using EarthWatch3D.App.Rendering;

namespace EarthWatch3D.Tests;

public class AlertColorsTests
{
    [Theory]
    [InlineData("Tornado Warning", 0xFF, 0x00, 0x00)]
    [InlineData("Severe Thunderstorm Warning", 0xFF, 0xA5, 0x00)]
    [InlineData("Flash Flood Warning", 0x8B, 0x00, 0x00)]
    [InlineData("Winter Storm Watch", 0x46, 0x82, 0xB4)]
    [InlineData("Wind Advisory", 0xD2, 0xB4, 0x8C)]
    [InlineData("Small Craft Advisory", 0xD8, 0xBF, 0xD8)]
    public void AlertColorsKnownAndFallback(string type, byte r, byte g, byte b)
    {
        Assert.Equal(Color.FromRgb(r, g, b), AlertColors.For(type));
        Assert.Equal(Color.FromRgb(0xA0, 0xA0, 0xA0), AlertColors.For("Dense Fog Advisory"));
        Assert.Equal(Color.FromRgb(0xA0, 0xA0, 0xA0), AlertColors.For(""));
    }

    [Theory]
    [InlineData(30, 0x34, 0xC7, 0x59)]
    [InlineData(60, 0x34, 0xC7, 0x59)]
    [InlineData(75, 0x34, 0xC7, 0x59)]
    [InlineData(90, 0xFF, 0x95, 0x00)]
    [InlineData(100, 0xFF, 0x95, 0x00)]
    [InlineData(120, 0xFF, 0x3B, 0x30)]
    [InlineData(150, 0xFF, 0x3B, 0x30)]
    public void WindBandColors(double kmh, byte r, byte g, byte b) =>
        Assert.Equal(Color.FromRgb(r, g, b), AlertColors.ForWind(kmh));
}
