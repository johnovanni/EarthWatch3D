using EarthWatch3D.Core.Globe;

namespace EarthWatch3D.Tests;

public class GlobeImageComposerTests
{
    private static Bgra32Image Solid(int w, int h, byte v)
    { var img = Bgra32Image.Create(w, h); for (int i = 0; i < img.Pixels.Length; i += 4) { img.Pixels[i] = img.Pixels[i+1] = img.Pixels[i+2] = v; img.Pixels[i+3] = 255; } return img; }
    private static byte R(Bgra32Image img, int x, int y) => img.Pixels[(y * img.Width + x) * 4 + 2];
    private static byte G(Bgra32Image img, int x, int y) => img.Pixels[(y * img.Width + x) * 4 + 1];
    private static byte B(Bgra32Image img, int x, int y) => img.Pixels[(y * img.Width + x) * 4];

    [Theory]
    [InlineData(10, 0)] [InlineData(0, 0)] [InlineData(-6, 0.425)] [InlineData(-12, 0.85)] [InlineData(-40, 0.85)]
    public void NightFactorFollowsSmoothstep(double h, double expected) =>
        Assert.Equal(expected, GlobeImageComposer.NightFactor(h, 0.85), 6);

    [Fact]
    public void DaySideUnchangedNightSideDarkenedWithBlueTint()
    {   // 360x180: pixel (180, 90) is centred at lat -0.5, lon 0.5; pixel (0, 89) at lat 0.5, lon -179.5 (antipode)
        var composer = new GlobeImageComposer(Solid(360, 180, 255));
        var target = Bgra32Image.Create(360, 180);
        composer.ComposeInto(new GlobeLayers(new(-0.5, 0.5), 0.85, DrawTerminator: false), target);
        Assert.Equal(255, R(target, 180, 90));
        // full night multiplies each channel by 1 - f·(1 - m): R m=0.32, G m=0.36, B m=0.50, f=0.85
        Assert.Equal(108, R(target, 0, 89));            // 255·0.422 = 107.6
        Assert.Equal(116, G(target, 0, 89));            // 255·0.456 = 116.3
        Assert.Equal(147, B(target, 0, 89));            // 255·0.575 = 146.6
    }

    [Fact] // the night side must never get lighter, even over near-black ocean
    public void NightNeverBrightensDarkPixels()
    {
        var composer = new GlobeImageComposer(Solid(360, 180, 12));
        var target = Bgra32Image.Create(360, 180);
        composer.ComposeInto(new GlobeLayers(new(-0.5, 0.5), 0.85, DrawTerminator: false), target);
        Assert.True(R(target, 0, 89) <= 12 && G(target, 0, 89) <= 12 && B(target, 0, 89) <= 12);
    }

    [Fact]
    public void TerminatorLineDrawnOnBothSidesOfSunset()
    {   // sub-solar (0,0): the terminator is at lon ±90. Pixel x=269 is lon 89.5 (h≈+0.5°), x=270 is lon 90.5 (h≈-0.5°).
        var composer = new GlobeImageComposer(Solid(360, 180, 0));
        var target = Bgra32Image.Create(360, 180);
        composer.ComposeInto(new GlobeLayers(new(0, 0), 0.85), target);
        Assert.InRange(R(target, 269, 90), 100, 255);
        Assert.InRange(R(target, 270, 90), 100, 255);
        Assert.True(R(target, 269, 90) > B(target, 269, 90));   // warm (gold) colour
        Assert.Equal(0, R(target, 180, 90));                     // mid-day: no line
        Assert.Equal(0, R(target, 300, 90));                     // deep night: no line
    }

    [Fact]
    public void TerminatorLineCanBeTurnedOff()
    {
        var composer = new GlobeImageComposer(Solid(360, 180, 0));
        var target = Bgra32Image.Create(360, 180);
        composer.ComposeInto(new GlobeLayers(new(0, 0), 0.85, DrawTerminator: false), target);
        Assert.Equal(0, R(target, 269, 90));
    }

    [Fact] // Review Focus 1: no seam at ±180°
    public void ShadingContinuousAcrossDateLine()
    {
        var composer = new GlobeImageComposer(Solid(360, 180, 255));
        var target = Bgra32Image.Create(360, 180);
        composer.ComposeInto(new GlobeLayers(new(0, 179.9), 0.55), target);
        Assert.InRange(Math.Abs(R(target, 0, 90) - R(target, 359, 90)), 0, 1);
    }

    [Fact]
    public void LinesDrawnOverNightShade()
    {
        var lines = Bgra32Image.Create(360, 180);
        int i = (89 * 360 + 0) * 4; lines.Pixels[i] = lines.Pixels[i+1] = lines.Pixels[i+2] = 255; lines.Pixels[i+3] = 255;
        var composer = new GlobeImageComposer(Solid(360, 180, 0));
        var target = Bgra32Image.Create(360, 180);
        composer.ComposeInto(new GlobeLayers(new(-0.5, 0.5), 0.55, Lines: lines), target);
        Assert.Equal(255, R(target, 0, 89));
    }

    [Fact]
    public void WrongSizedLayerThrows() =>
        Assert.Throws<ArgumentException>(() => new GlobeImageComposer(Solid(360, 180, 0))
            .ComposeInto(new GlobeLayers(new(0, 0), 0.55, Clouds: Bgra32Image.Create(10, 10)), Bgra32Image.Create(360, 180)));

    [Fact, Trait("Category", "Perf")]
    public void Composes8192UnderBudget()
    {
        var composer = new GlobeImageComposer(Solid(8192, 4096, 128));
        var target = Bgra32Image.Create(8192, 4096);
        composer.ComposeInto(new GlobeLayers(new(10, 20), 0.55), target);  // warm-up
        var sw = System.Diagnostics.Stopwatch.StartNew();
        composer.ComposeInto(new GlobeLayers(new(10, 20), 0.55, Lines: Bgra32Image.Create(8192, 4096)), target);
        Assert.True(sw.ElapsedMilliseconds < 300, $"{sw.ElapsedMilliseconds} ms");
    }
}
