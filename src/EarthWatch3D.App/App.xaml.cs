using System.IO;
using System.Windows;
using System.Windows.Threading;
using EarthWatch3D.App.Diagnostics;
using EarthWatch3D.App.Services;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Feeds.Eonet;
using EarthWatch3D.Core.Feeds.Gdacs;
using EarthWatch3D.Core.Feeds.Gibs;
using EarthWatch3D.Core.Feeds.Nhc;
using EarthWatch3D.Core.Feeds.Nws;
using EarthWatch3D.Core.Feeds.Usgs;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Globe;
using EarthWatch3D.Core.Lightning;
using EarthWatch3D.Core.Lightning.Glm;
using EarthWatch3D.Core.Lightning.Mtg;
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
    private DownloadMeter? _meter;
    private string? _offlineRoot;
    private CrashHandler? _crash;
    private bool _shutDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // The log comes first (it always uses the real time and the usual folder), so even a bad command line is logged.
        string logsDir = AppPaths.Default.LogsDir;
        var log = _log = new FileLog(logsDir, new SystemClock());
        log.Info($"EarthWatch3D {AppInfo.Version} starting" + (e.Args.Length > 0 ? " with " + string.Join(" ", e.Args) : ""));
        // Shown at once on the UI thread (after "too many errors" the process ends straight afterwards), otherwise
        // queued to it: unobserved task faults arrive on the finalizer thread.
        void ShowCrash(string message) => MessageBox.Show(message + Environment.NewLine + Environment.NewLine + "Log folder: " + logsDir,
            "EarthWatch3D", MessageBoxButton.OK, MessageBoxImage.Warning);
        _crash = new CrashHandler(log, message =>
        {
            if (Dispatcher.CheckAccess()) ShowCrash(message);
            else Dispatcher.BeginInvoke(() => ShowCrash(message));
        });
        _crash.Install(this);
        // Offline runs that were killed leave their temporary data folder behind.
        OfflineSamples.SweepStaleFolders(Path.GetTempPath(), DateTime.UtcNow, log);

        var options = LaunchOptions.Parse(e.Args);
        string? samplesDir = options.OfflineSamplesDir is { } dir ? Path.GetFullPath(dir) : null;
        string? optionsError = options.Error;
        if (samplesDir is not null && !Directory.Exists(samplesDir))
            optionsError = (optionsError is null ? "" : optionsError + Environment.NewLine) + $"--offline-samples: the folder {samplesDir} does not exist.";
        if (optionsError is not null)
        {
            log.Error("Command line: " + optionsError);
            MessageBox.Show(optionsError, "EarthWatch3D: command line", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        // With offline samples the app's clock starts at --display-time, so the samples (captured 2026-11-11) are
        // neither pruned by the store's retention nor treated as ended because of today's date; it then runs on in
        // real time. Otherwise the display runs on an adjustable clock only for --snapshot-recompose, which moves the
        // app's time forward part-way through. The log always uses the real time.
        var sampleOffset = samplesDir is not null && options.DisplayTimeUtc is { } start ? start - DateTime.UtcNow : TimeSpan.Zero;
        IClock wall = sampleOffset == TimeSpan.Zero ? new SystemClock() : new OffsetClock(new SystemClock()) { Offset = sampleOffset };
        OffsetClock? offsetClock = options.SnapshotRecompose ? new OffsetClock(new SystemClock()) { Offset = sampleOffset } : null;
        IClock clock = offsetClock is null ? wall : offsetClock;

        // Offline, the cache, zone files and settings live in a temporary folder (deleted on exit), so the user's own
        // cache and saved layer choices are not touched. The log still goes to the usual logs folder.
        var paths = AppPaths.Default;
        if (samplesDir is not null)
            paths = new AppPaths(_offlineRoot = Path.Combine(Path.GetTempPath(), OfflineSamples.TempFolderPrefix + Guid.NewGuid().ToString("N")));
        // The app must still start without its data folders: settings fall back to defaults, the cache to empty,
        // and failed writes are logged (FileLog itself never throws). Logged once the log exists.
        Exception? foldersError = null;
        try { paths.EnsureCreated(); }
        catch (Exception ex) { foldersError = ex; }
        if (foldersError is not null) log.Error($"Could not create the data folders under {paths.Root}", foldersError);
        if (samplesDir is not null)
            log.Info($"Offline samples from {samplesDir}; data folder {paths.Root}; clock moved by {sampleOffset.TotalHours:F2} h");
        // Saves are debounced and written off the UI thread (a failed write can sleep while it retries).
        _settings = new SettingsSaver(new SettingsStore(paths, log), log: log);

        var cache = new EventCache(paths, log);
        var store = LoadCachedEvents(cache, paths, wall, log);

        var http = _http = samplesDir is null ? new FeedHttp() : new FeedHttp(OfflineSamples.CreateHandler(samplesDir));
        // Today's download volume (wire bytes of every request), kept across restarts on the same local day. Offline it is
        // not saved to the user's file: the paths are the temporary folder's.
        var meter = _meter = new DownloadMeter(wall, TimeZoneInfo.Local, paths.DownloadMeterFile, log);
        meter.LimitBytes = AppSettings.DailyLimitBytes(_settings.Load().DailyLimitMb);   // out of range (hand-edited): no limit
        http.BytesReceived += meter.Add;
        // The user's own EUMETSAT key and secret: encrypted in the settings file, tested through the app's HTTP client.
        var eumetsat = new EumetsatClient(http, wall, log);
        var credentials = new EumetsatCredentialStore(_settings, new DpapiProtector());
        MainViewModel? main = null;
        MainWindow? mainWindow = null;
        // Everything that must change when Settings is saved; one place, so each setting is applied the same way.
        var savedCredentials = credentials.Current;
        void Applied(AppSettings saved)
        {
            if (main is { } m) m.Units = saved.Units;
            meter.LimitBytes = AppSettings.DailyLimitBytes(saved.DailyLimitMb);
            // A new, fixed or removed EUMETSAT key must not wait out the MTG feed's failure backoff.
            var nowSaved = credentials.Current;
            if (!Equals(nowSaved, savedCredentials))
            {
                savedCredentials = nowSaved;
                _scheduler?.RunSoon(StatusViewModel.MtgName);
            }
        }
        var dialogs = new DialogService(() => mainWindow, _settings, credentials, eumetsat.TestAsync, Applied,
            new ShellLinkOpener(), paths.Root);
        var vm = main = new MainViewModel(clock, _settings, Dispatcher, dialogs);
        var zones = new ZoneCache(http, paths, wall, log);
        // The feed runs on a pool thread, so it reads the toggle from a field that PropertyChanged keeps up to date.
        bool wantClouds = vm.Layers.ShowClouds;
        vm.Layers.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LayersViewModel.ShowClouds)) Volatile.Write(ref wantClouds, vm.Layers.ShowClouds);
        };
        // Same for lightning: wanted while the layer is on and the display is live (the feeds fill a live-only buffer), and
        // the window the buffer is kept for.
        bool wantLightning = vm.Layers.ShowLightning && vm.Timeline.IsLive;
        int lightningWindowMinutes = vm.Display.LightningWindowMinutes;
        void UpdateLightningWanted() => Volatile.Write(ref wantLightning, vm.Layers.ShowLightning && vm.Timeline.IsLive);
        vm.Layers.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LayersViewModel.ShowLightning)) UpdateLightningWanted();
        };
        vm.Timeline.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TimelineViewModel.IsLive)) UpdateLightningWanted();
        };
        vm.Display.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DisplayOptionsViewModel.LightningWindowMinutes))
                Volatile.Write(ref lightningWindowMinutes, vm.Display.LightningWindowMinutes);
        };
        var lightningBuffer = new LightningBuffer();
        // MTG signs in with the key saved in Settings, read on every run so a new key takes effect without a restart. Offline
        // runs use fixed dummy credentials (the sample handler answers any), never the user's stored ones.
        EumetsatCredentials? CurrentCredentials() => samplesDir is null ? credentials.Current : OfflineSamples.OfflineCredentials;
        MtgFeed? mtgFeed = null;
        var lightning = new LiveLightningSource(lightningBuffer, () => CurrentCredentials() is not null,
            () => mtgFeed?.State ?? MtgFeedState.Running);
        var view = new ViewState();
        var mtg = mtgFeed = new MtgFeed(eumetsat, lightningBuffer, wall, view, CurrentCredentials,
            () => TimeSpan.FromMinutes(Volatile.Read(ref lightningWindowMinutes)), () => meter.LimitReached,
            () => Volatile.Read(ref wantLightning), log);
        // GLM East leaves the flashes east of 37.6 W to MTG only once MTG has worked with the saved key and has not been refused since.
        GlmFeed Glm(LightningSensor sensor) => new(http, lightningBuffer, sensor, wall, view,
            () => TimeSpan.FromMinutes(Volatile.Read(ref lightningWindowMinutes)), () => meter.LimitReached,
            () => mtg.Active,
            () => Volatile.Read(ref wantLightning), log);
        var glmEast = Glm(LightningSensor.GoesEast);
        var clouds = new GibsCloudsFeed(http, paths, wall, () => Volatile.Read(ref wantClouds), log, DecodeForBlackCheck);
        IEventFeed[] feeds =
        [
            new UsgsFeed(http, store, wall, log),
            new GdacsFeed(http, store, wall, log),
            new NhcFeed(http, store, log),
            new NwsFeed(http, store, zones, () => vm.CurrentFilter.AlertTypes, wall, log),
            new EonetFeed(http, store, log),
            glmEast,
            Glm(LightningSensor.GoesWest),
            mtg,
            clouds,
        ];
        var scheduler = _scheduler = new FeedScheduler(feeds, wall, log);
        foreach (var status in scheduler.Statuses) vm.Status.Apply(status);   // grey dots until each first run
        scheduler.StatusChanged += (_, status) => vm.Status.Apply(status);   // background thread: Apply uses BeginInvoke
        // Offline, the diagnostics wait for the data: every feed has reported OK once and the zone downloads the NWS
        // feed asked for are done.
        Func<Task>? waitForData = null;
        if (samplesDir is not null)
        {
            // The clouds feed reports only when the layer is on (a skipped run reports nothing). --layers is applied later,
            // so it is read from the options here.
            // The same goes for GLM East. GLM West is left out: whether it runs depends on where the camera faces.
            var allOk = AllFeedsOkOnce(scheduler, feeds
                .Where(f => f != clouds || OfflineSamples.CloudsWanted(vm.Layers.ShowClouds, options.Layers))
                .Where(f => f != glmEast || OfflineSamples.LightningWanted(vm.Layers.ShowLightning, options.Layers))
                .Where(f => f is not GlmFeed || f == glmEast)
                .Where(f => f != mtg || OfflineSamples.LightningWanted(vm.Layers.ShowLightning, options.Layers))
                .Select(f => f.Name));
            waitForData = async () => { await allOk; await zones.WhenIdleAsync(); };
        }

#if PERF_LOG
        int cacheSaves = 0;
        _cacheWriter = new EventCacheWriter(store, s =>
        {
            bool ok = cache.Save(s);
            PerfLog.Write($"event cache save #{Interlocked.Increment(ref cacheSaves)} ok={ok}: {s.Earthquakes.Count} quakes, {s.Alerts.Count} alerts");
        }, log: log);
#else
        _cacheWriter = new EventCacheWriter(store, s => cache.Save(s), log: log);
#endif
        _pruneTimer = new Timer(_ => { PruneStore(store, log); meter.Save(); }, null, PruneInterval, PruneInterval);

        // Offline, the readout says SAMPLE instead of LIVE (and the window title says so), so sample data can never be
        // taken for now. With --display-time the clock itself starts at that time, so the display starts "live" on the
        // sample clock (SAMPLE 11 Nov 20:00 UTC) rather than in replay.
        vm.Timeline.IsSampleData = samplesDir is not null;
        if (samplesDir is null && options.DisplayTimeUtc is { } replayAt)
        {
            vm.Timeline.ReplayAt(replayAt);
            if (vm.DisplayTimeUtc != replayAt)
                log.Warn($"--display-time {replayAt:yyyy-MM-ddTHH:mm:ssZ} is outside the last 7 days; showing {vm.DisplayTimeUtc:yyyy-MM-ddTHH:mm:ssZ}");
        }
        if (options.Layers is { } extraLayers) vm.Layers.TurnOnForThisRun(extraLayers);   // --layers: not saved
        Volatile.Write(ref wantClouds, vm.Layers.ShowClouds);
        UpdateLightningWanted();
        vm.Tick();   // show real values immediately
        // The scene reports the camera's sub-point in the view state, which the GLM feeds read.
        var window = mainWindow = new MainWindow(vm, store, zones, lightning, view, log);
        if (samplesDir is not null) window.Title += " — SAMPLE DATA (offline)";
        // Raised on the feed's thread; the scene is used on the UI thread.
        clouds.ImageReady += (_, image) => Dispatcher.BeginInvoke(() => window.Scene.SetCloudImage(image));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            vm.Tick();   // also drives the globe's per-minute recompose
            vm.Status.DownloadText = meter.Text;   // says "(limit reached)" while the feeds are paused
        };
        window.Closed += (_, _) =>
        {
            timer.Stop();
            ShutDownServices();
        };
        timer.Start();
#if PERF_LOG
        // Once a minute: frames per second over that minute, memory, flashes held per sensor and today's download volume.
        {
            int frames = 0;
            var lastFrame = TimeSpan.MinValue;
            System.Windows.Media.CompositionTarget.Rendering += (_, ev) =>
            {
                var rt = ((System.Windows.Media.RenderingEventArgs)ev).RenderingTime;
                if (rt != lastFrame) { lastFrame = rt; frames++; }
            };
            long lastBytes = meter.TodayBytes;
            var started = DateTime.UtcNow;
            var perfTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            perfTimer.Tick += (_, _) =>
            {
                var span = DateTime.UtcNow - started;
                started = DateTime.UtcNow;
                long bytes = meter.TodayBytes;
                var all = lightningBuffer.Between(DateTime.MinValue, DateTime.MaxValue);
                Diagnostics.PerfLog.Write(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"lightning minute: fps={frames / span.TotalSeconds:F1} {Diagnostics.PerfLog.Memory()} flashes={all.Count} " +
                    $"east={all.Count(f => f.Sensor == LightningSensor.GoesEast)} west={all.Count(f => f.Sensor == LightningSensor.GoesWest)} " +
                    $"mtg={all.Count(f => f.Sensor == LightningSensor.Mtg)} " +
                    $"downloaded this minute={(bytes - lastBytes) / 1e6:F2} MB today={bytes / 1e6:F1} MB"));
                frames = 0; lastBytes = bytes;
            };
            perfTimer.Start();
        }
#endif
        scheduler.Start();

        if (options.Any)
            _ = SnapshotDiagnostic.RunAsync(window, window.Scene, vm, options, offsetClock, waitForData, log, name => name switch
            {
                "about" => ShowForDiagnostic(dialogs.CreateAbout()),
                "welcome" => ShowForDiagnostic(dialogs.CreateWelcome()),
                _ => throw new NotSupportedException($"--dialog {name} is not available"),
            });
#if PERF_LOG
        PerfRun.Start(window.Scene, e.Args, waitForData);
#endif
        // First run only, and never in a diagnostic or offline run. Shown once the globe is drawn, so it is already up
        // behind the Welcome and the feeds have started. The flag goes through the debounced saver, flushed at shutdown.
        if (WelcomeViewModel.ShouldShow(_settings.Load(), options))
        {
            void ShowWelcome(object? s, EventArgs a)
            {
                window.ContentRendered -= ShowWelcome;
                dialogs.ShowWelcome();
            }
            window.ContentRendered += ShowWelcome;
        }
        window.Show();
    }

    /// <summary>Opens a dialog without blocking (the diagnostic goes on to save it), over the main window.</summary>
    private Window ShowForDiagnostic(Window dialog)
    {
        if (MainWindow is { IsLoaded: true } owner) dialog.Owner = owner;
        dialog.Show();
        return dialog;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ShutDownServices();
        base.OnExit(e);
    }

    // A small copy is enough to count black pixels.
    private static Bgra32Image DecodeForBlackCheck(byte[] jpeg) =>
        Rendering.ImageConversions.DecodeToBgra32(new MemoryStream(jpeg), 1024);

    /// <summary>Completes once every named feed has reported <see cref="FeedState.Ok"/> at least once.</summary>
    private static Task AllFeedsOkOnce(FeedScheduler scheduler, IEnumerable<string> names)
    {
        var waiting = new HashSet<string>(names, StringComparer.Ordinal);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.StatusChanged += (_, status) =>
        {
            if (status.State != FeedState.Ok) return;
            lock (waiting) { if (waiting.Remove(status.Name) && waiting.Count == 0) done.TrySetResult(); }
        };
        return done.Task;
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
    /// try/catch, so one failure (a slow or throwing Stop, say) doesn't skip the saves after it. An offline run's
    /// temporary data folder is deleted last.
    /// </summary>
    private void ShutDownServices()
    {
        if (_shutDown) return;
        _shutDown = true;
        Step("stop the feed scheduler", () => _scheduler?.Stop());   // waits (up to 5 s) for running feeds
        Step("stop the prune timer", () => _pruneTimer?.Dispose());
        Step("save the download meter", () => _meter?.Save());
        Step("stop the cache writer", () => _cacheWriter?.Dispose());
        Step("save the event cache", () => _cacheWriter?.SaveNow());
        Step("save settings", () => _settings?.Flush());
        Step("stop the settings saver", () => _settings?.Dispose());
        Step("dispose the HTTP client", () => _http?.Dispose());
        if (_offlineRoot is { } root) Step("delete the offline data folder", () => Directory.Delete(root, recursive: true));
        Step("write the repeated-error counts", () => _crash?.FlushRepeats());
        _log?.Info("EarthWatch3D exiting");
        _log?.Dispose();
    }

    private void Step(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { _log?.Error("Shutdown: could not " + what, ex); }
    }
}
