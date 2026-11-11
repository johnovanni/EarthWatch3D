namespace EarthWatch3D.Core.Globe;

/// <summary>
/// A mostly transparent straight-alpha BGRA layer stored as its non-transparent pixels only.
/// Entry i is pixel <see cref="PixelIndices"/>[i] (y * Width + x) with colour <see cref="Bgra"/>[4i .. 4i+3].
/// Entries are in pixel order; row y spans entries [RowStarts[y], RowStarts[y + 1]).
/// </summary>
public sealed class SparseOverlay
{
    private SparseOverlay(int width, int height, int[] pixelIndices, byte[] bgra, int[] rowStarts)
    {
        Width = width;
        Height = height;
        PixelIndices = pixelIndices;
        Bgra = bgra;
        RowStarts = rowStarts;
    }

    public int Width { get; }
    public int Height { get; }
    public int Count => PixelIndices.Length;
    public int[] PixelIndices { get; }
    public byte[] Bgra { get; }
    public int[] RowStarts { get; }

    public static SparseOverlay FromImage(Bgra32Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        int w = image.Width, h = image.Height;
        var px = image.Pixels;
        int count = 0;
        for (int p = 3; p < px.Length; p += 4)
            if (px[p] != 0) count++;

        var indices = new int[count];
        var bgra = new byte[count * 4];
        var rowStarts = new int[h + 1];
        int n = 0;
        for (int y = 0; y < h; y++)
        {
            rowStarts[y] = n;
            for (int x = 0, i = y * w, p = i * 4; x < w; x++, i++, p += 4)
            {
                if (px[p + 3] == 0) continue;
                indices[n] = i;
                Buffer.BlockCopy(px, p, bgra, n * 4, 4);
                n++;
            }
        }
        rowStarts[h] = n;
        return new SparseOverlay(w, h, indices, bgra, rowStarts);
    }
}
