namespace EarthWatch3D.Core.Storage;

/// <summary>Folder layout under the app's data root.</summary>
public sealed record AppPaths(string Root)
{
    public string CacheDir => Path.Combine(Root, "cache");
    public string ZonesDir => Path.Combine(CacheDir, "zones");
    public string LogsDir => Path.Combine(Root, "logs");
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string EventsFile => Path.Combine(CacheDir, "events.json");

    public static AppPaths Default => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EarthWatch3D"));

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(ZonesDir);
        Directory.CreateDirectory(LogsDir);
    }
}

internal static class AtomicFile
{
    /// <summary>Writes to <c>file.tmp</c>, then moves it over <paramref name="file"/>.</summary>
    public static void WriteAllText(string file, string text)
    {
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, file, overwrite: true);
    }
}
