using System.Text.Json;
using AstroCore.Earth;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Logging;

namespace EarthWatch3D.Core.Feeds.Usgs;

/// <summary>Reads a USGS summary GeoJSON feed. Times are epoch milliseconds.</summary>
public static class UsgsReader
{
    public static (IReadOnlyList<Earthquake> Quakes, DateTime? GeneratedUtc) Read(Stream geojson, IAppLog log)
    {
        using var doc = JsonDocument.Parse(geojson);
        var result = new List<Earthquake>();
        DateTime? generated = null;
        if (doc.RootElement.TryGetProperty("metadata", out var md) && md.ValueKind == JsonValueKind.Object
            && md.TryGetProperty("generated", out var gen) && gen.ValueKind == JsonValueKind.Number && gen.TryGetInt64(out var genMs))
            generated = FromEpochMs(genMs);
        if (!doc.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            return (result, generated);

        foreach (var f in features.EnumerateArray())
        {
            if (f.ValueKind != JsonValueKind.Object) { log.Warn("USGS: feature is not an object; skipped"); continue; }
            var id = f.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : null;
            try
            {
                if (!f.TryGetProperty("properties", out var p) || p.ValueKind != JsonValueKind.Object) { log.Warn($"USGS: feature {id} has no properties; skipped"); continue; }
                if (!(p.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() == "earthquake")) continue;
                if (id is null) { log.Warn("USGS: feature without id; skipped"); continue; }
                if (!p.TryGetProperty("mag", out var mag) || mag.ValueKind != JsonValueKind.Number) { log.Warn($"USGS: {id} has no magnitude; skipped"); continue; }
                if (!f.TryGetProperty("geometry", out var g) || g.ValueKind != JsonValueKind.Object
                    || !g.TryGetProperty("coordinates", out var c) || c.ValueKind != JsonValueKind.Array || c.GetArrayLength() < 2)
                { log.Warn($"USGS: {id} has no coordinates; skipped"); continue; }
                if (!p.TryGetProperty("time", out var time) || time.ValueKind != JsonValueKind.Number) { log.Warn($"USGS: {id} has no time; skipped"); continue; }

                var lon = c[0].GetDouble();
                var lat = c[1].GetDouble();
                var depth = c.GetArrayLength() > 2 && c[2].ValueKind == JsonValueKind.Number ? c[2].GetDouble() : 0;
                var timeUtc = FromEpochMs(time.GetInt64());
                var updated = p.TryGetProperty("updated", out var u) && u.ValueKind == JsonValueKind.Number ? FromEpochMs(u.GetInt64()) : timeUtc;
                var place = Str(p, "place") ?? "";
                var title = Str(p, "title") ?? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"M {mag.GetDouble():0.#} - {place}");
                Uri? link = Uri.TryCreate(Str(p, "url"), UriKind.Absolute, out var uri) ? uri : null;
                result.Add(new Earthquake(id, timeUtc, new GeoPosition(lat, lon), title, link, mag.GetDouble(), depth, place, updated));
            }
            catch (Exception e) when (e is InvalidOperationException or FormatException or ArgumentOutOfRangeException)
            {
                log.Warn($"USGS: feature {id} malformed; skipped", e);
            }
        }
        return (result, generated);
    }

    private static DateTime FromEpochMs(long ms) => DateTime.UnixEpoch.AddMilliseconds(ms);

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
