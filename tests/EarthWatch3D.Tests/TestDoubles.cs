using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Storage;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Tests;

/// <summary>Records every message by level.</summary>
internal sealed class RecordingLog : IAppLog
{
    public List<string> Infos { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];
    public void Info(string m) => Infos.Add(m);
    public void Warn(string m, Exception? e = null) => Warnings.Add(m);
    public void Error(string m, Exception? e = null) => Errors.Add(m);
}

internal sealed class TestClock(DateTime t) : IClock { public DateTime UtcNow { get; set; } = t; }

/// <summary>In-memory, synchronous settings store: Load returns the last saved value.</summary>
internal sealed class FakeSettingsStore(AppSettings? initial = null) : ISettingsStore
{
    private readonly object _gate = new();
    private AppSettings _current = initial ?? new();
    public List<AppSettings> Saved { get; } = [];
    public List<int> SaveThreadIds { get; } = [];
    /// <summary>The next this-many saves throw an IOException.</summary>
    public int FailuresToThrow { get; set; }
    public int SaveCount { get { lock (_gate) return Saved.Count; } }
    public AppSettings Load() { lock (_gate) return _current; }
    public void Save(AppSettings s)
    {
        lock (_gate)
        {
            if (FailuresToThrow > 0) { FailuresToThrow--; throw new System.IO.IOException("disk full"); }
            _current = s; Saved.Add(s); SaveThreadIds.Add(Environment.CurrentManagedThreadId);
        }
    }
}

/// <summary>
/// A TimeProvider whose timers fire only from <see cref="Advance"/>, synchronously on the calling thread,
/// so debounce tests need no sleeps.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var t = new ManualTimer(this, callback, state);
        lock (_gate) _timers.Add(t);
        t.Change(dueTime, period);
        return t;
    }

    /// <summary>Moves time forward, firing each due timer (in due order) at its due time.</summary>
    public void Advance(TimeSpan by)
    {
        DateTimeOffset end;
        lock (_gate) end = _now + by;
        while (true)
        {
            ManualTimer? next;
            lock (_gate)
            {
                next = _timers.Where(t => t.DueAt is { } d && d <= end).MinBy(t => t.DueAt);
                if (next is null) { _now = end; return; }
                _now = next.DueAt!.Value;
                next.DueAt = next.Period > TimeSpan.Zero ? _now + next.Period : null;
            }
            next.Fire();   // outside the lock: the callback may call Change
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? DueAt { get; set; }
        public TimeSpan Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                Period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
            }
            return true;
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (owner._gate) { DueAt = null; owner._timers.Remove(this); }
        }

        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
