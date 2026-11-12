using System.Globalization;
using System.IO;
using AstroCore.Earth;

namespace EarthWatch3D.App.Diagnostics;

/// <summary>
/// Diagnostic command-line options (see README, "Diagnostics"):
/// <c>--snapshot &lt;png path&gt;</c> saves a picture of the window once the globe is shown, then exits;
/// <c>--face-sun</c> turns the camera to the Sun first; <c>--delay-ms N</c> waits N ms before the picture (default 1500);
/// <c>--snapshot-recompose</c> (with <c>--snapshot</c>) saves two pictures, <c>-a</c> and <c>-b</c>, the second after the
/// app's clock is moved 6 hours ahead and the globe recomposed;
/// <c>--offline-samples &lt;dir&gt;</c> answers every feed request from the sample files in the folder, with a temporary
/// cache and settings; <c>--display-time &lt;ISO UTC&gt;</c> starts in replay at that time;
/// <c>--look-at &lt;lat,lon&gt;</c> turns the camera to that point once the globe is shown.
/// Unknown arguments are ignored. The older options (snapshot, delay) also ignore a missing or bad value; the newer
/// ones report it in <see cref="Error"/>, one line per problem.
/// </summary>
public sealed record LaunchOptions(string? SnapshotPath, bool FaceSun, int DelayMs, bool SnapshotRecompose = false,
                                   string? OfflineSamplesDir = null, DateTime? DisplayTimeUtc = null,
                                   GeoPosition? LookAt = null, string? Error = null)
{
    public const int DefaultDelayMs = 1500;
    public const string DisplayTimeExample = "2026-11-11T20:00:00Z";

    private static readonly string[] UtcFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mmzzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
    ];

    /// <summary>True when <see cref="Diagnostics.SnapshotDiagnostic"/> has something to do after the first compose.</summary>
    public bool Any => SnapshotPath != null || FaceSun || LookAt != null;

    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        string? snapshot = null, offline = null;
        bool faceSun = false, recompose = false;
        int delay = DefaultDelayMs;
        DateTime? displayTime = null;
        GeoPosition? lookAt = null;
        var errors = new List<string>();
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
                case "--offline-samples":
                    if (Value(args, ref i, errors, "a folder of sample files, such as tests\\Samples") is { } dir) offline = dir;
                    break;
                case "--display-time":
                    if (Value(args, ref i, errors, "a UTC time such as " + DisplayTimeExample) is not { } t) break;
                    if (TryParseUtc(t, out var utc)) displayTime = utc;
                    else errors.Add($"--display-time: '{t}' is not a UTC date and time. Use the ISO form, for example {DisplayTimeExample}.");
                    break;
                case "--look-at":
                    if (Value(args, ref i, errors, "a latitude and longitude such as 10,-165") is not { } p) break;
                    if (TryParseLatLon(p, out var pos)) lookAt = pos;
                    else errors.Add($"--look-at: '{p}' is not a latitude,longitude pair in degrees (latitude −90 to 90, longitude −180 to 180, east positive), for example 10,-165.");
                    break;
            }
        }
        return new LaunchOptions(snapshot, faceSun, delay, recompose, offline, displayTime, lookAt,
                                 errors.Count == 0 ? null : string.Join(Environment.NewLine, errors));
    }

    private static string? Value(IReadOnlyList<string> args, ref int i, List<string> errors, string what)
    {
        if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) return args[++i];
        errors.Add($"{args[i]} needs a value: {what}.");
        return null;
    }

    /// <summary>ISO 8601 with a 'Z' or an offset (a time without a zone is refused rather than guessed), as UTC.</summary>
    public static bool TryParseUtc(string text, out DateTime utc)
    {
        if (DateTimeOffset.TryParseExact(text.Trim(), UtcFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var dto))
        {
            utc = dto.UtcDateTime;
            return true;
        }
        utc = default;
        return false;
    }

    private static bool TryParseLatLon(string text, out GeoPosition position)
    {
        position = default;
        var parts = text.Split(',');
        if (parts.Length != 2
            || !double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
            || !double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)
            || !(Math.Abs(lat) <= 90) || !(Math.Abs(lon) <= 180)) return false;
        position = new GeoPosition(lat, lon);
        return true;
    }

    /// <summary>Inserts "-<paramref name="suffix"/>" before the extension: earth.png → earth-a.png.</summary>
    public static string WithSuffix(string path, string suffix) =>
        Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + "-" + suffix + Path.GetExtension(path));
}
