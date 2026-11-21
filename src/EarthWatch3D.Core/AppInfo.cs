using System.Reflection;

namespace EarthWatch3D.Core;

public static class AppInfo
{
    /// <summary>The app's name as shown to the user (message boxes, the crash message).</summary>
    public const string Name = "EarthWatch3D";

    public const string ProjectPage = "https://github.com/johnovanni/EarthWatch3D";

    /// <summary>The informational version without the "+commit" build metadata, e.g. "1.0.0".</summary>
    public static string Version { get; } = GetVersion();

    /// <summary>Four-part version for the MSIX manifest, e.g. "1.0.0.0".</summary>
    public static string MsixVersion { get; } = Version + ".0";

    public static string UserAgent { get; } = $"EarthWatch3D/{Version} (+{ProjectPage})";

    /// <summary>Start of each day's log file name in the logs folder (earthwatch3d-yyyyMMdd.log).</summary>
    public const string LogFilePrefix = "earthwatch3d-";

    /// <summary>The tooltip note on an idle feed (true for the lightning and the clouds feeds alike).</summary>
    public const string FeedIdleNote = "Not polled right now (layer off, replay, or not in view)";

    private static string GetVersion()
    {
        var info = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // Drop the "+commit" build metadata that the SDK appends.
        var plus = info?.IndexOf('+') ?? -1;
        return plus >= 0 ? info![..plus] : info ?? "0.0.0";
    }
}
