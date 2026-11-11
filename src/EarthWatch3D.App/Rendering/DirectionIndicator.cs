using System.Windows;
using System.Windows.Media.Media3D;

namespace EarthWatch3D.App.Rendering;

/// <summary>Where to draw an indicator for a direction: on the point itself, or as an arrow on the screen edge.</summary>
/// <param name="Behind">The target is behind the camera (camera-space z &lt; 0).</param>
/// <param name="AngleDeg">Screen-space direction of the target (0 = right, 90 = down).</param>
public readonly record struct IndicatorPlacement(bool Visible, bool OnScreen, Point Position, double AngleDeg, bool Behind = false);

public static class DirectionIndicator
{
    /// <summary>
    /// Places an indicator for a target at <paramref name="fromEye"/> (a vector from the eye; a direction for objects at infinity).
    /// If the target projects inside the inset rectangle it is on screen; otherwise an arrow is placed on the rectangle's border.
    /// </summary>
    public static IndicatorPlacement Place(Vector3D fromEye, Projector projector, double inset)
    {
        double w = projector.Width, h = projector.Height;
        if (!(w >= 2 * inset + 1) || !(h >= 2 * inset + 1) || fromEye.LengthSquared < 1e-30)
            return new IndicatorPlacement(false, false, default, 0);

        var c = projector.ToCameraSpace(fromEye);
        if (c.Z > 0 && projector.TryProject(projector.Eye + fromEye, out var p)
            && p.X >= inset && p.X <= w - inset && p.Y >= inset && p.Y <= h - inset)
            return new IndicatorPlacement(true, true, p, 0);

        bool behind = c.Z < 0;
        double vx = c.X, vy = -c.Y;   // screen axes, y down
        double lateral = Math.Sqrt(vx * vx + vy * vy);
        // Nearly straight behind (within 10 degrees of the back axis): the side is ambiguous, so point down.
        if (lateral < 1e-9 || (behind && lateral < Math.Tan(10 * Math.PI / 180) * -c.Z)) { vx = 0; vy = 1; }
        double sx = vx == 0 ? double.PositiveInfinity : (w / 2 - inset) / Math.Abs(vx);
        double sy = vy == 0 ? double.PositiveInfinity : (h / 2 - inset) / Math.Abs(vy);
        double s = Math.Min(sx, sy);
        return new IndicatorPlacement(true, false, new Point(w / 2 + vx * s, h / 2 + vy * s),
            Math.Atan2(vy, vx) * 180 / Math.PI, behind);
    }
}
