using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Core.Lightning.Glm;

/// <summary>
/// Polls one GOES satellite's public NOAA S3 bucket every 20 s for new 20-second GLM files and puts their flashes into the
/// lightning buffer. Runs only while the lightning layer is wanted and the camera faces the satellite.
/// </summary>
public sealed class GlmFeed : IEventFeed
{
    public const int MaxFilesPerRun = 45;
    private static readonly TimeSpan BufferHorizon = TimeSpan.FromMinutes(60);

    private readonly FeedHttp _http;
    private readonly LightningBuffer _buffer;
    private readonly LightningSensor _sensor;
    private readonly IClock _clock;
    private readonly ViewState _view;
    private readonly Func<TimeSpan> _window;
    private readonly Func<bool> _paused, _mtgActive, _wanted;
    private readonly IAppLog _log;
    private readonly string _bucket;
    private readonly double _subSatelliteLon;

    // Per-feed state: the scheduler never runs a feed twice at once, so no lock.
    private readonly Dictionary<DateTime, string> _lastKeyByHour = [];
    private readonly List<GlmObject> _pending = [];

    public GlmFeed(FeedHttp http, LightningBuffer buffer, LightningSensor sensor, IClock clock, ViewState view,
        Func<TimeSpan> window, Func<bool> paused, Func<bool> mtgActive, Func<bool> wanted, IAppLog log)
    {
        _http = http; _buffer = buffer; _sensor = sensor; _clock = clock; _view = view;
        _window = window; _paused = paused; _mtgActive = mtgActive; _wanted = wanted; _log = log;
        (Name, _bucket, _subSatelliteLon) = sensor switch
        {
            LightningSensor.GoesEast => ("GLM East", "noaa-goes19", -75.2),
            LightningSensor.GoesWest => ("GLM West", "noaa-goes18", -137.2),
            _ => throw new ArgumentOutOfRangeException(nameof(sensor), sensor, "GLM is GoesEast or GoesWest"),
        };
    }

    public string Name { get; }
    public TimeSpan Interval => TimeSpan.FromSeconds(20);

    public async Task<FeedRunResult> RunOnceAsync(CancellationToken ct)
    {
        if (!_wanted()) return FeedRunResult.Skipped;
        if (_paused()) return FeedRunResult.Paused;
        if (!ViewState.Faces(_view.CameraSubPoint, _subSatelliteLon)) return FeedRunResult.Skipped;

        var now = _clock.UtcNow;
        var cutoff = now - _window();
        var firstHour = TruncateToHour(cutoff);

        // Listing state for hours older than the window is forgotten.
        foreach (var h in _lastKeyByHour.Keys.Where(h => h < firstHour).ToList()) _lastKeyByHour.Remove(h);

        for (var hour = firstHour; hour <= TruncateToHour(now); hour = hour.AddHours(1))
        {
            _lastKeyByHour.TryGetValue(hour, out var after);
            var bytes = await _http.GetBytesAsync(GlmListing.ListUri(_bucket, GlmListing.Prefix(hour), after), ct).ConfigureAwait(false);
            var objects = GlmListing.Parse(new MemoryStream(bytes))
                .Where(o => after is null || string.CompareOrdinal(o.Key, after) > 0)
                .OrderBy(o => o.Key, StringComparer.Ordinal).ToList();
            if (objects.Count == 0) continue;
            _pending.AddRange(objects);
            _lastKeyByHour[hour] = objects[^1].Key;
        }

        _pending.RemoveAll(o => o.EndUtc <= cutoff);
        var batch = _pending.OrderByDescending(o => o.EndUtc).Take(MaxFilesPerRun).ToList();
        foreach (var o in batch)
        {
            ct.ThrowIfCancellationRequested();
            _pending.Remove(o);   // a file that fails is logged once and not retried
            try
            {
                var nc = await _http.GetBytesAsync(new Uri($"https://{_bucket}.s3.amazonaws.com/{o.Key}"), ct).ConfigureAwait(false);
                var mtg = _mtgActive();
                var flashes = GlmReader.Read(new MemoryStream(nc), _sensor);
                _buffer.Add(flashes.Where(f => LightningOverlap.Keep(f, mtg)).ToList());
            }
            catch (Exception e) when (e is FormatException or HttpRequestException or TimeoutException or IOException)
            {
                _log.Warn($"{Name}: skipped {o.Key}: {e.Message}", e);
            }
        }

        _buffer.PruneBefore(now - BufferHorizon);
        return FeedRunResult.Ok;
    }

    private static DateTime TruncateToHour(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, 0, 0, DateTimeKind.Utc);
}
