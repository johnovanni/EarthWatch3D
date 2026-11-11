using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EarthWatch3D.App.Controllers;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.App.Diagnostics;

/// <summary>
/// Runs the <see cref="LaunchOptions"/> diagnostics: optional Face Sun, then an optional PNG snapshot and exit.
/// With <c>--snapshot-recompose</c> it saves two snapshots, <c>-a</c> and <c>-b</c>: the second after moving the clock
/// 6 hours ahead and waiting for that recompose to be displayed, which shows a later recompose reaches the screen.
/// </summary>
public static class SnapshotDiagnostic
{
    public static readonly TimeSpan RecomposeOffset = TimeSpan.FromHours(6);

    /// <param name="offsetClock">The app's clock when <see cref="LaunchOptions.SnapshotRecompose"/> is set, otherwise null.</param>
    public static async Task RunAsync(Window window, SceneController scene, MainViewModel vm, LaunchOptions options,
                                      OffsetClock? offsetClock = null)
    {
        int exitCode = 0;
        try
        {
            await scene.FirstComposeDisplayed;
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
                await RecomposeDisplayedAsync(scene, offsetClock.UtcNow);
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
