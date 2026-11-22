using System.Windows.Media;
using AstroCore.Earth;
using GlobeCore.Earth;
using GlobeCore.Imaging;
using GlobeCore.Logging;
using GlobeCore.Wpf.Layers;
using GlobeCore.Wpf.Rendering;

namespace EarthWatch3D.App.Layers;

/// <summary>
/// The lines drawn over the night shade, as one sparse layer: coastlines and country borders (rasterized once at load)
/// and/or the PB2002 plate boundaries, plates on top. The plates are read and rasterized on the STA worker the first
/// time they are shown, then kept. A failed load is logged once and not retried on every compose, only after the user
/// switches plates off and on again. (Moved from SceneController: GetLinesLayerAsync, BuildPlatesAsync, OnPlatesFailed.)
/// </summary>
public sealed class LinesImageLayer : ImageLayer
{
    public const string LayerId = "lines";
    public const int Order = 10;

    private readonly SparseOverlay _lines;               // coastlines and borders
    private readonly LazyLayer<SparseOverlay> _plates;   // plate boundaries, rasterized the first time they are shown
    private readonly StaWorker _sta;
    private readonly IAppLog _log;
    private readonly Func<bool> _isClosed;
    private SparseOverlay? _linesAndPlates;              // the two merged, plates on top
    private IImageLayer? _linesLayer, _platesLayer, _mergedLayer, _current;
    private bool _showCoastlines, _showPlates;

    /// <param name="coastAndBorders">The coastlines and borders at the globe image's size (<see cref="RasterizeCoastAndBorders"/>).</param>
    /// <param name="sta">Where the plates are rasterized.</param>
    /// <param name="isClosed">True once the window has closed (a cancelled plate build is then not an error).</param>
    public LinesImageLayer(SparseOverlay coastAndBorders, bool showCoastlines, bool showPlates, StaWorker sta, IAppLog log, Func<bool> isClosed)
        : base(LayerId, "Coastlines, borders and plate boundaries", Order)
    {
        _lines = coastAndBorders ?? throw new ArgumentNullException(nameof(coastAndBorders));
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
