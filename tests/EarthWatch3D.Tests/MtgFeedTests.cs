using System.IO;
using System.Net;
using System.Net.Http;
using AstroCore.Earth;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Lightning;
using EarthWatch3D.Core.Lightning.Glm;
using EarthWatch3D.Core.Lightning.Mtg;

namespace EarthWatch3D.Tests;

public sealed class MtgFeedTests
{
    internal const string TokenUrl = "https://api.eumetsat.int/token";
    internal const string SearchPrefix = "https://api.eumetsat.int/data/search-products/1.0.0/os";
    internal const string DownloadPrefix = "https://api.eumetsat.int/data/download/1.0.0/collections/";
    // The start of the (encoded) download URL of the oldest product in mtg_search.json.
    private const string OldestProduct = DownloadPrefix +
        "EO%3AEUM%3ADAT%3A0691/products/W_XX-EUMETSAT-Darmstadt%2CIMG%2BSAT%2CMTI1%2BLI-2-LFL--FD--x-x--ARC-x_C_EUMT_20261113212019";
    private static string Sample(string f) => Path.Combine(AppContext.BaseDirectory, "Samples", f);

    /// <summary>Hands requests to a <see cref="SampleHttpHandler"/>, except that the token request can be refused with 401.</summary>
    private sealed class TokenGate(SampleHttpHandler inner) : HttpMessageHandler
    {
        public bool RejectToken { get; set; }
        /// <summary>Answers a request itself when it returns a response (null: the samples answer).</summary>
        public Func<string, HttpResponseMessage?>? Intercept { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var url = r.RequestUri!.ToString();
            if (RejectToken && url == TokenUrl) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            if (Intercept?.Invoke(url) is { } answer) return answer;
            return await new HttpMessageInvoker(inner, disposeHandler: false).SendAsync(r, ct);
        }
    }

    private sealed class Rig : IDisposable
    {
        public SampleHttpHandler Handler { get; } = new();
        public TokenGate Gate { get; }
        public FeedHttp Http { get; }
        public LightningBuffer Buffer { get; } = new();
        // 5 min after the 21:40:05-21:50:05 product was published.
        public TestClock Clock { get; } = new(new DateTime(2026, 11, 13, 21, 55, 30, DateTimeKind.Utc));
        public ViewState View { get; } = new();
        public RecordingLog Log { get; } = new();
        public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(60);
        public bool Paused { get; set; }
        public bool Wanted { get; set; } = true;
        public EumetsatCredentials? Credentials { get; set; } = new("K-SECRET", "S-SECRET");
        public MtgFeed Feed { get; }

        public Rig()
        {
            Handler.Map(TokenUrl, Sample("eumetsat_token.json"));
            Handler.Map(SearchPrefix, Sample("mtg_search.json"));
            Handler.Map(DownloadPrefix, Sample("mtg_lfl_sample.zip"));
            Gate = new TokenGate(Handler);
            Http = new FeedHttp(Gate);
            View.CameraSubPoint = new GeoPosition(10, 15);
            Feed = new MtgFeed(new EumetsatClient(Http, Clock), Buffer, Clock, View, () => Credentials,
                () => Window, () => Paused, () => Wanted, Log);
        }

        public List<string> Downloads => Handler.Requests.Where(r => r.StartsWith(DownloadPrefix, StringComparison.Ordinal)).ToList();
        public List<string> Searches => Handler.Requests.Where(r => r.StartsWith(SearchPrefix, StringComparison.Ordinal)).ToList();
        public void Dispose() => Http.Dispose();
    }

    [Fact]
    public void NameAndInterval()
    {
        using var r = new Rig();
        Assert.Equal("MTG", r.Feed.Name);
        Assert.Equal(TimeSpan.FromSeconds(60), r.Feed.Interval);
    }

    [Fact]
    public async Task NoKeyIsNotSetUp()
    {
        using var r = new Rig { Credentials = null };
        Assert.Equal(FeedRunResult.NotSetUp, await r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.Empty(r.Handler.Requests);
    }

    [Fact]
    public async Task ReadsProductsIntoTheBuffer()
    {
        using var r = new Rig();
        Assert.Equal(FeedRunResult.Ok, await r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.Equal(4, r.Downloads.Count);   // the sample search lists four products, all inside 60 min
        Assert.True(r.Buffer.Count > 0);
        Assert.All(r.Buffer.Between(DateTime.MinValue, DateTime.MaxValue), f => Assert.Equal(LightningSensor.Mtg, f.Sensor));
        Assert.NotNull(r.Buffer.Newest(LightningSensor.Mtg));
        Assert.False(r.Feed.KeyRejected);
    }

    [Fact]
    public async Task ReadsNewProductsOnce()
    {
        using var r = new Rig();
        await r.Feed.RunOnceAsync(CancellationToken.None);
        r.Clock.UtcNow += TimeSpan.FromSeconds(60);
        Assert.Equal(FeedRunResult.Ok, await r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.Equal(2, r.Searches.Count);
        Assert.Equal(4, r.Downloads.Count);   // the same search answer again: nothing new to download
        Assert.Single(r.Handler.Requests, u => u == TokenUrl);   // the token is cached
    }

    [Fact]
    public async Task SearchUsesDtstartAndNewestFirst()
    {
        using var r = new Rig();
        await r.Feed.RunOnceAsync(CancellationToken.None);
        var first = new Uri(r.Searches[0]);
        Assert.Equal(SearchPrefix, first.GetLeftPart(UriPartial.Path));
        var q = first.Query;
        Assert.Contains("format=json", q);
        Assert.Contains("pi=EO%3AEUM%3ADAT%3A0691", q);
        Assert.Contains("sort=start,time,0", q);
        Assert.Contains("c=20", q);
        Assert.Contains("si=0", q);
        // First run: the window start (now - 60 min).
        Assert.Contains("dtstart=2026-11-13T20%3A55%3A30Z", q);

        // Next run: 20 minutes before the end of the newest product seen (21:50:05), so a late older product is still found.
        r.Clock.UtcNow += TimeSpan.FromSeconds(60);
        await r.Feed.RunOnceAsync(CancellationToken.None);
        Assert.Contains("dtstart=2026-11-13T21%3A30%3A05Z", new Uri(r.Searches[1]).Query);

        // The newest product is downloaded first.
        Assert.Contains("20261113214005_20261113215005", r.Downloads[0]);
    }

    [Fact]
    public async Task OnlyProductsInsideTheWindowAreDownloaded()
    {
        using var r = new Rig { Window = TimeSpan.FromMinutes(15) };   // 21:40:30 to 21:55:30
        await r.Feed.RunOnceAsync(CancellationToken.None);
        var one = Assert.Single(r.Downloads);
        Assert.Contains("20261113214005_20261113215005", one);
    }

    [Fact]
    public async Task AtMostEightProductsPerRun()
    {
        using var r = new Rig();
        // Eleven products, newest first, all ending inside the window.
        var features = string.Join(",", Enumerable.Range(0, 11).Select(i =>
        {
            var end = new DateTime(2026, 11, 13, 21, 50, 5, DateTimeKind.Utc).AddMinutes(-i * 3);
            var start = end.AddMinutes(-10);
            return "{\"id\":\"p" + i + "\",\"properties\":{\"date\":\"" + start.ToString("yyyy-MM-ddTHH:mm:ssZ") + "/" +
                   end.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\",\"links\":{\"data\":[{\"href\":\"" + DownloadPrefix + "p" + i + "\"}]}}}";
        }));
        r.Handler.MapText(SearchPrefix, "{\"features\":[" + features + "]}");
        await r.Feed.RunOnceAsync(CancellationToken.None);
        Assert.Equal(8, r.Downloads.Count);
        Assert.EndsWith("/p0", r.Downloads[0]);
    }

    [Fact]
    public async Task SkippedWhenNotInViewOrNotWanted()
    {
        using var r = new Rig();
        r.View.CameraSubPoint = new GeoPosition(0, 150);   // Western Pacific: neither MTG (0) nor GOES-East (-75) is within 100 degrees
        Assert.Equal(FeedRunResult.Skipped, await r.Feed.RunOnceAsync(CancellationToken.None));
        r.View.CameraSubPoint = new GeoPosition(0, 10);
        r.Wanted = false;
        Assert.Equal(FeedRunResult.Skipped, await r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.Empty(r.Handler.Requests);
    }

    [Fact]
    public async Task PolledWhenOnlyGoesEastFacesTheCamera()
    {
        using var r = new Rig();
        r.View.CameraSubPoint = new GeoPosition(45, -110);   // Rockies: GOES-East 54 degrees away, MTG 104
        Assert.Equal(FeedRunResult.Ok, await r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.NotEmpty(r.Downloads);
    }

    [Fact]
    public async Task StateTellsWhyThereIsNothingNew()
    {
        using var r = new Rig();
        Assert.Equal(MtgFeedState.Running, r.Feed.State);
        r.View.CameraSubPoint = new GeoPosition(0, 150);
        await r.Feed.RunOnceAsync(CancellationToken.None);
        Assert.Equal(MtgFeedState.NotInView, r.Feed.State);
        r.View.CameraSubPoint = new GeoPosition(10, 15);
        r.Paused = true;
        await r.Feed.RunOnceAsync(CancellationToken.None);
        Assert.Equal(MtgFeedState.Paused, r.Feed.State);
        r.Paused = false;
        await r.Feed.RunOnceAsync(CancellationToken.None);
        Assert.Equal(MtgFeedState.Running, r.Feed.State);
        r.Gate.RejectToken = true;
        r.Clock.UtcNow += TimeSpan.FromHours(2);
        await Assert.ThrowsAsync<EumetsatKeyRejectedException>(() => r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.Equal(MtgFeedState.KeyRejected, r.Feed.State);
    }

    [Fact]
    public async Task ActiveOnlyAfterASuccessWithTheseCredentials()
    {
        using var r = new Rig();
        Assert.False(r.Feed.Active);   // a key is saved but MTG has not run yet: GOES-East keeps covering
        await r.Feed.RunOnceAsync(CancellationToken.None);
        Assert.True(r.Feed.Active);
        r.Credentials = new EumetsatCredentials("K2", "S2");
        Assert.False(r.Feed.Active);   // a new key: back to covering until it has worked
        await r.Feed.RunOnceAsync(CancellationToken.None);
        Assert.True(r.Feed.Active);
        r.Gate.RejectToken = true;
        r.Clock.UtcNow += TimeSpan.FromHours(2);
        await Assert.ThrowsAsync<EumetsatKeyRejectedException>(() => r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.False(r.Feed.Active);
    }

    [Fact]
    public async Task MtgFlashesReplaceGoesEastOnesInTheSamePeriodEastOf37W()
    {
        using var r = new Rig();
        DateTime T(int h, int m) => new(2026, 11, 13, h, m, 0, DateTimeKind.Utc);
        var inside = new LightningFlash(T(21, 35), 5, -30, 1e-13, LightningSensor.GoesEast);      // east of 37.6 W, inside a product period
        var west = new LightningFlash(T(21, 35), 5, -50, 1e-13, LightningSensor.GoesEast);        // GOES-East owns this one
        var before = new LightningFlash(T(21, 5), 5, -30, 1e-13, LightningSensor.GoesEast);       // before every product
        var after = new LightningFlash(T(21, 52), 5, -30, 1e-13, LightningSensor.GoesEast);       // after every product
        var pacific = new LightningFlash(T(21, 35), 5, -130, 1e-13, LightningSensor.GoesWest);
        r.Buffer.Add([inside, west, before, after, pacific]);
        int changed = 0;
        r.Buffer.Changed += (_, _) => changed++;
        await r.Feed.RunOnceAsync(CancellationToken.None);
        var held = r.Buffer.Between(DateTime.MinValue, DateTime.MaxValue);
        Assert.DoesNotContain(inside, held);
        Assert.Contains(west, held);
        Assert.Contains(before, held);
        Assert.Contains(after, held);
        Assert.Contains(pacific, held);
        Assert.True(changed > 0);
    }

    [Fact]
    public async Task AnOlderProductPublishedLaterIsStillFetched()
    {
        using var r = new Rig();
        r.Handler.MapText(SearchPrefix, Features(("a", 21, 50)));
        await r.Feed.RunOnceAsync(CancellationToken.None);
        Assert.Single(r.Downloads);
        // The next search (from 20 minutes before 21:50:05) now also lists an older product that came out late.
        r.Handler.MapText(SearchPrefix, Features(("a", 21, 50), ("b", 21, 40)));
        r.Clock.UtcNow += TimeSpan.FromSeconds(60);
        await r.Feed.RunOnceAsync(CancellationToken.None);
        Assert.Contains("dtstart=2026-11-13T21%3A30%3A05Z", new Uri(r.Searches[1]).Query);
        Assert.Equal(2, r.Downloads.Count);
        Assert.EndsWith("/b", r.Downloads[1]);
    }

    private static string Features(params (string Id, int Hour, int Minute)[] products) =>
        "{\"features\":[" + string.Join(",", products.Select(p =>
        {
            var end = new DateTime(2026, 11, 13, p.Hour, p.Minute, 5, DateTimeKind.Utc);
            return "{\"id\":\"" + p.Id + "\",\"properties\":{\"date\":\"" + end.AddMinutes(-10).ToString("yyyy-MM-ddTHH:mm:ssZ") + "/" +
                   end.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\",\"links\":{\"data\":[{\"href\":\"" + DownloadPrefix + p.Id + "\"}]}}}";
        })) + "]}";

    [Fact]
    public async Task ADownloadRefusedWithAFreshTokenSkipsTheProductNotTheKey()
    {
        using var r = new Rig();
        r.Gate.Intercept = u => u.StartsWith(OldestProduct, StringComparison.Ordinal) ? new HttpResponseMessage(HttpStatusCode.Forbidden) : null;
        Assert.Equal(FeedRunResult.Ok, await r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.False(r.Feed.KeyRejected);
        Assert.True(r.Buffer.Count > 0);
        Assert.Empty(r.Log.Errors);
    }

    [Fact]
    public async Task ANetworkFailureGetsOneRetryOnALaterRun()
    {
        using var r = new Rig();
        int oldestTries = 0;
        r.Gate.Intercept = u =>
        {
            if (!u.StartsWith(OldestProduct, StringComparison.Ordinal)) return null;
            oldestTries++;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        };
        Assert.Equal(FeedRunResult.Ok, await r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.Equal(1, oldestTries);
        Assert.Empty(r.Log.Warnings);   // not given up on yet
        r.Clock.UtcNow += TimeSpan.FromSeconds(60);
        // The retry is the only download of this run and fails too: a failed run, and now it is given up on (one warning).
        await Assert.ThrowsAsync<HttpRequestException>(() => r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.Equal(2, oldestTries);
        Assert.Single(r.Log.Warnings);
        r.Clock.UtcNow += TimeSpan.FromSeconds(60);
        Assert.Equal(FeedRunResult.Ok, await r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.Equal(2, oldestTries);   // and no more
        Assert.Single(r.Log.Warnings);
    }

    [Fact]
    public async Task ARetriedProductThatWorksLoadsItsFlashes()
    {
        using var r = new Rig();
        r.Handler.MapText(SearchPrefix, Features(("a", 21, 50)));
        bool fail = true;
        r.Gate.Intercept = u => fail && u.EndsWith("/a", StringComparison.Ordinal) ? new HttpResponseMessage(HttpStatusCode.BadGateway) : null;
        await Assert.ThrowsAsync<HttpRequestException>(() => r.Feed.RunOnceAsync(CancellationToken.None));   // all downloads failed: not Ok
        Assert.Equal(0, r.Buffer.Count);
        fail = false;
        r.Clock.UtcNow += TimeSpan.FromSeconds(60);
        Assert.Equal(FeedRunResult.Ok, await r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.True(r.Buffer.Count > 0);
    }

    [Fact]
    public async Task EveryDownloadFailingIsAFailedRunNotOk()
    {
        using var r = new Rig();
        r.Gate.Intercept = u => u.StartsWith(DownloadPrefix, StringComparison.Ordinal) ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        await Assert.ThrowsAsync<HttpRequestException>(() => r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.False(r.Feed.Active);
    }

    [Fact]
    public async Task PausedWhenTheLimitIsReached()
    {
        using var r = new Rig { Paused = true };
        Assert.Equal(FeedRunResult.Paused, await r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.Empty(r.Handler.Requests);
    }

    [Fact]
    public async Task LimitReachedDuringACatchUpStopsBeforeTheNextProduct()
    {
        using var r = new Rig();
        var feed = new MtgFeed(new EumetsatClient(r.Http, r.Clock), r.Buffer, r.Clock, r.View, () => r.Credentials,
            () => r.Window, () => r.Downloads.Count >= 1, () => true, r.Log);
        Assert.Equal(FeedRunResult.Paused, await feed.RunOnceAsync(CancellationToken.None));
        Assert.Single(r.Downloads);
    }

    [Fact]
    public async Task BadProductIsSkippedOthersLoad()
    {
        using var r = new Rig();
        r.Handler.MapText(OldestProduct, "not a zip");
        Assert.Equal(FeedRunResult.Ok, await r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.True(r.Buffer.Count > 0);
        Assert.Single(r.Log.Warnings);

        // Not retried.
        r.Clock.UtcNow += TimeSpan.FromSeconds(60);
        await r.Feed.RunOnceAsync(CancellationToken.None);
        Assert.Equal(4, r.Downloads.Count);
        Assert.Single(r.Log.Warnings);
    }

    [Fact]
    public async Task MalformedSearchAnswerIsAFailedRun()
    {
        using var r = new Rig();
        r.Handler.MapText(SearchPrefix, "<html>maintenance</html>");
        await Assert.ThrowsAsync<HttpRequestException>(() => r.Feed.RunOnceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RejectedKeyFailsWithKeyRejected()
    {
        var samples = new SampleHttpHandler();
        samples.Map("https://noaa-goes19.s3.amazonaws.com/?list-type=2", Sample("glm_g19_listing.xml"));
        samples.Map("https://noaa-goes19.s3.amazonaws.com/GLM-L2-LCFA/", Sample("glm_g19_sample.nc"));
        var gate = new TokenGate(samples) { RejectToken = true };
        using var http = new FeedHttp(gate);
        var clock = new TestClock(new DateTime(2026, 11, 11, 19, 59, 50, DateTimeKind.Utc));
        var view = new ViewState { CameraSubPoint = new GeoPosition(0, -30) };   // faces both GOES-East and MTG
        var log = new RecordingLog();
        var buffer = new LightningBuffer();
        var mtg = new MtgFeed(new EumetsatClient(http, clock), buffer, clock, view, () => new EumetsatCredentials("k", "s"),
            () => TimeSpan.FromMinutes(15), () => false, () => true, log);
        var glm = new GlmFeed(http, buffer, LightningSensor.GoesEast, clock, view, () => TimeSpan.FromMinutes(15),
            () => false, () => !mtg.KeyRejected, () => true, log);
        using var scheduler = new FeedScheduler([mtg, glm], clock, log);

        for (int i = 0; i < 3; i++)
        {
            await scheduler.TickAsync(CancellationToken.None);
            clock.UtcNow = scheduler.Statuses.Max(s => s.NextRunUtc);
        }
        var mtgStatus = scheduler.Statuses.Single(s => s.Name == "MTG");
        Assert.Equal(FeedState.Failing, mtgStatus.State);
        Assert.Equal("EUMETSAT key rejected. Check it in Settings.", mtgStatus.LastError);
        Assert.True(mtg.KeyRejected);
        Assert.Equal(FeedState.Ok, scheduler.Statuses.Single(s => s.Name == glm.Name).State);
    }

    [Fact]
    public async Task KeyRejectedClearsAfterSuccessOrANewKey()
    {
        using var r = new Rig();
        r.Gate.RejectToken = true;
        await Assert.ThrowsAsync<EumetsatKeyRejectedException>(() => r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.True(r.Feed.KeyRejected);

        r.Gate.RejectToken = false;
        Assert.Equal(FeedRunResult.Ok, await r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.False(r.Feed.KeyRejected);

        // Rejected again, then the user saves another key: the flag drops as soon as the credentials differ.
        r.Gate.RejectToken = true;
        r.Clock.UtcNow += TimeSpan.FromHours(2);   // the token has expired
        await Assert.ThrowsAsync<EumetsatKeyRejectedException>(() => r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.True(r.Feed.KeyRejected);
        r.Credentials = new EumetsatCredentials("K2", "S2");
        r.Gate.RejectToken = false;
        await r.Feed.RunOnceAsync(CancellationToken.None);
        Assert.False(r.Feed.KeyRejected);
    }

    [Fact]
    public async Task CredentialsNeverAppearInLogsOrUris()
    {
        using var r = new Rig();
        r.Handler.MapText(OldestProduct, "bad");   // one warning line too
        await r.Feed.RunOnceAsync(CancellationToken.None);
        r.Clock.UtcNow += TimeSpan.FromSeconds(60);
        await r.Feed.RunOnceAsync(CancellationToken.None);
        Assert.NotEmpty(r.Handler.Requests);
        Assert.NotEmpty(r.Log.Warnings);
        Assert.All(r.Handler.Requests, u => { Assert.DoesNotContain("K-SECRET", u); Assert.DoesNotContain("S-SECRET", u); });
        var lines = r.Log.Infos.Concat(r.Log.Warnings).Concat(r.Log.Errors);
        Assert.All(lines, l => { Assert.DoesNotContain("K-SECRET", l); Assert.DoesNotContain("S-SECRET", l); });
    }
}
