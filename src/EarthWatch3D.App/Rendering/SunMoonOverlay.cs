using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace EarthWatch3D.App.Rendering;

/// <summary>
/// Draws the Sun and Moon on a canvas over the viewport: an icon at the true position when in view (dimmed and labelled
/// when the Earth is in the way), otherwise an arrow with a label on the screen edge, kept clear of the Sun &amp; Moon card.
/// All elements and label strings are created once and reused.
/// </summary>
public sealed class SunMoonOverlay
{
    public const double Inset = 40, IconSize = 18, ArrowSize = 22, LabelWidth = 110, LabelHeight = 14;
    /// <summary>Opacity of an on-screen icon whose line of sight passes through the Earth.</summary>
    public const double OccludedOpacity = 0.4;
    public static readonly Color SunColor = Color.FromRgb(0xFF, 0xC9, 0x40);
    public static readonly Color MoonColor = Color.FromRgb(0xD0, 0xD4, 0xDC);
    public static readonly Color MoonDark = Color.FromRgb(0x30, 0x34, 0x3C);
    public static readonly Color MoonLit = Color.FromRgb(0xE8, 0xEA, 0xEE);

    private static readonly Geometry Chevron = MakeChevron();
    // Boxed once, so setting an icon's opacity does not allocate.
    private static readonly object OccludedOpacityBox = OccludedOpacity, FullOpacityBox = 1.0;

    /// <summary>
    /// Keeps an edge arrow and its label off the card (a rectangle in overlay coordinates; Rect.Empty when there is none).
    /// If the arrow is inside the card inflated by half a label and half an arrow, it moves down the right edge to just below
    /// that rectangle (no lower than the bottom of the edge). Its angle and its label's offset are unchanged.
    /// </summary>
    public static Point AvoidCard(Point arrow, Rect card, double width, double height)
    {
        if (card.IsEmpty) return arrow;
        var keepOut = card;   // Rect is a struct: no allocation
        keepOut.Inflate(LabelWidth / 2 + ArrowSize / 2, LabelHeight / 2 + ArrowSize / 2);
        if (!keepOut.Contains(arrow)) return arrow;
        return new Point(width - Inset, Math.Min(keepOut.Bottom + 1, height - Inset));
    }

    private sealed class Marker
    {
        public required FrameworkElement Icon;
        public required Path Arrow;
        public required RotateTransform Rotation;
        public required TextBlock Label;
        public required string Text, BehindText, OccludedText;
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

    /// <summary>Hides every element (used while the window is minimised).</summary>
    public void HideAll()
    {
        foreach (var m in new[] { _sun, _moon })
            m.Icon.Visibility = m.Arrow.Visibility = m.Label.Visibility = Visibility.Collapsed;
    }

    /// <param name="card">The Sun &amp; Moon card in overlay coordinates (Rect.Empty if hidden); edge arrows keep clear of it.</param>
    public void Update(Projector projector, Vector3D sunDirection, Point3D moonPosition, double moonFraction, bool waxing, Rect card)
    {
        if (moonFraction != _moonFraction || waxing != _moonWaxing)
        {
            _moonFraction = moonFraction; _moonWaxing = waxing;
            _moonLit.Data = MoonPhaseGlyph.CreateGeometry(moonFraction, waxing, IconSize / 2);
        }
        var moonFromEye = moonPosition - projector.Eye;
        Apply(_sun, DirectionIndicator.Place(sunDirection, projector, Inset),
            DirectionIndicator.IsOccludedByEarth(projector.Eye, sunDirection), card, projector);
        Apply(_moon, DirectionIndicator.Place(moonFromEye, projector, Inset),
            DirectionIndicator.IsOccludedByEarth(projector.Eye, moonFromEye), card, projector);
    }

    private static void Apply(Marker m, IndicatorPlacement p, bool occluded, Rect card, Projector projector)
    {
        if (!p.Visible)
        {
            m.Icon.Visibility = m.Arrow.Visibility = m.Label.Visibility = Visibility.Collapsed;
            return;
        }
        if (p.OnScreen)
        {
            m.Arrow.Visibility = Visibility.Collapsed;
            m.Icon.Visibility = Visibility.Visible;
            var opacity = occluded ? OccludedOpacityBox : FullOpacityBox;
            if (m.Icon.Opacity != (double)opacity) m.Icon.SetValue(UIElement.OpacityProperty, opacity);
            Canvas.SetLeft(m.Icon, p.Position.X - IconSize / 2);
            Canvas.SetTop(m.Icon, p.Position.Y - IconSize / 2);
            if (!occluded) { m.Label.Visibility = Visibility.Collapsed; return; }
            // The Earth is between the camera and the body: say so under the dimmed icon.
            SetLabelText(m, m.OccludedText);
            m.Label.Visibility = Visibility.Visible;
            Canvas.SetLeft(m.Label, p.Position.X - LabelWidth / 2);
            Canvas.SetTop(m.Label, p.Position.Y + IconSize / 2 + 2);
            return;
        }
        m.Icon.Visibility = Visibility.Collapsed;
        m.Arrow.Visibility = m.Label.Visibility = Visibility.Visible;
        var at = AvoidCard(p.Position, card, projector.Width, projector.Height);
        m.Rotation.Angle = p.AngleDeg;
        Canvas.SetLeft(m.Arrow, at.X);
        Canvas.SetTop(m.Arrow, at.Y);
        SetLabelText(m, p.Behind ? m.BehindText : m.Text);
        // Distance along the inward direction that clears the chevron: half the label's extent along that direction.
        double rad = p.AngleDeg * Math.PI / 180;
        double inward = ArrowSize / 2 + 6 + Math.Abs(Math.Cos(rad)) * LabelWidth / 2 + Math.Abs(Math.Sin(rad)) * LabelHeight / 2;
        Canvas.SetLeft(m.Label, at.X - Math.Cos(rad) * inward - LabelWidth / 2);
        Canvas.SetTop(m.Label, at.Y - Math.Sin(rad) * inward - LabelHeight / 2);
    }

    private static void SetLabelText(Marker m, string text)
    {
        if (!ReferenceEquals(m.Label.Text, text)) m.Label.Text = text;
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
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.NoWrap, Visibility = Visibility.Collapsed,
        };
        icon.Visibility = Visibility.Collapsed;
        overlay.Children.Add(icon); overlay.Children.Add(arrow); overlay.Children.Add(label);
        return new Marker
        {
            Icon = icon, Arrow = arrow, Rotation = rotation, Label = label,
            Text = text, BehindText = text + " (behind)", OccludedText = text + " (behind Earth)",
        };
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
