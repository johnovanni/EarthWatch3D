using AstroCore.Earth;
using EarthWatch3D.App.Controllers;
using EarthWatch3D.Core.Globe;

namespace EarthWatch3D.Tests;

public class ComposeFallbackTests
{
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
        var layers = new GlobeLayers(new GeoPosition(0, 0), 1.0, Areas: Bgra32Image.Create(2, 2));

        bool ok = SceneController.ComposeOrPlain(new GlobeImageComposer(plain), layers, target, plain, log);

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
        var layers = new GlobeLayers(new GeoPosition(0, 0), 1.0);

        Assert.True(SceneController.ComposeOrPlain(new GlobeImageComposer(plain), layers, target, plain, log));
        Assert.Empty(log.Errors);
        Assert.NotEqual(plain.Pixels, target.Pixels);   // half the image is night-shaded
    }
}
