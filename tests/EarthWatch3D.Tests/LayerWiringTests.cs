using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using AstroCore.Earth;
using EarthWatch3D.App.Controllers;
using EarthWatch3D.App.Diagnostics;
using EarthWatch3D.App.Layers;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Feeds.Nws;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Lightning;
using EarthWatch3D.Core.Storage;
using GlobeCore.Feeds;
using GlobeCore.Imaging;
using GlobeCore.Storage;
using GlobeCore.Wpf.Layers;
using HelixToolkit.Wpf;
using Path = System.Windows.Shapes.Path;

namespace EarthWatch3D.Tests;

/// <summary>The LayerHost and layers that SceneController builds, in the order that keeps today's drawing order.</summary>
public class LayerWiringTests
{
    private static readonly DateTime T0 = new(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);

    private sealed record Rig(SceneController Scene, MainViewModel Vm, HelixViewport3D Viewport, Canvas Overlay);

    /// <summary>
    /// A SceneController in a window that is never shown (laid out by hand at 800x600, the camera facing 0°N 0°E, where
    /// an M6 quake is), started on a small base image as OnLoaded does after decoding.
    /// </summary>
    private static async Task<Rig> StartAsync()
    {
        var clock = new TestClock(T0);
        var vm = new MainViewModel(clock, new FakeSettingsStore());
        var store = new EventStore(clock);
        store.UpsertEarthquakes([new Earthquake("q1", T0.AddHours(-1), new GeoPosition(0, 0), "M6", null, 6.0, 10, "", T0)]);
        var paths = new AppPaths(new AppDataRoot(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ew3d-wiring-" + Guid.NewGuid().ToString("N"))));
        var zones = new ZoneCache(new FeedHttp(AppInfo.UserAgent, new SampleHttpHandler()), paths, clock, new RecordingLog());
        var lightning = new LiveLightningSource(new LightningBuffer(10), () => false);
        var viewport = new HelixViewport3D
        {
            Camera = new PerspectiveCamera(new Point3D(3, 0, 0), new Vector3D(-3, 0, 0), new Vector3D(0, 0, 1), 45),
        };
        var overlay = new Canvas();
        var card = new Border();
        var grid = new Grid { Children = { viewport, card, overlay } };
        var window = new Window { Content = grid };
        grid.Measure(new Size(800, 600));
        grid.Arrange(new Rect(0, 0, 800, 600));
        var scene = new SceneController(viewport, overlay, card, vm, window, store, zones, lightning, new ViewState(), new RecordingLog());

        const int w = 64, h = 32;
        var baseImage = Bgra32Image.Create(w, h);
        Array.Fill(baseImage.Pixels, (byte)200);
        var lineImage = Bgra32Image.Create(w, h);
        for (int x = 0; x < w; x++) lineImage.Pixels[(h / 2 * w + x) * 4 + 3] = 255;   // one opaque row: the "coastline"
        await scene.StartHostAsync(baseImage, SparseOverlay.FromImage(lineImage));
        await StaAsync.WaitUntil(() => !SnapshotDiagnostic.IsBusy(scene));
        return new Rig(scene, vm, viewport, overlay);
    }

    private static string Kind(GlobeLayer l) => l switch
    {
        CloudsImageLayer c => c.Pass.ToString(),
        AreasImageLayer a => a.Pass.ToString(),
        LinesImageLayer li => li.Pass.ToString(),
        SceneLayer => "scene",
        OverlayLayer => "overlay",
        _ => "?",
    };

    [Fact]
    public void LayersAndOrdersAsDocumented() => StaAsync.Run(async () =>
    {
        var rig = await StartAsync();
        var host = rig.Scene.Host!;

        var described = host.Layers.Select(l => (l.GetType().Name, Kind(l), l.ZOrder)).ToList();
        Assert.Equal(
        [
            (nameof(CloudsImageLayer), "Surface", 10),
            (nameof(LinesImageLayer), "AboveNight", 10),
            (nameof(AreasImageLayer), "Surface", 20),
            (nameof(EventsSceneLayer), "scene", 100),
            (nameof(LightningSceneLayer), "scene", 110),
            (nameof(SunMoonSceneLayer), "scene", 120),
            (nameof(EventLabelsOverlayLayer), "overlay", 200),
            (nameof(SunMoonOverlayLayer), "overlay", 210),
        ], described);
        Assert.Equal([nameof(EventsSceneLayer), nameof(SunMoonSceneLayer)],
            host.Layers.Where(l => l is IPickable).Select(l => l.GetType().Name));

        // What the lines layer blends carries the same pass and order (coastlines are on by default).
        var lines = Assert.IsAssignableFrom<IImageLayer>(host.Layers.OfType<LinesImageLayer>().Single().Current);
        Assert.Equal((ImagePass.AboveNight, 10), (lines.Pass, lines.Order));

        // WPF draws 3D content in child order, which matters for the transparent pixels: the globe, then the events,
        // the lightning and the Sun/Moon markers, as SceneController added them before the split.
        var children = rig.Viewport.Children.ToList();
        var scenes = host.Layers.OfType<SceneLayer>().ToList();
        int globe = children.FindIndex(c => c is ModelVisual3D { Content: GeometryModel3D });
        Assert.True(globe >= 0, "the globe is in the viewport");
        Assert.Equal(scenes.Select(s => (Visual3D)s.Root), children.Skip(globe + 1));
        Assert.Equal(4, host.Layers.OfType<LightningSceneLayer>().Single().Root.Children.OfType<PointsVisual3D>().Count());
        Assert.Equal(2, host.Layers.OfType<SunMoonSceneLayer>().Single().Root.Children.OfType<BillboardVisual3D>().Count());

        // The overlay canvas: the Sun/Moon icons, arrows and labels come first; event labels are added after them, the
        // first time they are drawn (one frame here).
        host.RenderFrame();
        Assert.Equal([typeof(Ellipse), typeof(Path), typeof(TextBlock), typeof(Canvas), typeof(Path), typeof(TextBlock), typeof(TextBlock)],
            rig.Overlay.Children.Cast<UIElement>().Select(e => e.GetType()));
        var label = (TextBlock)rig.Overlay.Children[6];
        Assert.Equal(("M6.0", Visibility.Visible), (label.Text, label.Visibility));
    });

    [Fact]
    public void TogglingLayerVisibilityFollowsViewModel() => StaAsync.Run(async () =>
    {
        var rig = await StartAsync();
        var host = rig.Scene.Host!;
        var lines = host.Layers.OfType<LinesImageLayer>().Single();
        Assert.True(lines.ShowCoastlines);
        Assert.False(lines.ShowPlates);
        Assert.NotNull(lines.Current);
        int composes = 0;
        host.Composed += (_, _) => composes++;

        rig.Vm.Layers.ShowCoastlines = false;

        Assert.False(lines.ShowCoastlines);
        await StaAsync.WaitUntil(() => host.IsComposeIdle);
        await Task.Delay(400);   // longer than the 250 ms throttle: no trailing compose follows
        Assert.True(host.IsComposeIdle);
        Assert.Equal(1, composes);
        Assert.Null(lines.Current);   // neither coastlines nor plates
    });
}
