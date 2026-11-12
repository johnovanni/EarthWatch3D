using System.Globalization;
using EarthWatch3D.Core.Globe;
using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Storage;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Core.Feeds.Gibs;

/// <summary>One daily VIIRS image: its UTC date (the GIBS TIME) and the JPEG bytes.</summary>
public sealed record CloudImage(DateTime DateUtc, byte[] Jpeg);

/// <summary>
/// Fetches the daily NASA GIBS VIIRS true-colour image (previous UTC day) while the clouds layer is wanted, checks it
/// hourly, and keeps it in <c>cache\clouds\viirs_yyyy-MM-dd.jpg</c>. A cached file for the date means no download.
/// If the previous day is still partly black the day before is used and the previous day is tried again next run.
/// </summary>
public sealed class GibsCloudsFeed : IEventFeed
{
    public const string Layer = "VIIRS_NOAA21_CorrectedReflectance_TrueColor";
    public const string UrlPrefix = "https://gibs.earthdata.nasa.gov/wms/epsg4326/best/wms.cgi";
    /// <summary>More black than this (between 60°N and 60°S) means the day's passes are not all in yet.</summary>
    public const double MaxBlackFraction = 0.05;

    private static readonly TimeSpan HourlyInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(10);

    private readonly FeedHttp _http;
    private readonly AppPaths _paths;
    private readonly IClock _clock;
    private readonly Func<bool> _wanted;
    private readonly IAppLog _log;
    private readonly Func<byte[], Bgra32Image>? _decode;
    private DateTime? _raised;
    private volatile bool _idle;

    /// <param name="decode">Decodes a JPEG (any size) for the black-fraction check; without it every image is accepted.</param>
    public GibsCloudsFeed(FeedHttp http, AppPaths paths, IClock clock, Func<bool> wanted, IAppLog log,
                          Func<byte[], Bgra32Image>? decode = null)
    {
        _http = http; _paths = paths; _clock = clock; _wanted = wanted; _log = log; _decode = decode;
    }

    public string Name => "Clouds";

    /// <summary>
    /// One hour. While the layer is off the feed looks again after 10 s instead, so switching it on does not wait
    /// up to an hour for the next run.
    /// </summary>
    public TimeSpan Interval => _idle ? IdleInterval : HourlyInterval;

    /// <summary>Raised on the feed's thread when the image for the day in use is first loaded (from the network or the cache).</summary>
    public event EventHandler<CloudImage>? ImageReady;

    public static Uri RequestUri(DateTime dateUtc) => new(UrlPrefix
        + "?SERVICE=WMS&REQUEST=GetMap&VERSION=1.3.0&CRS=EPSG:4326&BBOX=-90,-180,90,180&WIDTH=4096&HEIGHT=2048&STYLES="
        + "&LAYERS=" + Layer + "&FORMAT=image/jpeg&TIME=" + dateUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    public async Task<FeedRunResult> RunOnceAsync(CancellationToken ct)
    {
        _idle = !_wanted();
        if (_idle) return FeedRunResult.Skipped;

        var yesterday = _clock.UtcNow.Date.AddDays(-1);
        var image = await LoadAsync(yesterday, checkBlack: true, ct).ConfigureAwait(false);
        if (image is null)
        {
            _log.Info($"Clouds: the image for {yesterday:yyyy-MM-dd} is still partial; using the day before");
            image = await LoadAsync(yesterday.AddDays(-1), checkBlack: false, ct).ConfigureAwait(false);
        }
        DeleteOlderThan(image!.DateUtc);
        if (_raised != image.DateUtc)
        {
            _raised = image.DateUtc;
            ImageReady?.Invoke(this, image);
        }
        return FeedRunResult.Ok;
    }

    /// <summary>The cached image for the date, else a download. Null if the download is too black to use (and not cached).</summary>
    private async Task<CloudImage?> LoadAsync(DateTime date, bool checkBlack, CancellationToken ct)
    {
        var file = FileFor(date);
        if (File.Exists(file))
        {
            try
            {
                var cached = await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false);
                // A truncated JPEG decodes without an error (the decoder fills the rest), so check the end marker too.
                if (cached.Length < 2 || cached[^2] != 0xFF || cached[^1] != 0xD9) throw new InvalidDataException("The JPEG is cut short.");
                _decode?.Invoke(cached);   // a damaged file must not wedge the layer for the day
                return new CloudImage(date, cached);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)   // any decoder failure (FileFormatException, COMException...) means damaged
            {
                _log.Warn($"Clouds: {file} is unusable; downloading it again", ex);
                try { File.Delete(file); } catch (Exception) { }
            }
        }
        var bytes = await _http.GetBytesAsync(RequestUri(date), ct).ConfigureAwait(false);
        if (checkBlack && _decode is not null)
        {
            double black = CloudLayer.BlackFraction(_decode(bytes));
            if (black > MaxBlackFraction) return null;
        }
        if (!AtomicFile.TryWriteAllBytes(file, bytes, out var error))
            _log.Warn($"Clouds: could not save {file}", error);
        return new CloudImage(date, bytes);
    }

    private string FileFor(DateTime date) =>
        Path.Combine(_paths.CloudsDir, "viirs_" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jpg");

    private void DeleteOlderThan(DateTime date)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_paths.CloudsDir, "viirs_*.jpg"))
            {
                var stem = Path.GetFileNameWithoutExtension(f)["viirs_".Length..];
                if (DateTime.TryParseExact(stem, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d < date)
                    File.Delete(f);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn("Clouds: could not delete old images", ex);
        }
    }
}
