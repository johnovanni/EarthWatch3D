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
        Assert.Equal("https://alerts.weather.gov/", a.Link?.ToString());   // never the raw API JSON
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

    private static string Ref(string id, string sent, string messageType, string refId, string evt = "Flood Warning") => $$$"""
        {"id":"https://api.weather.gov/alerts/{{{id}}}","geometry":null,"properties":{"id":"{{{id}}}","affectedZones":["https://z/{{{id}}}"],
         "sent":"{{{sent}}}","effective":"{{{sent}}}","expires":"2026-11-11T23:00:00-05:00","ends":null,"messageType":"{{{messageType}}}",
         "severity":"Severe","event":"{{{evt}}}","references":[{"identifier":"{{{refId}}}"}]}}
        """;

    private static EventStore NewStore() => new(new TestClock(new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc)));
    private static WeatherAlert One(string json) => NwsReader.ReadPage(Text("{\"features\":[" + json + "]}"), new RecordingLog()).Alerts[0];

    [Fact]
    public void UpdateEndsTheOriginalAtItsSentTime()
    {
        var store = NewStore();
        var orig = One(Alert("o1"));
        var upd = One(Ref("u1", "2026-11-11T13:00:00-05:00", "Update", "o1"));
        Assert.Equal(["o1"], upd.SupersedesIds);
        store.UpsertAlerts([orig, upd]);
        var o = store.Snapshot.Alerts.Single(a => a.Id == "o1");
        Assert.Equal(upd.TimeUtc, o.CancelledUtc);
        Assert.Null(store.Snapshot.Alerts.Single(a => a.Id == "u1").CancelledUtc);
    }

    [Fact]
    public void CancelEndsTheOriginal()
    {
        var store = NewStore();
        store.UpsertAlerts([One(Alert("o1"))]);
        var cancel = One(Ref("c1", "2026-11-11T14:00:00-05:00", "Cancel", "o1"));
        store.UpsertAlerts([cancel]);
        Assert.Equal(cancel.TimeUtc, store.Snapshot.Alerts.Single(a => a.Id == "o1").CancelledUtc);
        Assert.Equal(cancel.TimeUtc, store.Snapshot.Alerts.Single(a => a.Id == "c1").CancelledUtc);
    }

    [Fact]
    public void EndReasonSaysWhetherAnUpdateOrACancelEndedTheAlert()
    {
        var store = NewStore();
        store.UpsertAlerts([One(Alert("o1")), One(Alert("o2"))]);
        Assert.Null(store.Snapshot.Alerts.Single(a => a.Id == "o1").EndReason);
        var cancel = One(Ref("c1", "2026-11-11T14:00:00-05:00", "Cancel", "o1"));
        Assert.Equal(AlertEndReason.Cancelled, cancel.EndReason);   // a Cancel message ends itself
        store.UpsertAlerts([cancel, One(Ref("u2", "2026-11-11T13:00:00-05:00", "Update", "o2"))]);
        Assert.Equal(AlertEndReason.Cancelled, store.Snapshot.Alerts.Single(a => a.Id == "o1").EndReason);
        Assert.Equal(AlertEndReason.Superseded, store.Snapshot.Alerts.Single(a => a.Id == "o2").EndReason);
        Assert.Null(store.Snapshot.Alerts.Single(a => a.Id == "u2").EndReason);
    }

    [Fact]
    public void ReferenceArrivingBeforeOriginalIsApplied()
    {
        var store = NewStore();
        var upd = One(Ref("u1", "2026-11-11T13:00:00-05:00", "Update", "o1"));
        store.UpsertAlerts([upd]);
        store.UpsertAlerts([One(Alert("o1"))]);
        Assert.Equal(upd.TimeUtc, store.Snapshot.Alerts.Single(a => a.Id == "o1").CancelledUtc);
    }

    [Fact]
    public void ReUpsertWithReferencesIsNoChange()
    {
        var store = NewStore();
        var orig = One(Alert("o1"));
        var upd = One(Ref("u1", "2026-11-11T13:00:00-05:00", "Update", "o1"));
        store.UpsertAlerts([orig, upd]);
        var changes = 0;
        store.Changed += (_, _) => changes++;
        store.UpsertAlerts([orig, upd]);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task FeedSkipsZonesOfSupersededAlerts()
    {
        var (handler, http, store, clock) = Setup();
        using var _ = http;
        handler.MapText("https://api.weather.gov/alerts/active",
            "{\"features\":[" + Alert("o1", "Flood Warning", "https://z/OLD") + "," + Ref("u1", "2026-11-11T13:00:00-05:00", "Update", "o1") + "]}");
        handler.MapText("https://api.weather.gov/alerts?start=", "{\"features\":[]}");
        handler.MapText("https://z/", "{\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]]}}");
        var zones = new ZoneCache(http, TempPaths(), clock, new RecordingLog());
        var feed = new NwsFeed(http, store, zones, () => AlertTypes.Warnings, clock, new RecordingLog());
        await feed.RunOnceAsync(CancellationToken.None);
        await zones.WhenIdleAsync();
        Assert.Equal(["https://z/u1"], handler.Requests.Where(r => r.StartsWith("https://z/", StringComparison.Ordinal)).ToList());
    }

    [Fact]
    public async Task HistoryStopsOnLoopOrForeignHost()
    {
        var (handler, http, store, clock) = Setup();
        using var _ = http;
        handler.MapText("https://api.weather.gov/alerts?start=", "{\"features\":[],\"pagination\":{\"next\":\"https://api.weather.gov/alerts?cursor=a\"}}");
        handler.MapText("https://api.weather.gov/alerts?cursor=a", "{\"features\":[],\"pagination\":{\"next\":\"https://api.weather.gov/alerts?cursor=a\"}}");
        handler.MapText("https://api.weather.gov/alerts/active", "{\"features\":[]}");
        var log = new RecordingLog();
        var feed = new NwsFeed(http, store, new ZoneCache(http, TempPaths(), clock, log), () => AlertTypes.None, clock, log);
        await feed.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, handler.Requests.Count(r => r.Contains("cursor=a")));
        Assert.Single(log.Warnings);
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

    [Theory]
    [InlineData("https://api.weather.gov/alerts/urn:oid:1", "https://alerts.weather.gov/")]   // the API's JSON: not a page
    [InlineData("urn:oid:2.49.0.1.840.0.1", "https://alerts.weather.gov/")]
    [InlineData("http://www.weather.gov/x", "https://alerts.weather.gov/")]                     // not https
    [InlineData(null, "https://alerts.weather.gov/")]
    [InlineData("https://www.weather.gov/safety/flood", "https://www.weather.gov/safety/flood")]
    public void LinkIsAWebPage(string? featureId, string expected) =>
        Assert.Equal(expected, NwsReader.WebLink(featureId).ToString());
}
