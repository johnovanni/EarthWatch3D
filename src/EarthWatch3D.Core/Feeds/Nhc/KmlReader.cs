using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using AstroCore.Earth;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Logging;

namespace EarthWatch3D.Core.Feeds.Nhc;

/// <summary>Reads lines, points and polygons (outer boundaries) from a KMZ. Elements are matched by local name.</summary>
public static class KmlReader
{
    public static (IReadOnlyList<GeoLine> Lines, IReadOnlyList<GeoPosition> Points, IReadOnlyList<GeoPolygon> Polygons) ReadKmz(Stream kmz, IAppLog? log = null)
    {
        var lines = new List<GeoLine>();
        var points = new List<GeoPosition>();
        var polys = new List<GeoPolygon>();
        using var zip = new ZipArchive(kmz, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase));
        if (entry is null) { log?.Warn("KMZ contains no .kml file"); return (lines, points, polys); }
        XDocument doc;
        using (var es = entry.Open()) doc = XDocument.Load(es);

        foreach (var e in doc.Descendants())
        {
            try
            {
                switch (e.Name.LocalName)
                {
                    case "LineString":
                        // A line starting exactly at -180 yields a zero-length piece on +180; drop pieces with identical points.
                        lines.AddRange(DateLine.SplitLine(Coords(e)).Where(l => l.Points.Any(q => q != l.Points[0])));
                        break;
                    case "Point":
                        var c = Coords(e);
                        if (c.Count > 0) points.Add(new GeoPosition(c[0].Lat, GeoPosition.NormalizeLongitude(c[0].Lon)));
                        break;
                    case "Polygon":
                        var outer = e.Descendants().FirstOrDefault(x => x.Name.LocalName == "outerBoundaryIs");
                        if (outer is not null) polys.AddRange(DateLine.SplitPolygon([Coords(outer)]));
                        break;
                }
            }
            catch (FormatException ex)
            {
                log?.Warn($"KML: {e.Name.LocalName} malformed; skipped", ex);
            }
        }
        return (lines, points, polys);
    }

    private static List<(double Lon, double Lat)> Coords(XElement parent)
    {
        var text = parent.Descendants().FirstOrDefault(x => x.Name.LocalName == "coordinates")?.Value ?? "";
        var result = new List<(double, double)>();
        foreach (var tuple in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = tuple.Split(',');
            if (parts.Length < 2
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
                throw new FormatException($"bad KML coordinate '{tuple}'");
            result.Add((lon, lat));
        }
        return result;
    }
}
