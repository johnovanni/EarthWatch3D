namespace EarthWatch3D.Core.Globe;

/// <summary>
/// Turns the daily VIIRS true-colour image into a cloud layer. The image shows land and desert as well as cloud, so
/// the alpha is keyed on how much brighter a pixel is than the same pixel of the Blue Marble base: land that looks as
/// it does in the base stays clear, and black gaps and polar night (darker than the base) are transparent.
/// </summary>
public static class CloudLayer
{
    private const double Floor = 0.06, Span = 0.30, MaxAlpha = 0.9;
    private const double BlackLuma = 0.02, BlackLatitudeDeg = 60;

    /// <summary>Alpha 0..255 for a pixel of luma <paramref name="viirsLuma"/> over a base of <paramref name="baseLuma"/> (both 0..1).</summary>
    public static byte Alpha(double viirsLuma, double baseLuma)
    {
        double a = Math.Clamp((viirsLuma - baseLuma - Floor) / Span, 0, 1) * MaxAlpha;
        return (byte)Math.Round(a * 255);
    }

    /// <summary>The cloud layer: the VIIRS colours with the keyed alpha. Both images must have the same size.</summary>
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
        int w = viirs.Width;
        Parallel.For(0, viirs.Height, y =>
        {
            int i = y * w * 4;
            for (int x = 0; x < w; x++, i += 4)
            {
                dst[i] = src[i]; dst[i + 1] = src[i + 1]; dst[i + 2] = src[i + 2];
                dst[i + 3] = Alpha(Luma(src, i), Luma(bas, i));
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
                if (Luma(viirs.Pixels, i) < BlackLuma) black++;
            total += w;
        }
        return total == 0 ? 0 : (double)black / total;
    }

    // Bgra32: B at i, G at i+1, R at i+2.
    private static double Luma(byte[] p, int i) => (0.299 * p[i + 2] + 0.587 * p[i + 1] + 0.114 * p[i]) / 255.0;
}
