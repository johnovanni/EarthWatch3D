using AstroCore.Earth;

namespace EarthWatch3D.Core.Geo;

/// <summary>
/// Splits lines and polygons at the ±180° meridian so that no downstream shape ever crosses it.
/// Cut points are built directly at exactly ±180 (not via NormalizeLongitude, which maps -180 to +180),
/// and original vertices are only ever shifted by whole multiples of 360.
/// </summary>
public static class DateLine
{
    // Cell k covers longitudes (-180 + 360k, 180 + 360k]; shifting by -360k brings it into (-180, 180].
    private static int CellOf(double unwrappedLon) => (int)Math.Ceiling((unwrappedLon - 180.0) / 360.0);

    private static List<(double Lon, double Lat)> Unwrap(IReadOnlyList<(double Lon, double Lat)> pts)
    {
        var u = new List<(double Lon, double Lat)>(pts.Count);
        foreach (var (lon, lat) in pts)
        {
            if (!double.IsFinite(lon) || !double.IsFinite(lat)) throw new ArgumentException("coordinates must be finite", nameof(pts));
            if (u.Count == 0) { u.Add((lon, lat)); continue; }
            double prev = u[^1].Lon;
            u.Add((lon + 360.0 * Math.Round((prev - lon) / 360.0), lat));
        }
        return u;
    }

    public static IReadOnlyList<GeoLine> SplitLine(IReadOnlyList<(double Lon, double Lat)> pts)
    {
        var result = new List<GeoLine>();
        if (pts.Count == 0) return result;
        var u = Unwrap(pts);

        int k = CellOf(u[0].Lon);
        var cur = new List<GeoPosition> { new(u[0].Lat, u[0].Lon - 360.0 * k) };

        for (int i = 1; i < u.Count; i++)
        {
            var (a, b) = (u[i - 1], u[i]);
            while (true)
            {
                double east = 180.0 + 360.0 * k, west = -180.0 + 360.0 * k;
                if (b.Lon > east)
                {
                    double lat = Lerp(a, b, east);
                    cur.Add(new GeoPosition(lat, 180.0));
                    if (cur.Count >= 2) result.Add(new GeoLine(cur));
                    cur = new List<GeoPosition> { new(lat, -180.0) };
                    a = (east, lat); k++;
                }
                else if (b.Lon < west)
                {
                    double lat = Lerp(a, b, west);
                    cur.Add(new GeoPosition(lat, -180.0));
                    if (cur.Count >= 2) result.Add(new GeoLine(cur));
                    cur = new List<GeoPosition> { new(lat, 180.0) };
                    a = (west, lat); k--;
                }
                else break;
            }
            cur.Add(new GeoPosition(b.Lat, b.Lon - 360.0 * k));
        }
        if (cur.Count >= 2) result.Add(new GeoLine(cur));
        return result;
    }

    private static double Lerp((double Lon, double Lat) a, (double Lon, double Lat) b, double lon)
        => a.Lat + (lon - a.Lon) * (b.Lat - a.Lat) / (b.Lon - a.Lon);

    public static IReadOnlyList<GeoPolygon> SplitPolygon(IReadOnlyList<IReadOnlyList<(double Lon, double Lat)>> rings)
    {
        var result = new List<GeoPolygon>();
        if (rings.Count == 0 || rings[0].Count == 0) return result;

        var outer = Unwrap(rings[0]);
        double minLon = outer.Min(p => p.Lon), maxLon = outer.Max(p => p.Lon);

        // Fast path: already inside [-180, 180]; points are kept untouched.
        if (minLon >= -180.0 && maxLon <= 180.0)
        {
            result.Add(BuildPolygon(outer, rings, 0, keepHoles: true));
            return result;
        }

        var pieces = new List<(int K, List<(double Lon, double Lat)> Ring)>();
        int kFirst = (int)Math.Floor((minLon + 180.0) / 360.0), kLast = (int)Math.Floor((maxLon + 180.0) / 360.0);
        for (int k = kFirst; k <= kLast; k++)
        {
            double lo = -180.0 + 360.0 * k, hi = 180.0 + 360.0 * k;
            var clipped = Clip(Clip(OpenRing(outer), lo, keepGreater: true), hi, keepGreater: false);
            if (clipped.Count < 3) continue;
            if (clipped.Max(p => p.Lon) <= clipped.Min(p => p.Lon)) continue; // degenerate sliver on the meridian
            clipped.Add(clipped[0]);
            pieces.Add((k, clipped));
        }

        foreach (var (k, ring) in pieces)
            result.Add(BuildPolygon(ring, rings, k, keepHoles: pieces.Count == 1));
        return result;
    }

    private static List<(double Lon, double Lat)> OpenRing(List<(double Lon, double Lat)> ring)
    {
        var r = new List<(double Lon, double Lat)>(ring);
        if (r.Count > 1 && r[0] == r[^1]) r.RemoveAt(r.Count - 1);
        return r;
    }

    // Sutherland-Hodgman against one vertical edge. Cut points get exactly the edge longitude.
    private static List<(double Lon, double Lat)> Clip(List<(double Lon, double Lat)> poly, double edge, bool keepGreater)
    {
        var res = new List<(double Lon, double Lat)>();
        bool Inside((double Lon, double Lat) p) => keepGreater ? p.Lon >= edge : p.Lon <= edge;
        for (int i = 0; i < poly.Count; i++)
        {
            var cur = poly[i];
            var prev = poly[(i + poly.Count - 1) % poly.Count];
            bool curIn = Inside(cur), prevIn = Inside(prev);
            if (curIn != prevIn) res.Add((edge, Lerp(prev, cur, edge)));
            if (curIn) res.Add(cur);
        }
        return res;
    }

    private static GeoPolygon BuildPolygon(List<(double Lon, double Lat)> outer,
        IReadOnlyList<IReadOnlyList<(double Lon, double Lat)>> src, int k, bool keepHoles)
    {
        var all = new List<IReadOnlyList<GeoPosition>> { Shift(outer, k) };
        if (keepHoles)
        {
            for (int h = 1; h < src.Count; h++)
            {
                if (src[h].Count == 0) continue;
                var hole = Unwrap(src[h]);
                // Bring the hole next to the outer ring's first vertex, then into the same cell.
                double offset = 360.0 * Math.Round((outer[0].Lon - hole[0].Lon) / 360.0);
                if (offset != 0) hole = hole.Select(p => (p.Lon + offset, p.Lat)).ToList();
                all.Add(Shift(hole, k));
            }
        }
        return new GeoPolygon(all);
    }

    private static List<GeoPosition> Shift(List<(double Lon, double Lat)> ring, int k)
        => ring.Select(p => new GeoPosition(p.Lat, p.Lon - 360.0 * k)).ToList();
}
