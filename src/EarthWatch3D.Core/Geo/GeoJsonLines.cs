using System.Text.Json;
using AstroCore.Earth;

namespace EarthWatch3D.Core.Geo;

/// <summary>Reads line geometry from a GeoJSON stream.</summary>
public static class GeoJsonLines
{
    /// <summary>
    /// Each LineString, and each part of a MultiLineString, becomes one polyline.
    /// Other geometry types (and null geometries) are skipped. GeoJSON order is [lon, lat].
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<GeoPosition>> Read(Stream geojson)
    {
        using var doc = JsonDocument.Parse(geojson);
        var result = new List<IReadOnlyList<GeoPosition>>();
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("features", out var features)
            && features.ValueKind == JsonValueKind.Array)
        {
            foreach (var feature in features.EnumerateArray())
            {
                if (feature.ValueKind == JsonValueKind.Object
                    && feature.TryGetProperty("geometry", out var geometry))
                    AddGeometry(geometry, result);
            }
        }
        else
        {
            AddGeometry(root, result);
        }
        return result;
    }

    private static void AddGeometry(JsonElement geometry, List<IReadOnlyList<GeoPosition>> result)
    {
        if (geometry.ValueKind != JsonValueKind.Object
            || !geometry.TryGetProperty("type", out var typeEl)
            || !geometry.TryGetProperty("coordinates", out var coords)
            || coords.ValueKind != JsonValueKind.Array)
            return;

        switch (typeEl.GetString())
        {
            case "LineString":
                result.Add(ReadLine(coords));
                break;
            case "MultiLineString":
                foreach (var part in coords.EnumerateArray())
                    result.Add(ReadLine(part));
                break;
        }
    }

    private static List<GeoPosition> ReadLine(JsonElement coordinates)
    {
        var line = new List<GeoPosition>(coordinates.GetArrayLength());
        foreach (var c in coordinates.EnumerateArray())
            line.Add(new GeoPosition(c[1].GetDouble(), c[0].GetDouble()));
        return line;
    }
}
