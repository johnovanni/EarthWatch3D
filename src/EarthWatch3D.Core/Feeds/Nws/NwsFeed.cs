using System.Globalization;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Core.Feeds.Nws;

/// <summary>
/// Polls NWS every 2 min. The first run also reads the last 24 h of history (following pagination, at most 20 pages).
/// Zone outlines are requested only for alerts of the types the user chose that are active now.
/// </summary>
public sealed class NwsFeed(FeedHttp http, EventStore store, ZoneCache zones, Func<AlertTypes> filter, IClock clock, IAppLog log) : IEventFeed
{
    private const int MaxHistoryPages = 20;
    private static readonly Uri ActiveUrl = new("https://api.weather.gov/alerts/active");

    private bool _historyDone;

    public string Name => "NWS";
    public TimeSpan Interval => TimeSpan.FromMinutes(2);

    public async Task<FeedRunResult> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var all = new List<WeatherAlert>();
        if (!_historyDone)
        {
            var first = new Uri("https://api.weather.gov/alerts?start="
                + (now - TimeSpan.FromHours(24)).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            var visited = new HashSet<Uri> { first };
            Uri? url = first;
            for (var page = 0; url is not null; page++)
            {
                if (page >= MaxHistoryPages) { log.Warn("NWS: history has more than 20 pages; stopped"); break; }
                var (alerts, next) = await ReadAsync(url, ct).ConfigureAwait(false);
                all.AddRange(alerts);
                if (next is not null && !string.Equals(next.Host, first.Host, StringComparison.OrdinalIgnoreCase))
                { log.Warn($"NWS: history next page is on another host ({next.Host}); stopped"); break; }
                if (next is not null && !visited.Add(next))
                { log.Warn("NWS: history next page was already read; stopped"); break; }
                url = next;
            }
        }
        all.AddRange((await ReadAsync(ActiveUrl, ct).ConfigureAwait(false)).Alerts);
        store.UpsertAlerts(all);
        _historyDone = true;

        // Use the store's resolved alerts, so alerts superseded by an Update or Cancel do not cause downloads.
        var wanted = filter();
        zones.Request(store.Snapshot.Alerts
            .Where(a => (AlertTypeClassifier.Classify(a.EventType) & wanted) != 0 && IsActive(a, now))
            .SelectMany(a => a.ZoneIds));
        return FeedRunResult.Ok;
    }

    private async Task<(IReadOnlyList<WeatherAlert> Alerts, Uri? Next)> ReadAsync(Uri url, CancellationToken ct)
    {
        var bytes = await http.GetBytesAsync(url, ct).ConfigureAwait(false);
        return NwsReader.ReadPage(new MemoryStream(bytes), log);
    }

    private static bool IsActive(WeatherAlert a, DateTime now) =>
        a.OnsetUtc <= now && now < (a.CancelledUtc is { } c && c < a.EndsUtc ? c : a.EndsUtc);
}
