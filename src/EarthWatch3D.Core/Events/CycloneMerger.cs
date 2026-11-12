using System.Text.RegularExpressions;
using EarthWatch3D.Core.Geo;

namespace EarthWatch3D.Core.Events;

public static partial class CycloneMerger
{
    public const double SameStormKm = 300;

    [GeneratedRegex(@"^(?<base>.*[A-Za-z].*)-\d\d$")]
    private static partial Regex YearSuffix();

    /// <summary>Drops a trailing "-YY" (GDACS, e.g. PENA-26) when the rest contains a letter, then trims and upper-cases.</summary>
    public static string NormalizeName(string name)
    {
        var n = name.Trim().ToUpperInvariant();
        var m = YearSuffix().Match(n);
        return m.Success ? m.Groups["base"].Value.Trim() : n;
    }

    /// <summary>
    /// An NHC and a GDACS advisory with the same normalized name within <see cref="SameStormKm"/> are one storm:
    /// the NHC advisory is kept (taking the GDACS wind areas, and the GDACS past track, if it has none) and the GDACS
    /// one dropped. NHC's feed gives only the forecast track and cone, so without GDACS's past track an NHC storm
    /// would show no solid past track at all.
    /// </summary>
    public static IReadOnlyList<CycloneAdvisory> Merge(IReadOnlyList<CycloneAdvisory> latestPerStorm)
    {
        var gdacs = latestPerStorm.Where(a => a.Source == EventSource.Gdacs).ToList();
        if (gdacs.Count == 0) return latestPerStorm;

        var dropped = new HashSet<CycloneAdvisory>(ReferenceEqualityComparer.Instance);
        var result = new List<CycloneAdvisory>(latestPerStorm.Count);
        foreach (var a in latestPerStorm)
        {
            if (a.Source != EventSource.Nhc) continue;
            var match = gdacs.FirstOrDefault(g => !dropped.Contains(g)
                && NormalizeName(g.Name) == NormalizeName(a.Name)
                && GeoMath.DistanceKm(g.Location, a.Location) <= SameStormKm);
            if (match is not null)
            {
                dropped.Add(match);
                var merged = a.WindAreas.Count == 0 ? a with { WindAreas = match.WindAreas } : a;
                result.Add(merged.PastTrack.Count == 0 ? merged with { PastTrack = match.PastTrack } : merged);
            }
            else result.Add(a);
        }
        foreach (var a in latestPerStorm)
            if (a.Source != EventSource.Nhc && !dropped.Contains(a)) result.Add(a);
        return result;
    }
}
