using System.IO;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.Core.Globe;

namespace EarthWatch3D.Tests;

public sealed class CloudLayerTests
{
    [Fact]
    public void KeysOnBrightnessAboveTheBase()
    {
        Assert.Equal(230, CloudLayer.Alpha(0.9, 0.2));   // clamped to 0.9
        Assert.Equal(0, CloudLayer.Alpha(0.3, 0.3));     // desert as bright as the base
        Assert.Equal(0, CloudLayer.Alpha(0.0, 0.1));     // gap
        Assert.Equal(115, CloudLayer.Alpha(0.41, 0.2));
    }

    [Fact]
    public void BuildKeepsTheVIIRSColourAndKeysTheAlpha()
    {
        var viirs = new Bgra32Image(2, 1, [200, 220, 240, 255, 0, 0, 0, 255]);
        var back = new Bgra32Image(2, 1, [40, 60, 20, 255, 40, 60, 20, 255]);
        var built = CloudLayer.Build(viirs, back);
        Assert.Equal(new byte[] { 200, 220, 240 }, built.Pixels[..3]);
        Assert.True(built.Pixels[3] > 200);
        Assert.Equal(0, built.Pixels[7]);
        Assert.Throws<ArgumentException>(() => CloudLayer.Build(viirs, Bgra32Image.Create(1, 1)));
    }

    [Fact]
    public void BlackFractionCountsOnlyBetween60NAnd60S()
    {
        // 18 rows of 10 degrees from 90N: rows 0-2 (90..60N) and 15-17 (60..90S) are outside. They are black; the rest bright.
        int w = 4, h = 18;
        var img = Bgra32Image.Create(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                byte v = (byte)(y < 3 || y >= 15 ? 0 : 200);
                img.Pixels[i] = img.Pixels[i + 1] = img.Pixels[i + 2] = v;
                img.Pixels[i + 3] = 255;
            }
        Assert.Equal(0.0, CloudLayer.BlackFraction(img));
        int p = 5 * w * 4;
        img.Pixels[p] = img.Pixels[p + 1] = img.Pixels[p + 2] = 0;
        Assert.Equal(1.0 / (w * 12), CloudLayer.BlackFraction(img), 9);
    }

    [Fact]
    public void SampleHasPlausibleCover()
    {
        using var asset = typeof(EarthWatch3D.App.App).Assembly.GetManifestResourceStream("EarthWatch3D.App.Assets.bluemarble-8192.jpg")!;
        var baseImage = ImageConversions.DecodeToBgra32(asset, 4096);
        using var sample = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Samples", "gibs_viirs_noaa21_truecolor_4096.jpg"));
        var viirs = ImageConversions.DecodeToBgra32(sample, 4096);
        Assert.Equal((baseImage.Width, baseImage.Height), (viirs.Width, viirs.Height));

        Assert.True(CloudLayer.BlackFraction(viirs) < 0.05, "black " + CloudLayer.BlackFraction(viirs));
        var clouds = CloudLayer.Build(viirs, baseImage);
        long covered = 0;
        for (int i = 3; i < clouds.Pixels.Length; i += 4) if (clouds.Pixels[i] > 128) covered++;
        double share = covered / (double)(clouds.Width * clouds.Height);
        Assert.InRange(share, 0.10, 0.80);
    }
}
