using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Geo;

namespace EarthWatch3D.Core.Storage;

/// <summary>User settings. Unknown JSON keys are ignored and missing keys take these defaults.</summary>
public sealed record AppSettings
{
    public bool ShowQuakes { get; init; } = true;
    public bool ShowCyclones { get; init; } = true;
    public bool ShowAlerts { get; init; } = true;
    public bool ShowEonet { get; init; } = true;
    public bool ShowCoastlines { get; init; } = true;
    /// <summary>Tectonic plate boundaries (PB2002), off by default.</summary>
    public bool ShowPlates { get; init; } = false;
    /// <summary>Lightning flashes (live only), on by default.</summary>
    public bool ShowLightning { get; init; } = true;
    /// <summary>The daily cloud image (NASA GIBS), off by default: it costs a 2 MB download a day.</summary>
    public bool ShowClouds { get; init; } = false;
    /// <summary>How many minutes of lightning stay on the globe: 5, 10, 15, 30 or 60.</summary>
    public int LightningWindowMinutes { get; init; } = 15;
    /// <summary>Minimum earthquake magnitude; NaN means "All".</summary>
    public double MinMagnitude { get; init; } = 2.5;
    /// <summary>1 or 7.</summary>
    public int QuakeWindowDays { get; init; } = 1;
    public AlertTypes AlertTypes { get; init; } = AlertTypes.Warnings;
    public double NightStrength { get; init; } = 0.85;
    public bool ShowTerminator { get; init; } = true;
    public bool LayerStripExpanded { get; init; } = false;
    /// <summary>Daily download limit in megabytes (<see cref="MinDailyLimitMb"/> to <see cref="MaxDailyLimitMb"/>); null means no limit.</summary>
    public int? DailyLimitMb { get; init; } = null;
    public const int MinDailyLimitMb = 10, MaxDailyLimitMb = 100_000;
    /// <summary>Kilometres or miles for every distance and speed shown.</summary>
    public DistanceUnits Units { get; init; } = DistanceUnits.Kilometres;
    /// <summary>True once the first-run Welcome window has been closed (any way), so it is not shown again.</summary>
    public bool WelcomeShown { get; init; } = false;
    /// <summary>The EUMETSAT consumer key, encrypted (base64 DPAPI blob); null means not set. Never stored in plain text.</summary>
    public string? EumetsatKeyProtected { get; init; } = null;
    /// <summary>The EUMETSAT consumer secret, encrypted (base64 DPAPI blob); null means not set.</summary>
    public string? EumetsatSecretProtected { get; init; } = null;

    /// <summary>
    /// The daily limit in bytes (decimal megabytes, as the meter shows) for a limit in megabytes; null for no limit. A value
    /// outside the allowed range (a hand-edited settings file, say 0 or negative) is ignored: no limit.
    /// </summary>
    public static long? DailyLimitBytes(int? limitMb) =>
        limitMb is { } mb && mb >= MinDailyLimitMb && mb <= MaxDailyLimitMb ? mb * 1_000_000L : null;

    public DisplayFilter ToFilter() => new(ShowQuakes, ShowCyclones, ShowAlerts, ShowEonet,
        double.IsNaN(MinMagnitude) ? double.NegativeInfinity : MinMagnitude,
        TimeSpan.FromDays(QuakeWindowDays), AlertTypes);
}
