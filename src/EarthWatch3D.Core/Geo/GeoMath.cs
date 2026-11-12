using AstroCore.Earth;

namespace EarthWatch3D.Core.Geo;

public static class GeoMath
{
    public const double EarthRadiusKm = 6371.0;

    /// <summary>Great-circle distance (haversine) in kilometres, R = 6371 km.</summary>
    public static double DistanceKm(GeoPosition a, GeoPosition b)
    {
        double lat1 = a.LatitudeDeg * Math.PI / 180, lat2 = b.LatitudeDeg * Math.PI / 180;
        double dLat = lat2 - lat1, dLon = (b.LongitudeDeg - a.LongitudeDeg) * Math.PI / 180;
        double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
    }
}
