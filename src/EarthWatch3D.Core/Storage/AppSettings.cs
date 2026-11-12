using EarthWatch3D.Core.Events;

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

    public DisplayFilter ToFilter() => new(ShowQuakes, ShowCyclones, ShowAlerts, ShowEonet,
        double.IsNaN(MinMagnitude) ? double.NegativeInfinity : MinMagnitude,
        TimeSpan.FromDays(QuakeWindowDays), AlertTypes);
}
