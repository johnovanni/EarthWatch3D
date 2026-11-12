using System.Globalization;
using System.Text.Json;
using AstroCore.Earth;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Logging;

namespace EarthWatch3D.Core.Feeds.Nws;

public static class NwsReader
{
    /// <summary>Reads one page of an alert collection. Next = pagination.next, or null.</summary>
    public static (IReadOnlyList<WeatherAlert> Alerts, Uri? Next) ReadPage(Stream s, IAppLog log)
    {
        using var doc = JsonDocument.Parse(s);
        var alerts = new List<WeatherAlert>();
        Uri? next = null;
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return (alerts, null);
        if (root.TryGetProperty("pagination", out var pg) && pg.ValueKind == JsonValueKind.Object
            && Str(pg, "next") is { } n && Uri.TryCreate(n, UriKind.Absolute, out var nu))
            next = nu;
        if (!root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            return (alerts, next);
        foreach (var f in features.EnumerateArray())
        {
            if (f.ValueKind != JsonValueKind.Object) { log.Warn("NWS: alert is not an object; skipped"); continue; }
            try
            {
                var a = ReadAlert(f, log);
                if (a is not null) alerts.Add(a);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException or KeyNotFoundException)
            {
                log.Warn("NWS: alert malformed; skipped", ex);
            }
        }
        return (alerts, next);
    }

    private static WeatherAlert? ReadAlert(JsonElement f, IAppLog log)
    {
        if (!f.TryGetProperty("properties", out var p) || p.ValueKind != JsonValueKind.Object)
        { log.Warn("NWS: alert has no properties; skipped"); return null; }
        var featureId = Str(f, "id");
        var id = Str(p, "id") ?? featureId;
        if (id is null) { log.Warn("NWS: alert without id; skipped"); return null; }
        var evt = Str(p, "event");
        if (evt is null) { log.Warn($"NWS: alert {id} without event; skipped"); return null; }
        var effective = Time(p, "effective");
        var sent = Time(p, "sent") ?? effective;
        var onset = Time(p, "onset") ?? effective;
        var ends = Time(p, "ends") ?? Time(p, "expires");
        if (sent is null || onset is null || ends is null)
        { log.Warn($"NWS: alert {id} has no usable times; skipped"); return null; }

        IReadOnlyList<GeoPolygon> areas = [];
        if (f.TryGetProperty("geometry", out var g) && g.ValueKind == JsonValueKind.Object)
            areas = ReadGeometry(g);
        var zones = new List<string>();
        if (areas.Count == 0 && p.TryGetProperty("affectedZones", out var az) && az.ValueKind == JsonValueKind.Array)
            foreach (var z in az.EnumerateArray())
                if (z.ValueKind == JsonValueKind.String && z.GetString() is { Length: > 0 } zs) zones.Add(zs);

        var location = areas.Count > 0 ? Centroid(areas[0]) : new GeoPosition(0, 0);
        Uri? link = Uri.TryCreate(featureId, UriKind.Absolute, out var lu) && lu.Scheme == Uri.UriSchemeHttps ? lu : null;
        // A Cancel message is itself ended at its own sent time, so it never shows; the alerts it references are ended via SupersedesIds.
        bool isCancel = Str(p, "messageType") == "Cancel";
        DateTime? cancelled = isCancel ? sent : null;
        var supersedes = new List<string>();
        if (p.TryGetProperty("references", out var refs) && refs.ValueKind == JsonValueKind.Array)
            foreach (var rf in refs.EnumerateArray())
                if (rf.ValueKind == JsonValueKind.Object && Str(rf, "identifier") is { Length: > 0 } rid) supersedes.Add(rid);
        return new WeatherAlert(id, sent.Value, location, Str(p, "headline") ?? evt, link, evt,
            Str(p, "severity") ?? "Unknown", onset.Value, ends.Value, cancelled, areas, zones, Str(p, "description") ?? "", supersedes,
            isCancel ? AlertEndReason.Cancelled : null);
    }

    /// <summary>Polygon or MultiPolygon geometry; every polygon goes through <see cref="DateLine.SplitPolygon"/>.</summary>
    internal static IReadOnlyList<GeoPolygon> ReadGeometry(JsonElement g)
    {
        var result = new List<GeoPolygon>();
        if (!g.TryGetProperty("coordinates", out var c) || c.ValueKind != JsonValueKind.Array) return result;
        var type = Str(g, "type");
        if (type == "Polygon") result.AddRange(DateLine.SplitPolygon(Rings(c)));
        else if (type == "MultiPolygon")
            foreach (var poly in c.EnumerateArray()) result.AddRange(DateLine.SplitPolygon(Rings(poly)));
        return result;
    }

    private static GeoPosition Centroid(GeoPolygon poly)
    {
        var ring = poly.Rings[0];
        var n = ring.Count > 1 && ring[0] == ring[^1] ? ring.Count - 1 : ring.Count;
        if (n == 0) return new GeoPosition(0, 0);
        double lat = 0, lon = 0;
        for (var i = 0; i < n; i++) { lat += ring[i].LatitudeDeg; lon += ring[i].LongitudeDeg; }
        return new GeoPosition(lat / n, lon / n);
    }

    private static List<IReadOnlyList<(double Lon, double Lat)>> Rings(JsonElement polygon)
    {
        var rings = new List<IReadOnlyList<(double Lon, double Lat)>>();
        foreach (var ring in polygon.EnumerateArray())
        {
            var r = new List<(double, double)>(ring.GetArrayLength());
            foreach (var pt in ring.EnumerateArray())
            {
                if (pt.ValueKind != JsonValueKind.Array || pt.GetArrayLength() < 2) throw new FormatException("coordinate has fewer than 2 numbers");
                r.Add((pt[0].GetDouble(), pt[1].GetDouble()));
            }
            rings.Add(r);
        }
        return rings;
    }

    private static DateTime? Time(JsonElement o, string name) =>
        Str(o, name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? t.UtcDateTime : null;

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
