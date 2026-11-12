using EarthWatch3D.Core.Lightning;

namespace EarthWatch3D.Tests;

public class LightningBufferTests
{
    private static readonly DateTime T0 = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
    private static LightningFlash F(DateTime t, double lat = 10, double lon = -80, LightningSensor s = LightningSensor.GoesEast) => new(t, lat, lon, 1e-14, s);

    [Fact]
    public void DeduplicatesAcrossFiles()
    {
        var b = new LightningBuffer();
        Assert.Equal(1, b.Add(new[] { F(T0) }));
        Assert.Equal(0, b.Add(new[] { F(T0.AddTicks(1000)) with { Lat = 10.00001 } }));
        Assert.Equal(1, b.Count);
        Assert.Equal(1, b.Add(new[] { F(T0, s: LightningSensor.GoesWest) }));
        Assert.Equal(1, b.Add(new[] { F(T0, lat: 10.001) }));
        Assert.Equal(1, b.Add(new[] { F(T0.AddMilliseconds(1)) }));
        Assert.Equal(4, b.Count);
    }

    [Fact]
    public void DuplicatesWithinOneBatchCountOnce()
    {
        var b = new LightningBuffer();
        Assert.Equal(1, b.Add(new[] { F(T0), F(T0) }));
    }

    [Fact]
    public void CapDropsOldestFirst()
    {
        var b = new LightningBuffer(1000);
        b.Add(Enumerable.Range(0, 1500).Select(i => F(T0.AddSeconds(i))));
        Assert.Equal(1000, b.Count);
        var all = b.Between(T0, T0.AddDays(1));
        Assert.Equal(T0.AddSeconds(500), all[0].TimeUtc);
        // a dropped flash's key is released, so it can be added again (and is then the oldest, dropped at once by the cap)
        Assert.Equal(1, b.Add(new[] { F(T0) }));
        Assert.Equal(1000, b.Count);
    }

    [Fact]
    public void CapDropsOldestByTimeEvenWhenInsertedLate()
    {
        var b = new LightningBuffer(3);
        b.Add(new[] { F(T0.AddSeconds(1)), F(T0.AddSeconds(2)), F(T0.AddSeconds(3)) });
        b.Add(new[] { F(T0) });   // older than everything held: dropped at once
        Assert.Equal(T0.AddSeconds(1), b.Between(T0, T0.AddDays(1))[0].TimeUtc);
        Assert.Equal(3, b.Count);
    }

    [Fact]
    public void OutOfOrderInsertsStayInTimeOrder()
    {
        var b = new LightningBuffer();
        b.Add(new[] { F(T0.AddSeconds(2)), F(T0), F(T0.AddSeconds(1), lat: 11) });
        var r = b.Between(T0, T0.AddDays(1));
        Assert.Equal(new[] { T0, T0.AddSeconds(1), T0.AddSeconds(2) }, r.Select(f => f.TimeUtc));
    }

    [Fact]
    public void BetweenAndPrune()
    {
        var b = new LightningBuffer();
        b.Add(Enumerable.Range(0, 10).Select(i => F(T0.AddSeconds(i))));
        var r = b.Between(T0.AddSeconds(2), T0.AddSeconds(5));
        Assert.Equal(new[] { T0.AddSeconds(2), T0.AddSeconds(3), T0.AddSeconds(4) }, r.Select(f => f.TimeUtc));
        b.PruneBefore(T0.AddSeconds(4));
        Assert.Equal(6, b.Count);
        Assert.Equal(T0.AddSeconds(4), b.Between(T0, T0.AddDays(1))[0].TimeUtc);
        Assert.Equal(1, b.Add(new[] { F(T0.AddSeconds(1)) })); // pruned key released
    }

    [Fact]
    public void NewestIsPerSensor()
    {
        var b = new LightningBuffer();
        Assert.Null(b.Newest(LightningSensor.Mtg));
        b.Add(new[] { F(T0), F(T0.AddSeconds(5)), F(T0.AddSeconds(9), s: LightningSensor.Mtg) });
        Assert.Equal(T0.AddSeconds(5), b.Newest(LightningSensor.GoesEast));
        Assert.Equal(T0.AddSeconds(9), b.Newest(LightningSensor.Mtg));
        Assert.Null(b.Newest(LightningSensor.GoesWest));
    }

    [Fact]
    public void ChangedRaisedOnlyForNewFlashes()
    {
        var b = new LightningBuffer();
        int n = 0;
        b.Changed += (_, _) => n++;
        b.Add(new[] { F(T0) });
        Assert.Equal(1, n);
        b.Add(new[] { F(T0) });
        Assert.Equal(1, n);
        b.Add(Array.Empty<LightningFlash>());
        Assert.Equal(1, n);
    }

    [Fact]
    public void ChangedIsRaisedOutsideTheLock()
    {
        var b = new LightningBuffer();
        int seen = -1;
        // The handler asks another thread to read the buffer and waits for it: that deadlocks if Add still holds the lock.
        b.Changed += (_, _) => seen = Task.Run(() => b.Count).Wait(2000) ? 1 : 0;
        b.Add(new[] { F(T0) });
        Assert.Equal(1, seen);
    }

    [Fact]
    public async Task ConcurrentAddAndBetweenAreSafe()
    {
        var b = new LightningBuffer(5000);
        using var cts = new CancellationTokenSource();
        var errors = new List<Exception>();
        var readers = Enumerable.Range(0, 3).Select(r0 => Task.Run(() =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    var r = b.Between(T0, T0.AddDays(1));
                    for (int i = 1; i < r.Count; i++)
                        if (r[i].TimeUtc < r[i - 1].TimeUtc) throw new InvalidOperationException("not in time order");
                    _ = b.Count; _ = b.Newest(LightningSensor.GoesEast);
                }
            }
            catch (Exception e) { lock (errors) errors.Add(e); }
        })).ToArray();
        var writers = Enumerable.Range(0, 2).Select(w => Task.Run(() =>
        {
            for (int i = 0; i < 4000; i++)
            {
                b.Add(new[] { F(T0.AddMilliseconds(i * 2 + w), lon: -80 + w) });
                if (i % 500 == 0) b.PruneBefore(T0.AddMilliseconds(i));
            }
        })).ToArray();
        await Task.WhenAll(writers);
        cts.Cancel();
        await Task.WhenAll(readers);
        Assert.Empty(errors);
        Assert.True(b.Count <= 5000);
    }
}
