using System.Windows.Threading;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Feeds.Nws;
using GlobeCore.Imaging;
using GlobeCore.Logging;
using GlobeCore.Wpf.Layers;
using GlobeCore.Wpf.Rendering;

namespace EarthWatch3D.App.Layers;

/// <summary>
/// The Areas layer (alert zones, cyclone cones, wind bands), drawn on the surface. Planned on the UI thread; a new
/// immutable image is rasterized on the STA worker only when the plan's key changes, then the globe is recomposed at
/// once. The previous image stays alive only while a compose reads it. (Moved from SceneController: RequestAreas,
/// AreasLoopAsync, OnZonesLoaded, AreasDisplayed, AreasSettled.)
/// </summary>
public sealed class AreasImageLayer : ImageLayer
{
    public const string LayerId = "areas";
    public const int Order = 20;

    private readonly int _width, _height;
    private readonly Func<VisibleEvents> _visible;
    private readonly Func<DisplayFilter> _filter;
    private readonly ZoneCache _zones;
    private readonly StaWorker _sta;
    private readonly IAppLog _log;
    private readonly Func<bool> _isClosed;
    private readonly Dispatcher _dispatcher;
    private Bgra32Image? _areas;
    private IImageLayer? _current;
    private int _areasKey;
    private bool _areasRunning, _areasPending;
    // 1 while a zone load is waiting to reach the UI thread (ZonesLoaded fires on background threads), so they coalesce.
    private int _zonesChangePending;

    /// <param name="width">The globe image's width (the layer is rasterized at the image's size).</param>
    /// <param name="visible">The visible events, as last worked out by the events layer.</param>
    /// <param name="filter">Which events are shown.</param>
    /// <param name="zones">NWS zone shapes for alerts that name zones only.</param>
    /// <param name="isClosed">True once the window has closed.</param>
    /// <param name="dispatcher">The UI thread's dispatcher.</param>
    public AreasImageLayer(int width, int height, Func<VisibleEvents> visible, Func<DisplayFilter> filter, ZoneCache zones,
                           StaWorker sta, IAppLog log, Func<bool> isClosed, Dispatcher dispatcher)
        : base(LayerId, "Alert areas, cyclone cones and wind bands", Order)
    {
        _width = width;
        _height = height;
        _visible = visible ?? throw new ArgumentNullException(nameof(visible));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _zones = zones ?? throw new ArgumentNullException(nameof(zones));
        _sta = sta ?? throw new ArgumentNullException(nameof(sta));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _isClosed = isClosed ?? throw new ArgumentNullException(nameof(isClosed));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _zones.ZonesLoaded += OnZonesLoaded;
    }

    public ImagePass Pass => ImagePass.Surface;

    public override IImageLayer? Current => _current;

    /// <summary>True when the globe image on screen includes an Areas layer (alert zones, cyclone cones or wind bands).</summary>
    public bool AreasDisplayed { get; private set; }

    /// <summary>
    /// True when the globe on screen shows the current Areas layer, or there is none to show (nothing visible has an
    /// area).
    /// </summary>
    public bool AreasSettled => _areas is null || AreasDisplayed;

    /// <summary>True while a rebuild is running or queued, or a zone load has not reached the UI thread yet.</summary>
    public bool IsBusy => _areasRunning || _areasPending || Volatile.Read(ref _zonesChangePending) != 0;

    /// <summary>Stops listening for zone loads (the window has closed).</summary>
    public void Close() => _zones.ZonesLoaded -= OnZonesLoaded;

    protected override void OnComposeDisplayed(IImageLayer? used, bool composed) => AreasDisplayed = composed && used is not null;

    // Raised on a background thread when zone shapes arrive; coalesced like store changes.
    private void OnZonesLoaded(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _zonesChangePending, 1) == 1) return;
        _dispatcher.BeginInvoke(() =>
        {
            Volatile.Write(ref _zonesChangePending, 0);
            Request();
        });
    }

    /// <summary>Re-rasterizes the layer if what it shows changed (one run at a time; requests meanwhile collapse into one). UI thread.</summary>
    public void Request()
    {
        if (_isClosed()) return;
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
                    // Planned here on the UI thread from memory only; zones not in memory yet are read from disk, or
                    // downloaded, in the background (so replayed and newly shown alerts get outlines too), and
                    // ZonesLoaded brings us back with a new key.
                    var unresolved = new List<string>();
                    var plan = AreasLayer.Plan(_visible(), _filter(),
                        id => _zones.TryGetCached(id, out var polys) ? polys : null, unresolved);
                    if (unresolved.Count > 0) _zones.LoadInBackground(unresolved, downloadMissing: true);
                    if (plan.Key == _areasKey) continue;   // nothing that shows has changed
                    int w = _width, h = _height;
                    var image = plan.Shapes.Count == 0 ? null : await _sta.InvokeAsync(() => AreasLayer.Rasterize(plan, w, h));
                    if (_isClosed()) return;
                    _areasKey = plan.Key;
                    _areas = image;   // null when nothing is to be drawn
                    _current = image is null ? null : new BitmapBlendLayer(image, Pass, Order);
                    InvalidateNow();
                }
                catch (OperationCanceledException) when (_isClosed()) { return; }
                catch (Exception ex)
                {
                    // One bad pass must not stop later ones: forget the key so the next trigger rebuilds.
                    _log.Error("Areas layer failed", ex);
                    _areasKey = 0;
                }
            } while (_areasPending && !_isClosed());
        }
        finally { _areasRunning = false; }
    }
}
