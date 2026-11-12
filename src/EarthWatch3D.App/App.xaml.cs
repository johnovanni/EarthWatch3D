using System.Windows;
using System.Windows.Threading;
using EarthWatch3D.App.Diagnostics;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Feeds.Eonet;
using EarthWatch3D.Core.Feeds.Gdacs;
using EarthWatch3D.Core.Feeds.Nhc;
using EarthWatch3D.Core.Feeds.Nws;
using EarthWatch3D.Core.Feeds.Usgs;
using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Storage;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.App;

/// <summary>
/// Composition root: data folders, log, settings, event store and cache, the feeds and their scheduler, the view
/// models and the window. Shuts them down in order when the main window closes or the app exits.
/// </summary>
public partial class App : Application
{
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(1);

    private FileLog? _log;
    private SettingsSaver? _settings;
    private FeedHttp? _http;
    private FeedScheduler? _scheduler;
    private EventCacheWriter? _cacheWriter;
    private Timer? _pruneTimer;
    private bool _shutDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var options = LaunchOptions.Parse(e.Args);
        // --snapshot-recompose moves the app's time forward part-way through, so the display runs on an adjustable
        // clock. Everything else (log, store retention, feeds) always uses the real time.
        OffsetClock? offsetClock = options.SnapshotRecompose ? new OffsetClock(new SystemClock()) : null;
        IClock clock = offsetClock is null ? new SystemClock() : offsetClock;
        IClock wall = new SystemClock();

        var paths = AppPaths.Default;
        paths.EnsureCreated();
        var log = _log = new FileLog(paths.LogsDir, wall);
        log.Info($"EarthWatch3D {AppInfo.Version} starting");
        // Saves are debounced and written off the UI thread (a failed write can sleep while it retries).
        _settings = new SettingsSaver(new SettingsStore(paths, log));

        var store = new EventStore(wall);
        var cache = new EventCache(paths, log);
        if (cache.Load() is { } cached)
        {
            store.Load(cached);
            store.Prune();   // the cache may be days old
            var s = store.Snapshot;
            log.Info($"Event cache loaded: {s.Earthquakes.Count} quakes, {s.Cyclones.Count} advisories, {s.Alerts.Count} alerts, {s.Eonet.Count} EONET storms");
        }

        var vm = new MainViewModel(clock, _settings);
        var http = _http = new FeedHttp();
        var zones = new ZoneCache(http, paths, wall, log);
        IEventFeed[] feeds =
        [
            new UsgsFeed(http, store, wall, log),
            new GdacsFeed(http, store, wall, log),
            new NhcFeed(http, store, log),
            new NwsFeed(http, store, zones, () => vm.CurrentFilter.AlertTypes, wall, log),
            new EonetFeed(http, store, log),
        ];
        var scheduler = _scheduler = new FeedScheduler(feeds, wall, log);
        foreach (var status in scheduler.Statuses) vm.Status.Apply(status);   // grey dots until each first run
        scheduler.StatusChanged += (_, status) => vm.Status.Apply(status);   // background thread: Apply uses BeginInvoke

        _cacheWriter = new EventCacheWriter(store, s => cache.Save(s));
        _pruneTimer = new Timer(_ => PruneStore(store, log), null, PruneInterval, PruneInterval);

        vm.Tick();   // show real values immediately
        var window = new MainWindow(vm, store, zones);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => vm.Tick();   // also drives the globe's per-minute recompose
        window.Closed += (_, _) =>
        {
            timer.Stop();
            ShutDownServices();
        };
        timer.Start();
        scheduler.Start();

        if (options.Any) _ = SnapshotDiagnostic.RunAsync(window, window.Scene, vm, options, offsetClock);
#if PERF_LOG
        PerfRun.Start(window.Scene, e.Args);
#endif
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ShutDownServices();
        base.OnExit(e);
    }

    private static void PruneStore(EventStore store, IAppLog log)
    {
        try { store.Prune(); }
        catch (Exception ex) { log.Warn("Event store prune failed", ex); }
    }

    /// <summary>Stops the feeds, saves the event cache and settings once, then disposes. Runs once.</summary>
    private void ShutDownServices()
    {
        if (_shutDown) return;
        _shutDown = true;
        try
        {
            _scheduler?.Stop();   // waits (up to 5 s) for running feeds to finish or cancel
            _pruneTimer?.Dispose();
            _cacheWriter?.Dispose();
            _cacheWriter?.SaveNow();
            _settings?.Flush();
            _settings?.Dispose();
            _http?.Dispose();
            _log?.Info("EarthWatch3D exiting");
        }
        catch (Exception ex) { _log?.Error("Shutdown failed", ex); }
        finally { _log?.Dispose(); }
    }
}
