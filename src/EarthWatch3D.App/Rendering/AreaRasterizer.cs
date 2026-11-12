using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Globe;

namespace EarthWatch3D.App.Rendering;

/// <summary>
/// Draws coloured polygons (35% fill, 1 px opaque edge) into a new equirectangular straight-alpha image.
/// Needs WPF rendering: call it on an STA thread.
/// </summary>
public static class AreaRasterizer
{
    private const byte FillAlpha = 89;   // 35% of 255

    public static Bgra32Image Rasterize(IEnumerable<(GeoPolygon Area, Color Color)> areas, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(areas);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            foreach (var (area, color) in areas)
            {
                var fill = new SolidColorBrush(Color.FromArgb(FillAlpha, color.R, color.G, color.B)); fill.Freeze();
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(255, color.R, color.G, color.B)), 1.0);
                pen.Brush.Freeze(); pen.Freeze();
                var fillGeo = new StreamGeometry { FillRule = FillRule.EvenOdd };
                var edgeGeo = new StreamGeometry();
                using (var fc = fillGeo.Open())
                using (var ec = edgeGeo.Open())
                    foreach (var ring in area.Rings) AddRing(fc, ec, ring, width, height);
                fillGeo.Freeze(); edgeGeo.Freeze();
                dc.DrawGeometry(fill, null, fillGeo);
                dc.DrawGeometry(null, pen, edgeGeo);
            }
        }
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var px = new byte[checked(width * height * 4)];
        rtb.CopyPixels(px, width * 4, 0);
        for (int i = 0; i < px.Length; i += 4)   // premultiplied -> straight alpha, as the composer expects
        {
            byte a = px[i + 3];
            if (a == 0 || a == 255) continue;
            px[i] = (byte)Math.Min(255, (px[i] * 255 + a / 2) / a);
            px[i + 1] = (byte)Math.Min(255, (px[i + 1] * 255 + a / 2) / a);
            px[i + 2] = (byte)Math.Min(255, (px[i + 2] * 255 + a / 2) / a);
        }
        return new Bgra32Image(width, height, px);
    }

    private static void AddRing(StreamGeometryContext fill, StreamGeometryContext edge,
                                IReadOnlyList<AstroCore.Earth.GeoPosition> ring, int w, int h)
    {
        int n = ring.Count;
        if (n > 1 && ring[0] == ring[n - 1]) n--;
        if (n < 3) return;
        Point P(int i) => new((ring[i].LongitudeDeg + 180) / 360 * w, (90 - ring[i].LatitudeDeg) / 180 * h);

        fill.BeginFigure(P(0), true, true);
        for (int i = 1; i < n; i++) fill.LineTo(P(i), true, false);

        // Edges lying on lon = +-180 (made by DateLine splitting) are not real boundaries: leave them unstroked.
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            if (Math.Abs(ring[i].LongitudeDeg) == 180 && Math.Abs(ring[j].LongitudeDeg) == 180) continue;
            edge.BeginFigure(P(i), false, false);
            edge.LineTo(P(j), true, false);
        }
    }
}
