using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using AstroCore.Earth;
using EarthWatch3D.Core.Events;
using GlobeCore.Logging;

namespace EarthWatch3D.Core.Feeds.Nhc;

/// <summary>Reads the timed fixes of an NHC best-track KMZ. Elements are matched by local name.</summary>
public static class BestTrackReader
{
    // Tropical and subtropical cyclone stages. DB (disturbance), LO, WV, EX and PT are not a storm yet or any more.
    private static readonly HashSet<string> Cyclone = new(["TD", "TS", "HU", "TY", "ST", "TC", "MH", "SD", "SS"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> NotCyclone = new(["DB", "LO", "WV", "EX", "PT"], StringComparer.OrdinalIgnoreCase);

    private const long MaxKmlBytes = 20L * 1024 * 1024;

    /// <summary>
    /// One fix per Placemark with atcfdtg (yyyyMMddHH, UTC), lat and lon; intensity (knots) becomes WindKt. Only cyclone
    /// stages are kept (stormType TD, TS, HU, ...; not DB, LO, WV, EX, PT); an unrecognised stormType is kept and logged once.
    /// Time-ordered, duplicate times dropped.
    /// </summary>
    public static IReadOnlyList<StormFix> Read(Stream kmz, IAppLog log)
    {
        using var zip = new ZipArchive(kmz, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase));
        if (entry is null) { log.Warn("Best-track KMZ contains no .kml file"); return []; }
        if (entry.Length > MaxKmlBytes) throw new InvalidDataException($"KML entry larger than {MaxKmlBytes / (1024 * 1024)} MB");
        XDocument doc;
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxKmlBytes };
        using (var xr = XmlReader.Create(entry.Open(), settings))
            doc = XDocument.Load(xr);

        var fixes = new List<StormFix>();
        var unknownTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pm in doc.Descendants().Where(e => e.Name.LocalName == "Placemark"))
        {
            string? Child(string name) => pm.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();
            var dtg = Child("atcfdtg");
            if (dtg is null) continue;
            var type = Child("stormType");
            if (!string.IsNullOrEmpty(type) && !Cyclone.Contains(type))
            {
                if (NotCyclone.Contains(type)) continue;
                if (unknownTypes.Add(type)) log.Info($"Best track: unknown stormType '{type}'; fixes kept");
            }
            if (!DateTime.TryParseExact(dtg, "yyyyMMddHH", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
                || !double.TryParse(Child("lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(Child("lon"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
                || !double.IsFinite(lat) || !double.IsFinite(lon) || lat is < -90 or > 90)
            { log.Warn($"Best track: fix '{dtg}' malformed; skipped"); continue; }
            // NHC writes 0 or -99 where it has no wind value.
            double? wind = double.TryParse(Child("intensity"), NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && double.IsFinite(w) && w > 0 ? w : null;
            fixes.Add(new StormFix(t, new GeoPosition(lat, GeoPosition.NormalizeLongitude(lon)), wind));
        }
        return [.. fixes.OrderBy(f => f.TimeUtc).GroupBy(f => f.TimeUtc).Select(g => g.First())];
    }
}
