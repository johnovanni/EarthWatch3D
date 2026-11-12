using AstroCore.Earth;

namespace EarthWatch3D.Core.Events;

/// <summary>Names a tropical cyclone's strength the way its basin does, from the storm's current position.</summary>
public static class StormNames
{
    /// <summary>
    /// <paramref name="category"/> is <see cref="SaffirSimpson.Category"/> (−1 depression, 0 storm, 1–5). Categories 1–5 are
    /// "Category N hurricane" in the North Atlantic (lat ≥ 0, lon −100..0) and the NE/Central Pacific (lat ≥ 0,
    /// lon −180..−100), "Category N typhoon" in the NW Pacific (lat ≥ 0, lon 100..180), and elsewhere, where neither word
    /// is used, "Category N tropical cyclone (Saffir–Simpson)". Weaker storms are a tropical storm or depression everywhere.
    /// </summary>
    public static string CategoryText(int category, GeoPosition position)
    {
        if (category <= -1) return "Tropical depression";
        if (category == 0) return "Tropical storm";
        int c = Math.Min(category, 5);
        double lat = position.LatitudeDeg, lon = position.LongitudeDeg;
        if (lat >= 0 && lon >= -180 && lon <= 0) return $"Category {c} hurricane";   // N Atlantic and NE/C Pacific
        if (lat >= 0 && lon >= 100 && lon <= 180) return $"Category {c} typhoon";    // NW Pacific, west of the date line
        return $"Category {c} tropical cyclone (Saffir–Simpson)";
    }
}
