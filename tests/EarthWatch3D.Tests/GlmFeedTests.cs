using System.IO;
using AstroCore.Earth;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Lightning;
using EarthWatch3D.Core.Lightning.Glm;

namespace EarthWatch3D.Tests;

public sealed class GlmFeedTests
{
    private const string Bucket = "https://noaa-goes19.s3.amazonaws.com/";
    private static string Sample(string f) => Path.Combine(AppContext.BaseDirectory, "Samples", f);
    private static string ListPrefix(string hour) => $"{Bucket}?list-type=2&prefix=GLM-L2-LCFA%2F2026%2F315%2F{hour}%2F";

    private sealed class Rig : IDisposable
    {
        public SampleHttpHandler Handler { get; } = new();
        public FeedHttp Http { get; }
        public LightningBuffer Buffer { get; } = new();
        public TestClock Clock { get; } = new(new DateTime(2026, 11, 11, 19, 59, 50, DateTimeKind.Utc));
        public ViewState View { get; } = new();
        public RecordingLog Log { get; } = new();
        public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(60);
        public bool Paused { get; set; }
        public bool Wanted { get; set; } = true;
        public GlmFeed Feed { get; }

        public Rig()
        {
            Handler.Map(ListPrefix("19"), Sample("glm_g19_listing.xml"));
            foreach (var h in new[] { "18", "20" })   // hours with no files yet (or none in the sample)
                Handler.MapText(ListPrefix(h), "<ListBucketResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\"></ListBucketResult>");
            Handler.Map(Bucket + "GLM-L2-LCFA/", Sample("glm_g19_sample.nc"));
            Http = new FeedHttp(Handler);
            View.CameraSubPoint = new GeoPosition(0, -75);
            Feed = new GlmFeed(Http, Buffer, LightningSensor.GoesEast, Clock, View, () => Window, () => Paused, () => false, () => Wanted, Log);
        }

        public IEnumerable<string> Downloads => Handler.Requests.Where(r => !r.Contains("list-type=2", StringComparison.Ordinal));
        public IEnumerable<string> Listings => Handler.Requests.Where(r => r.Contains("list-type=2", StringComparison.Ordinal));
        public void Dispose() => Http.Dispose();
    }

    [Fact]
    public async Task ReadsNewFilesIntoTheBuffer()
    {
        using var r = new Rig();
        Assert.Equal(FeedRunResult.Ok, await r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.True(r.Buffer.Count > 0);
        Assert.All(r.Buffer.Between(DateTime.MinValue, DateTime.MaxValue), f => Assert.Equal(LightningSensor.GoesEast, f.Sensor));
    }

    [Fact]
    public async Task SecondRunListsWithStartAfter()
    {
        using var r = new Rig();
        await r.Feed.RunOnceAsync(CancellationToken.None);
        await r.Feed.RunOnceAsync(CancellationToken.None);
        var newest = GlmListing.Parse(File.OpenRead(Sample("glm_g19_listing.xml"))).Select(o => o.Key).Max(StringComparer.Ordinal);
        var listings = r.Listings.Where(l => l.Contains("%2F19%2F", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, listings.Count);
        Assert.DoesNotContain("start-after=", listings[0]);
        Assert.Contains("start-after=" + Uri.EscapeDataString(newest!), listings[1]);
    }

    [Fact]
    public async Task CatchUpIsLimitedToTheWindowNewestFirst()
    {
        using var r = new Rig { Window = TimeSpan.FromMinutes(15) };
        await r.Feed.RunOnceAsync(CancellationToken.None);
        var all = GlmListing.Parse(File.OpenRead(Sample("glm_g19_listing.xml")));
        var expected = all.OrderByDescending(o => o.EndUtc).Take(GlmFeed.MaxFilesPerRun).Select(o => Bucket + o.Key).ToHashSet();
        Assert.Equal(45, r.Downloads.Count());
        Assert.True(expected.SetEquals(r.Downloads));

        r.Clock.UtcNow += TimeSpan.FromSeconds(20);   // the one left-over file is now outside the window
        await r.Feed.RunOnceAsync(CancellationToken.None);
        Assert.Empty(r.Downloads.Skip(45));
    }

    [Fact]
    public async Task ListsBothHoursWhenTheWindowCrossesTheHour()
    {
        using var r = new Rig { Window = TimeSpan.FromMinutes(15) };
        r.Clock.UtcNow = new DateTime(2026, 11, 11, 20, 5, 0, DateTimeKind.Utc);
        await r.Feed.RunOnceAsync(CancellationToken.None);
        Assert.Contains(r.Listings, l => l.Contains("%2F19%2F", StringComparison.Ordinal));
        Assert.Contains(r.Listings, l => l.Contains("%2F20%2F", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SkippedWhenNotInViewOrNotWanted()
    {
        using var r = new Rig();
        r.View.CameraSubPoint = new GeoPosition(0, 100);
        Assert.Equal(FeedRunResult.Skipped, await r.Feed.RunOnceAsync(CancellationToken.None));
        r.View.CameraSubPoint = new GeoPosition(0, -75);
        r.Wanted = false;
        Assert.Equal(FeedRunResult.Skipped, await r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.Empty(r.Handler.Requests);
    }

    [Fact]
    public async Task PausedWhenTheLimitIsReached()
    {
        using var r = new Rig { Paused = true };
        Assert.Equal(FeedRunResult.Paused, await r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.Empty(r.Handler.Requests);
    }

    [Fact]
    public async Task BadFileIsSkippedOthersLoad()
    {
        using var r = new Rig();
        var listing = GlmListing.Parse(File.OpenRead(Sample("glm_g19_listing.xml")));
        r.Handler.MapText(Bucket + listing[^1].Key, "not a netcdf file");
        Assert.Equal(FeedRunResult.Ok, await r.Feed.RunOnceAsync(CancellationToken.None));
        Assert.True(r.Buffer.Count > 0);
        Assert.Single(r.Log.Warnings);
    }
}
