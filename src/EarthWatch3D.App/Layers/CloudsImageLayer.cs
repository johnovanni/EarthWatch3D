using System.IO;
using EarthWatch3D.App.ViewModels;
using GlobeCore.Earth.Clouds;
using GlobeCore.Imaging;
using GlobeCore.Logging;
using GlobeCore.Wpf.Layers;
using GlobeCore.Wpf.Rendering;

namespace EarthWatch3D.App.Layers;

/// <summary>
/// The daily VIIRS cloud image, drawn on the surface (so the night shade darkens it). Each image that arrives is decoded
/// and keyed against the base image on the STA worker, then the globe is recomposed at once. The layer's visibility is
/// the Clouds toggle; the built layer is kept for when it is switched on again. (Moved from SceneController:
/// SetCloudImage, BuildCloudsAsync.)
/// </summary>
public sealed class CloudsImageLayer : ImageLayer
{
    public const string LayerId = "clouds";
    public const int Order = 10;

    private readonly Bgra32Image _baseImage;
    private readonly LayersViewModel _layers;
    private readonly StaWorker _sta;
    private readonly IAppLog _log;
    private readonly Func<bool> _isClosed;
    private CloudImage? _cloudImage;    // the newest daily image received (UI thread)
    private CloudImage? _cloudsTried;   // the image whose layer was last built (or failed to build)
    private IImageLayer? _current;      // its cloud layer, built against the base

    /// <param name="layers">Receives the legend line saying which day the image is from.</param>
    /// <param name="isClosed">True once the window has closed.</param>
    public CloudsImageLayer(Bgra32Image baseImage, LayersViewModel layers, StaWorker sta, IAppLog log, Func<bool> isClosed)
        : base(LayerId, "Clouds", Order)
    {
        _baseImage = baseImage ?? throw new ArgumentNullException(nameof(baseImage));
        _layers = layers ?? throw new ArgumentNullException(nameof(layers));
        _sta = sta ?? throw new ArgumentNullException(nameof(sta));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _isClosed = isClosed ?? throw new ArgumentNullException(nameof(isClosed));
    }

    public ImagePass Pass => ImagePass.Surface;

    public override IImageLayer? Current => _current;

    /// <summary>True while an image that has arrived has not been built (or failed to build) yet.</summary>
    public bool IsBuilding => _cloudImage is not null && !ReferenceEquals(_cloudImage, _cloudsTried);

    /// <summary>A daily cloud image has arrived (UI thread): builds its layer, then recomposes.</summary>
    public void SetImage(CloudImage image)
    {
        _cloudImage = image;
        _ = BuildAsync();
    }

    private async Task BuildAsync()
    {
        var image = _cloudImage;
        var baseImage = _baseImage;
        if (image is null || _isClosed()) return;
        try
        {
            var clouds = await _sta.InvokeAsync(() =>
            {
                using var ms = new MemoryStream(image.Jpeg);
                return CloudLayer.Build(ImageConversions.DecodeToBgra32(ms, baseImage.Width), baseImage);
            });
            if (_isClosed() || !ReferenceEquals(image, _cloudImage)) return;   // closed, or a newer image is on its way
            _current = new BitmapBlendLayer(clouds, Pass, Order);
            _cloudsTried = image;
            _layers.CloudsLegend = $"Image of {image.DateUtc:d MMM yyyy} (UTC day), VIIRS NOAA-21.";
            InvalidateNow();
        }
        catch (OperationCanceledException) when (_isClosed()) { }
        catch (Exception ex)
        {
            _log.Warn("The cloud image could not be used", ex);   // the layer stays as it was
            if (ReferenceEquals(image, _cloudImage)) _cloudsTried = image;
        }
    }
}
