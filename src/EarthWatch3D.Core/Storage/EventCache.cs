using System.Text.Json;
using System.Text.Json.Serialization;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Logging;

namespace EarthWatch3D.Core.Storage;

/// <summary>On-disk copy of the last event snapshot, so the app has something to show before the first fetch.</summary>
public sealed class EventCache(AppPaths paths, IAppLog log)
{
    /// <summary>Raised whenever the saved shape or meaning of the records changes; an older file is discarded.</summary>
    public const int CurrentSchemaVersion = 2;

    private sealed record Dto(int SchemaVersion, List<Earthquake> Earthquakes, List<CycloneAdvisory> Cyclones,
        List<WeatherAlert> Alerts, List<EonetStorm> Eonet, long Version);

    private static readonly JsonSerializerOptions Options = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Null when missing. A file from another schema version, a corrupt file, or one whose records break the model's
    /// rules (a null list or a null required string inside a record) is deleted, logged and treated as missing.
    /// </summary>
    public EventSnapshot? Load()
    {
        var file = paths.EventsFile;
        if (!File.Exists(file)) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(file), Options)
                      ?? throw new JsonException("empty cache");
            if (dto.SchemaVersion != CurrentSchemaVersion)
            {
                log.Warn($"Event cache has schema version {dto.SchemaVersion}, not {CurrentSchemaVersion}; deleting it");
                Delete(file);
                return null;
            }
            if (Problem(dto) is { } problem) throw new JsonException("invalid record: " + problem);
            return new EventSnapshot(dto.Earthquakes, dto.Cyclones, dto.Alerts, dto.Eonet, dto.Version);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            log.Warn("Event cache unreadable; deleting it", ex);
            Delete(file);
            return null;
        }
    }

    private static void Delete(string file)
    {
        try { File.Delete(file); } catch (Exception) { }
    }

    // ---------------------------------------------------------------- validation (JSON can hold nulls the types forbid)

    private static string? Problem(Dto d)
    {
        if (d.Earthquakes is null || d.Cyclones is null || d.Alerts is null || d.Eonet is null) return "missing lists";
        foreach (var q in d.Earthquakes)
            if (q is null) return "null earthquake";
            else if ((EventProblem(q) ?? (q.Place is null ? "Place" : null)) is { } p) return $"earthquake {q.Id}: {p}";
        foreach (var c in d.Cyclones)
            if (c is null) return "null cyclone advisory";
            else if (CycloneProblem(c) is { } p) return $"cyclone {c.Id}: {p}";
        foreach (var a in d.Alerts)
            if (a is null) return "null alert";
            else if (AlertProblem(a) is { } p) return $"alert {a.Id}: {p}";
        foreach (var e in d.Eonet)
            if (e is null) return "null EONET storm";
            else if ((EventProblem(e) ?? (e.Fixes is null || e.Fixes.Contains(null!) ? "Fixes" : null)) is { } p) return $"EONET storm {e.Id}: {p}";
        return null;
    }

    private static string? EventProblem(EarthEvent e) => e.Id is null ? "Id" : e.Title is null ? "Title" : null;

    private static string? CycloneProblem(CycloneAdvisory c) =>
        EventProblem(c)
        ?? (c.Name is null ? "Name" : null)
        ?? (BadLines(c.PastTrack) ? "PastTrack" : null)
        ?? (BadLines(c.ForecastTrack) ? "ForecastTrack" : null)
        ?? (BadPolygons(c.Cone) ? "Cone" : null)
        ?? (c.WindAreas is null || c.WindAreas.Any(w => w is null || BadPolygon(w.Area)) ? "WindAreas" : null);

    private static string? AlertProblem(WeatherAlert a) =>
        EventProblem(a)
        ?? (a.EventType is null ? "EventType" : null)
        ?? (a.Severity is null ? "Severity" : null)
        ?? (a.Description is null ? "Description" : null)
        ?? (BadPolygons(a.Areas) ? "Areas" : null)
        ?? (a.ZoneIds is null || a.ZoneIds.Contains(null!) ? "ZoneIds" : null)
        ?? (a.SupersedesIds is not null && a.SupersedesIds.Contains(null!) ? "SupersedesIds" : null);   // the list itself may be null

    private static bool BadLines(IReadOnlyList<GeoLine>? lines) => lines is null || lines.Any(l => l is null || l.Points is null);
    private static bool BadPolygons(IReadOnlyList<GeoPolygon>? polys) => polys is null || polys.Any(BadPolygon);
    private static bool BadPolygon(GeoPolygon? p) => p is null || p.Rings is null || p.Rings.Contains(null!);

    /// <summary>Returns false (and logs a warning) if the file could not be written; never throws for I/O failures.</summary>
    public bool Save(EventSnapshot s)
    {
        var dto = new Dto(CurrentSchemaVersion, [.. s.Earthquakes], [.. s.Cyclones], [.. s.Alerts], [.. s.Eonet], s.Version);
        if (AtomicFile.TryWriteAllText(paths.EventsFile, JsonSerializer.Serialize(dto, Options), out var error)) return true;
        log.Warn("Could not save event cache", error);
        return false;
    }
}
