namespace EarthWatch3D.Core.Globe;

/// <summary>
/// Turns the daily VIIRS true-colour image into a cloud layer. The image shows land and desert as well as cloud, so
/// the alpha is keyed against the same pixel of the Blue Marble base (spec §5.6), by two paths, the larger winning:
/// brightness (how much brighter the pixel is than the base) and, over bright coloured ground such as sand, whiteness
/// (how much a thin cloud washes the ground's colour out). Land that looks as it does in the base stays clear, and
/// black gaps and polar night (darker than the base) are transparent. Clouds then cast a soft shadow down and right.
/// </summary>
public static class CloudLayer
{
    // Brightness path: a = clamp((Lv − Lb − Floor) / Span, 0, 1), and the final alpha is capped at MaxAlpha.
    private const double Floor = 0.06, Span = 0.30, MaxAlpha = 0.9;
    // Whitening path, measured on the 2026-11-10 sample (spec §5.6): clear VIIRS sand is less saturated than Blue Marble
    // sand by up to 0.135 (99th percentile), so the saturation drop must exceed SatFloor; the pixel must be at least as
    // bright as the ground (full weight at LumaSpan brighter); and the path is only for bright ground (base luma from
    // BaseFloor, full at BaseFloor + BaseSpan), where the brightness path has little headroom. Dark ground and the sea
    // already show thin cloud by brightness, and there the saturation of dark pixels is mostly JPEG noise.
    private const double SatFloor = 0.14, SatSpan = 0.10, LumaSpan = 0.06, BaseFloor = 0.30, BaseSpan = 0.15;
    private const double BlackLuma = 0.02, BlackLatitudeDeg = 60;

    /// <summary>Shadow alpha as a share of the cloud alpha that casts it.</summary>
    public const double ShadowStrength = 0.25;
    /// <summary>Shadow offset, right and down, in pixels of a 4096-pixel-wide image (scaled with the width). Purely visual.</summary>
    public const int ShadowOffsetPx = 2;

    /// <summary>
    /// Alpha 0..255 by brightness alone, for a pixel of luma <paramref name="viirsLuma"/> over a base of
    /// <paramref name="baseLuma"/> (both 0..1). This is the whole key for grey (unsaturated) colours.
    /// </summary>
    public static byte Alpha(double viirsLuma, double baseLuma) => ToByte(Key(viirsLuma, 0, baseLuma, 0));

    /// <summary>Alpha 0..255 for a VIIRS pixel over the base pixel: the larger of the brightness and whitening paths.</summary>
    public static byte Alpha(byte r, byte g, byte b, byte baseR, byte baseG, byte baseB) =>
        ToByte(Key(Luma(r, g, b), Saturation(r, g, b), Luma(baseR, baseG, baseB), Saturation(baseR, baseG, baseB)));

    private static double Key(double lv, double sv, double lb, double sb)
    {
        double brightness = Math.Clamp((lv - lb - Floor) / Span, 0, 1);
        double whiteness = Math.Clamp((sb - sv - SatFloor) / SatSpan, 0, 1)
                           * Math.Clamp((lv - lb) / LumaSpan, 0, 1)
                           * Math.Clamp((lb - BaseFloor) / BaseSpan, 0, 1);
        return Math.Max(brightness, whiteness) * MaxAlpha;
    }

    private static byte ToByte(double a) => (byte)Math.Round(a * 255);

    /// <summary>
    /// The cloud layer: the VIIRS colours with the keyed alpha, plus a shadow. Both images must have the same size.
    /// The layer is blended with straight alpha, so a shadow pixel is black with alpha
    /// <see cref="ShadowStrength"/> × the alpha of the cloud pixel up and left of it, where that is more than its own.
    /// </summary>
    public static Bgra32Image Build(Bgra32Image viirs, Bgra32Image baseImage)
    {
        ArgumentNullException.ThrowIfNull(viirs);
        ArgumentNullException.ThrowIfNull(baseImage);
        if (viirs.Width != baseImage.Width || viirs.Height != baseImage.Height)
            throw new ArgumentException("The VIIRS image and the base image must be the same size.", nameof(baseImage));
        var result = Bgra32Image.Create(viirs.Width, viirs.Height);
        var src = viirs.Pixels;
        var bas = baseImage.Pixels;
        var dst = result.Pixels;
        int w = viirs.Width, h = viirs.Height;
        var alpha = new byte[w * h];
        // Bgra32: B at i, G at i+1, R at i+2.
        Parallel.For(0, h, y =>
        {
            int i = y * w * 4;
            for (int x = 0, k = y * w; x < w; x++, k++, i += 4)
            {
                dst[i] = src[i]; dst[i + 1] = src[i + 1]; dst[i + 2] = src[i + 2];
                alpha[k] = Alpha(src[i + 2], src[i + 1], src[i], bas[i + 2], bas[i + 1], bas[i]);
            }
        });
        // Shadow: one more pass. The source is up and left; x wraps across the date line, the top rows have no source.
        int off = Math.Max(1, (int)Math.Round(ShadowOffsetPx * w / 4096.0));
        Parallel.For(0, h, y =>
        {
            int i = y * w * 4, ys = y - off;
            for (int x = 0, k = y * w; x < w; x++, k++, i += 4)
            {
                byte own = alpha[k];
                byte shadow = ys < 0 ? (byte)0 : (byte)Math.Round(ShadowStrength * alpha[ys * w + (x - off + w) % w]);
                if (shadow > own)
                {
                    dst[i] = dst[i + 1] = dst[i + 2] = 0;
                    dst[i + 3] = shadow;
                }
                else dst[i + 3] = own;
            }
        });
        return result;
    }

    /// <summary>The share of pixels darker than luma 0.02 between 60°N and 60°S (poleward of about 66° the image is black in polar night, which is not a gap): black gaps where no satellite pass has been yet.</summary>
    public static double BlackFraction(Bgra32Image viirs)
    {
        ArgumentNullException.ThrowIfNull(viirs);
        long black = 0, total = 0;
        int w = viirs.Width;
        for (int y = 0; y < viirs.Height; y++)
        {
            double lat = 90 - (y + 0.5) / viirs.Height * 180;
            if (Math.Abs(lat) > BlackLatitudeDeg) continue;
            int i = y * w * 4;
            for (int x = 0; x < w; x++, i += 4)
                if (Luma(viirs.Pixels[i + 2], viirs.Pixels[i + 1], viirs.Pixels[i]) < BlackLuma) black++;
            total += w;
        }
        return total == 0 ? 0 : (double)black / total;
    }

    private static double Luma(byte r, byte g, byte b) => (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;

    /// <summary>(max − min) / max of the channels, 0 for black.</summary>
    private static double Saturation(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        return max == 0 ? 0 : (max - min) / (double)max;
    }
}
