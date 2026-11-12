using System.Windows.Media;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.Core.Geo;

namespace EarthWatch3D.Tests;

public sealed class StaWorkerTests
{
    [Fact]
    public async Task RunsAllWorkOnOneStaThread()
    {
        using var worker = new StaWorker("test-sta");
        var seen = new List<(int Id, ApartmentState Apartment)>();
        for (int i = 0; i < 3; i++)
            seen.Add(await worker.InvokeAsync(() => (Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState())));
        Assert.Single(seen.Distinct());
        Assert.Equal(ApartmentState.STA, seen[0].Apartment);
        Assert.NotEqual(Environment.CurrentManagedThreadId, seen[0].Id);
    }

    [Fact]
    public async Task CanRasterizeAreas()
    {
        using var worker = new StaWorker("test-sta");
        var box = new GeoPolygon([[new(0, 0), new(0, 10), new(10, 10), new(10, 0), new(0, 0)]]);
        var img = await worker.InvokeAsync(() => AreaRasterizer.Rasterize([(box, Colors.Red)], 360, 180));
        Assert.Contains(img.Pixels.Where((_, i) => i % 4 == 3), a => a != 0);
    }

    [Fact]
    public async Task PropagatesExceptionsAndKeepsWorking()
    {
        using var worker = new StaWorker("test-sta");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => worker.InvokeAsync<int>(() => throw new InvalidOperationException("boom")));
        Assert.Equal("boom", ex.Message);
        Assert.Equal(42, await worker.InvokeAsync(() => 42));
    }

    [Fact]
    public async Task ShutsDownCleanly()
    {
        var worker = new StaWorker("test-sta");
        var thread = await worker.InvokeAsync(() => Thread.CurrentThread);
        // Work queued behind a busy item when the worker is shut down is cancelled, not left hanging. The busy item
        // is held for 1 s, so Dispose (called straight away on this thread) is requested while it still runs.
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var busy = worker.InvokeAsync(() => { started.Set(); release.Wait(TimeSpan.FromSeconds(10)); return 1; });
        var queued = worker.InvokeAsync(() => 2);
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        _ = Task.Delay(1000).ContinueWith(_ => release.Set(), TaskScheduler.Default);
        worker.Dispose();   // returns once the busy item has finished and the dispatcher has shut down
        Assert.Equal(1, await busy);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(thread.IsAlive);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.InvokeAsync(() => 3).WaitAsync(TimeSpan.FromSeconds(10)));
        worker.Dispose();   // twice is fine
    }
}
