using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using AstroCore.Earth;
using EarthWatch3D.Core;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Feeds.Nhc;
using GlobeCore.Feeds;

namespace EarthWatch3D.Tests;

public sealed class NhcBestTrackTests
{
    private const string BestUrl = "https://www.nhc.noaa.gov/gis/best_track/cp042026_best_track.kmz";
    private static string Sample(string f) => Path.Combine(AppContext.BaseDirectory, "Samples", f);
    private static DateTime U(int d, int h) => new(2026, 11, d, h, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ReadsSampleBestTrack()
    {
        using var s = File.OpenRead(Sample("nhc_best_track.kmz"));
        var fixes = BestTrackReader.Read(s, new RecordingLog());
        Assert.Equal(41, fixes.Count);
        Assert.Equal(U(2, 12), fixes[0].TimeUtc);
        Assert.Equal(new GeoPosition(7.7, -129.8), fixes[0].Position);
        Assert.Equal(20, fixes[0].WindKt);
        Assert.Equal(U(12, 12), fixes[^1].TimeUtc);
        Assert.Equal(9.8, fixes[^1].Position.LatitudeDeg, 6);
        Assert.Equal(-173.2, fixes[^1].Position.LongitudeDeg, 6);
        Assert.Equal(30, fixes[^1].WindKt);
        for (var i = 1; i < fixes.Count; i++) Assert.True(fixes[i].TimeUtc > fixes[i - 1].TimeUtc);
    }

    private static (NhcFeed Feed, EventStore Store, RecordingLog Log) MakeFeed(HttpMessageHandler handler)
    {
        var store = new EventStore(new TestClock(new DateTime(2026, 11, 11, 15, 30, 0, DateTimeKind.Utc)));
        var log = new RecordingLog();
        var http = new FeedHttp(AppInfo.UserAgent, handler);
        return (new NhcFeed(http, store, log), store, log);
    }

    private static SampleHttpHandler Offline()
    {
        var h = new SampleHttpHandler();
        h.Map("https://www.nhc.noaa.gov/CurrentStorms.json", Sample("nhc_CurrentStorms.json"));
        h.Map("https://www.nhc.noaa.gov/storm_graphics/api/CP042026_020adv_TRACK.kmz", Sample("nhc_track.kmz"));
        h.Map("https://www.nhc.noaa.gov/storm_graphics/api/CP042026_020adv_CONE.kmz", Sample("nhc_cone.kmz"));
        h.Map(BestUrl, Sample("nhc_best_track.kmz"));
        return h;
    }

    [Fact]
    public async Task AdvisoryHistoryStopsAtAdvisoryTime()
    {
        var handler = Offline();
        var (feed, store, _) = MakeFeed(handler);
        await feed.RunOnceAsync(CancellationToken.None);
        await feed.RunOnceAsync(CancellationToken.None);
        var c = Assert.Single(store.Snapshot.Cyclones);
        Assert.Equal(U(11, 15), c.TimeUtc);
        Assert.True(c.History[^1].TimeUtc <= U(11, 15));
        Assert.Contains(c.History, f => f.TimeUtc == U(11, 12));
        Assert.Equal(1, handler.Requests.Count(r => r == BestUrl));   // same issuance: not downloaded again
    }

    private sealed class FailBestTrack(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public int Asked;

        private HttpResponseMessage Count(HttpResponseMessage r) { Asked++; return r; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            request.RequestUri!.ToString() == BestUrl
                ? Task.FromResult(Count(new HttpResponseMessage(HttpStatusCode.InternalServerError) { RequestMessage = request }))
                : base.SendAsync(request, ct);
    }

    [Fact]
    public async Task FailedBestTrackKeepsAdvisory()
    {
        var failing = new FailBestTrack(Offline());
        var (feed, store, log) = MakeFeed(failing);
        await feed.RunOnceAsync(CancellationToken.None);
        var c = Assert.Single(store.Snapshot.Cyclones);
        Assert.Empty(c.History);
        Assert.NotEmpty(c.ForecastTrack);
        Assert.Single(log.Warnings);
        var asked = failing.Asked;
        await feed.RunOnceAsync(CancellationToken.None);
        Assert.True(failing.Asked > asked);   // the next run asks again
    }

    [Fact]
    public void BestTrackUrlFromZipFile()
    {
        const string json = """
            {"activeStorms":[{"id":"cp042026","name":"Pena","classification":"TS","latitudeNumeric":9.9,"longitudeNumeric":-167.7,
              "lastUpdate":"2026-11-11T15:00:00.000Z",
              "bestTrackGIS":{"issuance":"2026-11-11T15:00:00.000Z","zipFile":"https://www.nhc.noaa.gov/gis/best_track/cp042026_best_track.zip"}}]}
            """;
        var s = Assert.Single(NhcReader.ReadCurrentStorms(new MemoryStream(Encoding.UTF8.GetBytes(json)), new RecordingLog()));
        Assert.Equal(BestUrl, s.BestTrackKmz?.ToString());
        Assert.Equal("2026-11-11T15:00:00.000Z", s.BestTrackIssuance);
    }
}
