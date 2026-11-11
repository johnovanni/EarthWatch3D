using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AstroCore.Earth;
using EarthWatch3D.Core.Globe;

namespace EarthWatch3D.App.Rendering;

/// <summary>Draws geographic polylines into an equirectangular straight-alpha image. Needs WPF rendering (UI/STA thread).</summary>
public static class LineRasterizer
{
    public static Bgra32Image Rasterize(IEnumerable<IReadOnlyList<GeoPosition>> lines, int w, int h, Color color, double thicknessPx) =>
        Rasterize([(lines, color)], w, h, thicknessPx);

    /// <summary>Several line sets, drawn in order (later ones on top), into one image.</summary>
    public static Bgra32Image Rasterize(IEnumerable<(IEnumerable<IReadOnlyList<GeoPosition>> Lines, Color Color)> layers,
                                        int w, int h, double thicknessPx)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            foreach (var (lines, color) in layers)
            {
                var brush = new SolidColorBrush(color); brush.Freeze();
                var pen = new Pen(brush, thicknessPx) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                pen.Freeze();
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                    foreach (var line in lines) AddLine(ctx, line, w, h);
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
            }
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var px = new byte[checked(w * h * 4)];
        rtb.CopyPixels(px, w * 4, 0);
        // Pbgra32 is premultiplied; the composer wants straight alpha.
        for (int i = 0; i < px.Length; i += 4)
        {
            byte a = px[i + 3];
            if (a == 0 || a == 255) continue;
            px[i] = (byte)Math.Min(255, (px[i] * 255 + a / 2) / a);
            px[i + 1] = (byte)Math.Min(255, (px[i + 1] * 255 + a / 2) / a);
            px[i + 2] = (byte)Math.Min(255, (px[i + 2] * 255 + a / 2) / a);
        }
        return new Bgra32Image(w, h, px);
    }

    private static void AddLine(StreamGeometryContext ctx, IReadOnlyList<GeoPosition> line, int w, int h)
    {
        if (line.Count < 2) return;
        double X(double lon) => (lon + 180) / 360 * w;
        double Y(double lat) => (90 - lat) / 180 * h;
        for (int i = 0; i < line.Count; i++)
        {
            var p = line[i];
            if (i == 0) { ctx.BeginFigure(new Point(X(p.LongitudeDeg), Y(p.LatitudeDeg)), false, false); continue; }
            var q = line[i - 1];
            double dLon = p.LongitudeDeg - q.LongitudeDeg;
            if (Math.Abs(dLon) > 180)
            {
                // Crosses the antimeridian: run each piece to its edge instead of drawing across the map.
                double pl = p.LongitudeDeg + (dLon < 0 ? 360 : -360);   // p unwrapped next to q
                double t = (q.LongitudeDeg > 0 ? 180 - q.LongitudeDeg : -180 - q.LongitudeDeg) / (pl - q.LongitudeDeg);
                double latEdge = q.LatitudeDeg + t * (p.LatitudeDeg - q.LatitudeDeg);
                double edgeQ = q.LongitudeDeg > 0 ? w : 0, edgeP = q.LongitudeDeg > 0 ? 0 : w;
                ctx.LineTo(new Point(edgeQ, Y(latEdge)), true, false);
                ctx.BeginFigure(new Point(edgeP, Y(latEdge)), false, false);
            }
            ctx.LineTo(new Point(X(p.LongitudeDeg), Y(p.LatitudeDeg)), true, false);
        }
    }
}
