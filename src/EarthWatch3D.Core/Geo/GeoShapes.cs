using AstroCore.Earth;

namespace EarthWatch3D.Core.Geo;

/// <summary>A polyline on the globe. Points never cross ±180° longitude (see <see cref="DateLine"/>).</summary>
public sealed record GeoLine(IReadOnlyList<GeoPosition> Points)
{
    public bool Equals(GeoLine? other) => other is not null && ListEquality.Equal(Points, other.Points);
    public override int GetHashCode() => ListEquality.Hash(Points);
}

/// <summary>Value equality helpers for list-bearing records.</summary>
public static class ListEquality
{
    public static bool Equal<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Count != b.Count) return false;
        return a.SequenceEqual(b);
    }

    public static bool Equal<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b, Func<T, T, bool> eq)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++) if (!eq(a[i], b[i])) return false;
        return true;
    }

    public static int Hash<T>(IReadOnlyList<T>? a)
    {
        if (a is null) return 0;
        var h = new HashCode();
        h.Add(a.Count);
        for (var i = 0; i < Math.Min(a.Count, 4); i++) h.Add(a[i]);
        return h.ToHashCode();
    }
}

/// <summary>
/// A polygon on the globe. The first ring is the outer boundary, the rest are holes.
/// No ring crosses ±180° longitude (see <see cref="DateLine"/>).
/// </summary>
public sealed record GeoPolygon(IReadOnlyList<IReadOnlyList<GeoPosition>> Rings)
{
    /// <summary>Lat/lon box around every ring, so <see cref="Contains"/> can reject far points without the ray cast.</summary>
    private sealed class Box(double minLat, double minLon, double maxLat, double maxLon)
    {
        public bool Holds(double lat, double lon) => lat >= minLat && lat <= maxLat && lon >= minLon && lon <= maxLon;   // false for NaN

        public static Box Of(IReadOnlyList<IReadOnlyList<GeoPosition>> rings)
        {
            double minLat = double.MaxValue, minLon = double.MaxValue, maxLat = double.MinValue, maxLon = double.MinValue;
            foreach (var ring in rings)
                foreach (var q in ring)
                {
                    minLat = Math.Min(minLat, q.LatitudeDeg); maxLat = Math.Max(maxLat, q.LatitudeDeg);
                    minLon = Math.Min(minLon, q.LongitudeDeg); maxLon = Math.Max(maxLon, q.LongitudeDeg);
                }
            return new Box(minLat, minLon, maxLat, maxLon);
        }
    }

    // Computed on the first Contains (a reference, so a racing second computation is harmless). Not part of equality,
    // hashing or serialization, and not copied by `with` (see the copy constructor), so it never goes stale.
    private Box? _box;

    private GeoPolygon(GeoPolygon original) => Rings = original.Rings;

    public bool Equals(GeoPolygon? other) =>
        other is not null && ListEquality.Equal(Rings, other.Rings, (x, y) => ListEquality.Equal(x, y));

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Rings?.Count ?? 0);
        if (Rings is not null)
            for (var i = 0; i < Math.Min(Rings.Count, 2); i++) h.Add(ListEquality.Hash(Rings[i]));
        return h.ToHashCode();
    }

    /// <summary>Even-odd ray cast in lon/lat over all rings, so holes are respected. Points outside the bounding box are rejected first.</summary>
    public bool Contains(GeoPosition p)
    {
        double x = p.LongitudeDeg, y = p.LatitudeDeg;
        if (!(_box ??= Box.Of(Rings)).Holds(y, x)) return false;
        bool inside = false;
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
