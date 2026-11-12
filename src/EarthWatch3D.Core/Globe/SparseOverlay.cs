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

    /// <summary>
    /// One overlay with <paramref name="top"/> drawn over <paramref name="bottom"/> (straight-alpha "over"), so composing it gives
    /// the same picture as composing the two in that order. Both must be the same size.
    /// </summary>
    public static SparseOverlay Merge(SparseOverlay bottom, SparseOverlay top)
    {
        ArgumentNullException.ThrowIfNull(bottom);
        ArgumentNullException.ThrowIfNull(top);
        if (bottom.Width != top.Width || bottom.Height != top.Height)
            throw new ArgumentException("Overlays must be the same size.", nameof(top));
        int h = bottom.Height;
        var idx = new List<int>(bottom.Count + top.Count);
        var col = new List<byte>((bottom.Count + top.Count) * 4);
        var rowStarts = new int[h + 1];
        int i = 0, j = 0;
        for (int y = 0; y < h; y++)
        {
            rowStarts[y] = idx.Count;
            int iEnd = bottom.RowStarts[y + 1], jEnd = top.RowStarts[y + 1];
            while (i < iEnd || j < jEnd)
            {
                int pi = i < iEnd ? bottom.PixelIndices[i] : int.MaxValue;
                int pj = j < jEnd ? top.PixelIndices[j] : int.MaxValue;
                if (pi < pj) { idx.Add(pi); AddColour(col, bottom.Bgra, i * 4); i++; }
                else if (pj < pi) { idx.Add(pj); AddColour(col, top.Bgra, j * 4); j++; }
                else { idx.Add(pi); Over(col, bottom.Bgra, i * 4, top.Bgra, j * 4); i++; j++; }
            }
        }
        rowStarts[h] = idx.Count;
        return new SparseOverlay(bottom.Width, h, [.. idx], [.. col], rowStarts);
    }

    private static void AddColour(List<byte> to, byte[] from, int at)
    { to.Add(from[at]); to.Add(from[at + 1]); to.Add(from[at + 2]); to.Add(from[at + 3]); }

    private static void Over(List<byte> to, byte[] b, int bi, byte[] t, int ti)
    {
        double ab = b[bi + 3] / 255.0, at = t[ti + 3] / 255.0;
        double ao = at + ab * (1.0 - at);
        if (ao <= 0) { to.Add(0); to.Add(0); to.Add(0); to.Add(0); return; }
        for (int c = 0; c < 3; c++)
            to.Add((byte)Math.Clamp(Math.Round((t[ti + c] * at + b[bi + c] * ab * (1.0 - at)) / ao), 0, 255));
        to.Add((byte)Math.Clamp(Math.Round(ao * 255.0), 0, 255));
    }

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
