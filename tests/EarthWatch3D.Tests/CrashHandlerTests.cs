using System.IO;
using EarthWatch3D.App.Diagnostics;

namespace EarthWatch3D.Tests;

public class CrashHandlerTests
{
    [Fact]
    public void CrashHandlerLogsAllShowsOnce()
    {
        var log = new RecordingLog();
        var shown = new List<string>();
        var handler = new CrashHandler(log, shown.Add);

        handler.Handle(new InvalidOperationException("first"));
        handler.Handle(new ArgumentException("second"));
        handler.Handle(new IOException("third"));

        Assert.Equal(3, log.Errors.Count);
        Assert.Contains("first", log.Errors[0]);
        Assert.Contains("third", log.Errors[2]);
        var message = Assert.Single(shown);
        Assert.StartsWith("Something went wrong; details are in the log file", message);
    }

    [Fact]
    public void HandleNeverThrowsEvenIfTheLogOrTheMessageThrows()
    {
        var handler = new CrashHandler(new ThrowingLog(), _ => throw new InvalidOperationException("no UI"));
        handler.Handle(new Exception("boom"));   // must not throw
        handler.Handle(new Exception("again"));
    }

    [Fact]
    public void ConcurrentFirstExceptionsShowOneMessage()
    {
        var log = new LockedLog();
        int shown = 0;
        var handler = new CrashHandler(log, _ => Interlocked.Increment(ref shown));
        Parallel.For(0, 64, i => handler.Handle(new Exception("e" + i)));
        Assert.Equal(1, shown);
        Assert.Equal(64, log.Count);
    }

    private static Exception Thrown(string message)
    {
        try { throw new InvalidOperationException(message); }
        catch (InvalidOperationException e) { return e; }
    }

    [Fact]
    public void RepeatsAreSummarisedNotLogged()
    {
        var clock = new TestClock(new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc));
        var log = new RecordingLog();
        var handler = new CrashHandler(log, _ => { }, clock);

        // The same fault every frame (60 a second) for 30 s: one full entry, nothing more yet.
        for (int i = 0; i < 1800; i++) { handler.Handle(Thrown("same")); clock.UtcNow = clock.UtcNow.AddMilliseconds(1000.0 / 60); }
        Assert.Single(log.Errors);
        Assert.Empty(log.Warnings);

        // Past the minute, the next repeat writes one summary line with the count.
        clock.UtcNow = clock.UtcNow.AddSeconds(31);
        handler.Handle(Thrown("same"));
        Assert.Single(log.Errors);
        var summary = Assert.Single(log.Warnings);
        Assert.Contains("repeated 1800 times", summary);
        Assert.Contains("same", summary);

        // A different fault is logged in full at once.
        handler.Handle(Thrown("other"));
        Assert.Equal(2, log.Errors.Count);

        // Repeats still pending are written on flush (the app calls it on exit).
        handler.Handle(Thrown("other"));
        handler.FlushRepeats();
        Assert.Contains(log.Warnings, w => w.Contains("repeated 1 time") && w.Contains("other"));
        Assert.Equal(2, log.Warnings.Count);   // nothing new for "same" since its summary
    }

    [Fact]
    public void TooManyErrorsStopsHandling()
    {
        var clock = new TestClock(new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc));
        var log = new RecordingLog();
        var shown = new List<string>();
        var handler = new CrashHandler(log, shown.Add, clock);

        // 50 within 10 s are still handled (the app keeps running) ...
        for (int i = 0; i < 50; i++) { Assert.True(handler.HandleDispatcher(Thrown("x" + i))); clock.UtcNow = clock.UtcNow.AddMilliseconds(100); }
        Assert.Single(shown);
        // ... the 51st within 10 s is not: the app is allowed to close, and stays that way.
        Assert.False(handler.HandleDispatcher(Thrown("one more")));
        Assert.Contains(log.Errors, e => e.Contains("too many errors, closing"));
        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        Assert.False(handler.HandleDispatcher(Thrown("later")));
        Assert.Single(shown);
    }

    [Fact]
    public void TooManyErrorsShowsTheMessageIfNotYetShown()
    {
        var clock = new TestClock(new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc));
        var shown = new List<string>();
        int calls = 0;
        // Every attempt to show fails until the limit trips (no UI yet), so the message has not been seen by then.
        var handler = new CrashHandler(new RecordingLog(), m => { if (calls++ < 50) throw new InvalidOperationException("UI busy"); shown.Add(m); }, clock);
        for (int i = 0; i < 51; i++) handler.HandleDispatcher(Thrown("x"));
        Assert.Contains(shown, m => m.Contains("has to close"));
    }

    [Fact]
    public void SlowSteadyErrorsNeverTrip()
    {
        var clock = new TestClock(new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc));
        var handler = new CrashHandler(new RecordingLog(), _ => { }, clock);
        for (int i = 0; i < 500; i++) { Assert.True(handler.HandleDispatcher(Thrown("slow"))); clock.UtcNow = clock.UtcNow.AddMilliseconds(250); }
    }

    private sealed class ThrowingLog : EarthWatch3D.Core.Logging.IAppLog
    {
        public void Info(string m) => throw new IOException("disk full");
        public void Warn(string m, Exception? e = null) => throw new IOException("disk full");
        public void Error(string m, Exception? e = null) => throw new IOException("disk full");
    }

    private sealed class LockedLog : EarthWatch3D.Core.Logging.IAppLog
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Info(string m) { }
        public void Warn(string m, Exception? e = null) { }
        public void Error(string m, Exception? e = null) => Interlocked.Increment(ref _count);
    }
}
