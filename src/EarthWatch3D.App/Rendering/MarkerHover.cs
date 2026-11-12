using System.Windows;
using System.Windows.Media.Media3D;
using AstroCore.Earth;
using EarthWatch3D.Core.Geo;

namespace EarthWatch3D.App.Rendering;

/// <summary>
/// Hover hit-testing and tooltip text for the Sun and Moon markers on the globe (the points directly under them).
/// The markers are billboards, which cannot carry WPF tooltips, so the scene tests the mouse against their
/// projected positions instead.
/// </summary>
public static class MarkerHover
{
    /// <summary>Radius of the markers above the globe, matching <see cref="SunMoonGlobeMarkers"/>.</summary>
    public const double MarkerRadius = 1.01;

    /// <summary>How close (in screen pixels) the mouse must be to a marker's centre.</summary>
    public const double HitRadiusPx = 12;

    /// <summary>True if a marker at <paramref name="p"/> is on the side of the globe facing <paramref name="eye"/>.</summary>
    public static bool IsOnVisibleSide(Point3D eye, GeoPosition p)
    {
        var u = p.ToUnitVector();
        // A point r·u is visible from the eye when the eye lies above its horizon plane: eye·u > r.
        return eye.X * u.X + eye.Y * u.Y + eye.Z * u.Z > MarkerRadius;
    }

    /// <summary>True if <paramref name="mouse"/> is over the marker at <paramref name="p"/> and that marker is visible.</summary>
    public static bool Hits(Point mouse, Projector projector, GeoPosition p)
    {
        if (!IsOnVisibleSide(projector.Eye, p)) return false;
        var u = p.ToUnitVector();
        if (!projector.TryProject(new Point3D(u.X * MarkerRadius, u.Y * MarkerRadius, u.Z * MarkerRadius), out var s))
            return false;
        return (s - mouse).Length <= HitRadiusPx;
    }

    public static string SunText(GeoPosition p) =>
        $"Sun overhead\n{GeoFormat.LatLon(p)}\nThe Sun is directly overhead here: the closest point on Earth to the Sun.";

    public static string MoonText(GeoPosition p) =>
        $"Moon overhead\n{GeoFormat.LatLon(p)}\nThe Moon is directly overhead here: the closest point on Earth to the Moon.";
}
