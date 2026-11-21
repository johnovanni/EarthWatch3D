using System.Windows.Media;
using AstroCore.Earth;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.Core;
using EarthWatch3D.Core.Geo;
using GlobeCore.Geo;
using GlobeCore.Imaging;

namespace EarthWatch3D.Tests;

public class AreaRasterizerTests
{
    private static GeoPolygon Box(double lat0, double lon0, double lat1, double lon1) =>
        new([[new(lat0, lon0), new(lat0, lon1), new(lat1, lon1), new(lat1, lon0), new(lat0, lon0)]]);

    private static (byte B, byte G, byte R, byte A) Px(Bgra32Image img, int x, int y)
    {
        int i = (y * img.Width + x) * 4;
        return (img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
    }

    private static readonly Func<string, IReadOnlyList<GeoPolygon>?> NoZones = static _ => null;

    private static EarthWatch3D.Core.Events.CycloneAdvisory Storm(DateTime t, IReadOnlyList<GeoPolygon> cone) =>
        new(EarthWatch3D.Core.Events.EventSource.Nhc, "nhc:x", t, new GeoPosition(5, 5), "x", null,
            "x", 80, null, [], [], cone, [new(95, Box(20, 40, 30, 60))]);

    [Fact]
    public void SceneBuildsConeAndSkipsWhenUnchanged()
    {
        var t = new DateTime(2026, 11, 12, 6, 0, 0, DateTimeKind.Utc);
        var vis = new EarthWatch3D.Core.Events.VisibleEvents([], [Storm(t, [Box(0, 0, 10, 10)])], [], []);
        var f = EarthWatch3D.Core.Events.DisplayFilter.Default;
        var first = AreasLayer.Plan(vis, f, NoZones);
        var second = AreasLayer.Plan(vis, f, NoZones);
        Bgra32Image? img = null;
        Sta.Run(() => img = AreasLayer.Rasterize(first, 360, 180));
        Assert.Equal((255, 255, 255), (Px(img!, 185, 85).R, Px(img!, 185, 85).G, Px(img!, 185, 85).B));   // white cone
        var wind = Px(img!, 230, 65);                                                                       // 95 km/h -> orange #FF9500
        Assert.Equal((0xFF, 0x95, 0x00), ((int)wind.R, (int)wind.G, (int)wind.B));
        Assert.Equal(first.Key, second.Key);   // unchanged: no rebuild
    }

    [Fact]
    public void NewConeListWithSameIdAndTimeChangesTheKey()
    {
        // A merge or a re-read can give the same advisory (Id, TimeUtc) a new cone: the layer must be rebuilt.
        var t = new DateTime(2026, 11, 12, 6, 0, 0, DateTimeKind.Utc);
        var f = EarthWatch3D.Core.Events.DisplayFilter.Default;
        var before = AreasLayer.Plan(new([], [Storm(t, [Box(0, 0, 10, 10)])], [], []), f, NoZones);
        var after = AreasLayer.Plan(new([], [Storm(t, [Box(0, 0, 10, 10), Box(-20, 0, -10, 10)])], [], []), f, NoZones);
        Assert.NotEqual(before.Key, after.Key);
        Assert.Equal(before.Shapes.Count + 1, after.Shapes.Count);
        Bgra32Image? img = null;
        Sta.Run(() => img = AreasLayer.Rasterize(after, 360, 180));
        Assert.NotEqual(0, Px(img!, 185, 105).A);   // the new cone part (lat -15) is drawn
    }

    [Fact]
    public void NothingToDrawGivesNoImage()
    {
        var plan = AreasLayer.Plan(EarthWatch3D.Core.Events.VisibleEvents.None, EarthWatch3D.Core.Events.DisplayFilter.Default, NoZones);
        Assert.Empty(plan.Shapes);
        Assert.Null(AreasLayer.Rasterize(plan, 360, 180));   // no WPF work, so no STA needed
    }

    [Fact]
    public async Task ZoneOnlyAlertIsDrawnOnceZoneResolvable()
    {
        const string url = "https://api.weather.gov/zones/forecast/PZZ150";
        var handler = new GlobeCore.Feeds.SampleHttpHandler();
        handler.Map(url, System.IO.Path.Combine(AppContext.BaseDirectory, "Samples", "nws_zone.json"));
        var paths = new EarthWatch3D.Core.Storage.AppPaths(new GlobeCore.Storage.AppDataRoot(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ew3d-ar-" + Guid.NewGuid().ToString("N"))));
        paths.EnsureCreated();
        using var http = new GlobeCore.Feeds.FeedHttp(AppInfo.UserAgent, handler);
        var zc = new EarthWatch3D.Core.Feeds.Nws.ZoneCache(http, paths, new TestClock(new DateTime(2026, 11, 12, 6, 0, 0, DateTimeKind.Utc)), new RecordingLog());
        var t = new DateTime(2026, 11, 12, 6, 0, 0, DateTimeKind.Utc);
        var alert = new EarthWatch3D.Core.Events.WeatherAlert("a1", t, new GeoPosition(0, 0), "t", null, "Gale Warning", "Severe",
            t, t.AddHours(2), null, [], [url], "");
        var vis = new EarthWatch3D.Core.Events.VisibleEvents([], [], [alert], []);
        var f = EarthWatch3D.Core.Events.DisplayFilter.Default;
        Func<string, IReadOnlyList<GeoPolygon>?> memory = id => zc.TryGetCached(id, out var p) ? p : null;
        var unresolved = new List<string>();
        var before = AreasLayer.Plan(vis, f, memory, unresolved);
        Assert.Empty(before.Shapes);              // zone not loaded: nothing drawn
        Assert.Equal([url], unresolved);          // and the caller is told which zones to load

        zc.Request([url]);
        await zc.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
        unresolved.Clear();
        var after = AreasLayer.Plan(vis, f, memory, unresolved);
        var again = AreasLayer.Plan(vis, f, memory, unresolved);
        Assert.NotEqual(before.Key, after.Key);   // the key changed
        Assert.Equal(after.Key, again.Key);
        Assert.Empty(unresolved);
        Bgra32Image? img = null;
        Sta.Run(() => img = AreasLayer.Rasterize(after, 360, 180));
        Assert.Contains(img!.Pixels.Where((_, i) => i % 4 == 3), a => a != 0);
    }
}
