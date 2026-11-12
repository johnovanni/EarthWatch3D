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

    [Theory]
    [InlineData(230, 51), InlineData(77, 77), InlineData(0, 26), InlineData(105, 51), InlineData(160, 140), InlineData(250, 200)]
    public void NeutralColoursKeyOnBrightnessAlone(byte grey, byte baseGrey) =>
        // Grey has no colour to wash out, so the whitening path adds nothing: the RGB key equals the brightness key.
        Assert.Equal(CloudLayer.Alpha(grey / 255.0, baseGrey / 255.0), CloudLayer.Alpha(grey, grey, grey, baseGrey, baseGrey, baseGrey));

    [Fact]
    public void ClearSandStaysClear() =>
        Assert.Equal(0, CloudLayer.Alpha(210, 170, 120, 210, 170, 120));

    [Fact]
    public void ThinCloudOverSandShows()
    {
        // A washed-out, slightly brighter version of the sand: the brightness key alone hardly shows it.
        Assert.True(CloudLayer.Alpha(Luma(225, 205, 175), Luma(210, 170, 120)) < 64);
        Assert.True(CloudLayer.Alpha(225, 205, 175, 210, 170, 120) > 100);
    }

    [Fact]
    public void PaleButDarkerThanTheSandIsNotCloud() =>
        Assert.Equal(0, CloudLayer.Alpha(150, 135, 115, 210, 170, 120));

    [Theory]
    [InlineData(40, 50, 70), InlineData(30, 45, 80), InlineData(120, 125, 130), InlineData(230, 235, 240), InlineData(5, 5, 5)]
    public void OceanKeysAsBefore(byte r, byte g, byte b)
    {
        // A deep-blue base: hazy or grey VIIRS sea is less saturated, but the whitening path is for bright ground only.
        const byte br = 15, bg = 35, bb = 80;
        Assert.Equal(CloudLayer.Alpha(Luma(r, g, b), Luma(br, bg, bb)), CloudLayer.Alpha(r, g, b, br, bg, bb));
    }

    [Fact]
    public void ACloudPixelCastsASoftShadowDownAndRight()
    {
        var (viirs, back) = Field(4096, 8, (x, y) => x == 100 && y == 2);
        var built = CloudLayer.Build(viirs, back);
        static int At(int x, int y) => (y * 4096 + x) * 4;
        Assert.True(built.Pixels[At(100, 2) + 3] > 200);                           // the cloud itself
        int s = At(102, 4);
        Assert.Equal(new byte[] { 0, 0, 0 }, built.Pixels[s..(s + 3)]);           // dark
        Assert.InRange(built.Pixels[s + 3], 50, 64);                                // 0.25 x the cloud's alpha
        Assert.Equal(0, built.Pixels[At(98, 0) + 3]);                               // nothing up and left
        Assert.Equal(0, built.Pixels[At(101, 3) + 3]);                              // only at the offset
    }

    [Fact]
    public void NoShadowInsideAUniformCloud()
    {
        var (viirs, back) = Field(4096, 8, (x, y) => x < 50);
        var built = CloudLayer.Build(viirs, back);
        byte cloud = built.Pixels[3];
        Assert.True(cloud > 200);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 50; x++)
            {
                int i = (y * 4096 + x) * 4;
                Assert.Equal(new byte[] { 255, 255, 255, cloud }, built.Pixels[i..(i + 4)]);
            }
        // Just beside it (two pixels right, rows 2 and below) the ground is shaded.
        Assert.True(built.Pixels[(4 * 4096 + 51) * 4 + 3] > 0);
    }

    /// <summary>A white cloud where <paramref name="cloud"/> is true over a dark grey ground that the VIIRS image matches elsewhere.</summary>
    private static (Bgra32Image Viirs, Bgra32Image Base) Field(int w, int h, Func<int, int, bool> cloud)
    {
        var viirs = Bgra32Image.Create(w, h);
        var back = Bgra32Image.Create(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                byte v = (byte)(cloud(x, y) ? 255 : 40);
                viirs.Pixels[i] = viirs.Pixels[i + 1] = viirs.Pixels[i + 2] = v; viirs.Pixels[i + 3] = 255;
                back.Pixels[i] = back.Pixels[i + 1] = back.Pixels[i + 2] = 40; back.Pixels[i + 3] = 255;
            }
        return (viirs, back);
    }

    private static double Luma(byte r, byte g, byte b) => (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;

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

    // The sample (2026-11-10) and the Blue Marble, decoded as the app does, built once for the sample tests.
    private static readonly Lazy<(Bgra32Image Viirs, Bgra32Image Base, Bgra32Image Clouds)> Sample = new(() =>
    {
        using var asset = typeof(EarthWatch3D.App.App).Assembly.GetManifestResourceStream("EarthWatch3D.App.Assets.bluemarble-8192.jpg")!;
        var baseImage = ImageConversions.DecodeToBgra32(asset, 4096);
        using var sample = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Samples", "gibs_viirs_noaa21_truecolor_4096.jpg"));
        var viirs = ImageConversions.DecodeToBgra32(sample, 4096);
        return (viirs, baseImage, CloudLayer.Build(viirs, baseImage));
    });

    /// <summary>
    /// Desert boxes that are cloud-free in the sample (checked by eye against the image; spec §5.6), as
    /// (south, north, west, east) in degrees. VIIRS sand there is a little less saturated than Blue Marble sand.
    /// </summary>
    public static readonly TheoryData<string, double, double, double, double> ClearSand = new()
    {
        { "Libya", 22, 27, 12, 20 },
        { "Egypt, Western Desert", 22, 27, 25, 30 },
        { "Rub' al Khali", 18, 22, 46, 54 },
        { "Chad", 15, 20, 5, 15 },
        { "Kalahari, south", -27, -25.5, 18, 24 },
    };

    [Theory]
    [MemberData(nameof(ClearSand))]
    public void ClearSandInTheSampleStaysClear(string name, double south, double north, double west, double east)
    {
        var clouds = Sample.Value.Clouds;
        int w = clouds.Width, h = clouds.Height;
        int x0 = (int)Math.Round((west + 180) / 360 * w), x1 = (int)Math.Round((east + 180) / 360 * w);
        int y0 = (int)Math.Round((90 - north) / 180 * h), y1 = (int)Math.Round((90 - south) / 180 * h);
        long over = 0, n = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++, n++)
                if (clouds.Pixels[(y * w + x) * 4 + 3] > 128) over++;
        Assert.True(over < 0.02 * n, $"{name}: {over} of {n} pixels have alpha > 128");
    }

    [Fact]
    public void SampleHasPlausibleCover()
    {
        var (viirs, baseImage, clouds) = Sample.Value;
        Assert.Equal((baseImage.Width, baseImage.Height), (viirs.Width, viirs.Height));

        Assert.True(CloudLayer.BlackFraction(viirs) < 0.05, "black " + CloudLayer.BlackFraction(viirs));
        long covered = 0;
        for (int i = 3; i < clouds.Pixels.Length; i += 4) if (clouds.Pixels[i] > 128) covered++;
        double share = covered / (double)(clouds.Width * clouds.Height);
        Assert.InRange(share, 0.10, 0.80);
    }
}
