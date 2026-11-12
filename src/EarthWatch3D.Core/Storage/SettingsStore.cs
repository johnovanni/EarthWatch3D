using System.Text.Json;
using System.Text.Json.Serialization;
using EarthWatch3D.Core.Logging;

namespace EarthWatch3D.Core.Storage;

public sealed class SettingsStore(AppPaths paths, IAppLog log)
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

    public void Save(AppSettings s) => AtomicFile.WriteAllText(paths.SettingsFile, JsonSerializer.Serialize(s, Options));
}
