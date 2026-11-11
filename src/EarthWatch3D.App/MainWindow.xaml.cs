using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using AstroCore.Astronomy;
using AstroCore.Earth;
using System.Windows.Threading;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Globe;
using HelixToolkit.Wpf;

namespace EarthWatch3D.App;

public partial class MainWindow : Window
{
    private const string AssetPrefix = "EarthWatch3D.App.Assets.";
    private bool _adjustingCamera;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => (DataContext as MainViewModel)?.Tick();
        timer.Start();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel old) old.SunMoon.FaceRequested -= OnFaceRequested;
        if (e.NewValue is MainViewModel vm) vm.SunMoon.FaceRequested += OnFaceRequested;
    }

    private void OnFaceRequested(object? sender, GeoPosition p)
    {
        double distance = ((Vector3D)Camera.Position).Length;
        var (pos, look, up) = CameraRules.LookDownOn(p, distance);
        CameraHelper.AnimateTo(Camera, pos, look, up, 700);
    }

    private PerspectiveCamera Camera => (PerspectiveCamera?)Viewport.Camera ?? throw new InvalidOperationException("No camera.");

    private static Stream Asset(string name) =>
        typeof(MainWindow).Assembly.GetManifestResourceStream(AssetPrefix + name)
        ?? throw new InvalidOperationException($"Missing embedded resource {name}.");

    private Bgra32Image? _baseImage, _lines, _composed;   // kept for the per-minute recompose (Task 12)

    private static Task<T> RunOnStaThread<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var t = new Thread(() =>
        {
            try { tcs.SetResult(work()); } catch (Exception ex) { tcs.SetException(ex); }
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }

    private void SetDefaultView(double distance)
    {
        var (p, l, u) = CameraRules.LookDownOn(new GeoPosition(20, -40), distance);
        Camera.Position = p; Camera.LookDirection = l; Camera.UpDirection = u;
    }

    private double FitDistanceForWindow() =>
        CameraRules.FitDistance(Camera.FieldOfView, ActualWidth / Math.Max(1, ActualHeight));

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SetDefaultView(FitDistanceForWindow());
            ApplyRotationSensitivity();
            var decodeTask = Task.Run(() => { using var s = Asset("bluemarble-8192.jpg"); return ImageConversions.DecodeToBgra32(s); });
            var linesTask = Task.Run(() => (Coast: ReadLines("ne_50m_coastline.geojson"),
                                            Borders: ReadLines("ne_50m_admin_0_boundary_lines_land.geojson")));
            await Task.WhenAll(decodeTask, linesTask);   // observes both exceptions
            var baseImage = decodeTask.Result;
            var (coast, borders) = linesTask.Result;
            int w = baseImage.Width, h = baseImage.Height;
            var lines = await RunOnStaThread(() => LineRasterizer.Rasterize(
                [(borders, Color.FromArgb(102, 211, 211, 211)), (coast, Color.FromArgb(153, 255, 255, 255))], w, h, 1.0));
            _baseImage = baseImage; _lines = lines; _composed = Bgra32Image.Create(w, h);

            var subSolar = SubPoints.SubSolarPoint(DateTime.UtcNow);
            var target = _composed;
            await Task.Run(() => new GlobeImageComposer(baseImage).ComposeInto(new GlobeLayers(subSolar, 0.55, Lines: lines), target));

            var bitmap = ImageConversions.CreateBitmap(w, h);
            ImageConversions.CopyInto(target, bitmap);
            var material = new DiffuseMaterial(new ImageBrush(bitmap));
            GlobeVisual.Content = new GeometryModel3D(GlobeMesh.Create(), material);
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
        SetDefaultView(FitDistanceForWindow());
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
