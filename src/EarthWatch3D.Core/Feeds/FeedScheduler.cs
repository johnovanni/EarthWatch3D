using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Core.Feeds;

/// <summary>Runs each feed when due, tracks per-feed status, and backs off after failures.</summary>
public sealed class FeedScheduler : IDisposable
{
    private sealed class Slot(IEventFeed feed, DateTime due)
    {
        public IEventFeed Feed { get; } = feed;
        public FeedStatus Status { get; set; } = new(feed.Name, FeedState.NotStarted, null, null, 0, due);
        public bool Running;
    }

    private readonly List<Slot> _slots;
    private readonly IClock _clock;
    private readonly IAppLog _log;
    private readonly object _gate = new();
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

    public async Task TickAsync(CancellationToken ct)
    {
        List<Slot> due;
        lock (_gate)
        {
            var now = _clock.UtcNow;
            due = _slots.Where(s => !s.Running && s.Status.NextRunUtc <= now).ToList();
            foreach (var s in due) s.Running = true;
        }
        await Task.WhenAll(due.Select(s => RunSlotAsync(s, ct))).ConfigureAwait(false);
    }

    private async Task RunSlotAsync(Slot slot, CancellationToken ct)
    {
        FeedStatus status;
        try
        {
            await Task.Run(() => slot.Feed.RunOnceAsync(ct), CancellationToken.None).ConfigureAwait(false);
            var now = _clock.UtcNow;
            status = new FeedStatus(slot.Feed.Name, FeedState.Ok, now, null, 0, now + slot.Feed.Interval);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown, not a feed failure: leave the status alone.
            lock (_gate) slot.Running = false;
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
        lock (_gate) { slot.Status = status; slot.Running = false; }
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
                try { await TickAsync(ct).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OperationCanceledException) { _log.Warn("Scheduler tick failed", ex); }
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_gate) { cts = _cts; loop = _loop; _cts = null; _loop = null; }
        if (cts is null) return;
        cts.Cancel();
        try
        {
            if (loop is not null && !loop.Wait(TimeSpan.FromSeconds(5)))
                _log.Warn("Feed scheduler loop did not stop within 5 s");
        }
        catch (AggregateException) { }
        cts.Dispose();
    }

    public void Dispose() => Stop();
}
