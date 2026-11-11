using AstroCore.Earth;

namespace EarthWatch3D.Core.Globe;

public sealed record GlobeLayers(
    GeoPosition SubSolarPoint,
    double NightMaxOpacity,
    Bgra32Image? Clouds = null,
    Bgra32Image? Areas = null,
    Bgra32Image? Lines = null,
    SparseOverlay? SparseLines = null);

/// <summary>
/// Composes the globe texture: base, clouds, areas, night shade, lines (each straight-alpha blended).
/// Lines may be given dense (<see cref="GlobeLayers.Lines"/>) or sparse (<see cref="GlobeLayers.SparseLines"/>,
/// same result, far less memory); if both are given, the sparse lines are drawn after the dense ones.
/// Pixel (x, y) is centred at lon = -180 + (x+0.5)*360/W, lat = 90 - (y+0.5)*180/H.
/// </summary>
public sealed class GlobeImageComposer(Bgra32Image baseImage)
{
    private const double TwilightDeg = -12.0;
    private static readonly double SinTwilight = Math.Sin(TwilightDeg * Math.PI / 180.0);
    private const byte ShadeB = 48, ShadeG = 40, ShadeR = 40;

    private readonly Bgra32Image _base = baseImage ?? throw new ArgumentNullException(nameof(baseImage));

    public static double NightOpacity(double sunAltitudeDeg, double maxOpacity)
    {
        double t = Math.Clamp((sunAltitudeDeg - 0.0) / (TwilightDeg - 0.0), 0.0, 1.0);
        return maxOpacity * (t * t * (3.0 - 2.0 * t));
    }

    public void ComposeInto(GlobeLayers layers, Bgra32Image target)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(target);
        int w = _base.Width, h = _base.Height;
        Check(target, nameof(target));
        Check(layers.Clouds, nameof(layers.Clouds));
        Check(layers.Areas, nameof(layers.Areas));
        Check(layers.Lines, nameof(layers.Lines));
        if (layers.SparseLines is { } sl && (sl.Width != w || sl.Height != h))
            throw new ArgumentException($"SparseLines must be {w}x{h}, got {sl.Width}x{sl.Height}.", nameof(layers));

        double max = Math.Clamp(layers.NightMaxOpacity, 0.0, 1.0);
        double deg = Math.PI / 180.0;
        double sinDec = Math.Sin(layers.SubSolarPoint.LatitudeDeg * deg);
        double cosDec = Math.Cos(layers.SubSolarPoint.LatitudeDeg * deg);
        double sunLon = layers.SubSolarPoint.LongitudeDeg;

        var cosDl = new double[w];
        for (int x = 0; x < w; x++)
            cosDl[x] = Math.Cos((-180.0 + (x + 0.5) * 360.0 / w - sunLon) * deg);

        // Full-night result per source value, per channel.
        var fullB = new byte[256]; var fullG = new byte[256]; var fullR = new byte[256];
        for (int v = 0; v < 256; v++)
        {
            fullB[v] = Mix(ShadeB, v, max);
            fullG[v] = Mix(ShadeG, v, max);
            fullR[v] = Mix(ShadeR, v, max);
        }

        var src = _base.Pixels;
        var dst = target.Pixels;
        var clouds = layers.Clouds?.Pixels;
        var areas = layers.Areas?.Pixels;
        var lines = layers.Lines?.Pixels;
        var sparse = layers.SparseLines;
        bool shade = max > 0.0;

        Parallel.For(0, h, y =>
        {
            int row = y * w * 4;
            Buffer.BlockCopy(src, row, dst, row, w * 4);
            if (clouds != null) Blend(clouds, dst, row, w);
            if (areas != null) Blend(areas, dst, row, w);

            if (shade)
            {
                double lat = 90.0 - (y + 0.5) * 180.0 / h;
                double sinLat = Math.Sin(lat * deg), cosLat = Math.Cos(lat * deg);
                double a = sinLat * sinDec, b = cosLat * cosDec;
                int p = row;
                for (int x = 0; x < w; x++, p += 4)
                {
                    double s = a + b * cosDl[x];
                    if (s >= 0.0) continue;
                    if (s <= SinTwilight)
                    {
                        dst[p] = fullB[dst[p]];
                        dst[p + 1] = fullG[dst[p + 1]];
                        dst[p + 2] = fullR[dst[p + 2]];
                    }
                    else
                    {
                        double alt = Math.Asin(s) / deg;
                        double op = NightOpacity(alt, max);
                        dst[p] = Mix(ShadeB, dst[p], op);
                        dst[p + 1] = Mix(ShadeG, dst[p + 1], op);
                        dst[p + 2] = Mix(ShadeR, dst[p + 2], op);
                    }
                }
            }

            if (lines != null) Blend(lines, dst, row, w);
            if (sparse != null) BlendSparse(sparse, dst, y);

            for (int p = row + 3, end = row + w * 4; p < end; p += 4) dst[p] = 255;
        });
    }

    private static byte Mix(int srcValue, int dstValue, double a) =>
        (byte)Math.Round(srcValue * a + dstValue * (1.0 - a), MidpointRounding.AwayFromZero);

    private static void Blend(byte[] layer, byte[] dst, int row, int w)
    {
        for (int p = row, end = row + w * 4; p < end; p += 4)
        {
            int al = layer[p + 3];
            if (al == 0) continue;
            if (al == 255)
            {
                dst[p] = layer[p]; dst[p + 1] = layer[p + 1]; dst[p + 2] = layer[p + 2];
                continue;
            }
            double a = al / 255.0;
            dst[p] = Mix(layer[p], dst[p], a);
            dst[p + 1] = Mix(layer[p + 1], dst[p + 1], a);
            dst[p + 2] = Mix(layer[p + 2], dst[p + 2], a);
        }
    }

    private static void BlendSparse(SparseOverlay layer, byte[] dst, int y)
    {
        var idx = layer.PixelIndices;
        var col = layer.Bgra;
        for (int n = layer.RowStarts[y], end = layer.RowStarts[y + 1]; n < end; n++)
        {
            int p = idx[n] * 4, c = n * 4;
            int al = col[c + 3];
            if (al == 255)
            {
                dst[p] = col[c]; dst[p + 1] = col[c + 1]; dst[p + 2] = col[c + 2];
                continue;
            }
            double a = al / 255.0;
            dst[p] = Mix(col[c], dst[p], a);
            dst[p + 1] = Mix(col[c + 1], dst[p + 1], a);
            dst[p + 2] = Mix(col[c + 2], dst[p + 2], a);
        }
    }

    private void Check(Bgra32Image? img, string name)
    {
        if (img != null && (img.Width != _base.Width || img.Height != _base.Height))
            throw new ArgumentException($"{name} must be {_base.Width}x{_base.Height}, got {img.Width}x{img.Height}.", name);
    }
}
