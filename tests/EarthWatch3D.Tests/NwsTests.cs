using System.IO;
using System.Text;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Feeds.Nws;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.Tests;

public sealed class NwsTests
{
    private static string Sample(string f) => Path.Combine(AppContext.BaseDirectory, "Samples", f);
    private static Stream Text(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    private static AppPaths TempPaths()
    {
        var p = new AppPaths(Path.Combine(Path.GetTempPath(), "ew3d-nws-" + Guid.NewGuid().ToString("N")));
        p.EnsureCreated();
        return p;
    }

    private static string Alert(string id, string evt = "Flood Warning", string zone = "https://api.weather.gov/zones/forecast/AAA001") => $$$"""
        {"id":"https://api.weather.gov/alerts/{{{id}}}","type":"Feature","geometry":null,"properties":{"id":"{{{id}}}",
         "affectedZones":["{{{zone}}}"],"sent":"2026-11-11T12:00:00-05:00","effective":"2026-11-11T12:00:00-05:00",
         "onset":null,"expires":"2026-11-11T23:00:00-05:00","ends":null,"messageType":"Alert","severity":"Severe",
         "event":"{{{evt}}}","headline":null,"description":"d"}}
        """;

    [Fact]
    public void ReadsActiveSample()
    {
        using var s = File.OpenRead(Sample("nws_active.json"));
        var (alerts, next) = NwsReader.ReadPage(s, new RecordingLog());
        Assert.Equal(428, alerts.Count);
        Assert.Equal(419, alerts.Count(a => a.Areas.Count == 0 && a.ZoneIds.Count > 0));
        Assert.Equal(138, alerts.Count(a => AlertTypeClassifier.Classify(a.EventType) == AlertTypes.Warnings));
        Assert.Null(next);
        Assert.All(alerts, a => Assert.Equal(DateTimeKind.Utc, a.OnsetUtc.Kind));
    }

    [Fact]
    public void TimesTitleLinkAndCancel()
    {
        var json = $$$"""{"features":[{{{Alert("urn:a1")}}}], "pagination":{"next":"https://api.weather.gov/alerts?cursor=x"}}""";
        var (alerts, next) = NwsReader.ReadPage(Text(json), new RecordingLog());
        var a = Assert.Single(alerts);
        Assert.Equal(new DateTime(2026, 11, 11, 17, 0, 0, DateTimeKind.Utc), a.OnsetUtc);
        Assert.Equal(new DateTime(2026, 11, 12, 4, 0, 0, DateTimeKind.Utc), a.EndsUtc);
        Assert.Equal("Flood Warning", a.Title);
        Assert.Equal("https://api.weather.gov/alerts/urn:a1", a.Link?.ToString());
        Assert.Null(a.CancelledUtc);
        Assert.Equal("https://api.weather.gov/alerts?cursor=x", next?.ToString());

        var cancel = json.Replace("\"Alert\"", "\"Cancel\"");
        Assert.NotNull(NwsReader.ReadPage(Text(cancel), new RecordingLog()).Alerts[0].CancelledUtc);
    }

    [Fact]
    public void ReadsGeometryWhenPresent()
    {
        const string json = """
        {"features":[{"id":"https://api.weather.gov/alerts/g1","geometry":{"type":"Polygon","coordinates":[[[0,0],[10,0],[10,10],[0,10],[0,0]]]},
         "properties":{"id":"g1","affectedZones":["https://z/1"],"sent":"2026-11-11T12:00:00Z","effective":"2026-11-11T12:00:00Z",
         "expires":"2026-11-11T18:00:00Z","messageType":"Alert","severity":"Severe","event":"Flood Warning"}}]}
        """;
        var a = NwsReader.ReadPage(Text(json), new RecordingLog()).Alerts.Single();
        Assert.Single(a.Areas);
        Assert.InRange(a.Location.LatitudeDeg, 3, 7);
        Assert.InRange(a.Location.LongitudeDeg, 3, 7);
    }

    [Fact]
    public void SkipsMalformedRecords()
    {
        var json = $$$"""
        {"features":[null,
         {"id":"x","geometry":null,"properties":{"id":"bad","sent":"2026-11-11T12:00:00Z"}},
         {{{Alert("urn:ok")}}}]}
        """;
        var log = new RecordingLog();
        var (alerts, _) = NwsReader.ReadPage(Text(json), log);
        Assert.Equal("urn:ok", Assert.Single(alerts).Id);
        Assert.Equal(2, log.Warnings.Count);
    }

    private static (SampleHttpHandler Handler, FeedHttp Http, EventStore Store, TestClock Clock) Setup()
    {
        var handler = new SampleHttpHandler();
        var clock = new TestClock(new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc));
        return (handler, new FeedHttp(handler), new EventStore(clock), clock);
    }

    [Fact]
    public async Task FollowsPaginationOnFirstRun()
    {
        var (handler, http, store, clock) = Setup();
        using var _ = http;
        handler.MapText("https://api.weather.gov/alerts?start=2026-11-10T20:00:00Z",
            $$$"""{"features":[{{{Alert("h1")}}}],"pagination":{"next":"https://api.weather.gov/alerts?cursor=p2"}}""");
        handler.MapText("https://api.weather.gov/alerts?cursor=p2", $$$"""{"features":[{{{Alert("h2")}}}]}""");
        handler.Map("https://api.weather.gov/alerts/active", Sample("nws_active.json"));
        var zones = new ZoneCache(http, TempPaths(), clock, new RecordingLog());
        var feed = new NwsFeed(http, store, zones, () => AlertTypes.None, clock, new RecordingLog());
        Assert.Equal("NWS", feed.Name);
        Assert.Equal(TimeSpan.FromMinutes(2), feed.Interval);

        await feed.RunOnceAsync(CancellationToken.None);

        Assert.Equal(430, store.Snapshot.Alerts.Count);
        Assert.Contains("https://api.weather.gov/alerts?start=2026-11-10T20:00:00Z", handler.Requests);
        Assert.Contains("https://api.weather.gov/alerts?cursor=p2", handler.Requests);

        await feed.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, handler.Requests.Count(r => r.Contains("cursor=p2")));
        Assert.Equal(2, handler.Requests.Count(r => r.Contains("/alerts/active")));
    }

    [Fact]
    public async Task FeedRequestsZonesOnlyForFilteredAlerts()
    {
        var (handler, http, store, clock) = Setup();
        using var _ = http;
        var old = Alert("old", "Flood Warning", "https://z/OLD").Replace("2026-11-11T23:00:00-05:00", "2026-11-11T10:00:00-05:00");
        handler.MapText("https://api.weather.gov/alerts/active",
            $$$"""{"features":[{{{Alert("w", "Flood Warning", "https://z/W")}}},{{{Alert("a", "Wind Advisory", "https://z/A")}}},{{{old}}}]}""");
        handler.MapText("https://api.weather.gov/alerts?start=", """{"features":[]}""");
        handler.MapText("https://z/", """{"id":"z","geometry":{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}}""");
        var zones = new ZoneCache(http, TempPaths(), clock, new RecordingLog());
        var feed = new NwsFeed(http, store, zones, () => AlertTypes.Warnings, clock, new RecordingLog());

        await feed.RunOnceAsync(CancellationToken.None);
        await zones.WhenIdleAsync();

        var zoneReqs = handler.Requests.Where(r => r.StartsWith("https://z/", StringComparison.Ordinal)).ToList();
        Assert.Equal(["https://z/W"], zoneReqs);
    }
}
