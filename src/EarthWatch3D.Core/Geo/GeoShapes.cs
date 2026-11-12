using AstroCore.Earth;

namespace EarthWatch3D.Core.Geo;

/// <summary>A polyline on the globe. Points never cross ±180° longitude (see <see cref="DateLine"/>).</summary>
public sealed record GeoLine(IReadOnlyList<GeoPosition> Points);

/// <summary>
/// A polygon on the globe. The first ring is the outer boundary, the rest are holes.
/// No ring crosses ±180° longitude (see <see cref="DateLine"/>).
/// </summary>
public sealed record GeoPolygon(IReadOnlyList<IReadOnlyList<GeoPosition>> Rings)
{
    /// <summary>Even-odd ray cast in lon/lat over all rings, so holes are respected.</summary>
    public bool Contains(GeoPosition p)
    {
        bool inside = false;
        double x = p.LongitudeDeg, y = p.LatitudeDeg;
        foreach (var ring in Rings)
        {
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                double xi = ring[i].LongitudeDeg, yi = ring[i].LatitudeDeg;
                double xj = ring[j].LongitudeDeg, yj = ring[j].LatitudeDeg;
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                    inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>Bounding box of the outer ring.</summary>
    public (double MinLat, double MinLon, double MaxLat, double MaxLon) Bounds
    {
        get
        {
            double minLat = double.MaxValue, minLon = double.MaxValue, maxLat = double.MinValue, maxLon = double.MinValue;
            foreach (var q in Rings[0])
            {
                minLat = Math.Min(minLat, q.LatitudeDeg); maxLat = Math.Max(maxLat, q.LatitudeDeg);
                minLon = Math.Min(minLon, q.LongitudeDeg); maxLon = Math.Max(maxLon, q.LongitudeDeg);
            }
            return (minLat, minLon, maxLat, maxLon);
        }
    }
}
