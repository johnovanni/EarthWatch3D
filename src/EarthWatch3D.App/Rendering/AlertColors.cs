using System.Windows.Media;

namespace EarthWatch3D.App.Rendering;

/// <summary>NWS hazard colours for alert types, and the wind-band colours for cyclone wind areas.</summary>
public static class AlertColors
{
    private static readonly Color Fallback = Color.FromRgb(0xA0, 0xA0, 0xA0);

    private static readonly Dictionary<string, Color> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tornado Warning"] = Rgb(0xFF0000),
        ["Severe Thunderstorm Warning"] = Rgb(0xFFA500),
        ["Flash Flood Warning"] = Rgb(0x8B0000),
        ["Flood Warning"] = Rgb(0x00FF00),
        ["Winter Storm Warning"] = Rgb(0xFF69B4),
        ["Blizzard Warning"] = Rgb(0xFF4500),
        ["Ice Storm Warning"] = Rgb(0x8B008B),
        ["Hurricane Warning"] = Rgb(0xDC143C),
        ["Tropical Storm Warning"] = Rgb(0xB22222),
        ["Storm Warning"] = Rgb(0x9400D3),
        ["Gale Warning"] = Rgb(0xDDA0DD),
        ["High Wind Warning"] = Rgb(0xDAA520),
        ["Excessive Heat Warning"] = Rgb(0xC71585),
        ["Red Flag Warning"] = Rgb(0xFF1493),
        ["Special Marine Warning"] = Rgb(0xFFA500),
        ["Tornado Watch"] = Rgb(0xFFFF00),
        ["Severe Thunderstorm Watch"] = Rgb(0xDB7093),
        ["Flood Watch"] = Rgb(0x2E8B57),
        ["Winter Storm Watch"] = Rgb(0x4682B4),
        ["Hurricane Watch"] = Rgb(0xFF00FF),
        ["Small Craft Advisory"] = Rgb(0xD8BFD8),
        ["Wind Advisory"] = Rgb(0xD2B48C),
    };

    public static readonly Color Cone = Colors.White;

    public static Color For(string eventType) =>
        eventType is not null && Table.TryGetValue(eventType, out var c) ? c : Fallback;

    /// <summary>60 km/h green, 90 km/h orange, 120 km/h and up red; between thresholds the next lower band's colour.</summary>
    public static Color ForWind(double thresholdKmh) =>
        thresholdKmh >= 120 ? Rgb(0xFF3B30) : thresholdKmh >= 90 ? Rgb(0xFF9500) : Rgb(0x34C759);

    private static Color Rgb(int v) => Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
}
