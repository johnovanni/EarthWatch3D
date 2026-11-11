using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using AstroCore.Earth;
using HelixToolkit.Wpf;

namespace EarthWatch3D.App.Rendering;

/// <summary>Small camera-facing Sun and Moon (phase-drawn) markers on the globe, at the points directly under them.</summary>
public sealed class SunMoonGlobeMarkers
{
    private const double Radius = 1.01, SizePx = 20;

    private readonly BillboardVisual3D _sun, _moon;
    private double _fraction = double.NaN;
    private bool _waxing;

    public SunMoonGlobeMarkers()
    {
        _sun = new BillboardVisual3D { Width = SizePx, Height = SizePx, Material = MaterialFor(SunImage()) };
        _moon = new BillboardVisual3D { Width = SizePx, Height = SizePx };
        Visuals = [_sun, _moon];
    }

    public IReadOnlyList<Visual3D> Visuals { get; }

    public void Update(GeoPosition subSolar, GeoPosition subLunar, double moonFraction, bool waxing)
    {
        SetPosition(_sun, subSolar);
        SetPosition(_moon, subLunar);
        if (moonFraction != _fraction || waxing != _waxing)
        {
            _fraction = moonFraction; _waxing = waxing;
            _moon.Material = MaterialFor(MoonImage(moonFraction, waxing));
        }
    }

    private static void SetPosition(BillboardVisual3D b, GeoPosition g)
    {
        var v = g.ToUnitVector();
        var p = new Point3D(v.X * Radius, v.Y * Radius, v.Z * Radius);
        if (b.Position != p) b.Position = p;
    }

    private static Material MaterialFor(ImageSource image)
    {
        var brush = new ImageBrush(image);
        brush.Freeze();
        var m = new DiffuseMaterial(brush);
        m.Freeze();
        return m;
    }

    private static readonly Rect Square = new(0, 0, 20, 20);

    private static DrawingImage SunImage()
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(Square)));
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(SunMoonOverlay.SunColor), new Pen(Brushes.White, 1),
            new EllipseGeometry(new Point(10, 10), 9, 9)));
        return Freeze(g);
    }

    private static DrawingImage MoonImage(double fraction, bool waxing)
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(Square)));
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(SunMoonOverlay.MoonDark), new Pen(Brushes.White, 1),
            new EllipseGeometry(new Point(10, 10), 9, 9)));
        var lit = new PathGeometry(((PathGeometry)MoonPhaseGlyph.CreateGeometry(fraction, waxing, 9)).Figures.CloneCurrentValue())
        { Transform = new TranslateTransform(10, 10) };
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(SunMoonOverlay.MoonLit), null, lit));
        return Freeze(g);
    }

    private static DrawingImage Freeze(DrawingGroup g)
    {
        g.Freeze();
        var img = new DrawingImage(g);
        img.Freeze();
        return img;
    }
}
