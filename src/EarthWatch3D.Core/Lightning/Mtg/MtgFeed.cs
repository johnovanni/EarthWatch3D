using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Core.Lightning.Mtg;

/// <summary>
/// Polls the EUMETSAT Data Store every minute for new MTG LI-2-LFL products (one every 10 minutes, published about 40 s after
/// their period ends) and puts their flashes into the lightning buffer. Runs only with a key, while the lightning layer is
/// wanted and the camera faces the satellite (0 degrees longitude).
/// </summary>
public sealed class MtgFeed(EumetsatClient client, LightningBuffer buffer, IClock clock, ViewState view,
    Func<EumetsatCredentials?> credentials, Func<TimeSpan> window, Func<bool> paused, Func<bool> wanted, IAppLog log) : IEventFeed
{
    public const int MaxProductsPerRun = 8;
    private const double SubSatelliteLon = 0.0;
    private static readonly TimeSpan BufferHorizon = TimeSpan.FromMinutes(60);

    // Per-feed state: the scheduler never runs a feed twice at once. KeyRejected is also read from other threads.
    private readonly Dictionary<string, DateTime> _readIds = [];   // product id -> end of its period
    private DateTime? _lastProductEnd;
    private volatile EumetsatCredentials? _rejectedFor;

    public string Name => "MTG";
    public TimeSpan Interval => TimeSpan.FromSeconds(60);

    /// <summary>True after a run in which EUMETSAT refused the key; false after a successful run or once the credentials change.</summary>
    public bool KeyRejected => _rejectedFor is { } r && r.Equals(credentials());

    public async Task<FeedRunResult> RunOnceAsync(CancellationToken ct)
    {
        var creds = credentials();
        if (creds is null) return FeedRunResult.NotSetUp;
        if (!wanted()) return FeedRunResult.Skipped;
        if (paused()) return FeedRunResult.Paused;
        if (!ViewState.Faces(view.CameraSubPoint, SubSatelliteLon)) return FeedRunResult.Skipped;

        var now = clock.UtcNow;
        var cutoff = now - window();
        foreach (var id in _readIds.Where(kv => kv.Value < now - BufferHorizon).Select(kv => kv.Key).ToList()) _readIds.Remove(id);

        try
        {
            var since = _lastProductEnd is { } last && last > cutoff ? last : cutoff;
            var found = await client.SearchAsync(creds, since, ct).ConfigureAwait(false);

            var batch = found
                .Where(p => p.SensingEndUtc > cutoff && !_readIds.ContainsKey(p.Id))
                .OrderByDescending(p => p.SensingEndUtc)
                .Take(MaxProductsPerRun).ToList();
            var limitHit = false;
            foreach (var p in batch)
            {
                ct.ThrowIfCancellationRequested();
                // The daily limit can be reached part-way through a catch-up: stop there.
                if (paused()) { limitHit = true; break; }
                _readIds[p.Id] = p.SensingEndUtc;   // a product that fails is logged once and not retried
                try
                {
                    var zip = await client.DownloadAsync(creds, p, ct).ConfigureAwait(false);
                    var flashes = MtgReader.Read(zip);
                    buffer.Add(flashes.Where(f => LightningOverlap.Keep(f, mtgActive: true)).ToList());
                }
                catch (Exception e) when (e is FormatException or HttpRequestException or TimeoutException or IOException)
                {
                    log.Warn($"{Name}: skipped {p.SensingStartUtc:HH:mm:ss}-{p.SensingEndUtc:HH:mm:ss}: {e.Message}", e);
                }
            }

            // The next search starts after the newest product, but not when the limit stopped this run: the products
            // left over are then still wanted (the ids already read keep them from being read twice).
            if (!limitHit && found.Count > 0) _lastProductEnd = found.Max(p => p.SensingEndUtc);
            buffer.PruneBefore(now - BufferHorizon);
            _rejectedFor = null;
            return limitHit ? FeedRunResult.Paused : FeedRunResult.Ok;
        }
        catch (EumetsatKeyRejectedException)
        {
            _rejectedFor = creds;
            throw;
        }
    }
}
