using System.Globalization;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Core.Feeds.Gdacs;

/// <summary>Polls GDACS every 15 min for tropical cyclones of the last 2 days; geometry is fetched once per new episode.</summary>
public sealed class GdacsFeed(FeedHttp http, EventStore store, IClock clock, IAppLog log) : IEventFeed
{
    private readonly HashSet<(int EventId, int EpisodeId, DateTime ModifiedUtc)> _seen = [];

    public string Name => "GDACS";
    public TimeSpan Interval => TimeSpan.FromMinutes(15);

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        // The ';' separators are sent literally; GDACS accepts them.
        var url = new Uri("https://www.gdacs.org/gdacsapi/api/events/geteventlist/SEARCH?eventlist=TC&alertlevel=green;orange;red"
            + "&fromdate=" + (now - TimeSpan.FromDays(2)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            + "&todate=" + now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var bytes = await http.GetBytesAsync(url, ct).ConfigureAwait(false);
        var events = GdacsReader.ReadEventList(new MemoryStream(bytes), log);

        var advisories = new List<CycloneAdvisory>();
        foreach (var e in events)
        {
            var key = (e.EventId, e.EpisodeId, e.ModifiedUtc);
            if (_seen.Contains(key)) continue;
            byte[] geo;
            try { geo = await http.GetBytesAsync(e.GeometryUrl, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
            {
                // One bad episode must not hide the others; it is retried on the next run.
                log.Warn($"GDACS: geometry of event {e.EventId} episode {e.EpisodeId} failed; will retry", ex);
                continue;
            }
            advisories.Add(GdacsReader.ReadGeometry(new MemoryStream(geo), e, log));
            _seen.Add(key);
        }
        if (advisories.Count > 0) store.AddCycloneAdvisories(advisories);
    }
}
