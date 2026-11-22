using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using AstroCore.Astronomy;
using AstroCore.Earth;
using EarthWatch3D.App.Layers;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Feeds.Nws;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Lightning;
using GlobeCore.Earth;
using GlobeCore.Earth.Clouds;
using GlobeCore.Imaging;
using GlobeCore.Logging;
using GlobeCore.Regions;
using GlobeCore.Wpf.Camera;
using GlobeCore.Wpf.Layers;
using GlobeCore.Wpf.Rendering;
using HelixToolkit.Wpf;
using Microsoft.Win32;

namespace EarthWatch3D.App.Controllers;

/// <summary>
/// Owns the 3D scene. Once the globe image is loaded it builds GlobeCore's <see cref="LayerHost"/> (the globe image,
/// recomposed every minute so the day/night line moves; hover and click picking) with EarthWatch3D's layers: clouds,
/// areas and lines in the image; events, lightning and the Sun/Moon markers in the scene; event labels and the Sun/Moon
/// arrows on the overlay. It wires the view model to the host and the layers, and keeps the camera rules, input and
/// animation, and the window and power events.
/// </summary>
public sealed class SceneController
{
    /// <summary>
    /// Width of the globe image in pixels (height is half). The spec's fallback size: at 8192 the app's working set was
    /// about 1.2 GB, mostly WPF's copies of the texture (see docs/perf.md). The 8192 asset is scaled down while decoding.
    /// </summary>
    public const int GlobeImageWidth = 4096;
    private static readonly GeoPosition HomeView = new(20, -40);

    private readonly HelixViewport3D _viewport;
    private readonly Canvas _overlayCanvas;
    private readonly FrameworkElement _card;
    private readonly MainViewModel _vm;
    private readonly Window _window;
    private readonly IAppLog _log;
    private readonly ViewState _view;
    private readonly Task<CurrentBoundaries> _boundaries;
    private CurrentBoundaries? _watchedBoundaries;   // whose Changed event redraws the borders (from StartHostAsync)
    private readonly CameraAnimator _animator;
    private readonly TaskCompletionSource _firstCompose = new(TaskCreationOptions.RunContinuationsAsynchronously);
    // All WPF drawing off the UI thread (coastlines, plates, alert areas, clouds) runs here, one item at a time. Shut down on Closed.
    private readonly StaWorker _sta = new("EarthWatch3D rasterizer");
    // The layers that need no globe image are made with the controller, so they follow the view model from the start
    // (the Sun/Moon overlay first: its elements go on the canvas now, before any event label). The image layers and the
    // host are made once the image is loaded (OnLoaded, StartHostAsync).
    private readonly SunMoonOverlayLayer _arrows;
    private readonly SunMoonSceneLayer _markers;
    private readonly EventsSceneLayer _events;
    private readonly LightningSceneLayer _lightning;
    private readonly EventLabelsOverlayLayer _labels;
    private LayerHost? _host;
    private LinesImageLayer? _lines;
    private CloudsImageLayer? _clouds;
    private AreasImageLayer? _areas;
    private CloudImage? _cloudImage;   // the newest daily image received, kept until the clouds layer exists
    private bool _closed, _adjustingCamera;

    /// <param name="overlay">The canvas over the viewport for the Sun/Moon icons and edge arrows and the event labels.</param>
    /// <param name="card">The Sun &amp; Moon card, which the edge arrows keep clear of. It and <paramref name="overlay"/> must share a parent.</param>
    /// <param name="window">The window hosting the viewport: its Loaded, Closed, StateChanged and PreviewKeyDown events drive the scene.</param>
    /// <param name="store">The events to draw.</param>
    /// <param name="zones">NWS zone shapes for alerts that name zones only.</param>
    /// <param name="lightning">Where the lightning flashes come from.</param>
    /// <param name="view">Receives the camera's sub-point whenever the camera moves (the lightning feeds read it).</param>
    /// <param name="boundaries">The country borders, loading in the background; the lines layer follows their updates.</param>
    /// <param name="log">Where compose and load failures are written (none if null).</param>
    public SceneController(HelixViewport3D viewport, Canvas overlay, FrameworkElement card, MainViewModel vm, Window window,
                           EventStore store, ZoneCache zones, ILightningSource lightning, ViewState view,
                           Task<CurrentBoundaries> boundaries, IAppLog? log = null)
    {
        _log = log ?? new NullLog();
        // Nothing need await the first compose (only the diagnostics do), so observe a fault here: otherwise it would
        // reach TaskScheduler.UnobservedTaskException and the crash handler would show a second message for it.
        _firstCompose.Task.ContinueWith(static t => _ = t.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        Store = store ?? throw new ArgumentNullException(nameof(store));
        Zones = zones ?? throw new ArgumentNullException(nameof(zones));
        ArgumentNullException.ThrowIfNull(lightning);
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _boundaries = boundaries ?? throw new ArgumentNullException(nameof(boundaries));
        // The camera is put on the home view when the window loads, after the feeds have started: report that view now,
        // so a GLM feed's first run does not skip a satellite the user is about to face (GOES-West from the Americas).
        _view.CameraSubPoint = HomeView;
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        _overlayCanvas = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _card = card ?? throw new ArgumentNullException(nameof(card));
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _window = window ?? throw new ArgumentNullException(nameof(window));

        _animator = new CameraAnimator(() => Camera);
        _arrows = new SunMoonOverlayLayer(overlay, card, vm.SunMoon);
        _markers = new SunMoonSceneLayer(vm.SunMoon);
        _events = new EventsSceneLayer(viewport, vm, store, zones, () => _areas?.Request(), window.Dispatcher);
        _lightning = new LightningSceneLayer(lightning, vm, window.Dispatcher);
        _labels = new EventLabelsOverlayLayer(_events, overlay);

        _window.Loaded += OnLoaded;
        _window.Closed += OnClosed;
        _window.StateChanged += OnWindowStateChanged;
        _window.PreviewKeyDown += OnPreviewKeyDown;
        _viewport.CameraChanged += OnCameraChanged;
        _viewport.PreviewMouseWheel += OnPreviewMouseWheel;
        _viewport.PreviewMouseDown += OnPreviewMouseDown;
        _vm.DisplayTimeChanged += OnDisplayTimeChanged;
        _vm.SunMoon.FaceRequested += OnFaceRequested;
        _vm.Layers.PropertyChanged += OnDisplaySettingChanged;
        _vm.Display.PropertyChanged += OnDisplaySettingChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;   // static event: unsubscribed in OnClosed
    }

    /// <summary>Completes once the first composed globe image is on the globe (cancelled if the window closes first).</summary>
    public Task FirstComposeDisplayed => _firstCompose.Task;

    /// <summary>Diagnostics only (--snapshot-scenes): when set, the pulsing rings are drawn at this fixed time instead of the running stopwatch.</summary>
    public TimeSpan? FrozenRingTime { get => _events.FrozenRingTime; set => _events.FrozenRingTime = value; }

    public bool IsCameraAnimating => _animator.IsRunning;

    /// <summary>
    /// Other overlay elements (the details panel, the layer strip): no hover picking while the mouse is over them. Add
    /// them before the window loads (they are handed to the host with the Sun &amp; Moon card when it is built).
    /// </summary>
    public IList<UIElement> HoverBlockers { get; } = [];

    /// <summary>The layer host, with every layer added, once the globe image has loaded (null before).</summary>
    internal LayerHost? Host => _host;

    /// <summary>The event store the scene will draw from.</summary>
    internal EventStore Store { get; }

    /// <summary>The NWS zone cache for zone-only alerts.</summary>
    internal ZoneCache Zones { get; }

    /// <summary>The viewport's camera, or null if something replaced it with a non-perspective camera.</summary>
    internal PerspectiveCamera? Camera => _viewport.Camera as PerspectiveCamera;
    internal HelixViewport3D Viewport => _viewport;

    // ---------------------------------------------------------------- globe image and layers

    /// <summary>A daily cloud image has arrived (UI thread): the clouds layer builds it and recomposes the globe.</summary>
    public void SetCloudImage(CloudImage image)
    {
        _cloudImage = image;
        _clouds?.SetImage(image);   // otherwise StartHostAsync does it once the base exists
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window.Loaded -= OnLoaded;   // one load, one host
        try
        {
            if (Camera is { } camera)
            {
                SetDefaultView(camera, FitDistanceForViewport(camera));
                ApplyRotationSensitivity(camera);
            }
            var (baseImage, lines, borders) = await GlobeLoader.LoadAsync(GlobeImageWidth, _sta, _boundaries, _log);
            if (_closed) return;
            _ = StartHostAsync(baseImage, lines, borders);
        }
        catch (OperationCanceledException) when (_closed) { }   // closed while loading
        catch (Exception ex)
        {
            _log.Error("Globe load failed", ex);
            _firstCompose.TrySetException(ex);
            if (!_closed) MessageBox.Show(_window, ex.Message, "EarthWatch3D", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Builds the host and the image layers on the loaded globe image, adds every layer, and makes the first compose
    /// (LayerHost's start-up sequence); then the Areas layer and any cloud image that arrived meanwhile. UI thread.
    /// Completes when the first compose (and any queued behind it) is on the globe. <paramref name="borders"/> is the
    /// boundary set <paramref name="coastAndBorders"/> was drawn from (null if the boundaries could not be loaded); from
    /// here on the lines layer redraws the borders whenever the "Borders" feed adopts a newer set.
    /// </summary>
    internal Task StartHostAsync(Bgra32Image baseImage, SparseOverlay coastAndBorders, BoundarySet? borders)
    {
        // Before the split, the visible events were worked out on frames while the image loaded; do that now, so the
        // Areas layer below and the first frame see them (the Areas layer does not exist yet, so it is not asked here).
        _events.EvaluateIfDue();
        var layers = _vm.Layers;
        _clouds = new CloudsImageLayer(baseImage, layers, _sta, _log, () => _closed) { IsVisible = layers.ShowClouds };
        _areas = new AreasImageLayer(baseImage.Width, baseImage.Height, () => _events.Visible, () => _vm.CurrentFilter,
                                     Zones, _sta, _log, () => _closed, _window.Dispatcher);
        _lines = new LinesImageLayer(coastAndBorders, borders, layers.ShowCoastlines, layers.ShowPlates, _sta, _log, () => _closed);
        var host = _host = new LayerHost(_viewport, _overlayCanvas, EarthBody.Instance, baseImage, _log);
        // The hover tip keeps the host's default style: the cards' dark tooltip (GlobeCore.Wpf CardStyles.xaml).
        host.HoverBlockers.Add(_card);
        foreach (var b in HoverBlockers) host.HoverBlockers.Add(b);
        // Scene layers are added in draw order (WPF draws 3D children in order): globe, events, lightning, markers.
        foreach (var layer in new GlobeLayer[] { _clouds, _areas, _lines, _events, _lightning, _markers, _labels, _arrows })
            host.Add(layer);
        host.FirstComposeDisplayed += OnFirstComposeDisplayed;
        host.WhenFirstComposeDisplayed.ContinueWith(t => _firstCompose.TrySetException(t.Exception!.InnerExceptions),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        host.CameraPoseChanged += OnCameraPoseChanged;
        host.Selected += OnSelected;
        host.SelectionCleared += OnSelectionCleared;
        // The light for each compose's time: the sub-solar point and the night settings, read as that compose starts.
        var display = _vm.Display;
        host.IlluminationAt = utc => new IlluminationState(SubPoints.SubSolarPoint(utc), display.NightStrength, display.ShowTerminator);
        host.DisplayTimeUtc = _vm.DisplayTimeUtc;
        var first = host.RecomposeNowAsync();
        _areas.Request();   // composes again once the Areas layer exists
        if (_cloudImage is not null) _clouds.SetImage(_cloudImage);   // one arrived while the base was loading
        WatchBoundaries(borders);
        return first;
    }

    // The Boundaries legend names the set drawn; a set adopted while the globe was loading is drawn now, and later ones
    // as the feed adopts them.
    private void WatchBoundaries(BoundarySet? drawn)
    {
        if (drawn is null || !_boundaries.IsCompletedSuccessfully)
        {
            _vm.Layers.BoundariesLegend = "Borders: could not be loaded (see the log); coastlines only.";
            return;
        }
        _vm.Layers.BoundariesLegend = LayersViewModel.BoundariesLegendFor(drawn);
        var current = _watchedBoundaries = _boundaries.Result;
        current.Changed += OnBoundariesChanged;
        if (!ReferenceEquals(current.Set, drawn)) RedrawBorders(current.Set);
    }

    // Raised on the feed's thread (inside the swap): the redraw is queued to the UI thread.
    private void OnBoundariesChanged(object? sender, BoundariesChangedEventArgs e) =>
        _window.Dispatcher.BeginInvoke(() => RedrawBorders(e.New));

    private async void RedrawBorders(BoundarySet set)
    {
        try
        {
            if (_closed || _lines is null) return;
            if (await _lines.ShowBordersAsync(set) && !_closed)
            {
                _vm.Layers.BoundariesLegend = LayersViewModel.BoundariesLegendFor(set);
                _log.Info($"Borders redrawn from {set.Edition}");
            }
        }
        catch (Exception ex)   // async void: nothing may escape to the dispatcher
        {
            _log.Error("Borders could not be redrawn", ex);
        }
    }

    private void OnFirstComposeDisplayed(object? sender, EventArgs e) => _firstCompose.TrySetResult();

    // The host composes once the time has moved on by a minute (throttled; on restore if minimized).
    private void OnDisplayTimeChanged(object? sender, EventArgs e) => _host?.DisplayTimeUtc = _vm.DisplayTimeUtc;

    private void OnDisplaySettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_host is null || _lines is null || _clouds is null) return;   // the layers read the settings when they are made
        switch (e.PropertyName)
        {
            case nameof(LayersViewModel.ShowCoastlines): _lines.ShowCoastlines = _vm.Layers.ShowCoastlines; break;
            case nameof(LayersViewModel.ShowPlates): _lines.ShowPlates = _vm.Layers.ShowPlates; break;
            case nameof(LayersViewModel.ShowClouds): _clouds.IsVisible = _vm.Layers.ShowClouds; break;
            case nameof(DisplayOptionsViewModel.NightStrength) or nameof(DisplayOptionsViewModel.ShowTerminator):
                _host.RequestRecompose();   // throttled: the night-shade slider changes it many times a second
                break;
        }
    }

    // A click on an event selects it; a click on a Sun/Moon marker does nothing.
    private void OnSelected(object? sender, PickResult hit) { if (hit.Item is EarthEvent picked) _vm.Selection.Select(picked); }

    private void OnSelectionCleared(object? sender, EventArgs e) => _vm.Selection.Select(null);

    // ---------------------------------------------------------------- window and power

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_window.WindowState == WindowState.Minimized) { _arrows.HideAll(); return; }
        _host?.RequestSceneUpdate();
        _host?.RecomposeIfStale();
    }

    // SystemEvents calls each handler through the SynchronizationContext that was current when it subscribed (here the
    // UI thread's, from the constructor), so this normally runs on the UI thread already. The BeginInvoke lets the
    // resume notification return first, and would also cover a subscription made where there was no context.
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        _window.Dispatcher.BeginInvoke(() =>
        {
            if (_closed || _host is null) return;
            _host.RequestSceneUpdate();
            _ = _host.RecomposeNowAsync();
        });
    }

    // ---------------------------------------------------------------- camera

    // Camera access tolerates a missing or non-perspective camera: the Helix gestures that could swap one in are
    // switched off in MainWindow.xaml, but nothing here should throw if one appears.

    private void SetDefaultView(PerspectiveCamera camera, double distance)
    {
        var (p, l, u) = CameraRules.LookDownOn(HomeView, distance);
        camera.Position = p; camera.LookDirection = l; camera.UpDirection = u;
    }

    private double FitDistanceForViewport(PerspectiveCamera camera) =>
        CameraRules.FitDistance(camera.FieldOfView, _viewport.ActualWidth / Math.Max(1, _viewport.ActualHeight), CameraLimits.EarthDefault);

    private void ApplyRotationSensitivity(PerspectiveCamera camera) =>
        _viewport.RotationSensitivity = CameraRules.RotationSensitivity(((Vector3D)camera.Position).Length);

    private void NoteSubPoint(PerspectiveCamera c) =>
        _view.CameraSubPoint = GeoPosition.FromVector(new Vector3d(c.Position.X, c.Position.Y, c.Position.Z));

    // Programmatic camera moves (CameraAnimator) do not always raise CameraChanged; the host compares the pose each frame.
    private void OnCameraPoseChanged(object? sender, EventArgs e)
    {
        if (Camera is { } camera) NoteSubPoint(camera);
    }

    /// <summary>Turns the camera at once (no animation) to look straight down on <paramref name="p"/>, keeping its distance.</summary>
    public void LookAt(GeoPosition p)
    {
        if (Camera is not { } camera) return;
        _animator.Cancel();
        var (pos, look, up) = CameraRules.LookDownOn(p, ((Vector3D)camera.Position).Length);
        camera.Position = pos; camera.LookDirection = look; camera.UpDirection = up;
        _host?.RequestSceneUpdate();
    }

    /// <summary>Turns the camera at once to the home view (the one the window opens with), keeping its distance.</summary>
    public void LookAtHome() => LookAt(HomeView);

    private void OnFaceRequested(object? sender, GeoPosition p)
    {
        if (Camera is not { } camera) return;
        double distance = ((Vector3D)camera.Position).Length;
        _animator.AnimateTo(CameraRules.LookDownOn(p, distance).Position);
    }

    private void OnCameraChanged(object? sender, RoutedEventArgs e)
    {
        if (_adjustingCamera || Camera is not { } camera) return;   // the host redraws the scene layers
        NoteSubPoint(camera);
        _adjustingCamera = true;
        try
        {
            var clamped = CameraRules.ClampDistance(camera.Position, CameraLimits.EarthDefault);
            if (clamped != camera.Position)
            {
                camera.Position = clamped;
                camera.LookDirection = -(Vector3D)clamped;
            }
            else if (!CameraRules.AimsAtOrigin(camera.Position, camera.LookDirection))
            {
                // Belt and braces: something moved Helix's target off the Earth's centre. Look back at the centre from
                // where the camera is (same distance), north up; the full-length look direction puts the target there.
                var v = (Vector3D)camera.Position;
                var (_, look, up) = CameraRules.LookDownOn(GeoPosition.FromVector(new Vector3d(v.X, v.Y, v.Z)), v.Length);
                camera.LookDirection = look;
                camera.UpDirection = up;
            }
            ApplyRotationSensitivity(camera);
        }
        finally { _adjustingCamera = false; }
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        _animator.Cancel();
        if (Camera is not { } camera) return;
        double notches = e.Delta / 120.0;
        var pos = (Vector3D)camera.Position;
        var next = CameraRules.ClampDistance(new Point3D(0, 0, 0) + pos * Math.Pow(CameraLimits.EarthDefault.ZoomPerNotch, -notches), CameraLimits.EarthDefault);
        camera.Position = next;
        camera.LookDirection = -(Vector3D)next;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _vm.Selection.IsOpen)
        {
            e.Handled = true;
            _vm.Selection.Select(null);
            return;
        }
        if (e.Key != Key.Home) return;
        e.Handled = true;
        if (Camera is not { } camera) return;
        _animator.AnimateTo(CameraRules.LookDownOn(HomeView, FitDistanceForViewport(camera)).Position);
    }

    /// <summary>A double-click flies to the point under it (a single click is the host's: it selects).</summary>
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 2) return;
        e.Handled = true;   // keep Helix's own double-click (look-at) out of it
        if (Camera is not { } camera) return;
        var projector = new Projector(camera, _viewport.ActualWidth, _viewport.ActualHeight);
        if (!projector.TryHitUnitSphere(e.GetPosition(_viewport), out Point3D hit)) return;
        var geo = GeoPosition.FromVector(new Vector3d(hit.X, hit.Y, hit.Z));
        double distance = ((Vector3D)camera.Position).Length;
        _animator.AnimateTo(CameraRules.LookDownOn(geo, distance).Position);
    }

    // ---------------------------------------------------------------- lifetime

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _animator.Cancel();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _window.Loaded -= OnLoaded;
        _window.Closed -= OnClosed;
        _window.StateChanged -= OnWindowStateChanged;
        _window.PreviewKeyDown -= OnPreviewKeyDown;
        _viewport.CameraChanged -= OnCameraChanged;
        _viewport.PreviewMouseWheel -= OnPreviewMouseWheel;
        _viewport.PreviewMouseDown -= OnPreviewMouseDown;
        _vm.DisplayTimeChanged -= OnDisplayTimeChanged;
        _vm.SunMoon.FaceRequested -= OnFaceRequested;
        _vm.Layers.PropertyChanged -= OnDisplaySettingChanged;
        _vm.Display.PropertyChanged -= OnDisplaySettingChanged;
        if (_watchedBoundaries is { } boundaries) boundaries.Changed -= OnBoundariesChanged;
        _arrows.Close(); _markers.Close(); _events.Close(); _lightning.Close(); _areas?.Close();
        // Before the rasterizer: a cancelled build is then seen as "closed", not logged as an error. A disposed host
        // raises no more events.
        _host?.Dispose();
        _sta.Dispose();   // queued rasterizing is cancelled; the awaiting loops see the window closed and stop
        _firstCompose.TrySetCanceled();
    }
}
