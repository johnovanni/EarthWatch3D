using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using AstroCore.Earth;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Geo;
using HelixToolkit.Geometry;
using HelixToolkit.Wpf;
using MeshGeometry3D = System.Windows.Media.Media3D.MeshGeometry3D;

namespace EarthWatch3D.App.Rendering;

/// <summary>
/// The events drawn on the globe: earthquake discs (one merged mesh per age colour) with pulsing rings for quakes under
/// an hour old, cyclone glyphs with their past and forecast tracks, EONET storm dots with short tracks, and the labels
/// for M5+ quakes and named storms. UI thread only.
/// </summary>
public sealed class EventScene
{
    public const double DiscRadius = 1.002, TrackRadius = 1.003, GlyphRadius = 1.01;
    /// <summary>Newer colour buckets sit this much higher (Earth radii), so overlapping discs of different ages don't z-fight.</summary>
    public const double BucketLift = 0.00025;
    public const int MaxRings = 50;
    public static readonly TimeSpan RingMaxAge = TimeSpan.FromHours(1);
    public static readonly TimeSpan RingPeriod = TimeSpan.FromSeconds(1.5);

    private const int DiscSegments = 24, RingSegments = 32, EonetTrackFixes = 8;
    private const double GlyphPx = 22, DotPx = 14;
    /// <summary>Rebuild the discs when the minimum on-screen size changes by more than this factor.</summary>
    private const double MinRadiusRebuildRatio = 1.2;

    private static readonly Color QuakeLabelColor = Color.FromRgb(0xF0, 0xF0, 0xF0);
    private static readonly Color EonetTrackColor = Color.FromRgb(0xA0, 0xA0, 0xA0);
    private static readonly Dictionary<(int Category, bool South), Material> GlyphMaterials = [];
    private static readonly Dictionary<int, Material> DotMaterials = [];

    private readonly HelixViewport3D _viewport;
    private readonly ModelVisual3D _root = new();
    private readonly GeometryModel3D[] _discModels = new GeometryModel3D[QuakeStyle.BucketCount];
    private readonly List<Earthquake>[] _discKeys = new List<Earthquake>[QuakeStyle.BucketCount];
    private readonly LinesVisual3D _pastTracks = new() { Color = Colors.White, Thickness = 1.5 };
    private readonly LinesVisual3D _forecastTracks = new() { Color = Colors.White, Thickness = 1.5 };
    private readonly LinesVisual3D _eonetTracks = new() { Color = EonetTrackColor, Thickness = 1 };
    private readonly LinesVisual3D _rings = new() { Color = QuakeStyle.BucketColor(0), Thickness = 1.5 };
    private readonly ModelVisual3D _billboardRoot = new();
    private readonly List<BillboardVisual3D> _billboards = [];
    private readonly List<Label> _labels = [];
    private readonly List<Ring> _ringSpecs = [];

    private VisibleEvents? _visible;
    private double _discMinRadius = double.NaN;
    private IReadOnlyList<CycloneAdvisory> _drawnCyclones = [];
    private IReadOnlyList<VisibleEonet> _drawnEonet = [];

    private readonly record struct Label(string Text, Vector3D Unit, double Radius, Color Color, double FontSize, double OffsetPx);
    private readonly record struct Ring(Point3D Centre, Vector3D E1, Vector3D E2, double Radius);

    public EventScene(HelixViewport3D viewport)
    {
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        var discs = new Model3DGroup();
        for (int b = 0; b < QuakeStyle.BucketCount; b++)
        {
            _discModels[b] = new GeometryModel3D { Material = SolidMaterial(QuakeStyle.BucketColor(b)) };
            _discKeys[b] = [];
            discs.Children.Add(_discModels[b]);
        }
        // Oldest first, so newer (and higher) discs are drawn over older ones.
        _root.Children.Add(new ModelVisual3D { Content = discs });
        _root.Children.Add(_eonetTracks);
        _root.Children.Add(_pastTracks);
        _root.Children.Add(_forecastTracks);
        _root.Children.Add(_rings);
        _root.Children.Add(_billboardRoot);
        _viewport.Children.Add(_root);
    }

    /// <summary>True while pulsing rings are shown; <see cref="AnimateRings"/> need only be called then.</summary>
    public bool HasRings => _ringSpecs.Count > 0;

    /// <summary>Number of quake discs in the meshes (for diagnostics).</summary>
    public int DiscCount => _discKeys.Sum(k => k.Count);

    /// <summary>
    /// Shows <paramref name="v"/>. Cheap when nothing changed: the disc meshes are rebuilt only when a colour bucket's
    /// quakes change or the minimum on-screen disc size moves by more than 20%; tracks and glyphs only when the storms change.
    /// </summary>
    /// <param name="displayTimeUtc">The time <paramref name="v"/> was evaluated at (quake ages for the rings).</param>
    /// <param name="live">Pulsing rings are shown only in live mode.</param>
    /// <param name="cameraDistance">Camera distance from the Earth's centre, in Earth radii.</param>
    public void Update(VisibleEvents v, DateTime displayTimeUtc, bool live, Projector projector, double cameraDistance)
    {
        ArgumentNullException.ThrowIfNull(v);
        double fov = (_viewport.Camera as PerspectiveCamera)?.FieldOfView ?? 45;
        double minRadius = QuakeStyle.MinRadiusForView(cameraDistance, fov, projector.Width);
        bool sizeChanged = double.IsNaN(_discMinRadius)
            || minRadius > _discMinRadius * MinRadiusRebuildRatio || minRadius * MinRadiusRebuildRatio < _discMinRadius;
        bool setChanged = !ReferenceEquals(v, _visible);
        if (!setChanged && !sizeChanged) return;

        if (sizeChanged) _discMinRadius = minRadius;
        UpdateDiscs(v, rebuildAll: sizeChanged);
        UpdateRings(v, displayTimeUtc, live);
        if (setChanged)
        {
            if (!ListEquality.Equal(v.Cyclones, _drawnCyclones) || !ListEquality.Equal(v.Eonet, _drawnEonet)) UpdateStorms(v);
            UpdateLabels(v);
        }
        _visible = v;
    }

    // ---------------------------------------------------------------- quakes

    private void UpdateDiscs(VisibleEvents v, bool rebuildAll)
    {
        var byBucket = new List<VisibleQuake>[QuakeStyle.BucketCount];
        for (int b = 0; b < byBucket.Length; b++) byBucket[b] = [];
        foreach (var q in v.Quakes) byBucket[QuakeStyle.ColorBucket(q.AgeFraction)].Add(q);

        for (int b = 0; b < byBucket.Length; b++)
        {
            var quakes = byBucket[b];
            var key = _discKeys[b];
            if (!rebuildAll && key.Count == quakes.Count && quakes.Select(q => q.Quake).SequenceEqual(key)) continue;
            key.Clear();
            key.AddRange(quakes.Select(q => q.Quake));
            _discModels[b].Geometry = quakes.Count == 0 ? null : DiscMesh(quakes, DiscRadius + (QuakeStyle.BucketCount - 1 - b) * BucketLift);
        }
    }

    private double DiscSize(double magnitude) => Math.Max(QuakeStyle.RadiusEarthRadii(magnitude), _discMinRadius);

    private MeshGeometry3D DiscMesh(List<VisibleQuake> quakes, double radius)
    {
        var mb = new MeshBuilder(false, false, false);
        var fan = new Vector3[DiscSegments + 2];
        foreach (var q in quakes)
        {
            var (centre, e1, e2) = TangentFrame(q.Quake.Location, radius);
            double r = DiscSize(q.Quake.Magnitude);
            fan[0] = ToVector3((Vector3D)centre);
            // e1 × e2 = outward normal, so increasing angle is counter-clockwise seen from outside (WPF's front face).
            for (int i = 0; i <= DiscSegments; i++)
            {
                double t = 2 * Math.PI * i / DiscSegments;
                fan[i + 1] = ToVector3((Vector3D)centre + r * (Math.Cos(t) * e1 + Math.Sin(t) * e2));
            }
            mb.AddTriangleFan(fan, null, null);
        }
        var mesh = mb.ToMesh().ToWndMeshGeometry3D(true);
        if (mesh.CanFreeze && !mesh.IsFrozen) mesh.Freeze();
        return mesh;
    }

    /// <summary>The point at <paramref name="radius"/> over <paramref name="g"/>, and two tangent unit vectors with e1 × e2 pointing outward.</summary>
    private static (Point3D Centre, Vector3D E1, Vector3D E2) TangentFrame(GeoPosition g, double radius)
    {
        var u = g.ToUnitVector();
        var n = new Vector3D(u.X, u.Y, u.Z);
        // East is undefined at the poles; any perpendicular will do there.
        var e1 = Vector3D.CrossProduct(new Vector3D(0, 0, 1), n);
        if (e1.LengthSquared < 1e-12) e1 = new Vector3D(1, 0, 0);
        e1.Normalize();
        var e2 = Vector3D.CrossProduct(n, e1);
        return ((Point3D)(n * radius), e1, e2);
    }

    private static Vector3 ToVector3(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);

    // ---------------------------------------------------------------- pulsing rings

    private void UpdateRings(VisibleEvents v, DateTime displayTimeUtc, bool live)
    {
        _ringSpecs.Clear();
        if (live)
            foreach (var q in v.Quakes
                         .Where(q => displayTimeUtc - q.Quake.TimeUtc < RingMaxAge && q.Quake.TimeUtc <= displayTimeUtc)
                         .OrderByDescending(q => q.Quake.TimeUtc)
                         .Take(MaxRings))
            {
                var (centre, e1, e2) = TangentFrame(q.Quake.Location, TrackRadius);
                _ringSpecs.Add(new Ring(centre, e1, e2, DiscSize(q.Quake.Magnitude)));
            }
        if (_ringSpecs.Count == 0 && _rings.Points is { Count: > 0 }) _rings.Points = new Point3DCollection();
    }

    /// <summary>Advances the rings: each grows from 1.0× to 1.8× its disc's radius and fades out over 1.5 s.</summary>
    public void AnimateRings(TimeSpan time)
    {
        if (_ringSpecs.Count == 0) return;
        double phase = time.TotalSeconds % RingPeriod.TotalSeconds / RingPeriod.TotalSeconds;
        double scale = 1.0 + 0.8 * phase;
        var pts = new Point3DCollection(_ringSpecs.Count * RingSegments * 2);
        foreach (var ring in _ringSpecs)
        {
            double r = ring.Radius * scale;
            var prev = ring.Centre + r * ring.E1;
            for (int i = 1; i <= RingSegments; i++)
            {
                double t = 2 * Math.PI * i / RingSegments;
                var p = ring.Centre + r * (Math.Cos(t) * ring.E1 + Math.Sin(t) * ring.E2);
                pts.Add(prev); pts.Add(p);
                prev = p;
            }
        }
        pts.Freeze();
        var c = QuakeStyle.BucketColor(0);
        _rings.Color = Color.FromArgb((byte)Math.Round(255 * (1 - phase)), c.R, c.G, c.B);
        _rings.Points = pts;
    }

    // ---------------------------------------------------------------- storms

    private void UpdateStorms(VisibleEvents v)
    {
        _drawnCyclones = v.Cyclones;
        _drawnEonet = v.Eonet;

        var past = new Point3DCollection();
        var forecast = new Point3DCollection();
        foreach (var c in v.Cyclones)
        {
            foreach (var path in Chain(c.PastTrack)) AddSegments(past, path);
            foreach (var path in Chain(c.ForecastTrack))
                foreach (var dash in SphereLines.Dashes(path)) AddSegments(forecast, dash);
        }
        var eonet = new Point3DCollection();
        foreach (var e in v.Eonet)
        {
            var fixes = e.Storm.Fixes.Where(f => f.TimeUtc <= e.Fix.TimeUtc).OrderBy(f => f.TimeUtc)
                .TakeLast(EonetTrackFixes).Select(f => f.Position).ToList();
            AddSegments(eonet, SphereLines.Densify(new GeoLine(fixes), TrackRadius));
        }
        past.Freeze(); forecast.Freeze(); eonet.Freeze();
        _pastTracks.Points = past;
        _forecastTracks.Points = forecast;
        _eonetTracks.Points = eonet;

        int n = 0;
        foreach (var c in v.Cyclones)
            SetBillboard(n++, c.Location, GlyphPx, GlyphMaterial(c.Category, c.Location.LatitudeDeg < 0));
        foreach (var e in v.Eonet)
            SetBillboard(n++, e.Fix.Position, DotPx, DotMaterial(SaffirSimpson.Category(e.Fix.WindKt)));
        while (_billboards.Count > n)
        {
            _billboardRoot.Children.Remove(_billboards[^1]);
            _billboards.RemoveAt(_billboards.Count - 1);
        }
    }

    /// <summary>Densified tracks, with lines that continue one another (GDACS sends one line per leg) joined into one path.</summary>
    private static List<IReadOnlyList<Point3D>> Chain(IReadOnlyList<GeoLine> lines)
    {
        var paths = new List<IReadOnlyList<Point3D>>();
        List<Point3D>? current = null;
        foreach (var line in lines)
        {
            var pts = SphereLines.Densify(line, TrackRadius);
            if (pts.Count == 0) continue;
            if (current is { Count: > 0 } && (current[^1] - pts[0]).LengthSquared < 1e-18) current.AddRange(pts.Skip(1));
            else
            {
                current = [.. pts];
                paths.Add(current);
            }
        }
        return paths;
    }

    private static void AddSegments(Point3DCollection target, IReadOnlyList<Point3D> path)
    {
        for (int i = 1; i < path.Count; i++) { target.Add(path[i - 1]); target.Add(path[i]); }
    }

    private void SetBillboard(int index, GeoPosition at, double sizePx, Material material)
    {
        if (index == _billboards.Count)
        {
            var b = new BillboardVisual3D();
            _billboards.Add(b);
            _billboardRoot.Children.Add(b);
        }
        var bb = _billboards[index];
        var u = at.ToUnitVector();
        bb.Position = new Point3D(u.X * GlyphRadius, u.Y * GlyphRadius, u.Z * GlyphRadius);
        bb.Width = sizePx; bb.Height = sizePx;
        if (!ReferenceEquals(bb.Material, material)) bb.Material = material;
    }

    private static Material GlyphMaterial(int category, bool south)
    {
        if (!GlyphMaterials.TryGetValue((category, south), out var m))
            GlyphMaterials[(category, south)] = m = ImageMaterial(StormGlyph(StormStyle.CategoryColor(category), south));
        return m;
    }

    private static Material DotMaterial(int category)
    {
        if (!DotMaterials.TryGetValue(category, out var m))
        {
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, DotPx, DotPx))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(StormStyle.CategoryColor(category)), new Pen(Brushes.White, 1),
                new EllipseGeometry(new Point(DotPx / 2, DotPx / 2), DotPx / 2 - 2, DotPx / 2 - 2)));
            DotMaterials[category] = m = ImageMaterial(g);
        }
        return m;
    }

    /// <summary>
    /// A 22 px storm symbol: a filled circle in the category colour with two curved arms. Going outward, the arms turn
    /// clockwise in the northern hemisphere (inflow spirals in anticlockwise there), and are mirrored in the southern.
    /// </summary>
    internal static DrawingGroup StormGlyph(Color color, bool south)
    {
        const double c = GlyphPx / 2;
        // Screen y points down: from the top of the circle the northern arm runs right (clockwise), the southern left.
        var arms = System.Windows.Media.Geometry.Parse(south
            ? "M 11,5.5 Q 5,1.5 1.5,6 M 11,16.5 Q 17,20.5 20.5,16"
            : "M 11,5.5 Q 17,1.5 20.5,6 M 11,16.5 Q 5,20.5 1.5,16");
        var fill = new SolidColorBrush(color);
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, GlyphPx, GlyphPx))));
        g.Children.Add(new GeometryDrawing(null, new Pen(Brushes.White, 3.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arms));
        g.Children.Add(new GeometryDrawing(null, new Pen(fill, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arms));
        g.Children.Add(new GeometryDrawing(fill, new Pen(Brushes.White, 1), new EllipseGeometry(new Point(c, c), 5.5, 5.5)));
        return g;
    }

    private static Material ImageMaterial(DrawingGroup g)
    {
        g.Freeze();
        var img = new DrawingImage(g);
        img.Freeze();
        var brush = new ImageBrush(img);
        brush.Freeze();
        var m = new DiffuseMaterial(brush);
        m.Freeze();
        return m;
    }

    private static Material SolidMaterial(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        var m = new DiffuseMaterial(brush);
        m.Freeze();
        return m;
    }

    // ---------------------------------------------------------------- labels

    private void UpdateLabels(VisibleEvents v)
    {
        _labels.Clear();
        // Storms first and the biggest quakes next, so they win when labels would overlap.
        foreach (var c in v.Cyclones)
        {
            if (string.IsNullOrWhiteSpace(c.Name)) continue;
            _labels.Add(new Label(StormStyle.Label(c.Name, c.Category), Unit(c.Location), GlyphRadius,
                StormStyle.CategoryColor(c.Category), 12, GlyphPx / 2 - 4));
        }
        foreach (var e in v.Eonet)
        {
            var name = StormStyle.EonetName(e.Storm.Title);
            if (name.Length == 0) continue;
            int cat = SaffirSimpson.Category(e.Fix.WindKt);
            _labels.Add(new Label(name + " " + StormStyle.CategoryText(cat), Unit(e.Fix.Position), GlyphRadius,
                StormStyle.CategoryColor(cat), 12, DotPx / 2 - 4));
        }
        foreach (var q in v.Quakes.Where(q => q.Quake.Magnitude >= QuakeStyle.LabelMinMagnitude)
                     .OrderByDescending(q => q.Quake.Magnitude))
            _labels.Add(new Label(QuakeStyle.Label(q.Quake.Magnitude)!, Unit(q.Quake.Location), DiscRadius,
                QuakeLabelColor, 11, 0));
    }

    private static Vector3D Unit(GeoPosition g)
    {
        var u = g.ToUnitVector();
        return new Vector3D(u.X, u.Y, u.Z);
    }

    /// <summary>Places the labels of the events last passed to <see cref="Update"/> that are on the side of the globe facing the camera.</summary>
    public void DrawLabels(LabelOverlay overlay, Projector projector)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        overlay.Begin();
        var eye = (Vector3D)projector.Eye;
        foreach (var l in _labels)
        {
            // A point r·u is in view when the eye is above its horizon plane (as MarkerHover.IsOnVisibleSide).
            if (Vector3D.DotProduct(eye, l.Unit) <= l.Radius) continue;
            if (!projector.TryProject((Point3D)(l.Unit * l.Radius), out var s)) continue;
            overlay.TryAdd(l.Text, new Point(s.X + l.OffsetPx, s.Y), l.Color, fontSize: l.FontSize);
        }
        overlay.End();
    }
}
