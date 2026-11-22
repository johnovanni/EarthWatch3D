using System.Windows.Media;
using AstroCore.Earth;
using GlobeCore.Earth;
using GlobeCore.Imaging;
using GlobeCore.Logging;
using GlobeCore.Regions;
using GlobeCore.Wpf.Layers;
using GlobeCore.Wpf.Rendering;

namespace EarthWatch3D.App.Layers;

/// <summary>
/// The lines drawn over the night shade, as one sparse layer: coastlines and country borders (rasterized at load, and
/// again off the UI thread when a newer boundary set is adopted, <see cref="ShowBordersAsync"/>) and/or the PB2002 plate
/// boundaries, plates on top. The plates are read and rasterized on the STA worker the first time they are shown, then
/// kept. A failed load is logged once and not retried on every compose, only after the user switches plates off and on
/// again. (Moved from SceneController: GetLinesLayerAsync, BuildPlatesAsync, OnPlatesFailed.)
/// </summary>
public sealed class LinesImageLayer : ImageLayer
{
    public const string LayerId = "lines";
    public const int Order = 10;

    private SparseOverlay _lines;                        // coastlines and borders (replaced when the borders are redrawn)
    private readonly LazyLayer<SparseOverlay> _plates;   // plate boundaries, rasterized the first time they are shown
    private readonly StaWorker _sta;
    private readonly IAppLog _log;
    private readonly Func<bool> _isClosed;
    private SparseOverlay? _linesAndPlates;              // the two merged, plates on top
    private IImageLayer? _linesLayer, _platesLayer, _mergedLayer, _current;
    private bool _showCoastlines, _showPlates;
    private BoundarySet? _wantedBorders;                  // the newest set asked for (a rebuild for an older one is dropped)

    /// <param name="coastAndBorders">The coastlines and borders at the globe image's size (<see cref="RasterizeCoastAndBorders"/>).</param>
    /// <param name="borders">The boundary set those borders were drawn from (null when the borders could not be loaded).</param>
    /// <param name="sta">Where the plates, and borders after an update, are rasterized.</param>
    /// <param name="isClosed">True once the window has closed (a cancelled plate build is then not an error).</param>
    public LinesImageLayer(SparseOverlay coastAndBorders, BoundarySet? borders, bool showCoastlines, bool showPlates, StaWorker sta,
                           IAppLog log, Func<bool> isClosed)
        : base(LayerId, "Coastlines, borders and plate boundaries", Order)
    {
        _lines = coastAndBorders ?? throw new ArgumentNullException(nameof(coastAndBorders));
        Borders = _wantedBorders = borders;
        _sta = sta ?? throw new ArgumentNullException(nameof(sta));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _isClosed = isClosed ?? throw new ArgumentNullException(nameof(isClosed));
        _plates = new LazyLayer<SparseOverlay>(BuildPlatesAsync);
        _showCoastlines = showCoastlines;
        _showPlates = showPlates;
    }

    public ImagePass Pass => ImagePass.AboveNight;

    /// <summary>The coastlines and borders are drawn. Changing it asks for a (throttled) recompose.</summary>
    public bool ShowCoastlines
    {
        get => _showCoastlines;
        set
        {
            if (_showCoastlines == value) return;
            _showCoastlines = value;
            Invalidate();
        }
    }

    /// <summary>
    /// The plate boundaries are drawn. Changing it asks for a (throttled) recompose; switching it off is the way to
    /// retry a plate load that failed.
    /// </summary>
    public bool ShowPlates
    {
        get => _showPlates;
        set
        {
            if (_showPlates == value) return;
            _showPlates = value;
            if (!value) _plates.ForgetFailure();
            Invalidate();
        }
    }

    /// <summary>What the last compose chose to blend (null for neither coastlines nor plates); set at the start of each compose.</summary>
    public override IImageLayer? Current => _current;

    /// <summary>The boundary set the borders now drawn come from (null if the borders could not be loaded). UI thread.</summary>
    public BoundarySet? Borders { get; private set; }

    /// <summary>The coastlines and borders now drawn, as line pixels (for tests).</summary>
    internal SparseOverlay CoastAndBorders => _lines;

    /// <summary>
    /// Draws the borders of <paramref name="set"/> (a newer set adopted by the "Borders" feed): reads the coastlines and
    /// rasterizes them with the set's lines at the same size, off the UI thread (the coastlines on the pool, the raster on
    /// the STA worker, as the plates are built), then swaps them in and asks for a compose if the layer is shown. Call on
    /// the UI thread. True once the set's borders are drawn; false if a newer set was asked for meanwhile, the window
    /// closed, or the raster failed (logged; the borders drawn before stay).
    /// </summary>
    public async Task<bool> ShowBordersAsync(BoundarySet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        _wantedBorders = set;   // a rebuild still running for another set is dropped when it ends
        if (ReferenceEquals(set, Borders)) return true;
        int w = _lines.Width, h = _lines.Height;
        SparseOverlay lines;
        try
        {
            var coast = await Task.Run(() => NaturalEarthLines.Coastlines());
            var borders = NaturalEarthLines.LandBoundaries(set);
            lines = await _sta.InvokeAsync(() => RasterizeCoastAndBorders(coast, borders, w, h));
        }
        catch (Exception ex)
        {
            if (!_isClosed())   // closing shuts the rasterizer down: not an error
                _log.Error($"Borders of {set.Edition} could not be drawn; still showing {Borders?.Edition ?? "the borders drawn before"}", ex);
            return false;
        }
        if (_isClosed() || !ReferenceEquals(set, _wantedBorders)) return false;
        _lines = lines;
        _linesLayer = _mergedLayer = null;   // rebuilt from the new lines at the next compose
        _linesAndPlates = null;
        Borders = set;
        if (_showCoastlines) InvalidateNow();   // built in the background: compose now (while hidden, no compose is needed)
        return true;
    }

    /// <summary>The coastlines and borders, rasterized at the globe image's size and kept as line pixels only (UI or STA thread).</summary>
    public static SparseOverlay RasterizeCoastAndBorders(IEnumerable<IReadOnlyList<GeoPosition>> coast,
                                                         IEnumerable<IReadOnlyList<GeoPosition>> borders, int w, int h) =>
        // Only the line pixels are kept: the dense layer would cost 4·w·h bytes.
        SparseOverlay.FromImage(LineRasterizer.Rasterize(
            [(borders, Color.FromArgb(102, 211, 211, 211)), (coast, Color.FromArgb(153, 255, 255, 255))], w, h, 1.0));

    // The toggles are read here, before the plates are built, as the compose loop read them before the split.
    protected override async Task PrepareForComposeAsync()
    {
        bool coastlines = _showCoastlines, plates = _showPlates;
        var plateLines = plates ? await _plates.GetAsync(OnPlatesFailed) : null;
        if (plateLines is not null)
            _current = !coastlines
                ? _platesLayer ??= new SparseOverlayLayer(plateLines, Pass, Order)
                : _mergedLayer ??= new SparseOverlayLayer(_linesAndPlates ??= SparseOverlay.Merge(_lines, plateLines), Pass, Order);
        else _current = coastlines ? _linesLayer ??= new SparseOverlayLayer(_lines, Pass, Order) : null;
    }

    private void OnPlatesFailed(Exception ex)
    {
        if (_isClosed() && ex is OperationCanceledException) return;   // the rasterizer was shut down by the window closing
        _log.Error("Plate boundaries could not be drawn; showing coastlines only (switch plates off and on to try again)", ex);
    }

    /// <summary>Reads and rasterizes the PB2002 plate boundaries at the size of the coastlines layer (on the STA worker).</summary>
    private Task<SparseOverlay> BuildPlatesAsync()
    {
        int w = _lines.Width, h = _lines.Height;
        return _sta.InvokeAsync(() =>
        {
            using var s = EarthAssets.Open(EarthAssets.PlateBoundariesFile);
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
}
