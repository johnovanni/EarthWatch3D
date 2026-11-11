using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using AstroCore.Astronomy;
using AstroCore.Earth;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Globe;
using EarthWatch3D.Core.Time;
using HelixToolkit.Wpf;
using Microsoft.Win32;
#if PERF_LOG
using EarthWatch3D.App.Diagnostics;
#endif

namespace EarthWatch3D.App.Controllers;

/// <summary>
/// Owns the 3D scene: the globe image (load, then a recompose every minute so the day/night line moves),
/// the Sun/Moon globe markers and screen overlay, and the camera rules, input and animation.
/// </summary>
public sealed class SceneController
{
    /// <summary>
    /// Width of the globe image in pixels (height is half). The spec's fallback size: at 8192 the app's working set was
    /// about 1.2 GB, mostly WPF's copies of the texture (see docs/perf.md). The 8192 asset is scaled down while decoding.
    /// </summary>
    public const int GlobeImageWidth = 4096;
    public const double NightMaxOpacity = 0.55;
    public static readonly TimeSpan RecomposeInterval = TimeSpan.FromMinutes(1);

    private const string AssetPrefix = "EarthWatch3D.App.Assets.";
    private const double EarthRadiusKm = 6371.0;
    private static readonly GeoPosition HomeView = new(20, -40);

    private readonly HelixViewport3D _viewport;
    private readonly MainViewModel _vm;
    private readonly IClock _clock;
    private readonly Window _window;
    private readonly CameraAnimator _animator;
    private readonly SunMoonOverlay _overlay;
    private readonly SunMoonGlobeMarkers _markers;
    private readonly ModelVisual3D _globeVisual = new();
    private readonly RecomposeSchedule _schedule = new(RecomposeInterval);
    private readonly TaskCompletionSource _firstCompose = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Kept between composes: the composer holds the base image; the target and bitmap are reused.
    private GlobeImageComposer? _composer;
    private SparseOverlay? _lines;
    private Bgra32Image? _composed;
    private WriteableBitmap? _bitmap;
    private bool _composing, _recomposePending, _closed;

    private bool _overlayDirty = true, _adjustingCamera;
    // Programmatic camera moves (CameraAnimator) do not always raise CameraChanged, so compare the pose too.
    private (Point3D, Vector3D, Vector3D, double) _lastPose;

#if PERF_LOG
    private int _composeCount;
#endif

    /// <param name="window">The window hosting the viewport: its Loaded, Closed, StateChanged and PreviewKeyDown events drive the scene.</param>
    public SceneController(HelixViewport3D viewport, Canvas overlay, MainViewModel vm, IClock clock, Window window)
    {
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        ArgumentNullException.ThrowIfNull(overlay);

        _animator = new CameraAnimator(Camera);
        _overlay = new SunMoonOverlay(overlay);
        _markers = new SunMoonGlobeMarkers();
        _viewport.Children.Add(_globeVisual);
        foreach (var v in _markers.Visuals) _viewport.Children.Add(v);

        _window.Loaded += OnLoaded;
        _window.Closed += OnClosed;
        _window.StateChanged += OnWindowStateChanged;
        _window.PreviewKeyDown += OnPreviewKeyDown;
        _viewport.CameraChanged += OnCameraChanged;
        _viewport.PreviewMouseWheel += OnPreviewMouseWheel;
        _viewport.PreviewMouseDown += OnPreviewMouseDown;
        _viewport.SizeChanged += OnViewportSizeChanged;
        _vm.Ticked += OnTicked;
        _vm.SunMoon.PropertyChanged += OnSunMoonChanged;
        _vm.SunMoon.FaceRequested += OnFaceRequested;
        CompositionTarget.Rendering += OnRendering;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;   // static event: unsubscribed in OnClosed
    }

    /// <summary>Completes once the first composed globe image is on the globe (cancelled if the window closes first).</summary>
    public Task FirstComposeDisplayed => _firstCompose.Task;

    public bool IsCameraAnimating => _animator.IsRunning;

    internal PerspectiveCamera Camera => (PerspectiveCamera?)_viewport.Camera ?? throw new InvalidOperationException("No camera.");

    // ---------------------------------------------------------------- globe image

    /// <summary>
    /// Composes the globe image for the clock's current time on a background task and shows it.
    /// Only one compose runs at a time; requests made meanwhile collapse into one follow-up compose.
    /// </summary>
    public void Recompose()
    {
        if (_closed || _composer is null) return;   // before load completes, the load itself composes
        if (_composing) { _recomposePending = true; return; }
        _ = ComposeLoopAsync();
    }

    private async Task ComposeLoopAsync()
    {
        _composing = true;
        try
        {
            do
            {
                _recomposePending = false;
                var now = _clock.UtcNow;
                _schedule.MarkComposed(now);
                var subSolar = SubPoints.SubSolarPoint(now);
                var layers = new GlobeLayers(subSolar, NightMaxOpacity, SparseLines: _lines);
                var composer = _composer!;
                var target = _composed!;
#if PERF_LOG
                var sw = Stopwatch.StartNew();
#endif
                await Task.Run(() => composer.ComposeInto(layers, target));
#if PERF_LOG
                long composeMs = sw.ElapsedMilliseconds;
#endif
                if (_closed) return;
                ImageConversions.CopyInto(target, _bitmap!);
                _globeVisual.Content ??= new GeometryModel3D(GlobeMesh.Create(), new DiffuseMaterial(new ImageBrush(_bitmap)));
#if PERF_LOG
                PerfLog.Write(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"compose #{++_composeCount} utc={now:yyyy-MM-ddTHH:mm:ss.fffZ} subsolar lat={subSolar.LatitudeDeg:F3} lon={subSolar.LongitudeDeg:F3} compose={composeMs} ms total={sw.ElapsedMilliseconds} ms {PerfLog.Memory()}"));
#endif
                _firstCompose.TrySetResult();
            } while (_recomposePending && !_closed);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Globe compose failed: {ex}");
            _firstCompose.TrySetException(ex);
        }
        finally { _composing = false; }
    }

    private void OnTicked(object? sender, EventArgs e)
    {
        if (_window.WindowState == WindowState.Minimized) return;   // composed on restore instead
        if (_schedule.IsDue(_clock.UtcNow)) Recompose();
    }

    // SystemEvents raise on their own thread.
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        _window.Dispatcher.BeginInvoke(() =>
        {
            if (_closed) return;
            _overlayDirty = true;
            Recompose();
        });
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SetDefaultView(FitDistanceForViewport());
            ApplyRotationSensitivity();
#if PERF_LOG
            var load = Stopwatch.StartNew();
#endif
            var decodeTask = Task.Run(() =>
            {
                using var s = Asset("bluemarble-8192.jpg");
                return ImageConversions.DecodeToBgra32(s, GlobeImageWidth);
            });
            var linesTask = Task.Run(() => (Coast: ReadLines("ne_50m_coastline.geojson"),
                                            Borders: ReadLines("ne_50m_admin_0_boundary_lines_land.geojson")));
            await Task.WhenAll(decodeTask, linesTask);   // observes both exceptions
            var baseImage = decodeTask.Result;
            var (coast, borders) = linesTask.Result;
            int w = baseImage.Width, h = baseImage.Height;
#if PERF_LOG
            PerfLog.Write($"load: decode + GeoJSON {load.ElapsedMilliseconds} ms ({w}x{h}) {PerfLog.Memory()}");
            load.Restart();
#endif
            // Rasterize on an STA thread, and keep only the line pixels: the dense layer would cost 4·w·h bytes.
            var lines = await RunOnStaThread(() => SparseOverlay.FromImage(LineRasterizer.Rasterize(
                [(borders, Color.FromArgb(102, 211, 211, 211)), (coast, Color.FromArgb(153, 255, 255, 255))], w, h, 1.0)));
#if PERF_LOG
            PerfLog.Write(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"load: rasterize + sparse {load.ElapsedMilliseconds} ms ({lines.Count} line pixels, {100.0 * lines.Count / ((double)w * h):F2}%) {PerfLog.Memory()}"));
#endif
            if (_closed) return;
            _composer = new GlobeImageComposer(baseImage);
            _lines = lines;
            _composed = Bgra32Image.Create(w, h);
            _bitmap = ImageConversions.CreateBitmap(w, h);
            Recompose();
        }
        catch (Exception ex)
        {
            _firstCompose.TrySetException(ex);
            if (!_closed) MessageBox.Show(_window, ex.Message, "EarthWatch3D", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static Stream Asset(string name) =>
        typeof(SceneController).Assembly.GetManifestResourceStream(AssetPrefix + name)
        ?? throw new InvalidOperationException($"Missing embedded resource {name}.");

    private static List<IReadOnlyList<GeoPosition>> ReadLines(string name)
    {
        using var s = Asset(name);
        return GeoJsonLines.Read(s).ToList();
    }

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

    // ---------------------------------------------------------------- Sun/Moon markers and overlay

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e) => _overlayDirty = true;
    private void OnSunMoonChanged(object? sender, PropertyChangedEventArgs e) => _overlayDirty = true;

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_window.WindowState == WindowState.Minimized) { _overlay.HideAll(); return; }
        _overlayDirty = true;
        if (_schedule.IsDue(_clock.UtcNow)) Recompose();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_window.WindowState == WindowState.Minimized) { _overlay.HideAll(); _overlayDirty = true; return; }
        NoteCameraChange();
        if (!_overlayDirty) return;
        _overlayDirty = false;
        UpdateSunMoon();
    }

    private void NoteCameraChange()
    {
        if (_viewport.Camera is not PerspectiveCamera c) return;
        var pose = (c.Position, c.LookDirection, c.UpDirection, c.FieldOfView);
        if (pose == _lastPose) return;
        _lastPose = pose;
        _overlayDirty = true;
    }

    private void UpdateSunMoon()
    {
        if (_viewport.Camera is not PerspectiveCamera camera) return;
        var sm = _vm.SunMoon;
        double fraction = sm.MoonIlluminatedPercent / 100.0;
        _markers.Update(sm.SubSolar, sm.SubLunar, fraction, sm.MoonWaxing);
        var projector = new Projector(camera, _viewport.ActualWidth, _viewport.ActualHeight);
        var sun = sm.SubSolar.ToUnitVector();
        var moon = sm.SubLunar.ToUnitVector();
        double moonR = sm.MoonDistanceKm / EarthRadiusKm;
        _overlay.Update(projector, new Vector3D(sun.X, sun.Y, sun.Z),
            new Point3D(moon.X * moonR, moon.Y * moonR, moon.Z * moonR), fraction, sm.MoonWaxing);
    }

    // ---------------------------------------------------------------- camera

    private void SetDefaultView(double distance)
    {
        var (p, l, u) = CameraRules.LookDownOn(HomeView, distance);
        Camera.Position = p; Camera.LookDirection = l; Camera.UpDirection = u;
    }

    private double FitDistanceForViewport() =>
        CameraRules.FitDistance(Camera.FieldOfView, _viewport.ActualWidth / Math.Max(1, _viewport.ActualHeight));

    private void ApplyRotationSensitivity() =>
        _viewport.RotationSensitivity = CameraRules.RotationSensitivity(((Vector3D)Camera.Position).Length);

    private void OnFaceRequested(object? sender, GeoPosition p)
    {
        double distance = ((Vector3D)Camera.Position).Length;
        _animator.AnimateTo(CameraRules.LookDownOn(p, distance).Position);
    }

    private void OnCameraChanged(object? sender, RoutedEventArgs e)
    {
        _overlayDirty = true;
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
        _animator.Cancel();
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
        _animator.AnimateTo(CameraRules.LookDownOn(HomeView, FitDistanceForViewport()).Position);
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 2) return;
        e.Handled = true;   // keep Helix's own double-click (look-at) out of it
        var pt = e.GetPosition(_viewport);
        if (!_viewport.FindNearest(pt, out Point3D hit, out _, out _)) return;
        var geo = GeoPosition.FromVector(new Vector3d(hit.X, hit.Y, hit.Z));
        double distance = ((Vector3D)Camera.Position).Length;
        _animator.AnimateTo(CameraRules.LookDownOn(geo, distance).Position);
    }

    // ---------------------------------------------------------------- lifetime

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _animator.Cancel();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        CompositionTarget.Rendering -= OnRendering;
        _window.Loaded -= OnLoaded;
        _window.Closed -= OnClosed;
        _window.StateChanged -= OnWindowStateChanged;
        _window.PreviewKeyDown -= OnPreviewKeyDown;
        _viewport.CameraChanged -= OnCameraChanged;
        _viewport.PreviewMouseWheel -= OnPreviewMouseWheel;
        _viewport.PreviewMouseDown -= OnPreviewMouseDown;
        _viewport.SizeChanged -= OnViewportSizeChanged;
        _vm.Ticked -= OnTicked;
        _vm.SunMoon.PropertyChanged -= OnSunMoonChanged;
        _vm.SunMoon.FaceRequested -= OnFaceRequested;
        _firstCompose.TrySetCanceled();
    }
}
