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
    /// <summary>Daily download limit in megabytes; null means no limit.</summary>
    public int? DailyLimitMb { get; init; } = null;
    /// <summary>Kilometres or miles for every distance and speed shown.</summary>
    public DistanceUnits Units { get; init; } = DistanceUnits.Kilometres;

    public DisplayFilter ToFilter() => new(ShowQuakes, ShowCyclones, ShowAlerts, ShowEonet,
        double.IsNaN(MinMagnitude) ? double.NegativeInfinity : MinMagnitude,
        TimeSpan.FromDays(QuakeWindowDays), AlertTypes);
}
