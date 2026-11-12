#if PERF_LOG
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using EarthWatch3D.App.Controllers;

namespace EarthWatch3D.App.Diagnostics;

/// <summary>
/// PERF_LOG builds only. After load (and, with offline samples, once their events are drawn): working set at 10 s, the optional <c>--bench</c> rotation (360° about +Z over 5 s,
/// average FPS from CompositionTarget.Rendering frames) and a forced-GC experiment.
/// Every compose is logged by the SceneController, and every event cache save by the App. Once a minute for the whole
/// run: memory, thread count, events.json size, alert count and how many of the alerts' zones are loaded.
/// </summary>
internal static class PerfRun
{
    /// <param name="waitForData">Offline samples only: the bench waits for the events to be drawn first (see
    /// <see cref="SnapshotDiagnostic.WaitForDataAsync"/>), so it measures the globe with events on it.</param>
    public static void Start(SceneController scene, string[] args, Func<Task>? waitForData = null)
    {
        _ = RunAsync(scene, args.Contains("--bench"), waitForData);
        _ = MinuteLogAsync(scene);
    }

    private static async Task MinuteLogAsync(SceneController scene)
    {
        try
        {
            var store = scene.Store;
            var zones = scene.Zones;
            int zonesLoadedEvents = 0;
            zones.ZonesLoaded += (_, _) => Interlocked.Increment(ref zonesLoadedEvents);
            var eventsFile = Core.Storage.AppPaths.Default.EventsFile;
            var zonesDir = Core.Storage.AppPaths.Default.ZonesDir;
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            for (int minute = 1; await timer.WaitForNextTickAsync(); minute++)
            {
                using var p = Process.GetCurrentProcess();
                long cacheBytes = File.Exists(eventsFile) ? new FileInfo(eventsFile).Length : -1;
                var s = store.Snapshot;
                var zoneIds = s.Alerts.SelectMany(a => a.ZoneIds).Distinct(StringComparer.Ordinal).ToList();
                int inMemory = zoneIds.Count(z => zones.TryGetCached(z, out _));
                int zoneFiles = Directory.Exists(zonesDir) ? Directory.EnumerateFiles(zonesDir, "*.json").Count() : 0;
                PerfLog.Write(string.Create(CultureInfo.InvariantCulture,
                    $"minute {minute}: {PerfLog.Memory()} threads={p.Threads.Count} handles={p.HandleCount} events.json={cacheBytes / 1024.0:F0} KB " +
                    $"quakes={s.Earthquakes.Count} advisories={s.Cyclones.Count} alerts={s.Alerts.Count} eonet={s.Eonet.Count} " +
                    $"alert zones loaded={inMemory}/{zoneIds.Count} zone files={zoneFiles} ZonesLoaded events={Volatile.Read(ref zonesLoadedEvents)}"));
            }
        }
        catch (Exception ex) { PerfLog.Write("minute log failed: " + ex); }
    }

    private static async Task RunAsync(SceneController scene, bool bench, Func<Task>? waitForData)
    {
        try
        {
            PerfLog.Write($"start: {Environment.ProcessorCount} logical CPUs, {RuntimeInformation.OSDescription}, " +
                          $"{RuntimeInformation.FrameworkDescription}, GlobeImageWidth={SceneController.GlobeImageWidth}, " +
                          $"server GC={GCSettings.IsServerGC}, render tier={RenderCapability.Tier >> 16}, bench={bench}");
            await scene.FirstComposeDisplayed;
            PerfLog.Write("first compose displayed: " + PerfLog.Memory());
            if (waitForData is not null)
            {
                await SnapshotDiagnostic.WaitForDataAsync(scene, waitForData, SnapshotDiagnostic.DataTimeout, new Core.Logging.NullLog());
                PerfLog.Write($"offline data drawn (areas shown={scene.AreasDisplayed}): " + PerfLog.Memory());
            }
            await Task.Delay(10_000);
            PerfLog.Write("10 s after load: " + PerfLog.Memory());

            if (bench && scene.Camera is { } camera)
            {
                var (frames, seconds) = await RotateAsync(camera, TimeSpan.FromSeconds(5));
                PerfLog.Write(string.Create(CultureInfo.InvariantCulture,
                    $"bench: {frames} frames in {seconds:F2} s = {frames / seconds:F1} fps (360° about +Z) {PerfLog.Memory()}"));
            }

            // How much of the working set is garbage that a full collection would return?
            await Task.Delay(1000);
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(1000);
            PerfLog.Write("after forced full GC (experiment): " + PerfLog.Memory());
        }
        catch (Exception ex) { PerfLog.Write("perf run failed: " + ex); }
    }

    private static Task<(int Frames, double Seconds)> RotateAsync(PerspectiveCamera camera, TimeSpan duration)
    {
        var tcs = new TaskCompletionSource<(int, double)>();
        var p0 = camera.Position; var l0 = camera.LookDirection; var u0 = camera.UpDirection;
        var sw = Stopwatch.StartNew();
        var lastFrame = TimeSpan.MinValue;
        int frames = 0;
        EventHandler? onRendering = null;
        onRendering = (_, e) =>
        {
            var rt = ((RenderingEventArgs)e).RenderingTime;
            if (rt == lastFrame) return;   // Rendering can fire more than once per frame
            lastFrame = rt;
            frames++;
            double t = Math.Min(1, sw.Elapsed.TotalSeconds / duration.TotalSeconds);
            var m = Matrix3D.Identity;
            m.Rotate(new Quaternion(new Vector3D(0, 0, 1), 360 * t));
            camera.Position = m.Transform(p0); camera.LookDirection = m.Transform(l0); camera.UpDirection = m.Transform(u0);
            if (t < 1) return;
            CompositionTarget.Rendering -= onRendering;
            tcs.SetResult((frames, sw.Elapsed.TotalSeconds));
        };
        CompositionTarget.Rendering += onRendering;
        return tcs.Task;
    }
}
#endif
