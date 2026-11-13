using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Core.Lightning.Mtg;

/// <summary>
/// Polls the EUMETSAT Data Store every minute for new MTG LI-2-LFL products (one every 10 minutes, published about 40 s after
/// their period ends) and puts their flashes into the lightning buffer. Runs only with a key, while the lightning layer is
/// wanted and the camera faces MTG (0 degrees longitude) or GOES-East (whose flashes east of 37.6 W belong to MTG).
/// </summary>
public sealed class MtgFeed(EumetsatClient client, LightningBuffer buffer, IClock clock, ViewState view,
    Func<EumetsatCredentials?> credentials, Func<TimeSpan> window, Func<bool> paused, Func<bool> wanted, IAppLog log) : IEventFeed
{
    public const int MaxProductsPerRun = 8;
    private const double SubSatelliteLon = 0.0, GoesEastLon = -75.2;
    private static readonly TimeSpan BufferHorizon = TimeSpan.FromMinutes(60);
    /// <summary>Each search starts this long before the newest product seen, so a product published late (out of order) is still found.</summary>
    private static readonly TimeSpan SearchOverlap = TimeSpan.FromMinutes(20);
    /// <summary>MTG flash times can fall a couple of seconds before their product's period starts.</summary>
    private static readonly TimeSpan PeriodSlack = TimeSpan.FromSeconds(10);

    // Per-feed state: the scheduler never runs a feed twice at once. KeyRejected, Active and State are also read from other threads.
    private readonly Dictionary<string, DateTime> _readIds = [];   // product id -> end of its period
    private readonly Dictionary<string, DateTime> _retried = [];   // products already given their one retry
    private DateTime? _lastProductEnd;
    private volatile EumetsatCredentials? _rejectedFor, _succeededFor;
    private int _state;

    public string Name => "MTG";
    public TimeSpan Interval => TimeSpan.FromSeconds(60);

    /// <summary>True after a run in which EUMETSAT refused the key; false after a successful run or once the credentials change.</summary>
    public bool KeyRejected => _rejectedFor is { } r && r.Equals(credentials());

    /// <summary>
    /// True once a run has worked with the current credentials and the key has not been refused since: GOES-East then
    /// leaves the flashes east of 37.6 W to MTG. Until then (a new key, a restart) GOES-East keeps covering them.
    /// </summary>
    public bool Active => !KeyRejected && _succeededFor is { } s && s.Equals(credentials());

    /// <summary>What the feed is doing, for the legend.</summary>
    public MtgFeedState State => KeyRejected ? MtgFeedState.KeyRejected : (MtgFeedState)Volatile.Read(ref _state);

    private FeedRunResult Done(FeedRunResult r, MtgFeedState state)
    {
        Volatile.Write(ref _state, (int)state);
        return r;
    }

    public async Task<FeedRunResult> RunOnceAsync(CancellationToken ct)
    {
        var creds = credentials();
        if (creds is null) return FeedRunResult.NotSetUp;
        if (!wanted()) return FeedRunResult.Skipped;
        if (paused()) return Done(FeedRunResult.Paused, MtgFeedState.Paused);
        // Polled while MTG or GOES-East faces the camera: GOES-East hands the flashes east of 37.6 W to MTG, so they have to
        // be fetched when the camera looks from the Americas too. The product is small (about 1.9 MB/h).
        var cam = view.CameraSubPoint;
        if (!ViewState.Faces(cam, SubSatelliteLon) && !ViewState.Faces(cam, GoesEastLon))
            return Done(FeedRunResult.Skipped, MtgFeedState.NotInView);

        var now = clock.UtcNow;
        var cutoff = now - window();
        foreach (var id in _readIds.Where(kv => kv.Value < now - BufferHorizon).Select(kv => kv.Key).ToList()) _readIds.Remove(id);
        foreach (var id in _retried.Where(kv => kv.Value < now - BufferHorizon).Select(kv => kv.Key).ToList()) _retried.Remove(id);

        try
        {
            var since = _lastProductEnd is { } last && last - SearchOverlap > cutoff ? last - SearchOverlap : cutoff;
            var found = await client.SearchAsync(creds, since, ct).ConfigureAwait(false);

            var candidates = found
                .Where(p => p.SensingEndUtc > cutoff && !_readIds.ContainsKey(p.Id))
                .OrderByDescending(p => p.SensingEndUtc).ToList();
            var batch = candidates.Take(MaxProductsPerRun).ToList();
            bool limitHit = false, retryPending = false;
            int failed = 0;
            Exception? lastFailure = null;
            foreach (var p in batch)
            {
                ct.ThrowIfCancellationRequested();
                // The daily limit can be reached part-way through a catch-up: stop there.
                if (paused()) { limitHit = true; break; }
                _readIds[p.Id] = p.SensingEndUtc;
                try
                {
                    var zip = await client.DownloadAsync(creds, p, ct).ConfigureAwait(false);
                    var flashes = MtgReader.Read(zip).Where(f => LightningOverlap.Keep(f, mtgActive: true)).ToList();
                    buffer.Add(flashes);
                    // GOES-East may have kept flashes east of 37.6 W for this period while MTG was not active: MTG owns them now.
                    var from = p.SensingStartUtc - PeriodSlack;
                    buffer.RemoveWhere(f => f.Sensor == LightningSensor.GoesEast && f.Lon >= LightningOverlap.EastMtgBoundary
                                            && f.TimeUtc >= from && f.TimeUtc < p.SensingEndUtc);
                }
                catch (Exception e) when (e is FormatException or HttpRequestException or TimeoutException or IOException)
                {
                    failed++;
                    lastFailure = e;
                    // A network problem gets one more try on a later run; a product that cannot be read is skipped for good.
                    if (e is not FormatException && _retried.TryAdd(p.Id, p.SensingEndUtc))
                    {
                        _readIds.Remove(p.Id);
                        retryPending = true;
                        continue;
                    }
                    log.Warn($"{Name}: skipped {p.SensingStartUtc:HH:mm:ss}-{p.SensingEndUtc:HH:mm:ss}: {e.Message}", e);
                }
            }

            // The next search starts shortly before the newest product, but not while products are left over (the limit
            // stopped the run, more than the cap was found, or one is waiting for its retry): they are still wanted, and the
            // ids already read keep them from being read twice.
            if (!limitHit && !retryPending && candidates.Count <= batch.Count && found.Count > 0)
                _lastProductEnd = found.Max(p => p.SensingEndUtc);
            buffer.PruneBefore(now - BufferHorizon);
            if (limitHit) return Done(FeedRunResult.Paused, MtgFeedState.Paused);
            // A network drop between the search and the downloads must not leave a green dot.
            if (batch.Count > 0 && failed == batch.Count)
                throw new HttpRequestException($"every product download failed ({lastFailure!.Message})", lastFailure);
            _rejectedFor = null;
            _succeededFor = creds;
            return Done(FeedRunResult.Ok, MtgFeedState.Running);
        }
        catch (EumetsatKeyRejectedException)
        {
            _rejectedFor = creds;
            throw;
        }
    }
}
