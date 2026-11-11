using System.Globalization;
using System.IO;

namespace EarthWatch3D.App.Diagnostics;

/// <summary>
/// Diagnostic command-line options (see README, "Diagnostics"):
/// <c>--snapshot &lt;png path&gt;</c> saves a picture of the window once the globe is shown, then exits;
/// <c>--face-sun</c> turns the camera to the Sun first; <c>--delay-ms N</c> waits N ms before the picture (default 1500);
/// <c>--snapshot-recompose</c> (with <c>--snapshot</c>) saves two pictures, <c>-a</c> and <c>-b</c>, the second after the
/// app's clock is moved 6 hours ahead and the globe recomposed.
/// Unknown arguments are ignored.
/// </summary>
public sealed record LaunchOptions(string? SnapshotPath, bool FaceSun, int DelayMs, bool SnapshotRecompose = false)
{
    public const int DefaultDelayMs = 1500;

    public bool Any => SnapshotPath != null || FaceSun;

    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        string? snapshot = null;
        bool faceSun = false, recompose = false;
        int delay = DefaultDelayMs;
        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--snapshot" when i + 1 < args.Count:
                    snapshot = args[++i];
                    break;
                case "--face-sun":
                    faceSun = true;
                    break;
                case "--snapshot-recompose":
                    recompose = true;
                    break;
                case "--delay-ms" when i + 1 < args.Count
                                       && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int ms):
                    delay = ms;
                    i++;
                    break;
            }
        }
        return new LaunchOptions(snapshot, faceSun, delay, recompose);
    }

    /// <summary>Inserts "-<paramref name="suffix"/>" before the extension: earth.png → earth-a.png.</summary>
    public static string WithSuffix(string path, string suffix) =>
        Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + "-" + suffix + Path.GetExtension(path));
}
