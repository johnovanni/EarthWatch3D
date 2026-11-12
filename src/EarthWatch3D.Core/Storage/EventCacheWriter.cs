using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Logging;

namespace EarthWatch3D.Core.Storage;

/// <summary>
/// Saves the event store some time (10 s by default) after its last change, and at most once every 5 minutes (by
/// default): each <see cref="EventStore.Changed"/> restarts a one-shot timer for the later of the two, whose callback
/// saves on a thread-pool thread. Saves never overlap. The file is rewritten whole, and the feeds change the store
/// every minute or two, so the interval keeps the disk writes down.
/// On exit: <see cref="Dispose"/> (stop watching), then <see cref="SaveNow"/> once (not held back by the interval).
/// </summary>
public sealed class EventCacheWriter : IDisposable
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultMinInterval = TimeSpan.FromMinutes(5);

    private readonly EventStore _store;
    private readonly Action<EventSnapshot> _save;
    private readonly TimeSpan _delay;
    private readonly TimeSpan _minInterval;
    private readonly TimeProvider _time;
    private readonly IAppLog _log;
    private readonly ITimer _timer;
    private readonly object _gate = new();
    private readonly object _writeGate = new();
    private bool _dirty, _disposed;
    private DateTimeOffset? _lastSave;   // when the last successful save started

    /// <param name="save">Writes a snapshot, e.g. <see cref="EventCache.Save"/> (which logs and swallows I/O failures).</param>
    /// <param name="time">Supplies the clock and the debounce timer; tests pass a manual one. Defaults to the system's.</param>
    /// <param name="minInterval">The shortest time between two timer saves (5 min by default).</param>
    public EventCacheWriter(EventStore store, Action<EventSnapshot> save, TimeSpan? delay = null, IAppLog? log = null,
                            TimeProvider? time = null, TimeSpan? minInterval = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _delay = delay ?? DefaultDelay;
        _log = log ?? new NullLog();
        _minInterval = minInterval ?? DefaultMinInterval;
        _time = time ?? TimeProvider.System;
        _timer = _time.CreateTimer(_ => OnTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _store.Changed += OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _dirty = true;
            _timer.Change(Max(_delay, UntilIntervalEnds()), Timeout.InfiniteTimeSpan);
        }
    }

    // Under _gate. Zero once the minimum interval since the last save has passed.
    private TimeSpan UntilIntervalEnds() =>
        _lastSave is { } last ? Max(TimeSpan.Zero, last + _minInterval - _time.GetUtcNow()) : TimeSpan.Zero;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    /// <summary>Saves now if the store changed since the last save.</summary>
    public void Flush() => Write(onlyIfDirty: true);

    /// <summary>Saves the current snapshot now, changed or not.</summary>
    public void SaveNow() => Write(onlyIfDirty: false);

    // If the save throws, the store stays marked as changed, so the next change, flush or SaveNow tries again.
    private void Write(bool onlyIfDirty)
    {
        lock (_writeGate)
        {
            lock (_gate)
            {
                if (onlyIfDirty && !_dirty) return;
                _dirty = false;   // a change from here on marks it dirty again and restarts the timer
            }
            var started = _time.GetUtcNow();
            try
            {
                _save(_store.Snapshot);
                lock (_gate) _lastSave = started;
            }
            catch
            {
                lock (_gate) _dirty = true;
                throw;
            }
        }
    }

    // A timer callback must not throw: that would end the process.
    private void OnTimer()
    {
        try
        {
            lock (_gate)
            {
                // A change made while the previous save ran set this timer before that save was recorded: wait out
                // the rest of the interval.
                var wait = UntilIntervalEnds();
                if (wait > TimeSpan.Zero)
                {
                    if (!_disposed) _timer.Change(wait, Timeout.InfiniteTimeSpan);
                    return;
                }
            }
            Flush();
        }
        catch (Exception ex) { _log.Warn("Could not save the event cache", ex); }
    }

    /// <summary>Stops watching the store and cancels a pending save (call <see cref="SaveNow"/> afterwards to keep it).</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _store.Changed -= OnChanged;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        _timer.Dispose();
    }
}
