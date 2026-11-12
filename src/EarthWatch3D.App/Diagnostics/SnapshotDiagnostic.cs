using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EarthWatch3D.App.Controllers;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.App.Diagnostics;

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
                                      OffsetClock? offsetClock = null, Func<Task>? waitForData = null, IAppLog? log = null)
    {
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
            if (options.SnapshotPath is null) return;
            await Task.Delay(options.DelayMs);
            if (options.SnapshotRecompose && offsetClock != null)
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
    /// Waits, at most <paramref name="timeout"/> in all, for <paramref name="waitForData"/>, then for the scene to draw
    /// it: no compose or Areas rebuild running or queued, and a globe image with an Areas layer on screen. A timeout is
    /// logged, not thrown, so the snapshot still shows whatever did arrive.
    /// </summary>
    public static async Task WaitForDataAsync(SceneController scene, Func<Task> waitForData, TimeSpan timeout, IAppLog log)
    {
        var sw = Stopwatch.StartNew();
        try { await waitForData().WaitAsync(timeout); }
        catch (TimeoutException) { log.Warn($"Diagnostics: not every feed reported OK within {timeout.TotalSeconds:0} s"); }
        while (scene.IsBusy || !scene.AreasDisplayed)
        {
            if (sw.Elapsed >= timeout)
            {
                log.Warn($"Diagnostics: the scene did not settle with areas shown within {timeout.TotalSeconds:0} s " +
                         $"(busy={scene.IsBusy}, areas shown={scene.AreasDisplayed})");
                return;
            }
            await Task.Delay(100);
        }
        log.Info($"Diagnostics: data drawn after {sw.Elapsed.TotalSeconds:F1} s");
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
