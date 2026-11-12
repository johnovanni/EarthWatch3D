using System.Globalization;
using System.Text.Json;
using AstroCore.Earth;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Logging;

namespace EarthWatch3D.Core.Feeds.Eonet;

/// <summary>Reads the EONET v3 events list (severe storms). Each geometry item is one point fix.</summary>
public static class EonetReader
{
    public static IReadOnlyList<EonetStorm> Read(Stream s, IAppLog log)
    {
        using var doc = JsonDocument.Parse(s);
        var result = new List<EonetStorm>();
        if (!doc.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var e in events.EnumerateArray())
        {
            var id = Str(e, "id");
            try
            {
                if (id is null) { log.Warn("EONET: event without id; skipped"); continue; }
                var fixes = new List<StormFix>();
                if (e.TryGetProperty("geometry", out var geo) && geo.ValueKind == JsonValueKind.Array)
                {
                    foreach (var g in geo.EnumerateArray())
                    {
                        var fix = ReadFix(g);
                        if (fix is null) log.Warn($"EONET: {id} has a malformed fix; skipped");
                        else fixes.Add(fix);
                    }
                }
                if (fixes.Count == 0) { log.Warn($"EONET: {id} has no usable fixes; skipped"); continue; }
                fixes.Sort((a, b) => a.TimeUtc.CompareTo(b.TimeUtc));

                var closed = ParseTime(Str(e, "closed"));
                Uri? link = Uri.TryCreate(Str(e, "link"), UriKind.Absolute, out var uri) ? uri : null;
                result.Add(new EonetStorm(id, fixes[0].TimeUtc, fixes[^1].Position, Str(e, "title") ?? id, link, fixes, closed));
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException)
            {
                log.Warn($"EONET: event {id} malformed; skipped", ex);
            }
        }
        return result;
    }

    private static StormFix? ReadFix(JsonElement g)
    {
        if (g.ValueKind != JsonValueKind.Object) return null;
        if (!g.TryGetProperty("coordinates", out var c) || c.ValueKind != JsonValueKind.Array || c.GetArrayLength() < 2
            || c[0].ValueKind != JsonValueKind.Number || c[1].ValueKind != JsonValueKind.Number) return null;
        var time = ParseTime(Str(g, "date"));
        if (time is null) return null;
        double? wind = null;
        if (Str(g, "magnitudeUnit") == "kts" && g.TryGetProperty("magnitudeValue", out var m) && m.ValueKind == JsonValueKind.Number)
            wind = m.GetDouble();
        return new StormFix(time.Value, new GeoPosition(c[1].GetDouble(), c[0].GetDouble()), wind);
    }

    private static DateTime? ParseTime(string? s) =>
        s is not null && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
            ? t.UtcDateTime : null;

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
