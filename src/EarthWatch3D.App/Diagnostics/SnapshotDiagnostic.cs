using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EarthWatch3D.App.Controllers;
using EarthWatch3D.App.ViewModels;

namespace EarthWatch3D.App.Diagnostics;

/// <summary>Runs the <see cref="LaunchOptions"/> diagnostics: optional Face Sun, then an optional PNG snapshot and exit.</summary>
public static class SnapshotDiagnostic
{
    public static async Task RunAsync(Window window, SceneController scene, MainViewModel vm, LaunchOptions options)
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
            Save(window, options.SnapshotPath);
        }
        catch (OperationCanceledException) { return; }   // the window closed first
        catch (Exception ex)
        {
            Trace.TraceError($"Snapshot failed: {ex}");
            exitCode = 1;
        }
        if (options.SnapshotPath != null) Application.Current.Shutdown(exitCode);
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
