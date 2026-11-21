using GlobeCore.Storage;

namespace EarthWatch3D.Core.Storage;

/// <summary>
/// Folder layout under the app's data root: GlobeCore's <see cref="AppDataRoot"/> (root, cache, logs, settings, download
/// meter) plus EarthWatch3D's own folders (NWS zones, the event cache and the cloud images).
/// </summary>
public sealed record AppPaths(AppDataRoot Data)
{
    /// <summary>The folder name under %LOCALAPPDATA% (or the package's LocalCache\Local).</summary>
    public const string AppName = "EarthWatch3D";

    public string Root => Data.Root;
    public string CacheDir => Data.CacheDir;
    public string ZonesDir => Path.Combine(CacheDir, "zones");
    public string LogsDir => Data.LogsDir;
    public string SettingsFile => Data.SettingsFile;
    public string EventsFile => Path.Combine(CacheDir, "events.json");
    public string CloudsDir => Path.Combine(CacheDir, "clouds");
    public string DownloadMeterFile => Data.DownloadMeterFile;

    public static AppPaths Default => ForUser(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), PackageIdentity.FamilyName);

    /// <summary>
    /// The data root for a user. Packaged (MSIX) apps get Windows' redirected folder named directly, so the
    /// paths the app shows are the real ones; unpackaged apps use <c>%LOCALAPPDATA%\EarthWatch3D</c>.
    /// </summary>
    public static AppPaths ForUser(string localAppData, string? packageFamilyName) =>
        new(AppDataRoot.ForUser(AppName, localAppData, packageFamilyName));

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(ZonesDir);
        Directory.CreateDirectory(CloudsDir);
        Directory.CreateDirectory(LogsDir);
    }
}
