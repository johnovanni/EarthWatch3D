using System.Windows;
using System.Windows.Media;

namespace EarthWatch3D.App.Rendering;

public static class MoonPhaseGlyph
{
    /// <summary>
    /// The lit part of a moon disc centred at (0,0). Waxing lights the right-hand side (northern hemisphere view).
    /// Bounded by the lit half-circle and the terminator half-ellipse (semi-minor axis r·|2k−1|).
    /// </summary>
    public static Geometry CreateGeometry(double illuminatedFraction, bool waxing, double radius)
    {
        double k = Math.Clamp(illuminatedFraction, 0, 1);
        double rx = radius * Math.Abs(2 * k - 1);
        var fig = new PathFigure { StartPoint = new Point(0, -radius), IsClosed = true, IsFilled = true };
        // Lit limb, top to bottom: clockwise (through the right) when waxing, counter-clockwise when waning.
        fig.Segments.Add(new ArcSegment(new Point(0, radius), new Size(radius, radius), 0, false,
            waxing ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true));
        // Terminator, bottom to top: bulges to the dark side when k > 0.5, to the lit side when k < 0.5.
        bool bulgesToDark = k > 0.5;
        bool clockwise = bulgesToDark == waxing;          // bottom -> left -> top is clockwise
        if (rx < 1e-9) fig.Segments.Add(new LineSegment(new Point(0, -radius), true));
        else fig.Segments.Add(new ArcSegment(new Point(0, -radius), new Size(rx, radius), 0, false,
            clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true));
        var g = new PathGeometry([fig]);
        g.Freeze();
        return g;
    }
}
