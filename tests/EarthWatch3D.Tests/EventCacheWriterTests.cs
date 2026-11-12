using AstroCore.Earth;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.Tests;

public class EventCacheWriterTests
{
    private static readonly DateTime T0 = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    private static Earthquake Quake(string id) =>
        new(id, T0, new GeoPosition(10, 20), "q " + id, null, 4, 10, "somewhere", T0);

    private sealed class Saves
    {
        private readonly object _gate = new();
        public List<(EventSnapshot Snapshot, int ThreadId)> Calls { get; } = [];
        public int Count { get { lock (_gate) return Calls.Count; } }
        public void Save(EventSnapshot s) { lock (_gate) Calls.Add((s, Environment.CurrentManagedThreadId)); }
    }

    private static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) Thread.Sleep(10);
        Assert.True(condition());
    }

    [Fact]
    public void SavesOnceAfterTheLastChange()
    {
        var store = new EventStore(new TestClock(T0));
        var saves = new Saves();
        using var writer = new EventCacheWriter(store, saves.Save, TimeSpan.FromMilliseconds(300));
        for (int i = 0; i < 3; i++)
        {
            store.UpsertEarthquakes([Quake("q" + i)]);
            Thread.Sleep(100);
        }
        Assert.Equal(0, saves.Count);   // each change restarts the 300 ms wait

        WaitFor(() => saves.Count >= 1);
        Thread.Sleep(400);
        Assert.Equal(1, saves.Count);
        Assert.Equal(3, saves.Calls[0].Snapshot.Earthquakes.Count);
        Assert.NotEqual(Environment.CurrentManagedThreadId, saves.Calls[0].ThreadId);
    }

    [Fact]
    public void NoChangeNoSave()
    {
        var store = new EventStore(new TestClock(T0));
        var saves = new Saves();
        using var writer = new EventCacheWriter(store, saves.Save, TimeSpan.FromMilliseconds(50));
        Thread.Sleep(200);
        writer.Flush();
        Assert.Equal(0, saves.Count);
    }

    [Fact]
    public void ShutdownSaveIsImmediateAndLaterChangesAreIgnored()
    {
        var store = new EventStore(new TestClock(T0));
        var saves = new Saves();
        var writer = new EventCacheWriter(store, saves.Save, TimeSpan.FromSeconds(30));
        store.UpsertEarthquakes([Quake("a")]);
        writer.Dispose();
        writer.SaveNow();   // what the app does on exit
        Assert.Equal(1, saves.Count);
        Assert.Single(saves.Calls[0].Snapshot.Earthquakes);

        store.UpsertEarthquakes([Quake("b")]);   // after dispose: not watched any more
        writer.Flush();
        Assert.Equal(1, saves.Count);
    }
}
