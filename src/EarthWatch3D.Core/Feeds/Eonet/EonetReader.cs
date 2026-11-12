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
            if (e.ValueKind != JsonValueKind.Object) { log.Warn("EONET: event is not an object; skipped"); continue; }
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

                var closedText = Str(e, "closed");
                var closed = ParseTime(closedText);
                if (closedText is not null && closed is null) log.Warn($"EONET: {id} has unparseable closed value '{closedText}'");
                var link = SourcePage(e) ?? (Uri.TryCreate(Str(e, "link"), UriKind.Absolute, out var uri) ? uri : null);
                result.Add(new EonetStorm(id, fixes[0].TimeUtc, fixes[^1].Position, Str(e, "title") ?? id, link, fixes, closed));
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException)
            {
                log.Warn($"EONET: event {id} malformed; skipped", ex);
            }
        }
        return result;
    }

    /// <summary>
    /// The first source's page (for example the JTWC warning), which people can read; EONET's own <c>link</c> is its
    /// API's JSON, so it is used only when there is no usable source URL.
    /// </summary>
    private static Uri? SourcePage(JsonElement e)
    {
        if (!e.TryGetProperty("sources", out var sources) || sources.ValueKind != JsonValueKind.Array || sources.GetArrayLength() == 0)
            return null;
        var first = sources[0];
        if (first.ValueKind != JsonValueKind.Object) return null;
        return Uri.TryCreate(Str(first, "url"), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
            ? u : null;
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
