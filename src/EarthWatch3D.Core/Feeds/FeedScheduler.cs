using System.Diagnostics;
using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Core.Feeds;

/// <summary>Runs each feed when due, tracks per-feed status, and backs off after failures.</summary>
public sealed class FeedScheduler : IDisposable
{
    /// <summary>One feed's state. Every member is used only under the scheduler's lock.</summary>
    private sealed class Slot(IEventFeed feed, DateTime due)
    {
        private bool _running;
        public IEventFeed Feed { get; } = feed;
        public FeedStatus Status { get; set; } = new(feed.Name, FeedState.NotStarted, null, null, 0, due);
        public bool IsIdleNow => !_running;
        public bool IsDue(DateTime now) => !_running && Status.NextRunUtc <= now;
        public void BeginRun() => _running = true;
        public void EndRun() => _running = false;
    }

    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly List<Slot> _slots;
    private readonly IClock _clock;
    private readonly IAppLog _log;
    private readonly object _gate = new();
    // Runs started and not yet finished, so Stop can wait for them.
    private readonly HashSet<Task> _inFlight = [];
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>
    /// Raised after every feed run, on a background thread. UI subscribers must marshal with
    /// Dispatcher.BeginInvoke (never Invoke, which can deadlock against Stop()).
    /// </summary>
    public event EventHandler<FeedStatus>? StatusChanged;

    public FeedScheduler(IEnumerable<IEventFeed> feeds, IClock clock, IAppLog log)
    {
        _clock = clock;
        _log = log;
        var now = clock.UtcNow;
        _slots = feeds.Select(f => new Slot(f, now)).ToList();
    }

    public IReadOnlyList<FeedStatus> Statuses
    {
        get { lock (_gate) return _slots.Select(s => s.Status).ToList(); }
    }

    /// <summary>Starts every due feed and waits for the runs it started (not for runs already in flight).</summary>
    public Task TickAsync(CancellationToken ct) => Task.WhenAll(StartDueRuns(ct));

    /// <summary>
    /// One step of the background loop: starts every due feed that is not already running, and returns at once
    /// with the runs it started. A slow feed therefore never holds back the others; a running feed is not started
    /// again until its run ends.
    /// </summary>
    public IReadOnlyList<Task> StartDueRuns(CancellationToken ct)
    {
        List<Slot> due;
        lock (_gate)
        {
            var now = _clock.UtcNow;
            due = _slots.Where(s => s.IsDue(now)).ToList();
            foreach (var s in due) s.BeginRun();
        }
        var started = new List<Task>(due.Count);
        foreach (var slot in due)
        {
            var run = RunSlotAsync(slot, ct);   // never throws: failures become the slot's status
            lock (_gate) _inFlight.Add(run);
            _ = run.ContinueWith(t => { lock (_gate) _inFlight.Remove(t); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            started.Add(run);
        }
        return started;
    }

    /// <summary>
    /// Makes the named feed due at the next tick with its failure count cleared, so a changed setting (a new EUMETSAT key)
    /// does not wait out a backoff. Does nothing for a feed that is running now or an unknown name.
    /// </summary>
    public void RunSoon(string name)
    {
        lock (_gate)
        {
            var slot = _slots.FirstOrDefault(s => s.Feed.Name == name);
            if (slot is null || !slot.IsIdleNow) return;
            slot.Status = slot.Status with { NextRunUtc = _clock.UtcNow, ConsecutiveFailures = 0 };
        }
    }

    private async Task RunSlotAsync(Slot slot, CancellationToken ct)
    {
        FeedStatus status;
        try
        {
            var result = await Task.Run(() => slot.Feed.RunOnceAsync(ct), CancellationToken.None).ConfigureAwait(false);
            var now = _clock.UtcNow;
            var next = now + slot.Feed.Interval;
            FeedStatus before;
            lock (_gate) before = slot.Status;
            switch (result)
            {
                case FeedRunResult.Skipped:
                    // Nothing was fetched (layer off, replay, out of view): the feed is idle, whatever it was before (a
                    // stale Paused or Failing would mislead), and keeps its last success. Already idle: only the next run
                    // moves, and nobody is told.
                    if (before.State == FeedState.Idle)
                    {
                        lock (_gate) { slot.Status = before with { NextRunUtc = next }; slot.EndRun(); }
                        return;
                    }
                    status = new FeedStatus(slot.Feed.Name, FeedState.Idle, before.LastSuccessUtc, null, 0, next, FeedStatus.IdleNote);
                    break;
                case FeedRunResult.NotSetUp:
                    status = new FeedStatus(slot.Feed.Name, FeedState.NotSetUp, before.LastSuccessUtc, null, 0, next);
                    break;
                case FeedRunResult.Paused:
                    status = new FeedStatus(slot.Feed.Name, FeedState.Paused, before.LastSuccessUtc, null, 0, next,
                        "Daily download limit reached");
                    break;
                default:
                    status = new FeedStatus(slot.Feed.Name, FeedState.Ok, now, null, 0, next);
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown, not a feed failure: leave the status alone.
            lock (_gate) slot.EndRun();
            return;
        }
        catch (Exception ex)
        {
            var now = _clock.UtcNow;
            int failures;
            DateTime? lastOk;
            lock (_gate) { failures = slot.Status.ConsecutiveFailures + 1; lastOk = slot.Status.LastSuccessUtc; }
            _log.Warn(slot.Feed.Name + ": " + ex.Message, ex);
            status = new FeedStatus(slot.Feed.Name, failures >= 3 ? FeedState.Failing : FeedState.Retrying,
                lastOk, ex.Message, failures, now + Backoff.Delay(failures));
        }
        lock (_gate) { slot.Status = status; slot.EndRun(); }
        try { StatusChanged?.Invoke(this, status); }
        catch (Exception ex) { _log.Warn("StatusChanged handler failed", ex); }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_loop is not null) return;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _loop = Task.Run(() => LoopAsync(ct));
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            do
            {
                try { StartDueRuns(ct); }   // not awaited: each feed runs on its own
                catch (Exception ex) when (ex is not OperationCanceledException) { _log.Warn("Scheduler tick failed", ex); }
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Cancels the loop and every run in flight, and waits for them all, at most 5 s in total.</summary>
    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_gate) { cts = _cts; loop = _loop; _cts = null; _loop = null; }
        if (cts is null) return;
        cts.Cancel();
        var deadline = Stopwatch.StartNew();
        bool stopped = true;
        try
        {
            if (loop is not null && !loop.Wait(StopTimeout))
            {
                stopped = false;
                _log.Warn("Feed scheduler loop did not stop within 5 s");
            }
            Task[] running;
            lock (_gate) running = [.. _inFlight];
            var left = StopTimeout - deadline.Elapsed;
            if (running.Length > 0 && (left <= TimeSpan.Zero || !Task.WaitAll(running, left)))
            {
                stopped = false;
                _log.Warn($"Feed scheduler: {running.Count(t => !t.IsCompleted)} feed run(s) did not stop within 5 s");
            }
        }
        catch (AggregateException) { }
        // A run that did not stop may still read the token; disposing its source under it is not worth the risk.
        if (stopped) cts.Dispose();
    }

    public void Dispose() => Stop();
}
