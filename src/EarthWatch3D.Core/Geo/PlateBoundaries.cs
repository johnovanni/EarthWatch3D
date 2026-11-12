using System.Text.Json;

namespace EarthWatch3D.Core.Geo;

public enum PlateBoundaryKind { Convergent, Divergent, Transform }

/// <summary>One plate-boundary polyline of one kind. Its points never cross ±180° longitude.</summary>
public sealed record PlateBoundary(PlateBoundaryKind Kind, GeoLine Line);

/// <summary>
/// Reads the plate boundaries asset (Peter Bird's PB2002 model, merged into polylines by tools/prepare-assets.ps1)
/// and holds the ARGB colours they are drawn in.
/// </summary>
public static class PlateBoundaries
{
    /// <summary>Converging (subduction, collision): red, 85% opaque.</summary>
    public const uint ConvergentArgb = 0xD9FF4D4D;
    /// <summary>Spreading (ridges, rifts): yellow, 85% opaque.</summary>
    public const uint DivergentArgb = 0xD9FFD23F;
    /// <summary>Sliding past (transform faults): blue, 85% opaque.</summary>
    public const uint TransformArgb = 0xD95AC8FA;

    public static uint ArgbOf(PlateBoundaryKind kind) => kind switch
    {
        PlateBoundaryKind.Convergent => ConvergentArgb,
        PlateBoundaryKind.Divergent => DivergentArgb,
        _ => TransformArgb,
    };

    /// <summary>
    /// Each LineString's <c>properties.kind</c> (convergent, divergent or transform) gives its kind; any other kind is skipped.
    /// A line that crosses the date line becomes two or more boundaries. Pieces with no length are dropped.
    /// </summary>
    public static IReadOnlyList<PlateBoundary> Read(Stream geojson)
    {
        using var doc = JsonDocument.Parse(geojson);
        var result = new List<PlateBoundary>();
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("features", out var features)
            || features.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var f in features.EnumerateArray())
        {
            if (f.ValueKind != JsonValueKind.Object
                || !f.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object
                || !props.TryGetProperty("kind", out var k) || k.ValueKind != JsonValueKind.String
                || !f.TryGetProperty("geometry", out var g) || g.ValueKind != JsonValueKind.Object
                || g.GetPropertyOrNull("type")?.GetString() != "LineString"
                || g.GetPropertyOrNull("coordinates") is not { ValueKind: JsonValueKind.Array } coords)
                continue;
            PlateBoundaryKind kind;
            switch (k.GetString())
            {
                case "convergent": kind = PlateBoundaryKind.Convergent; break;
                case "divergent": kind = PlateBoundaryKind.Divergent; break;
                case "transform": kind = PlateBoundaryKind.Transform; break;
                default: continue;
            }

            var pts = new List<(double Lon, double Lat)>(coords.GetArrayLength());
            foreach (var c in coords.EnumerateArray()) pts.Add((c[0].GetDouble(), c[1].GetDouble()));
            IReadOnlyList<GeoLine> pieces;
            try { pieces = DateLine.SplitLine(pts); }
            catch (ArgumentException) { continue; }   // out-of-range coordinates: skip the line
            foreach (var piece in pieces)
                if (piece.Points.Any(p => p != piece.Points[0]))
                    result.Add(new PlateBoundary(kind, piece));
        }
        return result;
    }

    private static JsonElement? GetPropertyOrNull(this JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v : null;
}
