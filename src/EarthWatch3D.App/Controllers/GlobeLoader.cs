using EarthWatch3D.App.Layers;
using GlobeCore.Earth;
using GlobeCore.Imaging;
using GlobeCore.Wpf.Rendering;
#if PERF_LOG
using System.Diagnostics;
using EarthWatch3D.App.Diagnostics;
#endif

namespace EarthWatch3D.App.Controllers;

/// <summary>The globe's start-up loads (moved from SceneController.OnLoaded): the Blue Marble and the coastlines and borders.</summary>
internal static class GlobeLoader
{
    /// <summary>
    /// Decodes the Blue Marble at <paramref name="width"/> and reads the Natural Earth lines in parallel on the pool,
    /// then rasterizes the lines at the image's size on <paramref name="sta"/>.
    /// </summary>
    public static async Task<(Bgra32Image BaseImage, SparseOverlay CoastAndBorders)> LoadAsync(int width, StaWorker sta)
    {
#if PERF_LOG
        var load = Stopwatch.StartNew();
#endif
        var decodeTask = Task.Run(() => EarthAssets.LoadBlueMarble(width));
        var linesTask = Task.Run(() => (Coast: NaturalEarthLines.Coastlines(),
                                        Borders: NaturalEarthLines.LandBoundaries()));
        await Task.WhenAll(decodeTask, linesTask);   // observes both exceptions
        var baseImage = decodeTask.Result;
        var (coast, borders) = linesTask.Result;
        int w = baseImage.Width, h = baseImage.Height;
#if PERF_LOG
        PerfLog.Write($"load: decode + GeoJSON {load.ElapsedMilliseconds} ms ({w}x{h}) {PerfLog.Memory()}");
        load.Restart();
#endif
        var lines = await sta.InvokeAsync(() => LinesImageLayer.RasterizeCoastAndBorders(coast, borders, w, h));
#if PERF_LOG
        PerfLog.Write(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"load: rasterize + sparse {load.ElapsedMilliseconds} ms ({lines.Count} line pixels, {100.0 * lines.Count / ((double)w * h):F2}%) {PerfLog.Memory()}"));
#endif
        return (baseImage, lines);
    }
}
