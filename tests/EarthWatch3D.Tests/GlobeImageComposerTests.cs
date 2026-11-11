using EarthWatch3D.Core.Globe;

namespace EarthWatch3D.Tests;

public class GlobeImageComposerTests
{
    private static Bgra32Image Solid(int w, int h, byte v)
    { var img = Bgra32Image.Create(w, h); for (int i = 0; i < img.Pixels.Length; i += 4) { img.Pixels[i] = img.Pixels[i+1] = img.Pixels[i+2] = v; img.Pixels[i+3] = 255; } return img; }
    private static byte R(Bgra32Image img, int x, int y) => img.Pixels[(y * img.Width + x) * 4 + 2];

    [Theory]
    [InlineData(10, 0)] [InlineData(0, 0)] [InlineData(-6, 0.275)] [InlineData(-12, 0.55)] [InlineData(-40, 0.55)]
    public void NightOpacityFollowsSmoothstep(double h, double expected) =>
        Assert.Equal(expected, GlobeImageComposer.NightOpacity(h, 0.55), 6);

    [Fact]
    public void DaySideUnchangedNightSideGreyed()
    {   // 360x180: pixel (180, 90) is centred at lat -0.5, lon 0.5; pixel (0, 89) at lat 0.5, lon -179.5 (antipode)
        var composer = new GlobeImageComposer(Solid(360, 180, 255));
        var target = Bgra32Image.Create(360, 180);
        composer.ComposeInto(new GlobeLayers(new(-0.5, 0.5), 0.55), target);
        Assert.Equal(255, R(target, 180, 90));
        Assert.Equal(137, R(target, 0, 89));            // 255·0.45 + 40·0.55 = 136.75 → 137 (red channel of grey is 40)
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
