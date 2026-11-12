namespace EarthWatch3D.Core.Storage;

/// <summary>Folder layout under the app's data root.</summary>
public sealed record AppPaths(string Root)
{
    public string CacheDir => Path.Combine(Root, "cache");
    public string ZonesDir => Path.Combine(CacheDir, "zones");
    public string LogsDir => Path.Combine(Root, "logs");
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string EventsFile => Path.Combine(CacheDir, "events.json");
    public string CloudsDir => Path.Combine(CacheDir, "clouds");
    public string DownloadMeterFile => Path.Combine(Root, "download-meter.json");

    public static AppPaths Default => ForUser(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), PackageIdentity.FamilyName);

    /// <summary>
    /// The data root for a user. Packaged (MSIX) apps get Windows' redirected folder named directly, so the
    /// paths the app shows are the real ones; unpackaged apps use <c>%LOCALAPPDATA%\EarthWatch3D</c>.
    /// </summary>
    public static AppPaths ForUser(string localAppData, string? packageFamilyName) => new(
        packageFamilyName is null
            ? Path.Combine(localAppData, "EarthWatch3D")
            : Path.Combine(localAppData, "Packages", packageFamilyName, "LocalCache", "Local", "EarthWatch3D"));

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(ZonesDir);
        Directory.CreateDirectory(CloudsDir);
        Directory.CreateDirectory(LogsDir);
    }
}

internal static class AtomicFile
{
    private const int Attempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Writes to <c>file.tmp</c>, then moves it over <paramref name="file"/>, retrying the move (e.g. while an
    /// antivirus scan holds the destination). Returns false, leaving no .tmp behind, if it still fails.
    /// </summary>
    public static bool TryWriteAllText(string file, string text, out Exception? error)
        => TryWrite(file, tmp => File.WriteAllText(tmp, text), out error);

    /// <summary>Like <see cref="TryWriteAllText"/>, for bytes.</summary>
    public static bool TryWriteAllBytes(string file, byte[] bytes, out Exception? error)
        => TryWrite(file, tmp => File.WriteAllBytes(tmp, bytes), out error);

    private static bool TryWrite(string file, Action<string> writeTemp, out Exception? error)
    {
        error = null;
        var tmp = file + ".tmp";
        try
        {
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            writeTemp(tmp);
            for (var i = 1; ; i++)
            {
                try
                {
                    File.Move(tmp, file, overwrite: true);
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (i >= Attempts) throw;
                    Thread.Sleep(RetryDelay);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex;
            try { File.Delete(tmp); } catch (Exception) { }
            return false;
        }
    }
}
