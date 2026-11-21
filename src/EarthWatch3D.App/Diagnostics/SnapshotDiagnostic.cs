using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AstroCore.Earth;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EarthWatch3D.App.Controllers;
using EarthWatch3D.App.ViewModels;
using GlobeCore.Logging;
using GlobeCore.Time;

namespace EarthWatch3D.App.Diagnostics;

/// <summary>
/// One picture of a <c>--snapshot-scenes</c> run. <paramref name="LookAt"/> null and <paramref name="FaceSun"/> false means
/// the default home view. Layers not named are off.
/// </summary>
public sealed record SnapshotScene(string Name, DateTime DisplayTimeUtc, GeoPosition? LookAt, bool FaceSun, string[] Layers);

/// <summary>
/// Runs the <see cref="LaunchOptions"/> diagnostics: with offline samples, a wait for the data to be drawn; then an
/// optional Look At and Face Sun, then an optional PNG snapshot and exit.
/// With <c>--snapshot-recompose</c> it saves two snapshots, <c>-a</c> and <c>-b</c>: the second after moving the clock
/// 6 hours ahead and waiting for that recompose to be displayed, which shows a later recompose reaches the screen.
/// </summary>
public static class SnapshotDiagnostic
{
    public static readonly TimeSpan RecomposeOffset = TimeSpan.FromHours(6);
    public static readonly TimeSpan DataTimeout = TimeSpan.FromSeconds(20);

    /// <param name="offsetClock">The app's clock when <see cref="LaunchOptions.SnapshotRecompose"/> is set, otherwise null.</param>
    /// <param name="waitForData">Offline samples only: completes when every feed has reported OK once and the zone
    /// downloads are done. Null to take the snapshot without waiting for data.</param>
    public static async Task RunAsync(Window window, SceneController scene, MainViewModel vm, LaunchOptions options,
                                      OffsetClock? offsetClock = null, Func<Task>? waitForData = null, IAppLog? log = null,
                                      Func<string, Window>? showDialog = null, IReadOnlyList<SnapshotScene>? scenes = null,
                                      SettableClock? sceneClock = null)
    {
        if (scenes is not null && sceneClock is not null && options.SnapshotOutDir is { } outDir)
        {
            await RunScenesAsync(window, scene, vm, scenes, sceneClock, outDir, options.DelayMs, waitForData, log ?? new NullLog());
            return;
        }
        int exitCode = 0;
        try
        {
            await scene.FirstComposeDisplayed;
            if (waitForData is not null) await WaitForDataAsync(scene, waitForData, DataTimeout, log ?? new NullLog());
            if (options.LookAt is { } lookAt) scene.LookAt(lookAt);
            if (options.FaceSun)
            {
                vm.SunMoon.FaceSunCommand.Execute(null);
                while (scene.IsCameraAnimating) await Task.Delay(50);
            }
            // --dialog: the dialog is opened (not modal, so this continues) and, with --snapshot, it alone is saved.
            Window? dialog = options.Dialog is { } name ? showDialog?.Invoke(name) : null;
            if (options.SnapshotPath is null) return;
            await Task.Delay(options.DelayMs);
            if (dialog is not null)
            {
                dialog.UpdateLayout();
                Save(dialog, options.SnapshotPath);
                dialog.Close();
            }
            else if (options.SnapshotRecompose && offsetClock != null)
            {
                Save(window, LaunchOptions.WithSuffix(options.SnapshotPath, "a"));
                offsetClock.Offset += RecomposeOffset;
                vm.Tick();   // the card and markers follow the new time too
                await RecomposeDisplayedAsync(scene, vm.DisplayTimeUtc);   // live display time, now 6 h ahead
                await Task.Delay(options.DelayMs);
                Save(window, LaunchOptions.WithSuffix(options.SnapshotPath, "b"));
            }
            else Save(window, options.SnapshotPath);
        }
        catch (OperationCanceledException) { return; }   // the window closed first
        catch (Exception ex)
        {
            Trace.TraceError($"Snapshot failed: {ex}");
            exitCode = 1;
        }
        if (options.SnapshotPath != null) Application.Current.Shutdown(exitCode);
    }

    /// <summary>
    /// The <c>--snapshot-scenes</c> run: for each scene, moves <paramref name="sceneClock"/> (the display clock; the feeds' clock stays put, so they do not run again;
    /// it otherwise stands still) to its display time, sets exactly its layers and camera, waits as the single --snapshot run does (every feed
    /// reported once, the scene settled, a compose for that time displayed) and saves the scene's PNG in
    /// <paramref name="outDir"/>. Exits with 0, or 1 if any scene failed.
    /// </summary>
    private static async Task RunScenesAsync(Window window, SceneController scene, MainViewModel vm,
                                             IReadOnlyList<SnapshotScene> scenes, SettableClock sceneClock, string outDir,
                                             int delayMs, Func<Task>? waitForData, IAppLog log)
    {
        int exitCode = 0;
        try
        {
            scene.FrozenRingTime = TimeSpan.Zero;   // the pulsing rings' phase is fixed, not read from the running stopwatch
            await scene.FirstComposeDisplayed;
            foreach (var s in scenes)
            {
                try
                {
                    sceneClock.Now = s.DisplayTimeUtc;   // stands still until the next scene
                    var layers = vm.Layers;
                    bool On(string n) => s.Layers.Contains(n, StringComparer.OrdinalIgnoreCase);
                    layers.ShowQuakes = On("quakes"); layers.ShowCyclones = On("cyclones"); layers.ShowAlerts = On("alerts");
                    layers.ShowEonet = On("eonet"); layers.ShowCoastlines = On("coastlines"); layers.ShowPlates = On("plates");
                    layers.ShowLightning = On("lightning"); layers.ShowClouds = On("clouds");
                    vm.Tick();   // the display time follows the clock
                    if (s.LookAt is { } at) scene.LookAt(at);
                    else if (s.FaceSun)
                    {
                        vm.SunMoon.FaceSunCommand.Execute(null);
                        while (scene.IsCameraAnimating) await Task.Delay(50);
                    }
                    else scene.LookAtHome();
                    await RecomposeDisplayedAsync(scene, vm.DisplayTimeUtc);
                    await WaitForDataAsync(scene, waitForData ?? (() => Task.CompletedTask), DataTimeout, log);
                    await Task.Delay(delayMs);
                    Save(window, Path.Combine(outDir, s.Name + ".png"));
                    log.Info($"Diagnostics: saved scene {s.Name}");
                }
                catch (OperationCanceledException) { return; }   // the window closed first
                catch (Exception ex)
                {
                    Trace.TraceError($"Scene {s.Name} failed: {ex}");
                    log.Error($"Diagnostics: scene {s.Name} failed", ex);
                    exitCode = 1;
                }
            }
        }
        catch (OperationCanceledException) { return; }
        Application.Current.Shutdown(exitCode);
    }

    /// <summary>
    /// Reads a scenes file: <c>{"scenes":[{"name":"default","displayTimeUtc":"2026-11-11T20:00:00Z","lookAt":[10,-165],
    /// "faceSun":false,"layers":["quakes","coastlines"]}]}</c>. <c>lookAt</c>, <c>faceSun</c> and <c>layers</c> are optional.
    /// Throws <see cref="FormatException"/> with a plain message when the file is not usable.
    /// </summary>
    public static IReadOnlyList<SnapshotScene> LoadScenes(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var list = new List<SnapshotScene>();
            foreach (var e in doc.RootElement.GetProperty("scenes").EnumerateArray())
            {
                string name = e.GetProperty("name").GetString() ?? "";
                if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new FormatException($"'{name}' is not usable as a picture name.");
                if (list.Any(x => x.Name == name)) throw new FormatException($"The scene name '{name}' is used twice.");
                string time = e.GetProperty("displayTimeUtc").GetString() ?? "";
                if (!LaunchOptions.TryParseUtc(time, out var utc))
                    throw new FormatException($"Scene {name}: '{time}' is not a UTC time such as {LaunchOptions.DisplayTimeExample}.");
                GeoPosition? lookAt = null;
                if (e.TryGetProperty("lookAt", out var la) && la.ValueKind == JsonValueKind.Array)
                {
                    double lat = la[0].GetDouble(), lon = la[1].GetDouble();
                    if (!(Math.Abs(lat) <= 90) || !(Math.Abs(lon) <= 180)) throw new FormatException($"Scene {name}: lookAt is out of range.");
                    lookAt = new GeoPosition(lat, lon);
                }
                bool faceSun = e.TryGetProperty("faceSun", out var fs) && fs.GetBoolean();
                var layers = e.TryGetProperty("layers", out var ls)
                    ? ls.EnumerateArray().Select(x => (x.GetString() ?? "").ToLowerInvariant()).ToArray() : [];
                if (layers.FirstOrDefault(l => !LaunchOptions.LayerNames.Contains(l)) is { } bad)
                    throw new FormatException($"Scene {name}: '{bad}' is not a layer. Use any of {string.Join(", ", LaunchOptions.LayerNames)}.");
                list.Add(new SnapshotScene(name, utc, lookAt, faceSun, layers));
            }
            if (list.Count == 0) throw new FormatException("The file has no scenes.");
            return list;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IOException or IndexOutOfRangeException)
        {
            throw new FormatException($"--snapshot-scenes: cannot read {path}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Waits, at most <paramref name="timeout"/> in all, for <paramref name="waitForData"/>, then for the scene to draw
    /// it: no compose or Areas rebuild running or queued, and the current Areas layer on screen (if anything visible has
    /// an area: a visible set with no alert, cone or wind area has nothing to wait for). A timeout is
    /// logged, not thrown, so the snapshot still shows whatever did arrive.
    /// </summary>
    public static async Task WaitForDataAsync(SceneController scene, Func<Task> waitForData, TimeSpan timeout, IAppLog log)
    {
        var sw = Stopwatch.StartNew();
        try { await waitForData().WaitAsync(timeout); }
        catch (TimeoutException) { log.Warn($"Diagnostics: not every feed reported OK within {timeout.TotalSeconds:0} s"); }
        while (scene.IsBusy || !scene.AreasSettled)
        {
            if (sw.Elapsed >= timeout)
            {
                log.Warn($"Diagnostics: the scene did not settle within {timeout.TotalSeconds:0} s " +
                         $"(busy={scene.IsBusy}, areas settled={scene.AreasSettled})");
                return;
            }
            await Task.Delay(100);
        }
        log.Info($"Diagnostics: data drawn after {sw.Elapsed.TotalSeconds:F1} s (areas shown={scene.AreasDisplayed})");
    }

    /// <summary>Asks for a recompose and completes when a compose for <paramref name="notBefore"/> or later is on the globe.</summary>
    private static async Task RecomposeDisplayedAsync(SceneController scene, DateTime notBefore)
    {
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnDisplayed(object? sender, DateTime utc) { if (utc >= notBefore) shown.TrySetResult(); }
        scene.ComposeDisplayed += OnDisplayed;
        try
        {
            scene.Recompose();   // if a compose is running, this queues one more, which reads the new time
            await shown.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { scene.ComposeDisplayed -= OnDisplayed; }
    }

    /// <summary>Renders the window's client area at the window's DPI and saves it as a PNG.</summary>
    public static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(window);
        int pw = (int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX);
        int ph = (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY);
        var rtb = new RenderTargetBitmap(pw, ph, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var stream = File.Create(full);
        encoder.Save(stream);
    }
}
