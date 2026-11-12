using System.Globalization;

namespace EarthWatch3D.Core.Geo;

/// <summary>Whether distances and speeds are shown in kilometres or in miles.</summary>
public enum DistanceUnits { Kilometres, Miles }

/// <summary>Formats distances and wind speeds in the chosen system.</summary>
public static class Units
{
    /// <summary>International mile.</summary>
    public const double KmPerMile = 1.609344;
    private const double KmPerHourPerKnot = 1.852;

    /// <summary>"35 km" or "22 mi" (thousands separated: "404,855 km").</summary>
    public static string Distance(double km, DistanceUnits u, int digits = 0)
    {
        double v = u == DistanceUnits.Miles ? km / KmPerMile : km;
        return v.ToString("N" + digits, CultureInfo.InvariantCulture) + (u == DistanceUnits.Miles ? " mi" : " km");
    }

    /// <summary>"65 kt (120 km/h)" or "65 kt (75 mph)".</summary>
    public static string WindFromKnots(double kt, DistanceUnits u)
    {
        double kmh = kt * KmPerHourPerKnot;
        double v = u == DistanceUnits.Miles ? kmh / KmPerMile : kmh;
        return kt.ToString("F0", CultureInfo.InvariantCulture) + " kt ("
            + v.ToString("F0", CultureInfo.InvariantCulture) + (u == DistanceUnits.Miles ? " mph)" : " km/h)");
    }

    /// <summary>"148.1 million km" or "92.0 million mi".</summary>
    public static string Large(double km, DistanceUnits u)
    {
        double v = u == DistanceUnits.Miles ? km / KmPerMile : km;
        return (v / 1e6).ToString("F1", CultureInfo.InvariantCulture) + (u == DistanceUnits.Miles ? " million mi" : " million km");
    }
}
