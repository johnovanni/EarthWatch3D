using System.Text.Json;
using System.Text.Json.Serialization;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Logging;

namespace EarthWatch3D.Core.Storage;

/// <summary>On-disk copy of the last event snapshot, so the app has something to show before the first fetch.</summary>
public sealed class EventCache(AppPaths paths, IAppLog log)
{
    private sealed record Dto(List<Earthquake> Earthquakes, List<CycloneAdvisory> Cyclones,
        List<WeatherAlert> Alerts, List<EonetStorm> Eonet, long Version);

    private static readonly JsonSerializerOptions Options = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Null when missing. A corrupt file is deleted, logged and treated as missing.</summary>
    public EventSnapshot? Load()
    {
        var file = paths.EventsFile;
        if (!File.Exists(file)) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(file), Options)
                      ?? throw new JsonException("empty cache");
            if (dto.Earthquakes is null || dto.Cyclones is null || dto.Alerts is null || dto.Eonet is null)
                throw new JsonException("missing lists");
            if (dto.Earthquakes.Contains(null!) || dto.Cyclones.Contains(null!)
                || dto.Alerts.Contains(null!) || dto.Eonet.Contains(null!))
                throw new JsonException("null element in a list");
            return new EventSnapshot(dto.Earthquakes, dto.Cyclones, dto.Alerts, dto.Eonet, dto.Version);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            log.Warn("Event cache unreadable; deleting it", ex);
            try { File.Delete(file); } catch (Exception) { }
            return null;
        }
    }

    /// <summary>Returns false (and logs a warning) if the file could not be written; never throws for I/O failures.</summary>
    public bool Save(EventSnapshot s)
    {
        var dto = new Dto([.. s.Earthquakes], [.. s.Cyclones], [.. s.Alerts], [.. s.Eonet], s.Version);
        if (AtomicFile.TryWriteAllText(paths.EventsFile, JsonSerializer.Serialize(dto, Options), out var error)) return true;
        log.Warn("Could not save event cache", error);
        return false;
    }
}
