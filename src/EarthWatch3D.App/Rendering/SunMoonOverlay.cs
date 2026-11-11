using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace EarthWatch3D.App.Rendering;

/// <summary>
/// Draws the Sun and Moon on a canvas over the viewport: an icon at the true position when in view,
/// otherwise an arrow with a label on the screen edge. All elements are created once and reused.
/// </summary>
public sealed class SunMoonOverlay
{
    public const double Inset = 40, IconSize = 18, ArrowSize = 22, LabelWidth = 40, LabelHeight = 14;
    public static readonly Color SunColor = Color.FromRgb(0xFF, 0xC9, 0x40);
    public static readonly Color MoonColor = Color.FromRgb(0xD0, 0xD4, 0xDC);
    public static readonly Color MoonDark = Color.FromRgb(0x30, 0x34, 0x3C);
    public static readonly Color MoonLit = Color.FromRgb(0xE8, 0xEA, 0xEE);

    private static readonly Geometry Chevron = MakeChevron();

    private sealed class Marker
    {
        public required FrameworkElement Icon;
        public required Path Arrow;
        public required RotateTransform Rotation;
        public required TextBlock Label;
    }

    private readonly Marker _sun, _moon;
    private readonly Path _moonLit;
    private double _moonFraction = double.NaN;
    private bool _moonWaxing;

    public SunMoonOverlay(Canvas overlay)
    {
        var sunIcon = new Ellipse { Width = IconSize, Height = IconSize, Fill = Frozen(new SolidColorBrush(SunColor)) };
        _sun = CreateMarker(overlay, sunIcon, SunColor, "Sun");

        var moonIcon = new Canvas { Width = IconSize, Height = IconSize };
        moonIcon.Children.Add(new Ellipse { Width = IconSize, Height = IconSize, Fill = Frozen(new SolidColorBrush(MoonDark)) });
        _moonLit = new Path
        {
            Fill = Frozen(new SolidColorBrush(MoonLit)),
            RenderTransform = new TranslateTransform(IconSize / 2, IconSize / 2),
        };
        moonIcon.Children.Add(_moonLit);
        _moon = CreateMarker(overlay, moonIcon, MoonColor, "Moon");
    }

    public void Update(Projector projector, Vector3D sunDirection, Point3D moonPosition, double moonFraction, bool waxing)
    {
        if (moonFraction != _moonFraction || waxing != _moonWaxing)
        {
            _moonFraction = moonFraction; _moonWaxing = waxing;
            _moonLit.Data = MoonPhaseGlyph.CreateGeometry(moonFraction, waxing, IconSize / 2);
        }
        Apply(_sun, DirectionIndicator.Place(sunDirection, projector, Inset));
        Apply(_moon, DirectionIndicator.Place(moonPosition - projector.Eye, projector, Inset));
    }

    private static void Apply(Marker m, IndicatorPlacement p)
    {
        if (!p.Visible)
        {
            m.Icon.Visibility = m.Arrow.Visibility = m.Label.Visibility = Visibility.Collapsed;
            return;
        }
        if (p.OnScreen)
        {
            m.Arrow.Visibility = m.Label.Visibility = Visibility.Collapsed;
            m.Icon.Visibility = Visibility.Visible;
            Canvas.SetLeft(m.Icon, p.Position.X - IconSize / 2);
            Canvas.SetTop(m.Icon, p.Position.Y - IconSize / 2);
            return;
        }
        m.Icon.Visibility = Visibility.Collapsed;
        m.Arrow.Visibility = m.Label.Visibility = Visibility.Visible;
        m.Rotation.Angle = p.AngleDeg;
        Canvas.SetLeft(m.Arrow, p.Position.X);
        Canvas.SetTop(m.Arrow, p.Position.Y);
        double rad = p.AngleDeg * Math.PI / 180, inward = ArrowSize / 2 + LabelHeight + 6;
        Canvas.SetLeft(m.Label, p.Position.X - Math.Cos(rad) * inward - LabelWidth / 2);
        Canvas.SetTop(m.Label, p.Position.Y - Math.Sin(rad) * inward - LabelHeight / 2);
    }

    private static Marker CreateMarker(Canvas overlay, FrameworkElement icon, Color color, string text)
    {
        var brush = Frozen(new SolidColorBrush(color));
        var rotation = new RotateTransform();
        var arrow = new Path
        {
            Data = Chevron, Stroke = brush, StrokeThickness = 4,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            RenderTransform = rotation, Visibility = Visibility.Collapsed,
        };
        var label = new TextBlock
        {
            Text = text, Foreground = brush, FontSize = 11, Width = LabelWidth, Height = LabelHeight,
            TextAlignment = TextAlignment.Center, Visibility = Visibility.Collapsed,
        };
        icon.Visibility = Visibility.Collapsed;
        overlay.Children.Add(icon); overlay.Children.Add(arrow); overlay.Children.Add(label);
        return new Marker { Icon = icon, Arrow = arrow, Rotation = rotation, Label = label };
    }

    // 22 px chevron pointing along +x, centred on the origin.
    private static Geometry MakeChevron()
    {
        var fig = new PathFigure { StartPoint = new Point(-ArrowSize / 2, -ArrowSize / 2) };
        fig.Segments.Add(new LineSegment(new Point(ArrowSize / 2, 0), true));
        fig.Segments.Add(new LineSegment(new Point(-ArrowSize / 2, ArrowSize / 2), true));
        var g = new PathGeometry([fig]);
        g.Freeze();
        return g;
    }

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
