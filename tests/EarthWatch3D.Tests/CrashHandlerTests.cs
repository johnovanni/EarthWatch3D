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
