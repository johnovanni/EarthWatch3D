using System.IO;
using System.Text;
using AstroCore.Earth;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Feeds.Usgs;

namespace EarthWatch3D.Tests;

public sealed class UsgsTests
{
    private static string Sample(string f) => Path.Combine(AppContext.BaseDirectory, "Samples", f);
    private const string WeekUrl = "https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/all_week.geojson";
    private const string HourUrl = "https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/all_hour.geojson";

    [Fact]
    public void ReadsWeekSample()
    {
        using var s = File.OpenRead(Sample("usgs_all_week.geojson"));
        var quakes = UsgsReader.Read(s, new RecordingLog()).Quakes;
        Assert.Equal(1806, quakes.Count);
        Assert.Equal(319, quakes.Count(q => q.Magnitude >= 2.5));
    }

    [Fact]
    public void FirstFeatureFields()
    {
        using var s = File.OpenRead(Sample("usgs_all_week.geojson"));
        var q = UsgsReader.Read(s, new RecordingLog()).Quakes[0];
        Assert.Equal("aka2026wjnmsm", q.Id);
        Assert.Equal(0.7, q.Magnitude);
        Assert.Equal(DateTime.UnixEpoch.AddMilliseconds(1794428350078), q.TimeUtc);
        Assert.Equal(DateTimeKind.Utc, q.TimeUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, q.UpdatedUtc.Kind);
        Assert.Equal(new GeoPosition(60.018, -153.06), q.Location);
        Assert.Equal(5, q.DepthKm);
        Assert.Equal("M 0.7 - 63 km ENE of Pedro Bay, Alaska", q.Title);
        Assert.Equal("63 km ENE of Pedro Bay, Alaska", q.Place);
        Assert.Equal("https://earthquake.usgs.gov/earthquakes/eventpage/aka2026wjnmsm", q.Link?.ToString());
    }

    [Fact]
    public void SkipsMalformedRecords()
    {
        const string json = """
        {"type":"FeatureCollection","features":[
         {"type":"Feature","id":"good","properties":{"mag":1.5,"place":"p","time":1794428350078,"updated":1794428385564,"url":"https://e/x","type":"earthquake","title":"M 1.5 - p"},"geometry":{"type":"Point","coordinates":[10,20,3]}},
         {"type":"Feature","id":"nomag","properties":{"mag":null,"place":"p","time":1794428350078,"updated":1794428385564,"type":"earthquake","title":"t"},"geometry":{"type":"Point","coordinates":[10,20,3]}},
         {"type":"Feature","id":"nogeom","properties":{"mag":2.0,"place":"p","time":1794428350078,"updated":1794428385564,"type":"earthquake","title":"t"},"geometry":null}
        ]}
        """;
        var log = new RecordingLog();
        var quakes = UsgsReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)), log).Quakes;
        Assert.Single(quakes);
        Assert.Equal("good", quakes[0].Id);
        Assert.Equal(2, log.Warnings.Count);
    }

    [Fact]
    public void SkipsNonObjectFeaturesAndReadsGenerated()
    {
        const string json = """
        {"metadata":{"generated":1794428504000},"features":[null,"x",
         {"type":"Feature","id":"good","properties":{"mag":1.5,"place":"p","time":1794428350078,"updated":1794428385564,"type":"earthquake"},"geometry":{"type":"Point","coordinates":[10,20,3]}}]}
        """;
        var log = new RecordingLog();
        var (quakes, generated) = UsgsReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)), log);
        Assert.Single(quakes);
        Assert.Equal(2, log.Warnings.Count);
        Assert.Equal(DateTime.UnixEpoch.AddMilliseconds(1794428504000), generated);
        Assert.Equal("M 1.5 - p", quakes[0].Title);
    }

    [Fact]
    public async Task FeedFetchesWeekThenHour()
    {
        var handler = new SampleHttpHandler();
        handler.Map(WeekUrl, Sample("usgs_all_week.geojson"));
        handler.Map(HourUrl, Sample("usgs_all_hour.geojson"));
        var t0 = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
        var clock = new TestClock(t0);
        var store = new EventStore(clock);
        using var http = new FeedHttp(handler);
        var feed = new UsgsFeed(http, store, clock, new RecordingLog());
        Assert.Equal("USGS", feed.Name);
        Assert.Equal(TimeSpan.FromSeconds(60), feed.Interval);

        await feed.RunOnceAsync(CancellationToken.None);
        Assert.Equal([WeekUrl], handler.Requests);
        Assert.Equal(1806, store.Snapshot.Earthquakes.Count);

        clock.UtcNow = t0.AddSeconds(60);
        await feed.RunOnceAsync(CancellationToken.None);
        Assert.Equal([WeekUrl, HourUrl], handler.Requests);

        clock.UtcNow = t0.AddSeconds(60).AddMinutes(31);
        await feed.RunOnceAsync(CancellationToken.None);
        Assert.Equal([WeekUrl, HourUrl, WeekUrl], handler.Requests);
    }

    [Fact]
    public async Task EmptyWeekResponseRemovesNothing()
    {
        var handler = new SampleHttpHandler();
        handler.Map(WeekUrl, Sample("usgs_all_week.geojson"));
        var t0 = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
        var clock = new TestClock(t0);
        var store = new EventStore(clock);
        var log = new RecordingLog();
        using var http = new FeedHttp(handler);
        var feed = new UsgsFeed(http, store, clock, log);
        await feed.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1806, store.Snapshot.Earthquakes.Count);

        // A week feed with no quakes at all (but a generated time) is a bad response, not a quiet week.
        handler.MapText(WeekUrl, """{"type":"FeatureCollection","metadata":{"generated":1794428504000},"features":[]}""");
        clock.UtcNow = t0.AddMinutes(31);
        await feed.RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count(r => r == WeekUrl));
        Assert.Equal(1806, store.Snapshot.Earthquakes.Count);
        Assert.Contains(log.Warnings, w => w.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }
}
