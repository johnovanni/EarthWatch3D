using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Tests;

public sealed class FeedSchedulerTests
{
    private sealed class FakeClock(DateTime t) : IClock { public DateTime UtcNow { get; set; } = t; }

    private sealed class FakeFeed(string name, TimeSpan interval) : IEventFeed
    {
        public string Name { get; } = name;
        public TimeSpan Interval { get; } = interval;
        public int Runs;
        public bool Fail;
        public FeedRunResult Result = FeedRunResult.Ok;
        public Task<FeedRunResult> RunOnceAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref Runs);
            if (Fail) throw new InvalidOperationException("boom");
            return Task.FromResult(Result);
        }
    }

    private static readonly DateTime T0 = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task DueFeedsRunAndReschedule()
    {
        var clock = new FakeClock(T0);
        var a = new FakeFeed("a", TimeSpan.FromSeconds(60));
        var b = new FakeFeed("b", TimeSpan.FromMinutes(15));
        using var s = new FeedScheduler([a, b], clock, new NullLog());
        Assert.All(s.Statuses, st => Assert.Equal(FeedState.NotStarted, st.State));

        await s.TickAsync(CancellationToken.None);
        Assert.Equal(1, a.Runs);
        Assert.Equal(1, b.Runs);

        clock.UtcNow = T0.AddSeconds(61);
        await s.TickAsync(CancellationToken.None);
        Assert.Equal(2, a.Runs);
        Assert.Equal(1, b.Runs);
    }

    [Fact]
    public async Task SkippedKeepsStateAndLastSuccess()
    {
        var clock = new FakeClock(T0);
        var f = new FakeFeed("f", TimeSpan.FromMinutes(1));
        using var s = new FeedScheduler([f], clock, new NullLog());
        await s.TickAsync(CancellationToken.None);
        var first = s.Statuses[0];
        Assert.Equal(FeedState.Ok, first.State);

        clock.UtcNow = T0.AddMinutes(1);
        f.Result = FeedRunResult.Skipped;
        int raised = 0;
        s.StatusChanged += (_, _) => raised++;
        await s.TickAsync(CancellationToken.None);
        var st = s.Statuses[0];
        Assert.Equal(2, f.Runs);
        Assert.Equal(FeedState.Ok, st.State);
        Assert.Equal(first.LastSuccessUtc, st.LastSuccessUtc);
        Assert.Equal(clock.UtcNow + f.Interval, st.NextRunUtc);
        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task NotSetUpAndPausedMapToTheirStates()
    {
        var clock = new FakeClock(T0);
        var f = new FakeFeed("f", TimeSpan.FromMinutes(1)) { Result = FeedRunResult.NotSetUp };
        using var s = new FeedScheduler([f], clock, new NullLog());
        await s.TickAsync(CancellationToken.None);
        Assert.Equal(FeedState.NotSetUp, s.Statuses[0].State);
        Assert.Equal(0, s.Statuses[0].ConsecutiveFailures);
        Assert.Equal(T0.AddMinutes(1), s.Statuses[0].NextRunUtc);

        clock.UtcNow = T0.AddMinutes(1);
        f.Result = FeedRunResult.Paused;
        await s.TickAsync(CancellationToken.None);
        var st = s.Statuses[0];
        Assert.Equal(FeedState.Paused, st.State);
        Assert.Equal("Daily download limit reached", st.Note);
        Assert.Equal(0, st.ConsecutiveFailures);
    }

    [Fact]
    public async Task FailingFeedBacksOffAndTurnsRed()
    {
        var clock = new FakeClock(T0);
        var f = new FakeFeed("f", TimeSpan.FromMinutes(1)) { Fail = true };
        using var s = new FeedScheduler([f], clock, new NullLog());
        var raised = new List<FeedStatus>();
        s.StatusChanged += (_, st) => raised.Add(st);

        var expected = new[] { (FeedState.Retrying, 30), (FeedState.Retrying, 60), (FeedState.Failing, 120) };
        for (int i = 0; i < expected.Length; i++)
        {
            await s.TickAsync(CancellationToken.None);
            var st = s.Statuses[0];
            Assert.Equal(expected[i].Item1, st.State);
            Assert.Equal(i + 1, st.ConsecutiveFailures);
            Assert.Equal("boom", st.LastError);
            Assert.Equal(clock.UtcNow.AddSeconds(expected[i].Item2), st.NextRunUtc);
            clock.UtcNow = st.NextRunUtc;
        }

        f.Fail = false;
        await s.TickAsync(CancellationToken.None);
        var ok = s.Statuses[0];
        Assert.Equal(FeedState.Ok, ok.State);
        Assert.Equal(0, ok.ConsecutiveFailures);
        Assert.Equal(clock.UtcNow, ok.LastSuccessUtc);
        Assert.Equal(clock.UtcNow.AddMinutes(1), ok.NextRunUtc);
        Assert.Equal(4, raised.Count);
        Assert.Equal(4, f.Runs);
    }

    [Fact]
    public async Task OneFailingFeedDoesNotStopOthers()
    {
        var clock = new FakeClock(T0);
        var bad = new FakeFeed("bad", TimeSpan.FromMinutes(1)) { Fail = true };
        var good = new FakeFeed("good", TimeSpan.FromMinutes(1));
        using var s = new FeedScheduler([bad, good], clock, new NullLog());
        await s.TickAsync(CancellationToken.None);
        Assert.Equal(FeedState.Retrying, s.Statuses[0].State);
        Assert.Equal(FeedState.Ok, s.Statuses[1].State);
    }

    [Fact]
    public void BackoffSequence()
    {
        var secs = Enumerable.Range(1, 6).Select(n => (int)Backoff.Delay(n).TotalSeconds).ToArray();
        Assert.Equal(new[] { 30, 60, 120, 300, 600, 600 }, secs);
    }

    private sealed class CountingLog : IAppLog
    {
        public int Warnings;
        public void Info(string m) { }
        public void Warn(string m, Exception? e = null) => Interlocked.Increment(ref Warnings);
        public void Error(string m, Exception? e = null) { }
    }

    private sealed class HangingFeed : IEventFeed
    {
        public string Name => "hang";
        public TimeSpan Interval => TimeSpan.FromMinutes(1);
        public async Task<FeedRunResult> RunOnceAsync(CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return FeedRunResult.Ok;
        }
    }

    [Fact]
    public async Task StopDuringRunIsNotAFailure()
    {
        var log = new CountingLog();
        using var s = new FeedScheduler([new HangingFeed()], new FakeClock(T0), log);
        int raised = 0;
        s.StatusChanged += (_, _) => raised++;
        using var cts = new CancellationTokenSource();
        var tick = s.TickAsync(cts.Token);
        cts.Cancel();
        await tick;
        Assert.Equal(0, s.Statuses[0].ConsecutiveFailures);
        Assert.Equal(FeedState.NotStarted, s.Statuses[0].State);
        Assert.Equal(0, log.Warnings);
        Assert.Equal(0, raised);
    }

    /// <summary>Starts, then waits on a gate the test opens; honours cancellation.</summary>
    private sealed class BlockedFeed(string name, TimeSpan interval) : IEventFeed
    {
        public readonly TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Name { get; } = name;
        public TimeSpan Interval { get; } = interval;
        public int Runs;
        public async Task<FeedRunResult> RunOnceAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref Runs);
            await Gate.Task.WaitAsync(ct);
            return FeedRunResult.Ok;
        }
    }

    [Fact]
    public async Task SlowFeedDoesNotDelayOthers()
    {
        var clock = new FakeClock(T0);
        var slow = new BlockedFeed("slow", TimeSpan.FromSeconds(60));
        var fast = new FakeFeed("fast", TimeSpan.FromSeconds(60));
        using var s = new FeedScheduler([slow, fast], clock, new NullLog());

        // One loop step starts both and returns at once, without waiting for the blocked feed.
        var first = s.StartDueRuns(CancellationToken.None);
        Assert.Equal(2, first.Count);
        await first[1].WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, fast.Runs);
        Assert.False(first[0].IsCompleted);

        // A minute later the fast feed is due again and runs on schedule; the blocked one is not started twice.
        for (int minute = 1; minute <= 3; minute++)
        {
            clock.UtcNow = T0.AddSeconds(60 * minute);
            var next = s.StartDueRuns(CancellationToken.None);
            _ = Assert.Single(next);
            await next[0].WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(minute + 1, fast.Runs);
            Assert.Equal(clock.UtcNow.AddSeconds(60), s.Statuses[1].NextRunUtc);
        }
        Assert.Equal(1, slow.Runs);
        Assert.Equal(FeedState.NotStarted, s.Statuses[0].State);

        slow.Gate.SetResult();
        await first[0].WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(FeedState.Ok, s.Statuses[0].State);
    }

    /// <summary>Hangs until cancelled, then takes a moment to wind down (so Stop must wait for it).</summary>
    private sealed class SlowToStopFeed : IEventFeed
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public volatile bool Finished;
        public string Name => "slow-stop";
        public TimeSpan Interval => TimeSpan.FromMinutes(1);
        public async Task<FeedRunResult> RunOnceAsync(CancellationToken ct)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally
            {
                Thread.Sleep(200);
                Finished = true;
            }
            return FeedRunResult.Ok;
        }
    }

    [Fact]
    public async Task StopCancelsAndWaitsForInFlightRuns()
    {
        var feed = new SlowToStopFeed();
        var log = new CountingLog();
        var s = new FeedScheduler([feed], new FakeClock(T0), log);
        s.Start();
        await feed.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        s.Stop();
        Assert.True(feed.Finished);                       // Stop waited for the run, not only the loop
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Equal(0, log.Warnings);
    }
}
