using System.Windows;
using System.Windows.Media.Media3D;
using AstroCore.Earth;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Geo;

namespace EarthWatch3D.App.Rendering;

/// <summary>
/// Chooses the event under the mouse: the nearest point event (quake, cyclone centre, EONET fix) within a few
/// pixels on the visible side, else the most severe alert whose area contains the surface point under the mouse.
/// </summary>
public static class EventPicker
{
    public const double DefaultHitPx = 10;

    public static EarthEvent? Pick(Point mouse, Projector p, VisibleEvents v, GeoPosition? surfaceUnderMouse, double hitPx = DefaultHitPx) =>
        Pick(mouse, p, v, surfaceUnderMouse, static _ => null, hitPx);

    /// <param name="zoneLookup">Shapes of an NWS zone by its URL, or null while they are not loaded.</param>
    public static EarthEvent? Pick(Point mouse, Projector p, VisibleEvents v, GeoPosition? surfaceUnderMouse,
                                   Func<string, IReadOnlyList<GeoPolygon>?> zoneLookup, double hitPx = DefaultHitPx)
    {
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(zoneLookup);
        EarthEvent? best = null;
        double bestDist = hitPx;

        void Consider(EarthEvent e, GeoPosition at)
        {
            if (!MarkerHover.IsOnVisibleSide(p.Eye, at)) return;
            var u = at.ToUnitVector();
            if (!p.TryProject(new Point3D(u.X, u.Y, u.Z), out var s)) return;
            double d = (s - mouse).Length;
            if (d <= bestDist) { bestDist = d; best = e; }
        }

        foreach (var q in v.Quakes) Consider(q.Quake, q.Quake.Location);
        foreach (var c in v.Cyclones) Consider(c, c.Location);
        foreach (var x in v.Eonet) Consider(x.Storm, x.Fix.Position);
        if (best is not null) return best;

        if (surfaceUnderMouse is not { } pt) return null;
        WeatherAlert? hit = null;
        foreach (var a in v.Alerts)
        {
            if (!AlertContains(a, pt, zoneLookup)) continue;
            if (hit is null || Rank(a) > Rank(hit) || (Rank(a) == Rank(hit) && a.TimeUtc > hit.TimeUtc)) hit = a;
        }
        return hit;
    }

    private static bool AlertContains(WeatherAlert a, GeoPosition pt, Func<string, IReadOnlyList<GeoPolygon>?> zoneLookup)
    {
        foreach (var poly in a.Areas) if (poly.Contains(pt)) return true;
        foreach (var z in a.ZoneIds)
            if (zoneLookup(z) is { } polys)
                foreach (var poly in polys) if (poly.Contains(pt)) return true;
        return false;
    }

    /// <summary>Extreme 4 &gt; Severe 3 &gt; Moderate 2 &gt; Minor 1 &gt; anything else 0.</summary>
    public static int Rank(WeatherAlert a) => a.Severity?.Trim().ToLowerInvariant() switch
    {
        "extreme" => 4,
        "severe" => 3,
        "moderate" => 2,
        "minor" => 1,
        _ => 0,
    };
}
