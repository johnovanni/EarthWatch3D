using EarthWatch3D.App.Layers;
using GlobeCore.Earth;
using GlobeCore.Imaging;
using GlobeCore.Logging;
using GlobeCore.Regions;
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
    /// Decodes the Blue Marble at <paramref name="width"/> and reads the Natural Earth coastlines in parallel on the pool,
    /// with the borders of the boundary set in use once <paramref name="boundaries"/> has loaded, then rasterizes the
    /// lines at the image's size on <paramref name="sta"/>. If the boundaries could not be loaded, that is logged and the
    /// coastlines are drawn without borders (<c>Borders</c> is then null; the "Borders" dot shows the reason).
    /// </summary>
    public static async Task<(Bgra32Image BaseImage, SparseOverlay CoastAndBorders, BoundarySet? Borders)> LoadAsync(
        int width, StaWorker sta, Task<CurrentBoundaries> boundaries, IAppLog log)
    {
#if PERF_LOG
        var load = Stopwatch.StartNew();
#endif
        var decodeTask = Task.Run(() =>
        {
            using var s = EarthAssets.Open(EarthAssets.BlueMarbleFile);
            return ImageConversions.DecodeToBgra32(s, width);   // scales while decoding
        });
        var linesTask = Task.Run(async () =>
        {
            var coast = NaturalEarthLines.Coastlines();
            BoundarySet? set = null;
            try { set = (await boundaries.ConfigureAwait(false)).Set; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                log.Error("The country borders could not be loaded; drawing the coastlines without them", ex);
            }
            return (Coast: coast, Set: set, Borders: set is null ? [] : NaturalEarthLines.LandBoundaries(set));
        });
        await Task.WhenAll(decodeTask, linesTask);   // observes both exceptions
        var baseImage = decodeTask.Result;
        var (coast, set, borders) = linesTask.Result;
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
        return (baseImage, lines, set);
    }
}
