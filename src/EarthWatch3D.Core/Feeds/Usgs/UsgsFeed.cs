using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Core.Feeds.Usgs;

/// <summary>Polls USGS every 60 s: the hour feed, and the week feed on the first run and every 30 min or more.</summary>
public sealed class UsgsFeed(FeedHttp http, EventStore store, IClock clock, IAppLog log) : IEventFeed
{
    private static readonly Uri HourUrl = new("https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/all_hour.geojson");
    private static readonly Uri WeekUrl = new("https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/all_week.geojson");
    private static readonly TimeSpan WeekEvery = TimeSpan.FromMinutes(30);

    private DateTime? _lastWeekUtc;

    public string Name => "USGS";
    public TimeSpan Interval => TimeSpan.FromSeconds(60);

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var week = _lastWeekUtc is null || now - _lastWeekUtc.Value >= WeekEvery;
        var bytes = await http.GetBytesAsync(week ? WeekUrl : HourUrl, ct).ConfigureAwait(false);
        var (quakes, generated) = UsgsReader.Read(new MemoryStream(bytes), log);
        if (week)
        {
            // Only quakes inside the feed's own time span may be removed: the CDN copy can be up to a minute old.
            var end = generated ?? (quakes.Count > 0 ? quakes.Max(q => q.TimeUtc) : DateTime.MinValue);
            store.ReplaceEarthquakeWeek([.. quakes], now - TimeSpan.FromDays(7), end);
            _lastWeekUtc = now;
        }
        else
        {
            store.UpsertEarthquakes(quakes);
        }
    }
}
