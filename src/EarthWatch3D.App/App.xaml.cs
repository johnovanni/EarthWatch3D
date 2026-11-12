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
/// Composition root: data folders, log, crash handler, settings, event store and cache, the feeds and their scheduler,
/// the view models and the window. Shuts them down in order when the main window closes or the app exits.
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
        // The app must still start without its data folders: settings fall back to defaults, the cache to empty,
        // and failed writes are logged (FileLog itself never throws). Logged once the log exists.
        Exception? foldersError = null;
        try { paths.EnsureCreated(); }
        catch (Exception ex) { foldersError = ex; }
        var log = _log = new FileLog(paths.LogsDir, wall);
        log.Info($"EarthWatch3D {AppInfo.Version} starting");
        // Shown on the UI thread: unobserved task faults arrive on the finalizer thread.
        new CrashHandler(log, message => Dispatcher.BeginInvoke(() =>
            MessageBox.Show(message + Environment.NewLine + Environment.NewLine + "Log folder: " + paths.LogsDir,
                "EarthWatch3D", MessageBoxButton.OK, MessageBoxImage.Warning))).Install(this);
        if (foldersError is not null) log.Error($"Could not create the data folders under {paths.Root}", foldersError);
        // Saves are debounced and written off the UI thread (a failed write can sleep while it retries).
        _settings = new SettingsSaver(new SettingsStore(paths, log), log: log);

        var cache = new EventCache(paths, log);
        var store = LoadCachedEvents(cache, paths, wall, log);

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

        _cacheWriter = new EventCacheWriter(store, s => cache.Save(s), log: log);
        _pruneTimer = new Timer(_ => PruneStore(store, log), null, PruneInterval, PruneInterval);

        vm.Tick();   // show real values immediately
        var window = new MainWindow(vm, store, zones, log);
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

    /// <summary>
    /// A store filled from the cache when it is present and usable. A cache that loads but then fails (bad contents
    /// in valid JSON) is logged and deleted, and the app starts with an empty store.
    /// </summary>
    public static EventStore LoadCachedEvents(EventCache cache, AppPaths paths, IClock wall, IAppLog log)
    {
        var store = new EventStore(wall);
        try
        {
            if (cache.Load() is not { } cached) return store;
            store.Load(cached);
            store.Prune();   // the cache may be days old
            var s = store.Snapshot;
            log.Info($"Event cache loaded: {s.Earthquakes.Count} quakes, {s.Cyclones.Count} advisories, {s.Alerts.Count} alerts, {s.Eonet.Count} EONET storms");
            return store;
        }
        catch (Exception ex)
        {
            log.Warn("Event cache unusable; deleting it and starting empty", ex);
            try { System.IO.File.Delete(paths.EventsFile); } catch (Exception) { }
            return new EventStore(wall);   // the first store may hold part of the bad snapshot
        }
    }

    private static void PruneStore(EventStore store, IAppLog log)
    {
        try { store.Prune(); }
        catch (Exception ex) { log.Warn("Event store prune failed", ex); }
    }

    /// <summary>
    /// Stops the feeds, saves the event cache and settings once, then disposes. Runs once. Each step has its own
    /// try/catch, so one failure (a slow or throwing Stop, say) doesn't skip the saves after it.
    /// </summary>
    private void ShutDownServices()
    {
        if (_shutDown) return;
        _shutDown = true;
        Step("stop the feed scheduler", () => _scheduler?.Stop());   // waits (up to 5 s) for running feeds
        Step("stop the prune timer", () => _pruneTimer?.Dispose());
        Step("stop the cache writer", () => _cacheWriter?.Dispose());
        Step("save the event cache", () => _cacheWriter?.SaveNow());
        Step("save settings", () => _settings?.Flush());
        Step("stop the settings saver", () => _settings?.Dispose());
        Step("dispose the HTTP client", () => _http?.Dispose());
        _log?.Info("EarthWatch3D exiting");
        _log?.Dispose();
    }

    private void Step(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { _log?.Error("Shutdown: could not " + what, ex); }
    }
}
