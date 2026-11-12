using System.IO;
using System.Text;
using AstroCore.Earth;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Feeds.Gdacs;

namespace EarthWatch3D.Tests;

public sealed class GdacsTests
{
    private static string Sample(string f) => Path.Combine(AppContext.BaseDirectory, "Samples", f);

    private static IReadOnlyList<GdacsEvent> ReadList(RecordingLog? log = null)
    {
        using var s = File.OpenRead(Sample("gdacs_events_tc.json"));
        return GdacsReader.ReadEventList(s, log ?? new RecordingLog());
    }

    [Fact]
    public void ReadsEventList()
    {
        var events = ReadList();
        Assert.Equal(4, events.Count);
        var pena = events.Single(e => e.EventId == 1001348);
        Assert.Equal(20, pena.EpisodeId);
        Assert.Equal("PENA-26", pena.EventName);
        Assert.True(pena.IsCurrent);
        Assert.Equal("NOAA", pena.Source);
        Assert.Equal(new GeoPosition(9.9, -167.7), pena.Position);
        Assert.Equal(92.592, pena.WindKmh);
        Assert.Equal("https://www.gdacs.org/gdacsapi/api/polygons/getgeometry?eventtype=TC&eventid=1001348&episodeid=20", pena.GeometryUrl.ToString());
        Assert.Equal("https://www.gdacs.org/report.aspx?eventid=1001348&episodeid=20&eventtype=TC", pena.ReportUrl?.ToString());
        Assert.Equal(new DateTime(2026, 11, 11, 14, 51, 55, DateTimeKind.Utc), pena.ModifiedUtc);
        var cem = events.Single(e => e.EventId == 1001347);
        Assert.Equal((22, "CEMPAKA-26", "JTWC"), (cem.EpisodeId, cem.EventName, cem.Source));
        var rsmc = events.Single(e => e.EventId == 1001349);
        Assert.Equal((6, "02-20262027-27", "RSMC"), (rsmc.EpisodeId, rsmc.EventName, rsmc.Source));
        var inFa = events.Single(e => e.EventId == 1001343);
        Assert.Equal((52, "IN-FA-26"), (inFa.EpisodeId, inFa.EventName));
        Assert.False(inFa.IsCurrent);
        Assert.All(events, e => Assert.Equal(DateTimeKind.Utc, e.ModifiedUtc.Kind));
    }

    [Fact]
    public void ReadsGeometryRoles()
    {
        var pena = ReadList().Single(e => e.EventId == 1001348);
        using var s = File.OpenRead(Sample("gdacs_geometry.json"));
        var a = GdacsReader.ReadGeometry(s, pena, new RecordingLog());
        Assert.Equal(EventSource.Gdacs, a.Source);
        Assert.Equal("gdacs:1001348", a.Id);
        Assert.Equal(pena.ModifiedUtc, a.TimeUtc);
        Assert.Equal("PENA-26", a.Name);
        Assert.Equal("Tropical cyclone PENA-26", a.Title);
        Assert.Equal(pena.ReportUrl, a.Link);
        Assert.Equal(92.592 / 1.852, a.WindKt!.Value, 6);
        Assert.Equal(new GeoPosition(9.9, -167.7), a.Location);
        Assert.Equal(19, a.PastTrack.Count);
        Assert.Equal(6, a.ForecastTrack.Count);
        Assert.Equal([60.0, 90.0], a.WindAreas.Select(w => w.ThresholdKmh).Order().ToArray());
        Assert.NotEmpty(a.Cone);
        Assert.All(a.Cone.SelectMany(p => p.Rings).SelectMany(r => r),
            p => Assert.InRange(p.LongitudeDeg, -180.0, 180.0));
    }

    [Fact]
    public void SkipsMalformedRecords()
    {
        const string json = """
        {"features":[null,
         {"type":"Feature","geometry":{"type":"Point","coordinates":[1,2]},"properties":{"episodeid":1,"eventname":"X"}},
         {"type":"Feature","geometry":{"type":"Point","coordinates":[10,20]},"properties":{"eventid":5,"episodeid":2,"eventname":"OK-26","iscurrent":"true","source":"NOAA","datemodified":"2026-11-11T10:00:00","severitydata":{"severity":100},"url":{"geometry":"https://g/x","report":"https://g/r"}}}
        ]}
        """;
        var log = new RecordingLog();
        var events = GdacsReader.ReadEventList(new MemoryStream(Encoding.UTF8.GetBytes(json)), log);
        Assert.Single(events);
        Assert.Equal("OK-26", events[0].EventName);
        Assert.Equal(2, log.Warnings.Count);
    }

    [Fact]
    public async Task FeedFetchesGeometryOncePerEpisode()
    {
        var handler = new SampleHttpHandler();
        handler.Map("https://www.gdacs.org/gdacsapi/api/events/geteventlist/SEARCH", Sample("gdacs_events_tc.json"));
        handler.Map("https://www.gdacs.org/gdacsapi/api/polygons/getgeometry?", Sample("gdacs_geometry.json"));
        var clock = new TestClock(new DateTime(2026, 11, 11, 15, 0, 0, DateTimeKind.Utc));
        var store = new EventStore(clock);
        using var http = new FeedHttp(handler);
        var feed = new GdacsFeed(http, store, clock, new RecordingLog());
        Assert.Equal("GDACS", feed.Name);
        Assert.Equal(TimeSpan.FromMinutes(15), feed.Interval);

        await feed.RunOnceAsync(CancellationToken.None);
        await feed.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count(r => r.Contains("geteventlist")));
        Assert.Equal(1, handler.Requests.Count(r => r.Contains("eventid=1001348&episodeid=20")));
        Assert.Contains(handler.Requests, r => r == "https://www.gdacs.org/gdacsapi/api/events/geteventlist/SEARCH?eventlist=TC&alertlevel=green;orange;red&fromdate=2026-11-09&todate=2026-11-11");
        Assert.Contains(store.Snapshot.Cyclones, c => c.Id == "gdacs:1001348");
    }

    private static GdacsEvent Ev(int id) => new(id, 1, "T-" + id, true, "NOAA", new GeoPosition(10, 20), 100,
        new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc), new Uri("https://g/" + id), null);

    [Fact]
    public void ShortCoordinateFeatureIsSkipped()
    {
        const string json = """
        {"features":[
         {"type":"Feature","geometry":{"type":"LineString","coordinates":[[1,2],[3]]},"properties":{"Class":"Line_Line_0","forecast":false}},
         {"type":"Feature","geometry":{"type":"LineString","coordinates":[[1,2],[3,4]]},"properties":{"Class":"Line_Line_1","forecast":false}},
         {"type":"Feature","geometry":{"type":"MultiPolygon","coordinates":[[[[0,0],[1,0],[1,1],[0,0]]],[[[5,5],[6,5],[6,6],[5,5]]]]},"properties":{"Class":"Poly_Green","polygonlabel":"60 km/h"}}
        ]}
        """;
        var log = new RecordingLog();
        var a = GdacsReader.ReadGeometry(new MemoryStream(Encoding.UTF8.GetBytes(json)), Ev(1), log);
        Assert.Single(a.PastTrack);
        Assert.Single(log.Warnings);
        Assert.Equal(2, a.WindAreas.Count);
        Assert.All(a.WindAreas, w => Assert.Equal(60, w.ThresholdKmh));
    }

    [Fact]
    public async Task MalformedGeometryDocumentIsSkippedAndOthersLoad()
    {
        const string list = """
        {"features":[
         {"type":"Feature","geometry":{"type":"Point","coordinates":[10,20]},"properties":{"eventid":1,"episodeid":1,"eventname":"BAD","iscurrent":"true","source":"NOAA","datemodified":"2026-11-11T10:00:00","url":{"geometry":"https://g.test/bad"}}},
         {"type":"Feature","geometry":{"type":"Point","coordinates":[10,20]},"properties":{"eventid":2,"episodeid":1,"eventname":"GOOD","iscurrent":"true","source":"NOAA","datemodified":"2026-11-11T10:00:00","url":{"geometry":"https://g.test/good"}}}
        ]}
        """;
        var handler = new SampleHttpHandler();
        handler.MapText("https://www.gdacs.org/gdacsapi/api/events/geteventlist/SEARCH", list);
        handler.MapText("https://g.test/bad", "not json");
        handler.MapText("https://g.test/good", "{\"features\":[]}");
        var clock = new TestClock(new DateTime(2026, 11, 11, 15, 0, 0, DateTimeKind.Utc));
        var store = new EventStore(clock);
        var log = new RecordingLog();
        using var http = new FeedHttp(handler);
        await new GdacsFeed(http, store, clock, log).RunOnceAsync(CancellationToken.None);
        Assert.Single(store.Snapshot.Cyclones);
        Assert.Equal("gdacs:2", store.Snapshot.Cyclones[0].Id);
        Assert.Single(log.Warnings);
    }
}
