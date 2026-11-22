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
    private const long MaxKmlBytes = 20L * 1024 * 1024;

    /// <summary>One fix per Placemark with atcfdtg (yyyyMMddHH, UTC), lat and lon; intensity (knots) becomes WindKt. Time-ordered, duplicate times dropped.</summary>
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
        foreach (var pm in doc.Descendants().Where(e => e.Name.LocalName == "Placemark"))
        {
            string? Child(string name) => pm.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();
            var dtg = Child("atcfdtg");
            if (dtg is null) continue;
            if (!DateTime.TryParseExact(dtg, "yyyyMMddHH", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
                || !double.TryParse(Child("lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(Child("lon"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
                || lat is < -90 or > 90)
            { log.Warn($"Best track: fix '{dtg}' malformed; skipped"); continue; }
            double? wind = double.TryParse(Child("intensity"), NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : null;
            fixes.Add(new StormFix(t, new GeoPosition(lat, GeoPosition.NormalizeLongitude(lon)), wind));
        }
        return [.. fixes.OrderBy(f => f.TimeUtc).GroupBy(f => f.TimeUtc).Select(g => g.First())];
    }
}
