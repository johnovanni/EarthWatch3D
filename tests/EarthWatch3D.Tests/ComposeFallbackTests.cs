using AstroCore.Earth;
using GlobeCore.Imaging;
using GlobeCore.Wpf.Layers;

namespace EarthWatch3D.Tests;

/// <summary>The scene's compose fallback, now LayerHost.ComposeOrPlain (GlobeCore), with EarthWatch3D's light and an Areas-style layer.</summary>
public class ComposeFallbackTests
{
    private static readonly IlluminationState Light = new(new GeoPosition(0, 0), 1.0, true);

    private static Bgra32Image Pattern(int w, int h)
    {
        var img = Bgra32Image.Create(w, h);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = (byte)(i * 7 + 3);
        return img;
    }

    [Fact]
    public void FailedComposeShowsThePlainBaseImageAndLogs()
    {
        var plain = Pattern(8, 4);
        var target = Bgra32Image.Create(8, 4);
        Array.Fill(target.Pixels, (byte)0x55);   // half-written garbage from the failed compose
        var log = new RecordingLog();
        // An Areas layer of the wrong size makes ComposeInto throw.
        IImageLayer[] layers = [new BitmapBlendLayer(Bgra32Image.Create(2, 2), ImagePass.Surface, 20)];

        bool ok = LayerHost.ComposeOrPlain(new GlobeImageComposer(plain), layers, Light, target, plain, log);

        Assert.False(ok);
        Assert.Equal(plain.Pixels, target.Pixels);
        Assert.Contains("compose failed", Assert.Single(log.Errors));
    }

    [Fact]
    public void SuccessfulComposeIsUntouched()
    {
        var plain = Pattern(8, 4);
        var target = Bgra32Image.Create(8, 4);
        var log = new RecordingLog();
        Assert.True(LayerHost.ComposeOrPlain(new GlobeImageComposer(plain), [], Light, target, plain, log));
        Assert.Empty(log.Errors);
        Assert.NotEqual(plain.Pixels, target.Pixels);   // half the image is night-shaded
    }
}
