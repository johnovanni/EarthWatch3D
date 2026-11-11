using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using AstroCore.Astronomy;
using AstroCore.Earth;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Globe;
using HelixToolkit.Wpf;

namespace EarthWatch3D.App;

public partial class MainWindow : Window
{
    private const string AssetPrefix = "EarthWatch3D.App.Assets.";
    private const double DefaultDistance = 3.0;
    private bool _adjustingCamera;

    public MainWindow()
    {
        InitializeComponent();
    }

    private PerspectiveCamera Camera => (PerspectiveCamera?)Viewport.Camera ?? throw new InvalidOperationException("No camera.");

    private static Stream Asset(string name) =>
        typeof(MainWindow).Assembly.GetManifestResourceStream(AssetPrefix + name)
        ?? throw new InvalidOperationException($"Missing embedded resource {name}.");

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyRotationSensitivity();
            // Decode off the UI thread while the lines are rasterized here (DrawingVisual needs the UI thread).
            var decodeTask = Task.Run(() => { using var s = Asset("bluemarble-8192.jpg"); return ImageConversions.DecodeToBgra32(s); });
            var coast = ReadLines("ne_50m_coastline.geojson");
            var borders = ReadLines("ne_50m_admin_0_boundary_lines_land.geojson");
            var baseImage = await decodeTask;
            int w = baseImage.Width, h = baseImage.Height;
            var lines = LineRasterizer.Rasterize(
                [(borders, Color.FromArgb(102, 211, 211, 211)), (coast, Color.FromArgb(153, 255, 255, 255))], w, h, 1.0);

            var subSolar = SubPoints.SubSolarPoint(DateTime.UtcNow);
            var composed = await Task.Run(() =>
            {
                var target = Bgra32Image.Create(w, h);
                new GlobeImageComposer(baseImage).ComposeInto(new GlobeLayers(subSolar, 0.55, Lines: lines), target);
                return target;
            });

            var bitmap = ImageConversions.CreateBitmap(w, h);
            ImageConversions.CopyInto(composed, bitmap);
            var material = new DiffuseMaterial(new ImageBrush(bitmap));
            GlobeVisual.Content = new GeometryModel3D(GlobeMesh.Create(), material) { BackMaterial = material };
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "EarthWatch3D", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static List<IReadOnlyList<GeoPosition>> ReadLines(string name)
    {
        using var s = Asset(name);
        return GeoJsonLines.Read(s).ToList();
    }

    private void ApplyRotationSensitivity() =>
        Viewport.RotationSensitivity = CameraRules.RotationSensitivity(((Vector3D)Camera.Position).Length);

    private void OnCameraChanged(object? sender, RoutedEventArgs e)
    {
        if (_adjustingCamera) return;
        _adjustingCamera = true;
        try
        {
            var clamped = CameraRules.ClampDistance(Camera.Position);
            if (clamped != Camera.Position)
            {
                Camera.Position = clamped;
                Camera.LookDirection = -(Vector3D)clamped;
            }
            ApplyRotationSensitivity();
        }
        finally { _adjustingCamera = false; }
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        double notches = e.Delta / 120.0;
        var pos = (Vector3D)Camera.Position;
        var next = CameraRules.ClampDistance(new Point3D(0, 0, 0) + pos * Math.Pow(CameraRules.ZoomPerNotch, -notches));
        Camera.Position = next;
        Camera.LookDirection = -(Vector3D)next;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Home) return;
        e.Handled = true;
        var (p, l, u) = CameraRules.LookDownOn(new GeoPosition(20, -40), DefaultDistance);
        Camera.Position = p; Camera.LookDirection = l; Camera.UpDirection = u;
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 2) return;
        e.Handled = true;   // keep Helix's own double-click (look-at) out of it
        var pt = e.GetPosition(Viewport);
        if (!Viewport.FindNearest(pt, out Point3D hit, out _, out _)) return;
        var geo = GeoPosition.FromVector(new Vector3d(hit.X, hit.Y, hit.Z));
        double distance = ((Vector3D)Camera.Position).Length;
        var (p, l, u) = CameraRules.LookDownOn(geo, distance);
        CameraHelper.AnimateTo(Camera, p, l, u, 700);
    }
}
