using EarthWatch3D.Core.Events;

namespace EarthWatch3D.Core.Storage;

/// <summary>
/// Saves the event store some time (10 s by default) after its last change: each <see cref="EventStore.Changed"/>
/// restarts a one-shot timer, whose callback saves on a thread-pool thread. Saves never overlap.
/// On exit: <see cref="Dispose"/> (stop watching), then <see cref="SaveNow"/> once.
/// </summary>
public sealed class EventCacheWriter : IDisposable
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(10);

    private readonly EventStore _store;
    private readonly Action<EventSnapshot> _save;
    private readonly TimeSpan _delay;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private readonly object _writeGate = new();
    private bool _dirty, _disposed;

    /// <param name="save">Writes a snapshot, e.g. <see cref="EventCache.Save"/> (which logs and swallows I/O failures).</param>
    public EventCacheWriter(EventStore store, Action<EventSnapshot> save, TimeSpan? delay = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _delay = delay ?? DefaultDelay;
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        _store.Changed += OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _dirty = true;
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Saves now if the store changed since the last save.</summary>
    public void Flush() => Write(onlyIfDirty: true);

    /// <summary>Saves the current snapshot now, changed or not.</summary>
    public void SaveNow() => Write(onlyIfDirty: false);

    private void Write(bool onlyIfDirty)
    {
        lock (_writeGate)
        {
            lock (_gate)
            {
                if (onlyIfDirty && !_dirty) return;
                _dirty = false;   // a change from here on marks it dirty again and restarts the timer
            }
            _save(_store.Snapshot);
        }
    }

    /// <summary>Stops watching the store and cancels a pending save (call <see cref="SaveNow"/> afterwards to keep it).</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _store.Changed -= OnChanged;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        _timer.Dispose();
    }
}
