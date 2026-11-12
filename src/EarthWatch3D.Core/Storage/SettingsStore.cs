using System.Text.Json;
using System.Text.Json.Serialization;
using EarthWatch3D.Core.Logging;

namespace EarthWatch3D.Core.Storage;

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings s);
}

public sealed class SettingsStore(AppPaths paths, IAppLog log) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Defaults when the file is missing or corrupt (corrupt is logged).</summary>
    public AppSettings Load()
    {
        if (!File.Exists(paths.SettingsFile)) return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(paths.SettingsFile), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            log.Warn("Settings file unreadable; using defaults", ex);
            return new AppSettings();
        }
    }

    /// <summary>Returns false (and logs a warning) if the file could not be written; never throws for I/O failures.</summary>
    public bool Save(AppSettings s)
    {
        if (AtomicFile.TryWriteAllText(paths.SettingsFile, JsonSerializer.Serialize(s, Options), out var error)) return true;
        log.Warn("Could not save settings", error);
        return false;
    }

    void ISettingsStore.Save(AppSettings s) => Save(s);
}
