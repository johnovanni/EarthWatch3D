using System.IO;
using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Tests;

public sealed class FileLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ew3d-log-" + Guid.NewGuid().ToString("N"));

    public FileLogTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private sealed class FixedClock(DateTime t) : IClock { public DateTime UtcNow => t; }

    [Fact]
    public void FileLogWritesAndPrunesOldFiles()
    {
        var now = new DateTime(2026, 11, 12, 14, 3, 5, 123, DateTimeKind.Utc);
        var old = Path.Combine(_dir, "earthwatch3d-20261101.log");
        var recent = Path.Combine(_dir, "earthwatch3d-20261110.log");
        var other = Path.Combine(_dir, "other.log");
        File.WriteAllText(old, "x"); File.SetLastWriteTimeUtc(old, now.AddDays(-8));
        File.WriteAllText(recent, "x"); File.SetLastWriteTimeUtc(recent, now.AddDays(-2));
        File.WriteAllText(other, "x"); File.SetLastWriteTimeUtc(other, now.AddDays(-30));

        using (var log = new FileLog(_dir, new FixedClock(now)))
        {
            log.Info("hello");
            log.Warn("careful", new InvalidOperationException("boom"));
            log.Error("bad");
        }

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(other));

        var file = Directory.GetFiles(_dir, "earthwatch3d-*.log").Single(f => f != recent);
        Assert.Equal($"earthwatch3d-{now.ToLocalTime():yyyyMMdd}.log", Path.GetFileName(file));
        var lines = File.ReadAllLines(file);
        Assert.Equal(3, lines.Length);
        Assert.Equal("2026-11-12T14:03:05.123Z INFO hello", lines[0]);
        Assert.StartsWith("2026-11-12T14:03:05.123Z WARN careful | ", lines[1]);
        Assert.Contains("boom", lines[1]);
        Assert.StartsWith("2026-11-12T14:03:05.123Z ERROR bad", lines[2]);
    }

    [Fact]
    public async Task ConcurrentWritesDoNotInterleave()
    {
        var now = new DateTime(2026, 11, 12, 14, 0, 0, DateTimeKind.Utc);
        using (var log = new FileLog(_dir, new FixedClock(now)))
            await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
            {
                for (var i = 0; i < 50; i++) log.Info($"t{t} line {i}");
            })));
        var file = Directory.GetFiles(_dir, "earthwatch3d-*.log").Single();
        var lines = File.ReadAllLines(file);
        Assert.Equal(400, lines.Length);
        Assert.All(lines, l => Assert.Matches(@"^\S+Z INFO t\d line \d+$", l));
    }
}
