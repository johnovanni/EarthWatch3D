using EarthWatch3D.Core.Lightning;

namespace EarthWatch3D.Tests;

public class LightningBufferTests
{
    private static readonly DateTime T0 = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
    private static Task Long(Action a) => Task.Factory.StartNew(a, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
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
        // a dropped flash's key is released, so it can be added again; being the oldest it is evicted on arrival and is not new
        Assert.Equal(0, b.Add(new[] { F(T0) }));
        Assert.Equal(1000, b.Count);
    }

    [Fact]
    public void FlashEvictedOnArrivalIsNotNewAndRaisesNoChanged()
    {
        var b = new LightningBuffer(3);
        b.Add(new[] { F(T0.AddSeconds(1)), F(T0.AddSeconds(2)), F(T0.AddSeconds(3)) });
        int n = 0;
        b.Changed += (_, _) => n++;
        Assert.Equal(0, b.Add(new[] { F(T0) }));
        Assert.Equal(0, n);
        Assert.Equal(3, b.Count);
        // a batch bigger than the cap counts only what is still held
        Assert.Equal(3, b.Add(Enumerable.Range(10, 5).Select(i => F(T0.AddSeconds(i)))));
        Assert.Equal(1, n);
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
    public void CapEvictedKeyIsReleased()
    {
        var b = new LightningBuffer(3);
        b.Add(new[] { F(T0.AddSeconds(1)), F(T0.AddSeconds(2)), F(T0.AddSeconds(3)) });
        Assert.Equal(1, b.Add(new[] { F(T0.AddSeconds(4)) }));   // the cap evicts the flash at +1 s
        b.PruneBefore(T0.AddSeconds(3));                          // room again: +3 s and +4 s are left
        Assert.Equal(2, b.Count);
        Assert.Equal(1, b.Add(new[] { F(T0.AddSeconds(1)) }));   // the evicted key was released, so it is new again
        Assert.Equal(new[] { T0.AddSeconds(1), T0.AddSeconds(3), T0.AddSeconds(4) }, b.Between(T0, T0.AddDays(1)).Select(f => f.TimeUtc));
    }

    [Fact]
    public void NewestFirstCatchUpStaysSortedDeduplicatedAndCapped()
    {
        // The GLM catch-up: 45 files of 2,000 flashes each, 20 s per file, downloaded newest file first. Adjacent files
        // overlap by about a second, so the first 100 flashes of each file repeat the last 100 of the file before it.
        const int files = 45, perFile = 2000, cap = 50_000;
        var b = new LightningBuffer(cap);
        static LightningFlash Flash(int i) => F(T0.AddMilliseconds(i * 10), lat: i % 80, lon: -80 + i % 50 * 0.001);
        int total = 0;
        for (int file = files - 1; file >= 0; file--)
        {
            int first = file * (perFile - 100);
            total += b.Add(Enumerable.Range(first, perFile).Select(Flash));
        }
        int distinct = (files - 1) * (perFile - 100) + perFile;
        var all = b.Between(DateTime.MinValue, DateTime.MaxValue);
        Assert.Equal(cap, b.Count);
        Assert.Equal(cap, all.Count);
        for (int i = 1; i < all.Count; i++) Assert.True(all[i - 1].TimeUtc <= all[i].TimeUtc, $"out of order at {i}");
        // The newest `cap` distinct flashes are the ones held, each once.
        Assert.Equal(Flash(distinct - cap).TimeUtc, all[0].TimeUtc);
        Assert.Equal(Flash(distinct - 1).TimeUtc, all[^1].TimeUtc);
        Assert.Equal(cap, all.Select(f => f.TimeUtc).Distinct().Count());
        // Files older than everything held while the buffer is full add nothing.
        Assert.Equal(cap, total);
    }

    [Fact]
    public void MergedBatchKeepsTimeOrderAndArrivalOrderForEqualTimes()
    {
        var b = new LightningBuffer();
        b.Add(new[] { F(T0.AddSeconds(1)), F(T0.AddSeconds(3)), F(T0.AddSeconds(5)) });
        Assert.Equal(4, b.Add(new[] { F(T0.AddSeconds(4)), F(T0.AddSeconds(3), lat: 20), F(T0), F(T0.AddSeconds(6)), F(T0) }));
        var r = b.Between(T0, T0.AddDays(1));
        Assert.Equal(new[] { 0, 1, 3, 3, 4, 5, 6 }, r.Select(f => (int)(f.TimeUtc - T0).TotalSeconds));
        Assert.Equal(10, r[2].Lat);   // the flash already held comes first among equal times
        Assert.Equal(20, r[3].Lat);
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
        b.Changed += (_, _) =>
        {
            var t = new Thread(() => _ = b.Count) { IsBackground = true };
            t.Start();
            seen = t.Join(10_000) ? 1 : 0;
        };
        b.Add(new[] { F(T0) });
        Assert.Equal(1, seen);
    }

    [Fact]
    public async Task ConcurrentAddAndBetweenAreSafe()
    {
        var b = new LightningBuffer(5000);
        using var cts = new CancellationTokenSource();
        var errors = new List<Exception>();
        var readers = Enumerable.Range(0, 3).Select(r0 => Long(() =>
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
        var writers = Enumerable.Range(0, 2).Select(w => Long(() =>
        {
            for (int i = 0; i < 4000; i++)
            {
                b.Add(new[] { F(T0.AddMilliseconds(i * 2 + w), lon: -80 + w) });
                if (i % 500 == 0) b.PruneBefore(T0.AddMilliseconds(i));
            }
        })).ToArray();
        try { await Task.WhenAll(writers); }
        finally { cts.Cancel(); }   // the readers stop even if a writer throws
        await Task.WhenAll(readers);
        Assert.Empty(errors);
        Assert.True(b.Count <= 5000);
    }

    [Fact]
    public void RemoveWhereRemovesMatchesRaisesChangedOnceAndFreesTheirKeys()
    {
        var buffer = new LightningBuffer();
        var t = new DateTime(2026, 11, 13, 21, 0, 0, DateTimeKind.Utc);
        var a = new LightningFlash(t, 1, 1, 0, LightningSensor.GoesEast);
        var b = new LightningFlash(t.AddSeconds(1), 2, 2, 0, LightningSensor.GoesEast);
        var c = new LightningFlash(t.AddSeconds(2), 3, 3, 0, LightningSensor.GoesWest);
        buffer.Add([a, b, c]);
        int changed = 0;
        buffer.Changed += (_, _) => changed++;
        Assert.Equal(2, buffer.RemoveWhere(f => f.Sensor == LightningSensor.GoesEast));
        Assert.Equal(1, changed);
        Assert.Equal([c], buffer.Between(DateTime.MinValue, DateTime.MaxValue));
        Assert.Equal(0, buffer.RemoveWhere(f => f.Sensor == LightningSensor.GoesEast));
        Assert.Equal(1, changed);   // nothing removed: no event
        Assert.Equal(1, buffer.Add([a]));   // a removed flash can be added again
    }
}
