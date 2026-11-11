using System.Globalization;
using AstroCore.Earth;

namespace EarthWatch3D.Core.Geo;

public static class GeoFormat
{
    /// <summary>"12.3°S 45.0°W": one decimal, invariant culture; zero counts as N/E.</summary>
    public static string LatLon(GeoPosition p)
    {
        double lat = Math.Round(p.LatitudeDeg, 1, MidpointRounding.AwayFromZero);
        double lon = Math.Round(p.LongitudeDeg, 1, MidpointRounding.AwayFromZero);
        return string.Create(CultureInfo.InvariantCulture,
            $"{Math.Abs(lat):0.0}°{(lat >= 0 ? 'N' : 'S')} {Math.Abs(lon):0.0}°{(lon >= 0 ? 'E' : 'W')}");
    }
}
