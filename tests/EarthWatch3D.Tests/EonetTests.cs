using System.IO;
using System.Text;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Feeds.Eonet;

namespace EarthWatch3D.Tests;

public sealed class EonetTests
{
    private static string Sample(string f) => Path.Combine(AppContext.BaseDirectory, "Samples", f);

    [Fact]
    public void ReadsSample()
    {
        using var s = File.OpenRead(Sample("eonet_storms.json"));
        var storms = EonetReader.Read(s, new RecordingLog());
        Assert.Equal(4, storms.Count);
        var a = storms.Single(x => x.Id == "EONET_26159");
        Assert.Equal("Tropical Cyclone 03S", a.Title);
        Assert.Equal(5, a.Fixes.Count);
        Assert.Equal(35, a.Fixes[0].WindKt);
        Assert.Equal(a.Fixes[0].TimeUtc, a.TimeUtc);
        Assert.Equal(a.Fixes[^1].Position, a.Location);
        Assert.Equal(DateTimeKind.Utc, a.TimeUtc.Kind);
        Assert.Null(a.ClosedUtc);
        Assert.Equal("https://eonet.gsfc.nasa.gov/api/v3/events/EONET_26159", a.Link?.ToString());
        Assert.Equal(19, storms.Single(x => x.Id == "EONET_26157").Fixes.Count);
        Assert.Equal(20, storms.Single(x => x.Id == "EONET_26158").Fixes.Count);
        var c = storms.Single(x => x.Id == "EONET_25728");
        Assert.Equal(51, c.Fixes.Count);
        Assert.Equal(new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc), c.ClosedUtc);
        Assert.Equal(DateTimeKind.Utc, c.ClosedUtc!.Value.Kind);
        foreach (var st in storms)
            for (var i = 1; i < st.Fixes.Count; i++)
                Assert.True(st.Fixes[i].TimeUtc > st.Fixes[i - 1].TimeUtc);
    }

    [Fact]
    public void SortsFixesByDate()
    {
        const string json = """
        {"events":[{"id":"E1","title":"T","link":"https://e/1","closed":null,"geometry":[
          {"magnitudeValue":50,"magnitudeUnit":"kts","date":"2026-11-08T00:00:00Z","type":"Point","coordinates":[2,3]},
          {"magnitudeValue":40,"magnitudeUnit":"kts","date":"2026-11-07T00:00:00Z","type":"Point","coordinates":[1,2]}]}]}
        """;
        var s = EonetReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)), new RecordingLog())[0];
        Assert.Equal(40, s.Fixes[0].WindKt);
        Assert.Equal(new DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc), s.TimeUtc);
        Assert.Equal(3, s.Location.LatitudeDeg);
    }

    [Fact]
    public void SkipsMalformedRecords()
    {
        const string json = """
        {"events":[
         {"id":"A","title":"keep","link":"https://e/a","closed":null,"geometry":[
           {"magnitudeValue":40,"magnitudeUnit":"kts","date":"2026-11-07T00:00:00Z","type":"Point"},
           {"magnitudeValue":45,"magnitudeUnit":"kts","date":"2026-11-07T06:00:00Z","type":"Point","coordinates":[1,2]}]},
         {"id":"B","title":"empty","link":"https://e/b","closed":null,"geometry":[]}
        ]}
        """;
        var log = new RecordingLog();
        var storms = EonetReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)), log);
        Assert.Single(storms);
        Assert.Equal("A", storms[0].Id);
        Assert.Single(storms[0].Fixes);
        Assert.NotEmpty(log.Warnings);
    }

    [Fact]
    public void SkipsNonObjectEventsAndWarnsOnBadClosed()
    {
        const string json = """
        {"events":[null,"x",
         {"id":"A","title":"t","link":"https://e/a","closed":"not a date","geometry":[
           {"magnitudeValue":40,"magnitudeUnit":"kts","date":"2026-11-07T00:00:00Z","type":"Point","coordinates":[1,2]}]}]}
        """;
        var log = new RecordingLog();
        var storms = EonetReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)), log);
        Assert.Single(storms);
        Assert.Null(storms[0].ClosedUtc);
        Assert.Equal(3, log.Warnings.Count);
    }

    [Fact]
    public async Task FeedWritesToStore()
    {
        var handler = new SampleHttpHandler();
        handler.Map("https://eonet.gsfc.nasa.gov/api/v3/events?category=severeStorms&status=all&days=7", Sample("eonet_storms.json"));
        var store = new EventStore(new TestClock(new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc)));
        using var http = new FeedHttp(handler);
        var feed = new EonetFeed(http, store, new RecordingLog());
        Assert.Equal("EONET", feed.Name);
        Assert.Equal(TimeSpan.FromMinutes(30), feed.Interval);
        await feed.RunOnceAsync(CancellationToken.None);
        Assert.Equal(4, store.Snapshot.Eonet.Count);
    }
}
