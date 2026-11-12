using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Feeds.Gibs;
using EarthWatch3D.Core.Globe;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.Tests;

public sealed class GibsCloudsFeedTests : IDisposable
{
    private static readonly string SampleJpeg = Path.Combine(AppContext.BaseDirectory, "Samples", "gibs_viirs_noaa21_truecolor_4096.jpg");
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "ew3d-clouds-" + Guid.NewGuid().ToString("N")));
    private readonly SampleHttpHandler _handler = new();
    private readonly FeedHttp _http;
    private readonly TestClock _clock = new(new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc));
    private readonly RecordingLog _log = new();
    private bool _wanted = true;

    public GibsCloudsFeedTests()
    {
        _paths.EnsureCreated();
        _handler.Map(GibsCloudsFeed.UrlPrefix, SampleJpeg);
        _http = new FeedHttp(_handler);
    }

    public void Dispose()
    {
        _http.Dispose();
        try { Directory.Delete(_paths.Root, recursive: true); } catch (IOException) { }
    }

    private static Bgra32Image Decode(byte[] jpeg) => ImageConversions.DecodeToBgra32(new MemoryStream(jpeg), 512);

    private GibsCloudsFeed NewFeed() => new(_http, _paths, _clock, () => _wanted, _log, Decode);

    private IEnumerable<string> GetMaps => _handler.Requests.Where(r => r.Contains("REQUEST=GetMap", StringComparison.Ordinal));

    [Fact]
    public async Task DownloadsYesterdayOnceAndCaches()
    {
        var feed = NewFeed();
        var raised = new List<CloudImage>();
        feed.ImageReady += (_, i) => raised.Add(i);

        Assert.Equal(FeedRunResult.Ok, await feed.RunOnceAsync(CancellationToken.None));
        var request = Assert.Single(GetMaps);
        Assert.Contains("TIME=2026-11-10", request);
        Assert.Contains("BBOX=-90,-180,90,180", request);
        Assert.Contains("LAYERS=VIIRS_NOAA21_CorrectedReflectance_TrueColor", request);
        var first = Assert.Single(raised);
        Assert.Equal(new DateTime(2026, 11, 10), first.DateUtc);
        Assert.Equal(await File.ReadAllBytesAsync(SampleJpeg), first.Jpeg);
        Assert.True(File.Exists(Path.Combine(_paths.CloudsDir, "viirs_2026-11-10.jpg")));

        await feed.RunOnceAsync(CancellationToken.None);
        Assert.Single(GetMaps);
        Assert.Single(raised);

        var again = NewFeed();
        var fromDisk = new List<CloudImage>();
        again.ImageReady += (_, i) => fromDisk.Add(i);
        await again.RunOnceAsync(CancellationToken.None);
        Assert.Single(GetMaps);
        Assert.Equal(first.Jpeg, Assert.Single(fromDisk).Jpeg);
    }

    [Fact]
    public async Task FallsBackWhenYesterdayIsPartial()
    {
        var blackFile = Path.Combine(_paths.Root, "black.jpg");
        await File.WriteAllBytesAsync(blackFile, MostlyBlackJpeg());
        var handler = new SampleHttpHandler();
        handler.Map(GibsCloudsFeed.UrlPrefix, SampleJpeg);
        handler.Map(GibsCloudsFeed.RequestUri(new DateTime(2026, 11, 10)).ToString(), blackFile);
        using var http = new FeedHttp(handler);
        var feed = new GibsCloudsFeed(http, _paths, _clock, () => true, _log, Decode);
        CloudImage? ready = null;
        feed.ImageReady += (_, i) => ready = i;

        Assert.Equal(FeedRunResult.Ok, await feed.RunOnceAsync(CancellationToken.None));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("TIME=2026-11-10", handler.Requests[0]);
        Assert.Contains("TIME=2026-11-09", handler.Requests[1]);
        Assert.Equal(new DateTime(2026, 11, 9), ready!.DateUtc);
        Assert.False(File.Exists(Path.Combine(_paths.CloudsDir, "viirs_2026-11-10.jpg")));   // the partial day is not kept
    }

    [Fact]
    public async Task ACorruptCachedImageIsDownloadedAgain()
    {
        var file = Path.Combine(_paths.CloudsDir, "viirs_2026-11-10.jpg");
        await File.WriteAllBytesAsync(file, [1, 2, 3, 4, 5]);
        var feed = NewFeed();
        CloudImage? ready = null;
        feed.ImageReady += (_, i) => ready = i;
        await feed.RunOnceAsync(CancellationToken.None);
        Assert.Single(GetMaps);
        Assert.Equal(await File.ReadAllBytesAsync(SampleJpeg), ready!.Jpeg);
        Assert.Equal(ready.Jpeg, await File.ReadAllBytesAsync(file));
    }

    [Fact]
    public async Task DeletesOlderImages()
    {
        var old = Path.Combine(_paths.CloudsDir, "viirs_2026-11-05.jpg");
        await File.WriteAllBytesAsync(old, [1, 2, 3]);
        await NewFeed().RunOnceAsync(CancellationToken.None);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(Path.Combine(_paths.CloudsDir, "viirs_2026-11-10.jpg")));
    }

    [Fact]
    public async Task SkippedWhenNotWanted()
    {
        _wanted = false;
        var feed = NewFeed();
        Assert.Equal(TimeSpan.FromHours(1), feed.Interval);
        Assert.Equal(FeedRunResult.Skipped, await feed.RunOnceAsync(CancellationToken.None));
        Assert.Empty(_handler.Requests);
        Assert.True(feed.Interval < TimeSpan.FromMinutes(1));   // looks again soon, so switching the layer on is quick
        _wanted = true;
        await feed.RunOnceAsync(CancellationToken.None);
        Assert.Equal(TimeSpan.FromHours(1), feed.Interval);
    }

    private static byte[] MostlyBlackJpeg()
    {
        const int w = 256, h = 128;
        var pixels = new byte[w * h * 4];
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
