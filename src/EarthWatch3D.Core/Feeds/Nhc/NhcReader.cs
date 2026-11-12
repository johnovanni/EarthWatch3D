using System.Globalization;
using System.Text.Json;
using AstroCore.Earth;
using EarthWatch3D.Core.Logging;

namespace EarthWatch3D.Core.Feeds.Nhc;

/// <summary>One active NHC storm from CurrentStorms.json. Adv/Kmz fields are null when the product is absent.</summary>
public sealed record NhcStorm(string Id, string Name, string Classification, double? WindKt, double? PressureMb,
    GeoPosition Position, DateTime LastUpdateUtc, string? TrackAdv, Uri? TrackKmz, string? ConeAdv, Uri? ConeKmz);

public static class NhcReader
{
    public static IReadOnlyList<NhcStorm> ReadCurrentStorms(Stream s, IAppLog log)
    {
        using var doc = JsonDocument.Parse(s);
        var result = new List<NhcStorm>();
        if (!doc.RootElement.TryGetProperty("activeStorms", out var storms) || storms.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var st in storms.EnumerateArray())
        {
            try
            {
                var storm = ReadStorm(st, log);
                if (storm is not null) result.Add(storm);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentOutOfRangeException or OverflowException)
            {
                log.Warn("NHC: storm malformed; skipped", ex);
            }
        }
        return result;
    }

    private static NhcStorm? ReadStorm(JsonElement st, IAppLog log)
    {
        if (st.ValueKind != JsonValueKind.Object) { log.Warn("NHC: storm is not an object; skipped"); return null; }
        var id = Str(st, "id");
        if (string.IsNullOrWhiteSpace(id)) { log.Warn("NHC: storm without id; skipped"); return null; }
        if (!Num(st, "latitudeNumeric", out var lat) || !Num(st, "longitudeNumeric", out var lon))
        { log.Warn($"NHC: storm {id} has no position; skipped"); return null; }
        if (!DateTime.TryParse(Str(st, "lastUpdate"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var updated))
        { log.Warn($"NHC: storm {id} has no valid lastUpdate; skipped"); return null; }
        var (trackAdv, trackKmz) = Product(st, "forecastTrack");
        var (coneAdv, coneKmz) = Product(st, "trackCone");
        return new NhcStorm(id, Str(st, "name") ?? "", Str(st, "classification") ?? "", Parse(Str(st, "intensity")),
            Parse(Str(st, "pressure")), new GeoPosition(lat, GeoPosition.NormalizeLongitude(lon)), updated,
            trackAdv, trackKmz, coneAdv, coneKmz);
    }

    private static (string? Adv, Uri? Kmz) Product(JsonElement st, string name)
    {
        if (!st.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Object) return (null, null);
        return (Str(p, "advNum"), Uri.TryCreate(Str(p, "kmzFile"), UriKind.Absolute, out var u) ? u : null);
    }

    private static double? Parse(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static bool Num(JsonElement o, string name, out double v)
    {
        v = 0;
        return o.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out v);
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
