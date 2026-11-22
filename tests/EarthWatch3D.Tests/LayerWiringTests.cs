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
using GlobeCore.Earth;
using GlobeCore.Feeds;
using GlobeCore.Geo;
using GlobeCore.Imaging;
using GlobeCore.Regions;
using GlobeCore.Storage;
using GlobeCore.Wpf.Layers;
using HelixToolkit.Wpf;
using Path = System.Windows.Shapes.Path;

namespace EarthWatch3D.Tests;

/// <summary>The LayerHost and layers that SceneController builds, in the order that keeps today's drawing order.</summary>
public class LayerWiringTests
{
    private static readonly DateTime T0 = new(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);

    private sealed record Rig(SceneController Scene, MainViewModel Vm, HelixViewport3D Viewport, Canvas Overlay, CurrentBoundaries Boundaries);

    // The bundled boundary set with the app's rules, parsed once (the countries file is 3 MB).
    private static readonly Lazy<BoundarySet> BundledBorders = new(() =>
    {
        using var s = System.IO.File.OpenRead(System.IO.Path.Combine(AppContext.BaseDirectory, "data", "boundary-rules.json"));
        return NaturalEarthBoundaries.LoadBundled(BoundaryRules.Parse(s));
    });

    /// <summary>
    /// A SceneController in a window that is never shown (laid out by hand at 800x600, the camera facing 0°N 0°E, where
    /// an M6 quake is), not started.
    /// </summary>
    private static Rig NewScene()
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
        var boundaries = new CurrentBoundaries(BundledBorders.Value);
        var scene = new SceneController(viewport, overlay, card, vm, window, store, zones, lightning, new ViewState(),
                                        Task.FromResult(boundaries), new RecordingLog());
        return new Rig(scene, vm, viewport, overlay, boundaries);
    }

    /// <summary><see cref="NewScene"/>, started on a small base image as OnLoaded does after decoding.</summary>
    private static async Task<Rig> StartAsync()
    {
        var rig = NewScene();
        var scene = rig.Scene;
        const int w = 64, h = 32;
        var baseImage = Bgra32Image.Create(w, h);
        Array.Fill(baseImage.Pixels, (byte)200);
        var lineImage = Bgra32Image.Create(w, h);
        for (int x = 0; x < w; x++) lineImage.Pixels[(h / 2 * w + x) * 4 + 3] = 255;   // one opaque row: the "coastline"
        await scene.StartHostAsync(baseImage, SparseOverlay.FromImage(lineImage), rig.Boundaries.Set);
        await StaAsync.WaitUntil(() => !SnapshotDiagnostic.IsBusy(scene));
        return rig;
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
        host.Dispose();
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
        host.Dispose();
    });

    [Fact]
    public void SwapRebuildsLinesLayer() => StaAsync.Run(async () =>
    {
        var rig = await StartAsync();
        var host = rig.Scene.Host!;
        var lines = host.Layers.OfType<LinesImageLayer>().Single();
        var old = rig.Boundaries.Set;
        Assert.Same(old, lines.Borders);
        Assert.Equal(BoundariesTests.BundledLegend, rig.Vm.Layers.BoundariesLegend);
        var before = lines.Current;
        Assert.NotNull(before);
        int composes = 0;
        host.Composed += (_, _) => composes++;

        // A newer edition with one more line, along 10°N from 0° to 60°E. The feed swaps it in on its own thread.
        var next = old with
        {
            Edition = "Natural Earth 9.9.9 (2030-01-01)", Tag = "v9.9.9",
            Lines = [.. old.Lines, new GeoLine([new GeoPosition(10, 0), new GeoPosition(10, 30), new GeoPosition(10, 60)])],
        };
        await Task.Run(() => rig.Boundaries.Swap(next));

        // The lines are rasterized again off the UI thread, then the globe is recomposed with them.
        await StaAsync.WaitUntil(() => lines.Borders == next && composes > 0 && host.IsComposeIdle, 15_000);
        Assert.NotSame(before, lines.Current);
        var coast = NaturalEarthLines.Coastlines();
        int w = lines.CoastAndBorders.Width, h = lines.CoastAndBorders.Height;
        var expected = LinesImageLayer.RasterizeCoastAndBorders(coast, NaturalEarthLines.LandBoundaries(next), w, h);
        var withoutTheNewLine = LinesImageLayer.RasterizeCoastAndBorders(coast, NaturalEarthLines.LandBoundaries(old), w, h);
        Assert.Equal(expected.Count, lines.CoastAndBorders.Count);
        Assert.True(expected.Count > withoutTheNewLine.Count, "the new line is drawn");
        Assert.Equal("Borders: Natural Earth 9.9.9 (2030-01-01); Crimea shown as part of Ukraine (UN General Assembly resolution 68/262)",
            rig.Vm.Layers.BoundariesLegend);

        // Hidden, a swap still rebuilds (so showing it again is current) but needs no compose.
        rig.Vm.Layers.ShowCoastlines = false;
        await StaAsync.WaitUntil(() => host.IsComposeIdle);
        await Task.Delay(400);
        composes = 0;
        await Task.Run(() => rig.Boundaries.Swap(old));
        await StaAsync.WaitUntil(() => lines.Borders == old, 15_000);
        await Task.Delay(400);
        Assert.Equal(0, composes);
        host.Dispose();
    });

    [Fact]
    public void AFaultedFirstComposeIsNotAnUnobservedTaskException()
    {
        // A load failure (or a compose fault) faults FirstComposeDisplayed, which nothing awaits in a normal run. If it
        // went unobserved, the crash handler would show a second message after the "Globe load failed" one.
        string marker = "first compose fault " + Guid.NewGuid().ToString("N");
        int unobserved = 0;   // set on the finalizer thread
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            if (e.Exception.Flatten().InnerExceptions.Any(x => x.Message == marker)) Interlocked.Exchange(ref unobserved, 1);
        }
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            WeakReference? task = null;
            Sta.Run(() => task = FaultFirstComposeAndClose(marker));
            // Collect until the Task is gone, not for a fixed number of tries: under a loaded parallel test run a fixed
            // budget of about one second sometimes ran out first (seen 1 run in 3). A real leak still fails at the limit.
            var limit = System.Diagnostics.Stopwatch.StartNew();
            while (task!.IsAlive && limit.Elapsed < TimeSpan.FromSeconds(15))
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                Thread.Sleep(20);
            }
            // One more round so the finalizer that would raise UnobservedTaskException has certainly run.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            Assert.False(task!.IsAlive, $"the faulted task was not collected within {limit.Elapsed.TotalSeconds:0.0} s");
            Assert.True(Volatile.Read(ref unobserved) == 0, "the fault reached TaskScheduler.UnobservedTaskException");
        }
        finally { TaskScheduler.UnobservedTaskException -= OnUnobserved; }
    }

    // Faults the scene's first-compose Task as a load failure does (OnLoaded's catch), then closes the window's scene,
    // and keeps only a weak reference, so the Task can be collected.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference FaultFirstComposeAndClose(string marker)
    {
        var scene = NewScene().Scene;
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var tcs = (TaskCompletionSource)typeof(SceneController).GetField("_firstCompose", flags)!.GetValue(scene)!;
        Assert.True(tcs.TrySetException(new InvalidOperationException(marker)));
        typeof(SceneController).GetMethod("OnClosed", flags)!.Invoke(scene, [null, EventArgs.Empty]);
        return new WeakReference(scene.FirstComposeDisplayed);
    }
}
