using System.Globalization;
using System.Text.Json;
using AstroCore.Earth;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Logging;

namespace EarthWatch3D.Core.Feeds.Gdacs;

/// <summary>One GDACS tropical-cyclone event (its current episode). Dates have no zone in the feed and are UTC.</summary>
public sealed record GdacsEvent(int EventId, int EpisodeId, string EventName, bool IsCurrent, string Source,
    GeoPosition Position, double? WindKmh, DateTime ModifiedUtc, Uri GeometryUrl, Uri? ReportUrl);

public static class GdacsReader
{
    public static IReadOnlyList<GdacsEvent> ReadEventList(Stream s, IAppLog log)
    {
        using var doc = JsonDocument.Parse(s);
        var result = new List<GdacsEvent>();
        if (!doc.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var f in features.EnumerateArray())
        {
            if (f.ValueKind != JsonValueKind.Object) { log.Warn("GDACS: event is not an object; skipped"); continue; }
            try
            {
                var e = ReadEvent(f, log);
                if (e is not null) result.Add(e);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentOutOfRangeException or OverflowException)
            {
                log.Warn("GDACS: event malformed; skipped", ex);
            }
        }
        return result;
    }

    private static GdacsEvent? ReadEvent(JsonElement f, IAppLog log)
    {
        if (!f.TryGetProperty("properties", out var p) || p.ValueKind != JsonValueKind.Object) { log.Warn("GDACS: event has no properties; skipped"); return null; }
        if (!Int(p, "eventid", out var eventId)) { log.Warn("GDACS: event without eventid; skipped"); return null; }
        if (!Int(p, "episodeid", out var episodeId)) { log.Warn($"GDACS: event {eventId} without episodeid; skipped"); return null; }
        if (!f.TryGetProperty("geometry", out var g) || g.ValueKind != JsonValueKind.Object
            || !g.TryGetProperty("coordinates", out var c) || c.ValueKind != JsonValueKind.Array || c.GetArrayLength() < 2)
        { log.Warn($"GDACS: event {eventId} has no position; skipped"); return null; }
        if (!DateTime.TryParse(Str(p, "datemodified"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var modified))
        { log.Warn($"GDACS: event {eventId} has no valid datemodified; skipped"); return null; }
        string? geomUrl = null, reportUrl = null;
        if (p.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.Object) { geomUrl = Str(u, "geometry"); reportUrl = Str(u, "report"); }
        if (!Uri.TryCreate(geomUrl, UriKind.Absolute, out var geometry)) { log.Warn($"GDACS: event {eventId} has no geometry url; skipped"); return null; }
        Uri? report = Uri.TryCreate(reportUrl, UriKind.Absolute, out var r) ? r : null;
        double? wind = p.TryGetProperty("severitydata", out var sd) && sd.ValueKind == JsonValueKind.Object
            && sd.TryGetProperty("severity", out var sv) && sv.ValueKind == JsonValueKind.Number ? sv.GetDouble() : null;
        var isCurrent = string.Equals(Str(p, "iscurrent"), "true", StringComparison.OrdinalIgnoreCase);
        return new GdacsEvent(eventId, episodeId, Str(p, "eventname") ?? "", isCurrent, Str(p, "source") ?? "",
            new GeoPosition(c[1].GetDouble(), GeoPosition.NormalizeLongitude(c[0].GetDouble())), wind, modified, geometry, report);
    }

    public static CycloneAdvisory ReadGeometry(Stream s, GdacsEvent e, IAppLog log)
    {
        using var doc = JsonDocument.Parse(s);
        var location = e.Position;
        var past = new List<GeoLine>();
        var forecast = new List<GeoLine>();
        var cone = new List<GeoPolygon>();
        var winds = new List<WindArea>();
        var unknown = new HashSet<string>(StringComparer.Ordinal);

        if (doc.RootElement.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in features.EnumerateArray())
            {
                if (f.ValueKind != JsonValueKind.Object) { log.Warn("GDACS: geometry feature is not an object; skipped"); continue; }
                try
                {
                    if (!f.TryGetProperty("properties", out var p) || p.ValueKind != JsonValueKind.Object
                        || !f.TryGetProperty("geometry", out var g) || g.ValueKind != JsonValueKind.Object
                        || !g.TryGetProperty("coordinates", out var coords) || coords.ValueKind != JsonValueKind.Array)
                    { log.Warn($"GDACS: geometry feature of event {e.EventId} incomplete; skipped"); continue; }
                    var cls = Str(p, "Class") ?? "";
                    if (cls == "Point_Centroid")
                    {
                        if (coords.GetArrayLength() >= 2) location = new GeoPosition(coords[1].GetDouble(), GeoPosition.NormalizeLongitude(coords[0].GetDouble()));
                        else log.Warn($"GDACS: centroid of event {e.EventId} has fewer than 2 coordinates; skipped");
                    }
                    else if (cls.StartsWith("Line_Line_", StringComparison.Ordinal))
                    {
                        var isForecast = p.TryGetProperty("forecast", out var fc) && fc.ValueKind == JsonValueKind.True;
                        // SplitLine turns a line that starts exactly at -180 into a zero-length piece on +180 plus the real line; drop such pieces.
                        (isForecast ? forecast : past).AddRange(DateLine.SplitLine(Ring(coords)).Where(l => l.Points.Any(q => q != l.Points[0])));
                    }
                    else if (cls == "Poly_Cones")
                    {
                        foreach (var poly in coords.EnumerateArray()) cone.AddRange(DateLine.SplitPolygon(Rings(poly)));
                    }
                    else if (cls is "Poly_Green" or "Poly_Orange" or "Poly_Red")
                    {
                        var label = Str(p, "polygonlabel")?.Trim() ?? "";
                        if (!label.EndsWith("km/h", StringComparison.Ordinal)) continue;
                        if (!double.TryParse(label[..^4], NumberStyles.Float, CultureInfo.InvariantCulture, out var kmh))
                        { log.Warn($"GDACS: wind band label '{label}' not understood; skipped"); continue; }
                        var isMulti = Str(g, "type") == "MultiPolygon";
                        foreach (var poly in isMulti ? coords.EnumerateArray().ToList() : [coords])
                            foreach (var piece in DateLine.SplitPolygon(Rings(poly))) winds.Add(new WindArea(kmh, piece));
                    }
                    else if (cls.StartsWith("Point_Polygon_Point_", StringComparison.Ordinal)) { }
                    else if (unknown.Add(cls)) log.Info($"GDACS: ignoring geometry class '{cls}'");
                }
                catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentOutOfRangeException or KeyNotFoundException)
                {
                    log.Warn($"GDACS: geometry feature of event {e.EventId} malformed; skipped", ex);
                }
            }
        }

        return new CycloneAdvisory(EventSource.Gdacs, "gdacs:" + e.EventId.ToString(CultureInfo.InvariantCulture), e.ModifiedUtc,
            location, "Tropical cyclone " + e.EventName, e.ReportUrl, e.EventName, e.WindKmh / 1.852, null,
            past, forecast, cone, winds);
    }

    private static List<(double Lon, double Lat)> Ring(JsonElement a)
    {
        var r = new List<(double, double)>(a.GetArrayLength());
        foreach (var pt in a.EnumerateArray())
        {
            if (pt.ValueKind != JsonValueKind.Array || pt.GetArrayLength() < 2) throw new FormatException("coordinate has fewer than 2 numbers");
            r.Add((pt[0].GetDouble(), pt[1].GetDouble()));
        }
        return r;
    }

    private static List<IReadOnlyList<(double Lon, double Lat)>> Rings(JsonElement polygon)
    {
        var rings = new List<IReadOnlyList<(double Lon, double Lat)>>();
        foreach (var ring in polygon.EnumerateArray()) rings.Add(Ring(ring));
        return rings;
    }

    private static bool Int(JsonElement o, string name, out int v)
    {
        v = 0;
        return o.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out v);
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
