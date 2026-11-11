using EarthWatch3D.Core.Globe;

namespace EarthWatch3D.Tests;

public class SparseOverlayTests
{
    private static void Put(Bgra32Image img, int x, int y, byte b, byte g, byte r, byte a)
    { int i = (y * img.Width + x) * 4; img.Pixels[i] = b; img.Pixels[i + 1] = g; img.Pixels[i + 2] = r; img.Pixels[i + 3] = a; }

    private static Bgra32Image Gradient(int w, int h)
    {
        var img = Bgra32Image.Create(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                Put(img, x, y, (byte)(x * 7), (byte)(y * 5), (byte)((x + y) * 3), 255);
        return img;
    }

    [Fact]
    public void KeepsOnlyNonTransparentPixelsInOrder()
    {
        var img = Bgra32Image.Create(4, 3);
        Put(img, 2, 0, 1, 2, 3, 255);
        Put(img, 1, 2, 10, 20, 30, 128);
        Put(img, 3, 2, 9, 9, 9, 0);   // colour but alpha 0: dropped
        var s = SparseOverlay.FromImage(img);
        Assert.Equal((4, 3), (s.Width, s.Height));
        Assert.Equal(2, s.Count);
        Assert.Equal([2, 9], s.PixelIndices);
        Assert.Equal([1, 2, 3, 255, 10, 20, 30, 128], s.Bgra);
        Assert.Equal([0, 1, 1, 2], s.RowStarts);   // row y spans entries [RowStarts[y], RowStarts[y+1])
    }

    [Fact]
    public void SparseAndDenseLinesComposeIdentically()
    {
        int w = 360, h = 180;
        var lines = Bgra32Image.Create(w, h);
        var rnd = new Random(42);
        for (int n = 0; n < 2000; n++)
            Put(lines, rnd.Next(w), rnd.Next(h), (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256),
                (byte)(n % 3 == 0 ? 255 : rnd.Next(1, 255)));
        var composer = new GlobeImageComposer(Gradient(w, h));
        var dense = Bgra32Image.Create(w, h);
        var sparse = Bgra32Image.Create(w, h);
        var sun = new AstroCore.Earth.GeoPosition(-15, 60);
        composer.ComposeInto(new GlobeLayers(sun, 0.55, Lines: lines), dense);
        composer.ComposeInto(new GlobeLayers(sun, 0.55, SparseLines: SparseOverlay.FromImage(lines)), sparse);
        Assert.Equal(dense.Pixels, sparse.Pixels);
    }

    [Fact]
    public void WrongSizedSparseLinesThrows() =>
        Assert.Throws<ArgumentException>(() => new GlobeImageComposer(Gradient(360, 180)).ComposeInto(
            new GlobeLayers(new(0, 0), 0.55, SparseLines: SparseOverlay.FromImage(Bgra32Image.Create(10, 10))),
            Bgra32Image.Create(360, 180)));
}
