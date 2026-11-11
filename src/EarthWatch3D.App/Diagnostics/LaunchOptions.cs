using System.Globalization;

namespace EarthWatch3D.App.Diagnostics;

/// <summary>
/// Diagnostic command-line options (see README, "Diagnostics"):
/// <c>--snapshot &lt;png path&gt;</c> saves a picture of the window once the globe is shown, then exits;
/// <c>--face-sun</c> turns the camera to the Sun first; <c>--delay-ms N</c> waits N ms before the picture (default 1500).
/// Unknown arguments are ignored.
/// </summary>
public sealed record LaunchOptions(string? SnapshotPath, bool FaceSun, int DelayMs)
{
    public const int DefaultDelayMs = 1500;

    public bool Any => SnapshotPath != null || FaceSun;

    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        string? snapshot = null;
        bool faceSun = false;
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
                case "--delay-ms" when i + 1 < args.Count
                                       && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int ms):
                    delay = ms;
                    i++;
                    break;
            }
        }
        return new LaunchOptions(snapshot, faceSun, delay);
    }
}
