using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using AstroCore.Astronomy;
using AstroCore.Earth;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Feeds.Nws;
using GlobeCore.Geo;
using GlobeCore.Wpf.Layers;
using GlobeCore.Wpf.Rendering;
using HelixToolkit.Wpf;

namespace EarthWatch3D.App.Layers;

/// <summary>
/// The events on the globe (<see cref="EventScene"/>: quake discs and rings, cyclone glyphs and tracks, EONET dots) and
/// picking them (<see cref="EventPicker"/>). Which events are visible is worked out again on a frame, at most every
/// 250 ms, after the display time, the store, the filter or live mode changed. (Moved from SceneController: the
/// evaluation and ring animation of OnRendering, UpdateEvents, OnStoreChanged, OnFilterChanged, PickEvent.)
/// </summary>
public sealed class EventsSceneLayer : SceneLayer, IPickable
{
    public const string LayerId = "events";
    public const int Order = 100;

    private readonly EventScene _scene;
    private readonly MainViewModel _vm;
    private readonly EventStore _store;
    private readonly ZoneCache _zones;
    private readonly Action _onEvaluated;
    private readonly Dispatcher _dispatcher;
    // Which events are visible is worked out again at most every 250 ms (scrubbing changes the time many times a second).
    private readonly Throttle _eventThrottle = new(TimeSpan.FromMilliseconds(250));
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private VisibleEvents _visible = VisibleEvents.None;
    private DateTime _visibleAtUtc;
    // The visible events must be worked out again (display time, store, filter or live mode changed).
    private bool _eventsDirty = true;
    // 1 while a store change is waiting to reach the UI thread (Changed fires on feed threads), so they coalesce.
    private int _storeChangePending;
    // The events were just worked out: the rings are animated after this frame's Update, as before the split.
    private bool _ringsAfterUpdate;

    /// <param name="viewport">The viewport the scene is drawn in (its camera's field of view sizes the discs).</param>
    /// <param name="onEvaluated">Called on the UI thread each time the visible events have been worked out (the Areas layer follows them).</param>
    /// <param name="dispatcher">The UI thread's dispatcher.</param>
    public EventsSceneLayer(HelixViewport3D viewport, MainViewModel vm, EventStore store, ZoneCache zones, Action onEvaluated, Dispatcher dispatcher)
        : this(new EventScene(viewport, attachToViewport: false), vm, store, zones, onEvaluated, dispatcher) { }

    private EventsSceneLayer(EventScene scene, MainViewModel vm, EventStore store, ZoneCache zones, Action onEvaluated, Dispatcher dispatcher)
        : base(LayerId, "Events", Order, scene.Root)
    {
        _scene = scene;
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _zones = zones ?? throw new ArgumentNullException(nameof(zones));
        _onEvaluated = onEvaluated ?? throw new ArgumentNullException(nameof(onEvaluated));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _vm.DisplayTimeChanged += OnEventsInputChanged;
        _vm.FilterChanged += OnEventsInputChanged;
        _vm.Timeline.PropertyChanged += OnTimelineChanged;
        _store.Changed += OnStoreChanged;
    }

    /// <summary>Diagnostics only (--snapshot-scenes): when set, the pulsing rings are drawn at this fixed time instead of the running stopwatch.</summary>
    public TimeSpan? FrozenRingTime { get; set; }

    /// <summary>The visible events as last worked out.</summary>
    public VisibleEvents Visible => _visible;

    /// <summary>True while a store, filter or display-time change has not yet been worked into the visible events.</summary>
    public bool IsEvaluationPending => _eventsDirty || Volatile.Read(ref _storeChangePending) != 0;

    /// <summary>Stops listening to the view model and the store (the window has closed).</summary>
    public void Close()
    {
        _vm.DisplayTimeChanged -= OnEventsInputChanged;
        _vm.FilterChanged -= OnEventsInputChanged;
        _vm.Timeline.PropertyChanged -= OnTimelineChanged;
        _store.Changed -= OnStoreChanged;
    }

    /// <summary>
    /// Works out the visible events if they changed and the 250 ms throttle allows (UI thread); then redraws the scene and
    /// lets the Areas layer follow. True if it did.
    /// </summary>
    public bool EvaluateIfDue()
    {
        if (!_eventsDirty || !_eventThrottle.TryAcquire(DateTime.UtcNow)) return false;
        _eventsDirty = false;
        _visibleAtUtc = _vm.DisplayTimeUtc;
        _visible = EventVisibility.At(_store.Snapshot, _visibleAtUtc, _vm.CurrentFilter);
        _vm.Selection.ClearIfNotVisible(_visible);
        _ringsAfterUpdate = true;
        Invalidate();   // the labels
        _onEvaluated();
        return true;
    }

    protected override void OnFrame(SceneContext ctx)
    {
        if (EvaluateIfDue() || _ringsAfterUpdate) return;   // this frame's Update follows, and animates the rings after it
        AnimateRings();
    }

    /// <summary>Hands the visible events to the scene (cheap if nothing changed).</summary>
    public override void Update(SceneContext ctx)
    {
        _scene.Update(_visible, _visibleAtUtc, _vm.Timeline.IsLive, ctx.Projector, ((Vector3D)ctx.Camera.Position).Length);
        if (!_ringsAfterUpdate) return;
        _ringsAfterUpdate = false;
        AnimateRings();
    }

    private void AnimateRings()
    {
        if (_scene.HasRings) _scene.AnimateRings(FrozenRingTime ?? _clock.Elapsed);
    }

    /// <summary>Places the labels of the visible events (for the labels overlay layer).</summary>
    public void DrawLabels(LabelOverlay labels, Projector projector) => _scene.DrawLabels(labels, projector);

    public PickResult? HitTest(Point screen, Projector projector, bool hover) =>
        PickEvent(screen, projector, hover) is { } hit
            ? new PickResult(LayerId, hit, SelectionViewModel.HoverText(hit, _visibleAtUtc, _vm.Units))
            : null;

    /// <summary>The event under the mouse: point events first, then (needing a surface hit test) alert areas.</summary>
    private EarthEvent? PickEvent(Point mouse, Projector projector, bool hover)
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
            id => (hover ? _zones.TryGetCached(id, out var polys) : _zones.TryGet(id, out polys)) ? polys : null);
    }

    private void OnEventsInputChanged(object? sender, EventArgs e) => _eventsDirty = true;

    private void OnTimelineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineViewModel.IsLive)) _eventsDirty = true;   // rings only while live
    }

    // Called on the feed thread that wrote the store. Only a flag crosses threads; the store is read on the UI thread.
    private void OnStoreChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _storeChangePending, 1) == 1) return;
        _dispatcher.BeginInvoke(() =>
        {
            Volatile.Write(ref _storeChangePending, 0);
            _eventsDirty = true;
        });
    }
}
