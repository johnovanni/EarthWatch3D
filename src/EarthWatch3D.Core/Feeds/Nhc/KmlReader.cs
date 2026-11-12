using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using AstroCore.Earth;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Logging;

namespace EarthWatch3D.Core.Feeds.Nhc;

/// <summary>Reads lines, points and polygons (outer boundaries) from a KMZ. Elements are matched by local name.</summary>
public static class KmlReader
{
    private const long MaxKmlBytes = 20L * 1024 * 1024;

    private sealed class LimitedStream(Stream inner, long max) : Stream
    {
        private long _read;
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = inner.Read(buffer, offset, count);
            _read += n;
            if (_read > max) throw new InvalidDataException($"KML entry larger than {max / (1024 * 1024)} MB");
            return n;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    public static (IReadOnlyList<GeoLine> Lines, IReadOnlyList<GeoPosition> Points, IReadOnlyList<GeoPolygon> Polygons) ReadKmz(Stream kmz, IAppLog? log = null)
    {
        var lines = new List<GeoLine>();
        var points = new List<GeoPosition>();
        var polys = new List<GeoPolygon>();
        using var zip = new ZipArchive(kmz, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase));
        if (entry is null) { log?.Warn("KMZ contains no .kml file"); return (lines, points, polys); }
        if (entry.Length > MaxKmlBytes) throw new InvalidDataException($"KML entry larger than {MaxKmlBytes / (1024 * 1024)} MB");
        XDocument doc;
        // The declared Length can lie, so the stream itself is capped. DTDs are prohibited and no resolver is used (XXE / entity expansion).
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using (var es = new LimitedStream(entry.Open(), MaxKmlBytes))
        using (var xr = XmlReader.Create(es, settings))
            doc = XDocument.Load(xr);

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
            if (!double.IsFinite(lon) || !double.IsFinite(lat) || Math.Abs(lon) > 720 || Math.Abs(lat) > 90) throw new FormatException($"out-of-range KML coordinate '{tuple}'");
            result.Add((lon, lat));
        }
        return result;
    }
}
