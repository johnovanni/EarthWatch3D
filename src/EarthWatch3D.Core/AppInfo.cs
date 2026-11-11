using System.Reflection;

namespace EarthWatch3D.Core;

public static class AppInfo
{
    public const string ProjectPage = "https://github.com/johnovanni/EarthWatch3D";

    public static string UserAgent { get; } = $"EarthWatch3D/{GetVersion()} (+{ProjectPage})";

    private static string GetVersion()
    {
        var info = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // Drop the "+commit" build metadata that the SDK appends.
        var plus = info?.IndexOf('+') ?? -1;
        return plus >= 0 ? info![..plus] : info ?? "0.0.0";
    }
}
