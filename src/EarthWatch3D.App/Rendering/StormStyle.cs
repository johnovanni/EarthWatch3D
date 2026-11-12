using System.Windows.Media;
using EarthWatch3D.Core.Events;

namespace EarthWatch3D.App.Rendering;

/// <summary>Colours and label text for tropical cyclones and EONET storms, by Saffir–Simpson category.</summary>
public static class StormStyle
{
    // Words EONET puts before a storm's name ("Super Typhoon In-fa"). Longest first, so "Super Typhoon" wins over "Typhoon".
    private static readonly string[] TypeWords =
    [
        "Severe Tropical Storm", "Tropical Depression", "Tropical Cyclone", "Tropical Storm", "Super Typhoon",
        "Hurricane", "Typhoon", "Cyclone",
    ];

    /// <param name="category">−1 = tropical depression, 0 = tropical storm, 1–5 = hurricane category (<see cref="SaffirSimpson"/>).</param>
    public static Color CategoryColor(int category) => category switch
    {
        <= -1 => Color.FromRgb(0x5E, 0xBA, 0xFF),
        0 => Color.FromRgb(0x00, 0xFA, 0xF4),
        1 => Color.FromRgb(0xFF, 0xFF, 0xCC),
        2 => Color.FromRgb(0xFF, 0xE7, 0x75),
        3 => Color.FromRgb(0xFF, 0xC1, 0x40),
        4 => Color.FromRgb(0xFF, 0x8F, 0x20),
        _ => Color.FromRgb(0xFF, 0x60, 0x60),
    };

    /// <summary>TD, TS, or C1–C5.</summary>
    public static string CategoryText(int category) => category switch
    {
        <= -1 => "TD",
        0 => "TS",
        _ => "C" + Math.Min(category, 5),
    };

    /// <summary>The storm name (upper case, without GDACS's "-YY") and category: "PENA TS", "CEMPAKA C2".</summary>
    public static string Label(string name, int category) => CycloneMerger.NormalizeName(name) + " " + CategoryText(category);

    /// <summary>An EONET title without the storm-type words, upper case: "Typhoon Cempaka" → "CEMPAKA".</summary>
    public static string EonetName(string title)
    {
        var t = title.Trim();
        foreach (var w in TypeWords)
            if (t.Length > w.Length && t.StartsWith(w + " ", StringComparison.OrdinalIgnoreCase))
            {
                t = t[(w.Length + 1)..].Trim();
                break;
            }
        return t.ToUpperInvariant();
    }
}
