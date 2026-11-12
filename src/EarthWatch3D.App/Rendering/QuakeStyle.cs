using System.Globalization;
using System.Windows.Media;

namespace EarthWatch3D.App.Rendering;

/// <summary>Size, age colour and label of an earthquake disc on the globe.</summary>
public static class QuakeStyle
{
    /// <summary>Magnitudes at or above this get a label.</summary>
    public const double LabelMinMagnitude = 5.0;

    private static readonly Color[] Buckets =
    [
        Color.FromRgb(0xFF, 0x3B, 0x30),   // newest
        Color.FromRgb(0xFF, 0x95, 0x00),
        Color.FromRgb(0xFF, 0xD6, 0x0A),
        Color.FromRgb(0x9C, 0x8F, 0x5A),   // oldest
    ];

    public const int BucketCount = 4;

    /// <summary>Disc radius in Earth radii: 0.004 at M2.5, ×1.5 per magnitude, clamped to [0.0015, 0.05].</summary>
    public static double RadiusEarthRadii(double mag) => Math.Clamp(0.004 * Math.Pow(1.5, mag - 2.5), 0.0015, 0.05);

    /// <summary>Age colour bucket from the fraction of the quake window elapsed (0 = now, 1 = end of window).</summary>
    public static int ColorBucket(double ageFraction) => ageFraction switch
    {
        < 0.1 => 0,
        < 0.35 => 1,
        < 0.7 => 2,
        _ => 3,
    };

    public static Color BucketColor(int b) => Buckets[Math.Clamp(b, 0, BucketCount - 1)];

    /// <summary>
    /// The world size of 3 px at the nearest surface point (distance d − 1 from a camera at distance d), so every disc
    /// stays at least that big on screen. <paramref name="fovHorizontalDeg"/> is WPF's horizontal field of view.
    /// </summary>
    public static double MinRadiusForView(double cameraDistance, double fovHorizontalDeg, double viewportWidthPx) =>
        3 * 2 * Math.Max(0, cameraDistance - 1) * Math.Tan(fovHorizontalDeg * Math.PI / 360) / Math.Max(1, viewportWidthPx);

    /// <summary>"M6.1" for magnitude 5 and up, otherwise null.</summary>
    public static string? Label(double mag) =>
        mag >= LabelMinMagnitude ? "M" + mag.ToString("F1", CultureInfo.InvariantCulture) : null;
}
