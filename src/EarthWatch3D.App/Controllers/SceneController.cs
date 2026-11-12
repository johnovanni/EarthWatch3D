using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using AstroCore.Astronomy;
using AstroCore.Earth;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Feeds.Nws;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Globe;
using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Time;
using HelixToolkit.Wpf;
using Microsoft.Win32;
#if PERF_LOG
using EarthWatch3D.App.Diagnostics;
#endif

namespace EarthWatch3D.App.Controllers;

/// <summary>
/// Owns the 3D scene: the globe image (load, then a recompose every minute so the day/night line moves),
/// the events and their labels, the Sun/Moon globe markers and screen overlay, and the camera rules, input and animation.
/// </summary>
public sealed class SceneController
{
    /// <summary>
    /// Width of the globe image in pixels (height is half). The spec's fallback size: at 8192 the app's working set was
    /// about 1.2 GB, mostly WPF's copies of the texture (see docs/perf.md). The 8192 asset is scaled down while decoding.
    /// </summary>
    public const int GlobeImageWidth = 4096;
    public static readonly TimeSpan RecomposeInterval = TimeSpan.FromMinutes(1);

    private const string AssetPrefix = "EarthWatch3D.App.Assets.";
    private const double EarthRadiusKm = 6371.0;
    private const double ClickSlopPx = 4;
    private static readonly GeoPosition HomeView = new(20, -40);

    private readonly HelixViewport3D _viewport;
    private readonly Canvas _overlayCanvas;
    private readonly FrameworkElement _card;
    private readonly MainViewModel _vm;
    private readonly Window _window;
    private readonly IAppLog _log;
    private readonly CameraAnimator _animator;
    private readonly SunMoonOverlay _overlay;
    private readonly SunMoonGlobeMarkers _markers;
    private readonly EventScene _events;
    private readonly LabelOverlay _labels;
    // Which events are visible is worked out again at most every 250 ms (scrubbing changes the time many times a second).
    private readonly Throttle _eventThrottle = new(TimeSpan.FromMilliseconds(250));
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ModelVisual3D _globeVisual = new();
    // Tooltip for the Sun/Moon surface markers (billboards can't carry WPF tooltips, so it is driven by hover tests).
    // Left button went down at this point and has not moved far (a click, not a drag); null otherwise.
    private Point? _clickStart;
    private readonly Throttle _hoverThrottle = new(TimeSpan.FromMilliseconds(33));
    private readonly System.Windows.Threading.DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private Point _hoverMouse;
    private readonly ToolTip _markerTip = new() { Placement = PlacementMode.Relative, StaysOpen = true };
    private readonly RecomposeSchedule _schedule = new(RecomposeInterval);
    private readonly Throttle _throttle = new(TimeSpan.FromMilliseconds(250));
    private readonly System.Windows.Threading.DispatcherTimer _trailing = new();
    private readonly TaskCompletionSource _firstCompose = new(TaskCreationOptions.RunContinuationsAsynchronously);
    // All WPF drawing off the UI thread (coastlines, plates, alert areas) runs here, one item at a time. Shut down on Closed.
    private readonly StaWorker _sta = new("EarthWatch3D rasterizer");

    // Kept between composes: the composer holds the base image; the target and bitmap are reused. The base image is
    // also kept here (no extra copy) as the fallback shown when a compose fails.
    private GlobeImageComposer? _composer;
    private Bgra32Image? _plain;
    private SparseOverlay? _lines;          // coastlines and borders
    private readonly LazyLayer<SparseOverlay> _plates;   // plate boundaries, rasterized the first time they are shown
    private SparseOverlay? _linesAndPlates; // the two merged, plates on top
    private Bgra32Image? _composed;
    private WriteableBitmap? _bitmap;
    private bool _composing, _recomposePending, _closed;
    // The Areas layer (alert zones, cones, wind bands): planned on the UI thread; a new immutable image is rasterized
    // on the STA worker only when the plan's key changes. The previous image stays alive only while a compose reads it.
    private Bgra32Image? _areas;
    private int _areasKey;
    private bool _areasRunning, _areasPending;
    private int _zonesChangePending;
    // A display setting (night strength, day/night line, coastlines) changed since the last compose started.
    private bool _settingsDirty;

    private bool _overlayDirty = true, _adjustingCamera;
    // The visible events must be worked out again (display time, store, filter or live mode changed).
    private bool _eventsDirty = true;
    // 1 while a store change is waiting to reach the UI thread (Changed fires on feed threads), so they coalesce.
    private int _storeChangePending;
    private VisibleEvents _visible = VisibleEvents.None;
    private DateTime _visibleAtUtc;
    // Programmatic camera moves (CameraAnimator) do not always raise CameraChanged, so compare the pose too.
    private (Point3D, Vector3D, Vector3D, double) _lastPose;

#if PERF_LOG
    private int _composeCount;
#endif

    /// <param name="overlay">The canvas over the viewport for the Sun/Moon icons and edge arrows.</param>
    /// <param name="card">The Sun &amp; Moon card, which the edge arrows keep clear of. It and <paramref name="overlay"/> must share a parent.</param>
    /// <param name="window">The window hosting the viewport: its Loaded, Closed, StateChanged and PreviewKeyDown events drive the scene.</param>
    /// <param name="store">The events to draw.</param>
    /// <param name="zones">NWS zone shapes for alerts that name zones only (used from Task 15 on).</param>
    /// <param name="log">Where compose and load failures are written (none if null).</param>
    public SceneController(HelixViewport3D viewport, Canvas overlay, FrameworkElement card, MainViewModel vm, Window window,
                           EventStore store, ZoneCache zones, IAppLog? log = null)
    {
        _log = log ?? new NullLog();
        // Nothing need await the first compose (only the diagnostics do), so observe a fault here: otherwise it would
        // reach TaskScheduler.UnobservedTaskException and the crash handler would show a second message for it.
        _firstCompose.Task.ContinueWith(static t => _ = t.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        Store = store ?? throw new ArgumentNullException(nameof(store));
        Zones = zones ?? throw new ArgumentNullException(nameof(zones));
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        _overlayCanvas = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _card = card ?? throw new ArgumentNullException(nameof(card));
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _window = window ?? throw new ArgumentNullException(nameof(window));

        _animator = new CameraAnimator(() => Camera);
        _plates = new LazyLayer<SparseOverlay>(BuildPlatesAsync);
        _overlay = new SunMoonOverlay(overlay);
        _markers = new SunMoonGlobeMarkers();
        _viewport.Children.Add(_globeVisual);
        _events = new EventScene(_viewport);   // after the globe, before the Sun/Moon markers
        foreach (var v in _markers.Visuals) _viewport.Children.Add(v);
        _labels = new LabelOverlay(overlay);

        _window.Loaded += OnLoaded;
        _window.Closed += OnClosed;
        _window.StateChanged += OnWindowStateChanged;
        _window.PreviewKeyDown += OnPreviewKeyDown;
        _viewport.CameraChanged += OnCameraChanged;
        _viewport.PreviewMouseWheel += OnPreviewMouseWheel;
        _viewport.PreviewMouseDown += OnPreviewMouseDown;
        _viewport.PreviewMouseUp += OnPreviewMouseUp;
        _viewport.MouseMove += OnViewportMouseMove;
        _viewport.MouseLeave += OnViewportMouseLeave;
        _markerTip.PlacementTarget = _viewport;
        _hoverTimer.Tick += OnHoverTimerTick;
        _viewport.SizeChanged += OnViewportSizeChanged;
        _card.SizeChanged += OnViewportSizeChanged;   // collapsing the card changes the area the arrows avoid
        _vm.DisplayTimeChanged += OnDisplayTimeChanged;
        _vm.FilterChanged += OnFilterChanged;
        _vm.Timeline.PropertyChanged += OnTimelineChanged;
        Store.Changed += OnStoreChanged;
        Zones.ZonesLoaded += OnZonesLoaded;
        _vm.SunMoon.PropertyChanged += OnSunMoonChanged;
        _vm.SunMoon.FaceRequested += OnFaceRequested;
        _vm.Layers.PropertyChanged += OnDisplaySettingChanged;
        _vm.Display.PropertyChanged += OnDisplaySettingChanged;
        _trailing.Tick += OnTrailingTick;
        CompositionTarget.Rendering += OnRendering;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;   // static event: unsubscribed in OnClosed
    }

    /// <summary>Completes once the first composed globe image is on the globe (cancelled if the window closes first).</summary>
    public Task FirstComposeDisplayed => _firstCompose.Task;

    /// <summary>Raised on the UI thread each time a composed globe image is put on the globe, with the UTC time it shows.</summary>
    public event EventHandler<DateTime>? ComposeDisplayed;

    public bool IsCameraAnimating => _animator.IsRunning;

    /// <summary>True when the globe image on screen includes an Areas layer (alert zones, cyclone cones or wind bands).</summary>
    internal bool AreasDisplayed { get; private set; }

    /// <summary>
    /// True when the globe on screen shows the current Areas layer, or there is none to show (nothing visible has an
    /// area). With <see cref="IsBusy"/> false, the visible events are fully drawn.
    /// </summary>
    internal bool AreasSettled => _areas is null || AreasDisplayed;

    /// <summary>
    /// True while a compose or an Areas rebuild is running or queued, or a store, zone or display-time change has not
    /// yet been worked into the visible events. The diagnostics wait for this to clear before a snapshot.
    /// </summary>
    internal bool IsBusy => _composing || _recomposePending || _areasRunning || _areasPending || _eventsDirty
                            || Volatile.Read(ref _storeChangePending) != 0 || Volatile.Read(ref _zonesChangePending) != 0;

    /// <summary>The event store the scene will draw from.</summary>
    internal EventStore Store { get; }

    /// <summary>The NWS zone cache for zone-only alerts.</summary>
    internal ZoneCache Zones { get; }

    /// <summary>Other overlay elements (the details panel, the layer strip): no hover picking while the mouse is over them.</summary>
    public IList<UIElement> HoverBlockers { get; } = [];

    /// <summary>The viewport's camera, or null if something replaced it with a non-perspective camera.</summary>
    internal PerspectiveCamera? Camera => _viewport.Camera as PerspectiveCamera;

    // ---------------------------------------------------------------- globe image

    /// <summary>
    /// Composes the globe image for the display time on a background task and shows it.
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
                _settingsDirty = false;
                var now = _vm.DisplayTimeUtc;
                _schedule.MarkComposed(now);
                var subSolar = SubPoints.SubSolarPoint(now);
                // The coastlines are rasterized once at load and the plates the first time they are shown;
                // switching a layer off just leaves it out.
                var display = _vm.Display;
                var lineLayer = await GetLinesLayerAsync(_vm.Layers.ShowCoastlines, _vm.Layers.ShowPlates);
                if (_closed) return;
                var layers = new GlobeLayers(subSolar, display.NightStrength, Areas: _areas,
                    SparseLines: lineLayer, DrawTerminator: display.ShowTerminator);
                var composer = _composer!;
                var target = _composed!;
                var plain = _plain!;
                var log = _log;
#if PERF_LOG
                var sw = Stopwatch.StartNew();
#endif
                bool composed = await Task.Run(() => ComposeOrPlain(composer, layers, target, plain, log));
#if PERF_LOG
                long composeMs = sw.ElapsedMilliseconds;
#endif
                if (_closed) return;
                ImageConversions.CopyInto(target, _bitmap!);
                _globeVisual.Content ??= new GeometryModel3D(GlobeMesh.Create(), new DiffuseMaterial(new ImageBrush(_bitmap)));
                AreasDisplayed = composed && layers.Areas is not null;
#if PERF_LOG
                PerfLog.Write(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"compose #{++_composeCount} utc={now:yyyy-MM-ddTHH:mm:ss.fffZ} subsolar lat={subSolar.LatitudeDeg:F3} lon={subSolar.LongitudeDeg:F3} compose={composeMs} ms total={sw.ElapsedMilliseconds} ms {PerfLog.Memory()}"));
#endif
                _firstCompose.TrySetResult();
                ComposeDisplayed?.Invoke(this, now);
            } while (_recomposePending && !_closed);
        }
        catch (OperationCanceledException) when (_closed) { }   // the rasterizer was shut down by the window closing
        catch (Exception ex)
        {
            // ComposeOrPlain already handles a failed compose; this is anything else (copying to the bitmap, say).
            _log.Error("Globe image could not be shown", ex);
            _firstCompose.TrySetException(ex);
        }
        finally { _composing = false; }
    }

    /// <summary>
    /// Composes <paramref name="layers"/> into <paramref name="target"/>. If that throws (low memory, for example), logs it,
    /// fills <paramref name="target"/> with <paramref name="plain"/>, the base image with no shading (spec section 11), and
    /// returns false. The schedule was marked before the compose, so the next scheduled compose (within a minute) retries.
    /// </summary>
    public static bool ComposeOrPlain(GlobeImageComposer composer, GlobeLayers layers, Bgra32Image target, Bgra32Image plain, IAppLog log)
    {
        try
        {
            composer.ComposeInto(layers, target);
            return true;
        }
        catch (Exception ex)
        {
            log.Error("Globe compose failed; showing the plain image until the next compose", ex);
            Buffer.BlockCopy(plain.Pixels, 0, target.Pixels, 0, Math.Min(plain.Pixels.Length, target.Pixels.Length));
            return false;
        }
    }

    private void OnDisplayTimeChanged(object? sender, EventArgs e)
    {
        _eventsDirty = true;
        if (_window.WindowState == WindowState.Minimized) return;   // composed on restore instead
        if (_schedule.IsDue(_vm.DisplayTimeUtc)) RequestRecompose();
    }

    private void OnDisplaySettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(LayersViewModel.ShowCoastlines) or nameof(LayersViewModel.ShowPlates)
                                   or nameof(DisplayOptionsViewModel.NightStrength)
                                   or nameof(DisplayOptionsViewModel.ShowTerminator))) return;
        // Switching plates off is the way to retry a plate load that failed (it is not retried on every compose).
        if (e.PropertyName == nameof(LayersViewModel.ShowPlates) && !_vm.Layers.ShowPlates) _plates.ForgetFailure();
        _settingsDirty = true;
        if (_window.WindowState == WindowState.Minimized) return;   // composed on restore instead
        RequestRecompose();   // throttled: the night-shade slider changes it many times a second
    }

    // Scrubbing changes the display time many times a second: compose at most every 250 ms, and let a one-shot
    // timer make the last request after the gap so the globe ends on the final position.
    private void RequestRecompose()
    {
        if (_closed) return;
        if (_throttle.TryAcquire(DateTime.UtcNow))
        {
            _trailing.Stop();
            Recompose();
            return;
        }
        var wait = _throttle.NextAllowedUtc - DateTime.UtcNow;
        _trailing.Interval = wait > TimeSpan.FromMilliseconds(1) ? wait : TimeSpan.FromMilliseconds(1);
        _trailing.Stop();
        _trailing.Start();
    }

    private void OnTrailingTick(object? sender, EventArgs e)
    {
        _trailing.Stop();
        if (_closed || _window.WindowState == WindowState.Minimized) return;
        if (_settingsDirty || _schedule.IsDue(_vm.DisplayTimeUtc)) RequestRecompose();
    }

    // SystemEvents calls each handler through the SynchronizationContext that was current when it subscribed (here the
    // UI thread's, from the constructor), so this normally runs on the UI thread already. The BeginInvoke lets the
    // resume notification return first, and would also cover a subscription made where there was no context.
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
            if (Camera is { } camera)
            {
                SetDefaultView(camera, FitDistanceForViewport(camera));
                ApplyRotationSensitivity(camera);
            }
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
            // Rasterize on the STA worker, and keep only the line pixels: the dense layer would cost 4·w·h bytes.
            var lines = await _sta.InvokeAsync(() => SparseOverlay.FromImage(LineRasterizer.Rasterize(
                [(borders, Color.FromArgb(102, 211, 211, 211)), (coast, Color.FromArgb(153, 255, 255, 255))], w, h, 1.0)));
#if PERF_LOG
            PerfLog.Write(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"load: rasterize + sparse {load.ElapsedMilliseconds} ms ({lines.Count} line pixels, {100.0 * lines.Count / ((double)w * h):F2}%) {PerfLog.Memory()}"));
#endif
            if (_closed) return;
            _composer = new GlobeImageComposer(baseImage);
            _plain = baseImage;
            _lines = lines;
            _composed = Bgra32Image.Create(w, h);
            _bitmap = ImageConversions.CreateBitmap(w, h);
            Recompose();
            RequestAreas();   // composes again once the Areas layer exists
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
    /// The lines layer for the toggles: coastlines and/or plate boundaries (plates on top), or null for neither.
    /// The plates are read and rasterized on the STA worker the first time they are wanted, then kept. A failed load
    /// is logged once and not retried on every compose, only after the user switches plates off and on again.
    /// </summary>
    private async Task<SparseOverlay?> GetLinesLayerAsync(bool coastlines, bool plates)
    {
        var plateLines = plates && _lines is not null ? await _plates.GetAsync(OnPlatesFailed) : null;
        if (plateLines is not null)
        {
            if (!coastlines || _lines is null) return plateLines;
            return _linesAndPlates ??= SparseOverlay.Merge(_lines, plateLines);
        }
        return coastlines ? _lines : null;
    }

    private void OnPlatesFailed(Exception ex)
    {
        if (_closed && ex is OperationCanceledException) return;   // the rasterizer was shut down by the window closing
        _log.Error("Plate boundaries could not be drawn; showing coastlines only (switch plates off and on to try again)", ex);
    }

    /// <summary>Reads and rasterizes the PB2002 plate boundaries at the size of the coastlines layer (on the STA worker).</summary>
    private Task<SparseOverlay> BuildPlatesAsync()
    {
        int w = _lines!.Width, h = _lines.Height;
        return _sta.InvokeAsync(() =>
        {
            using var s = Asset("plate_boundaries.geojson");
            var all = PlateBoundaries.Read(s);
            IEnumerable<IReadOnlyList<GeoPosition>> Of(PlateBoundaryKind k) => all.Where(b => b.Kind == k).Select(b => b.Line.Points);
            Color C(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            return SparseOverlay.FromImage(LineRasterizer.Rasterize(
            [
                (Of(PlateBoundaryKind.Transform), C(PlateBoundaries.TransformArgb)),
                (Of(PlateBoundaryKind.Divergent), C(PlateBoundaries.DivergentArgb)),
                (Of(PlateBoundaryKind.Convergent), C(PlateBoundaries.ConvergentArgb)),
            ], w, h, 3.0));
        });
    }

    private static Stream Asset(string name) =>
        typeof(SceneController).Assembly.GetManifestResourceStream(AssetPrefix + name)
        ?? throw new InvalidOperationException($"Missing embedded resource {name}.");

    private static List<IReadOnlyList<GeoPosition>> ReadLines(string name)
    {
        using var s = Asset(name);
        return GeoJsonLines.Read(s).ToList();
    }

    // ---------------------------------------------------------------- Sun/Moon markers and overlay

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e) => _overlayDirty = true;
    private void OnSunMoonChanged(object? sender, PropertyChangedEventArgs e) => _overlayDirty = true;

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_window.WindowState == WindowState.Minimized) { _overlay.HideAll(); return; }
        _overlayDirty = true;
        if (_settingsDirty || _schedule.IsDue(_vm.DisplayTimeUtc)) Recompose();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_window.WindowState == WindowState.Minimized) { _overlay.HideAll(); _labels.Begin(); _labels.End(); _overlayDirty = true; return; }
        NoteCameraChange();
        bool evaluate = _eventsDirty && _eventThrottle.TryAcquire(DateTime.UtcNow);
        if (evaluate)
        {
            _eventsDirty = false;
            _visibleAtUtc = _vm.DisplayTimeUtc;
            _visible = EventVisibility.At(Store.Snapshot, _visibleAtUtc, _vm.CurrentFilter);
            _vm.Selection.ClearIfNotVisible(_visible);
            _overlayDirty = true;   // the labels
            RequestAreas();
        }
        if (_overlayDirty)
        {
            _overlayDirty = false;
            UpdateSunMoon();
            UpdateEvents();
        }
        if (_events.HasRings) _events.AnimateRings(_clock.Elapsed);
    }

    // ---------------------------------------------------------------- events

    // Called on the feed thread that wrote the store. Only a flag crosses threads; the store is read on the UI thread.
    private void OnStoreChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _storeChangePending, 1) == 1) return;
        _window.Dispatcher.BeginInvoke(() =>
        {
            Volatile.Write(ref _storeChangePending, 0);
            _eventsDirty = true;
        });
    }

    // Raised on a background thread when zone shapes arrive; coalesced like store changes.
    private void OnZonesLoaded(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _zonesChangePending, 1) == 1) return;
        _window.Dispatcher.BeginInvoke(() =>
        {
            Volatile.Write(ref _zonesChangePending, 0);
            RequestAreas();
        });
    }

    // ---------------------------------------------------------------- areas layer

    /// <summary>Re-rasterizes the Areas layer if what it shows changed (one run at a time; requests meanwhile collapse into one).</summary>
    private void RequestAreas()
    {
        if (_closed || _composed is null) return;   // before load completes, the load requests it
        if (_areasRunning) { _areasPending = true; return; }
        _ = AreasLoopAsync();
    }

    private async Task AreasLoopAsync()
    {
        _areasRunning = true;
        try
        {
            do
            {
                _areasPending = false;
                try
                {
                    // Planned here on the UI thread from memory only; zones not in memory yet are read from disk in the
                    // background, and ZonesLoaded brings us back with a new key.
                    var unresolved = new List<string>();
                    var plan = AreasLayer.Plan(_visible, _vm.CurrentFilter,
                        id => Zones.TryGetCached(id, out var polys) ? polys : null, unresolved);
                    if (unresolved.Count > 0) Zones.LoadInBackground(unresolved);
                    if (plan.Key == _areasKey) continue;   // nothing that shows has changed
                    int w = _composed!.Width, h = _composed.Height;
                    var image = plan.Shapes.Count == 0 ? null : await _sta.InvokeAsync(() => AreasLayer.Rasterize(plan, w, h));
                    if (_closed) return;
                    _areasKey = plan.Key;
                    _areas = image;   // null when nothing is to be drawn
                    Recompose();
                }
                catch (OperationCanceledException) when (_closed) { return; }
                catch (Exception ex)
                {
                    // One bad pass must not stop later ones: forget the key so the next trigger rebuilds.
                    _log.Error("Areas layer failed", ex);
                    _areasKey = 0;
                }
            } while (_areasPending && !_closed);
        }
        finally { _areasRunning = false; }
    }

    private void OnFilterChanged(object? sender, EventArgs e) => _eventsDirty = true;

    private void OnTimelineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineViewModel.IsLive)) _eventsDirty = true;   // rings only while live
    }

    /// <summary>Hands the visible events to the scene (cheap if nothing changed) and places their labels.</summary>
    private void UpdateEvents()
    {
        if (_viewport.Camera is not PerspectiveCamera camera) return;
        var projector = new Projector(camera, _viewport.ActualWidth, _viewport.ActualHeight);
        _events.Update(_visible, _visibleAtUtc, _vm.Timeline.IsLive, projector, ((Vector3D)camera.Position).Length);
        _events.DrawLabels(_labels, projector);
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
            new Point3D(moon.X * moonR, moon.Y * moonR, moon.Z * moonR), fraction, sm.MoonWaxing, CardBounds());
    }

    // ---------------------------------------------------------------- marker tooltips

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        _hoverTimer.Stop();
        if (e.LeftButton == MouseButtonState.Pressed || IsOverOverlayElement()) { HideMarkerTip(); return; }
        _hoverMouse = e.GetPosition(_viewport);
        // About 30 Hz, with a trailing pick so the last position is always handled.
        if (_hoverThrottle.TryAcquire(DateTime.UtcNow)) UpdateHover(_hoverMouse);
        else _hoverTimer.Start();
    }

    private void OnHoverTimerTick(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        _hoverThrottle.TryAcquire(DateTime.UtcNow);
        if (!IsOverOverlayElement()) UpdateHover(_hoverMouse);
    }

    private bool IsOverOverlayElement()
    {
        if (_card.IsMouseOver) return true;
        foreach (var b in HoverBlockers) if (b.IsMouseOver) return true;
        return false;
    }

    private void UpdateHover(Point mouse)
    {
        if (Camera is not { } camera || Mouse.LeftButton == MouseButtonState.Pressed) { HideMarkerTip(); return; }
        var projector = new Projector(camera, _viewport.ActualWidth, _viewport.ActualHeight);
        var sm = _vm.SunMoon;
        string? text = MarkerHover.Hits(mouse, projector, sm.SubSolar) ? MarkerHover.SunText(sm.SubSolar)
                     : MarkerHover.Hits(mouse, projector, sm.SubLunar) ? MarkerHover.MoonText(sm.SubLunar)
                     : null;
        if (text is null && PickEvent(mouse, projector, hover: true) is { } hovered)
            text = SelectionViewModel.HoverText(hovered, _vm.DisplayTimeUtc);
        if (text is null) { HideMarkerTip(); return; }
        if (!Equals(_markerTip.Content, text)) _markerTip.Content = text;
        _markerTip.HorizontalOffset = mouse.X + 16;
        _markerTip.VerticalOffset = mouse.Y + 16;
        _markerTip.IsOpen = true;
    }

    /// <summary>The event under the mouse: point events first, then (needing a surface hit test) alert areas.</summary>
    private EarthEvent? PickEvent(Point mouse, Projector projector, bool hover = false)
    {
        var visible = _visible;
        var hit = EventPicker.Pick(mouse, projector, visible, null);
        if (hit is not null || visible.Alerts.Count == 0) return hit;
        // Analytic ray/sphere hit: cheap enough for 30 Hz hover (a Viewport3D hit test walks every mesh in the scene).
        GeoPosition? surface = null;
        if (projector.TryHitUnitSphere(mouse, out Point3D p))
            surface = GeoPosition.FromVector(new Vector3d(p.X, p.Y, p.Z));
        return EventPicker.Pick(mouse, projector, visible, surface,
            // Hover must not touch the disk (it runs at 30 Hz); a click may.
            id => (hover ? Zones.TryGetCached(id, out var polys) : Zones.TryGet(id, out polys)) ? polys : null);
    }

    private void OnViewportMouseLeave(object sender, MouseEventArgs e)
    {
        // A trailing hover tick still queued from the last move would otherwise reopen the tooltip after the mouse left.
        _hoverTimer.Stop();
        HideMarkerTip();
    }

    private void HideMarkerTip()
    {
        if (_markerTip.IsOpen) _markerTip.IsOpen = false;
    }

    /// <summary>The card's rectangle in overlay coordinates, or Rect.Empty if it is not shown. Allocation-free.</summary>
    private Rect CardBounds()
    {
        if (!_card.IsVisible || _card.ActualWidth <= 0 || _card.ActualHeight <= 0) return Rect.Empty;
        // Both are children of the same grid, so the difference of their offsets in it is the card's offset in the overlay.
        var o = VisualTreeHelper.GetOffset(_card) - VisualTreeHelper.GetOffset(_overlayCanvas);
        return new Rect(o.X, o.Y, _card.ActualWidth, _card.ActualHeight);
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
        CameraRules.FitDistance(camera.FieldOfView, _viewport.ActualWidth / Math.Max(1, _viewport.ActualHeight));

    private void ApplyRotationSensitivity(PerspectiveCamera camera) =>
        _viewport.RotationSensitivity = CameraRules.RotationSensitivity(((Vector3D)camera.Position).Length);

    /// <summary>Turns the camera at once (no animation) to look straight down on <paramref name="p"/>, keeping its distance.</summary>
    public void LookAt(GeoPosition p)
    {
        if (Camera is not { } camera) return;
        _animator.Cancel();
        var (pos, look, up) = CameraRules.LookDownOn(p, ((Vector3D)camera.Position).Length);
        camera.Position = pos; camera.LookDirection = look; camera.UpDirection = up;
        _overlayDirty = true;
    }

    private void OnFaceRequested(object? sender, GeoPosition p)
    {
        if (Camera is not { } camera) return;
        double distance = ((Vector3D)camera.Position).Length;
        _animator.AnimateTo(CameraRules.LookDownOn(p, distance).Position);
    }

    private void OnCameraChanged(object? sender, RoutedEventArgs e)
    {
        _overlayDirty = true;
        if (_adjustingCamera || Camera is not { } camera) return;
        _adjustingCamera = true;
        try
        {
            var clamped = CameraRules.ClampDistance(camera.Position);
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
        var next = CameraRules.ClampDistance(new Point3D(0, 0, 0) + pos * Math.Pow(CameraRules.ZoomPerNotch, -notches));
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

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) _clickStart = e.ClickCount == 1 ? e.GetPosition(_viewport) : null;
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 2) return;
        e.Handled = true;   // keep Helix's own double-click (look-at) out of it
        if (Camera is not { } camera) return;
        var projector = new Projector(camera, _viewport.ActualWidth, _viewport.ActualHeight);
        if (!projector.TryHitUnitSphere(e.GetPosition(_viewport), out Point3D hit)) return;
        var geo = GeoPosition.FromVector(new Vector3d(hit.X, hit.Y, hit.Z));
        double distance = ((Vector3D)camera.Position).Length;
        _animator.AnimateTo(CameraRules.LookDownOn(geo, distance).Position);
    }

    /// <summary>A left click (released within 4 px of where it went down) selects the event under it, or clears the selection.</summary>
    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _clickStart is not { } start) return;
        _clickStart = null;
        var end = e.GetPosition(_viewport);
        if ((end - start).Length >= ClickSlopPx || Camera is not { } camera) return;
        var projector = new Projector(camera, _viewport.ActualWidth, _viewport.ActualHeight);
        var sm = _vm.SunMoon;   // a Sun/Moon marker under the mouse wins: the click does nothing
        if (MarkerHover.Hits(end, projector, sm.SubSolar) || MarkerHover.Hits(end, projector, sm.SubLunar)) return;
        _vm.Selection.Select(PickEvent(end, projector));
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
        _viewport.PreviewMouseUp -= OnPreviewMouseUp;
        _viewport.MouseMove -= OnViewportMouseMove;
        _viewport.MouseLeave -= OnViewportMouseLeave;
        _hoverTimer.Stop();
        _hoverTimer.Tick -= OnHoverTimerTick;
        HideMarkerTip();
        _viewport.SizeChanged -= OnViewportSizeChanged;
        _card.SizeChanged -= OnViewportSizeChanged;
        _vm.DisplayTimeChanged -= OnDisplayTimeChanged;
        _vm.FilterChanged -= OnFilterChanged;
        _vm.Timeline.PropertyChanged -= OnTimelineChanged;
        Store.Changed -= OnStoreChanged;
        Zones.ZonesLoaded -= OnZonesLoaded;
        _trailing.Stop();
        _trailing.Tick -= OnTrailingTick;
        _vm.SunMoon.PropertyChanged -= OnSunMoonChanged;
        _vm.SunMoon.FaceRequested -= OnFaceRequested;
        _vm.Layers.PropertyChanged -= OnDisplaySettingChanged;
        _vm.Display.PropertyChanged -= OnDisplaySettingChanged;
        _sta.Dispose();   // queued rasterizing is cancelled; the awaiting loops see _closed and stop
        _firstCompose.TrySetCanceled();
    }
}
