using AstroCore.Earth;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.Tests;

public class EventCacheWriterTests
{
    private static readonly DateTime T0 = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(10);

    private static Earthquake Quake(string id) =>
        new(id, T0, new GeoPosition(10, 20), "q " + id, null, 4, 10, "somewhere", T0);

    private sealed class Saves
    {
        private readonly object _gate = new();
        public List<(EventSnapshot Snapshot, int ThreadId)> Calls { get; } = [];
        public int Count { get { lock (_gate) return Calls.Count; } }
        public int FailuresToThrow { get; set; }
        public void Save(EventSnapshot s)
        {
            lock (_gate)
            {
                if (FailuresToThrow > 0) { FailuresToThrow--; throw new System.IO.IOException("disk full"); }
                Calls.Add((s, Environment.CurrentManagedThreadId));
            }
        }
    }

    [Fact]
    public void SavesOnceAfterTheLastChange()
    {
        var store = new EventStore(new TestClock(T0));
        var saves = new Saves();
        var time = new ManualTimeProvider();
        using var writer = new EventCacheWriter(store, saves.Save, Delay, time: time);
        for (int i = 0; i < 3; i++)
        {
            store.UpsertEarthquakes([Quake("q" + i)]);
            time.Advance(TimeSpan.FromSeconds(6));   // each change restarts the 10 s wait
        }
        Assert.Equal(0, saves.Count);   // 18 s in all, but only 6 s since the last change

        time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(1, saves.Count);
        Assert.Equal(3, saves.Calls[0].Snapshot.Earthquakes.Count);
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, saves.Count);
    }

    [Fact]
    public void TimerSaveRunsOffTheCallingThread()
    {
        var store = new EventStore(new TestClock(T0));
        var saves = new Saves();
        using var writer = new EventCacheWriter(store, saves.Save, TimeSpan.FromMilliseconds(50));
        store.UpsertEarthquakes([Quake("a")]);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (saves.Count == 0 && DateTime.UtcNow < deadline) Thread.Sleep(10);
        Assert.Equal(1, saves.Count);
        Assert.NotEqual(Environment.CurrentManagedThreadId, saves.Calls[0].ThreadId);
    }

    [Fact]
    public void NoChangeNoSave()
    {
        var store = new EventStore(new TestClock(T0));
        var saves = new Saves();
        var time = new ManualTimeProvider();
        using var writer = new EventCacheWriter(store, saves.Save, Delay, time: time);
        time.Advance(TimeSpan.FromMinutes(1));
        writer.Flush();
        Assert.Equal(0, saves.Count);
    }

    [Fact]
    public void FailedTimerSaveIsLoggedAndKeptForRetry()
    {
        var store = new EventStore(new TestClock(T0));
        var saves = new Saves { FailuresToThrow = 1 };
        var time = new ManualTimeProvider();
        var log = new RecordingLog();
        using var writer = new EventCacheWriter(store, saves.Save, Delay, log, time);
        store.UpsertEarthquakes([Quake("a")]);
        time.Advance(Delay);   // throws inside the timer callback: logged, not thrown
        Assert.Equal(0, saves.Count);
        Assert.Single(log.Warnings);

        writer.Flush();   // still marked as changed, so this retries
        Assert.Equal(1, saves.Count);
    }

    [Fact]
    public void ShutdownSaveIsImmediateAndLaterChangesAreIgnored()
    {
        var store = new EventStore(new TestClock(T0));
        var saves = new Saves();
        var time = new ManualTimeProvider();
        var writer = new EventCacheWriter(store, saves.Save, Delay, time: time);
        store.UpsertEarthquakes([Quake("a")]);
        writer.Dispose();
        writer.SaveNow();   // what the app does on exit
        Assert.Equal(1, saves.Count);
        Assert.Single(saves.Calls[0].Snapshot.Earthquakes);

        store.UpsertEarthquakes([Quake("b")]);   // after dispose: not watched any more
        time.Advance(TimeSpan.FromMinutes(1));
        writer.Flush();
        Assert.Equal(1, saves.Count);
    }

    [Fact]
    public void SavesAtMostEveryFiveMinutesAndStillDebounces()
    {
        var store = new EventStore(new TestClock(T0));
        var saves = new Saves();
        var time = new ManualTimeProvider();
        using var writer = new EventCacheWriter(store, saves.Save, Delay, time: time);   // default minimum interval: 5 min
        Assert.Equal(TimeSpan.FromMinutes(5), EventCacheWriter.DefaultMinInterval);

        store.UpsertEarthquakes([Quake("a")]);
        time.Advance(Delay);
        Assert.Equal(1, saves.Count);                    // the first save is not held back

        store.UpsertEarthquakes([Quake("b")]);           // 10 s after the first save
        time.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        Assert.Equal(1, saves.Count);                    // the 10 s debounce has passed, but not 5 min since the last save
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, saves.Count);
        Assert.Equal(2, saves.Calls[1].Snapshot.Earthquakes.Count);

        time.Advance(TimeSpan.FromMinutes(6));           // quiet: nothing to save
        Assert.Equal(2, saves.Count);
        store.UpsertEarthquakes([Quake("c")]);
        time.Advance(TimeSpan.FromSeconds(9));
        store.UpsertEarthquakes([Quake("d")]);           // restarts the 10 s wait
        time.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal(2, saves.Count);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(3, saves.Count);
        Assert.Equal(4, saves.Calls[2].Snapshot.Earthquakes.Count);
    }

    [Fact]
    public void ExitSaveIgnoresTheMinimumInterval()
    {
        var store = new EventStore(new TestClock(T0));
        var saves = new Saves();
        var time = new ManualTimeProvider();
        var writer = new EventCacheWriter(store, saves.Save, Delay, time: time);
        store.UpsertEarthquakes([Quake("a")]);
        time.Advance(Delay);
        store.UpsertEarthquakes([Quake("b")]);
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, saves.Count);                    // held back by the 5 min interval
        writer.Dispose();
        writer.SaveNow();                                // on exit: saved at once
        Assert.Equal(2, saves.Count);
        Assert.Equal(2, saves.Calls[1].Snapshot.Earthquakes.Count);
    }
}
