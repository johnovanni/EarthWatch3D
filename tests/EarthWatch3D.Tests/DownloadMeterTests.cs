using System.IO;
using System.Threading.Tasks;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.Tests;

public sealed class DownloadMeterTests : IDisposable
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ew3d-meter-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static DateTime Local(int y, int m, int d, int h, int min, int sec = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(y, m, d, h, min, sec, DateTimeKind.Unspecified), Berlin);

    private static DownloadMeter NewMeter(TestClock clock, string? file = null, RecordingLog? log = null) =>
        new(clock, Berlin, file, log ?? new RecordingLog());

    [Fact]
    public void AddsAcrossThreads()
    {
        var m = NewMeter(new TestClock(Local(2026, 10, 24, 12, 0)));
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
        {
            for (int i = 0; i < 10_000; i++) m.Add(10);
        });
        Assert.Equal(800_000, m.TodayBytes);
    }

    [Fact]
    public void ResetsAtLocalMidnightIncludingDstDays()
    {
        var clock = new TestClock(Local(2026, 10, 24, 23, 59));
        var m = NewMeter(clock);
        m.Add(500);
        Assert.Equal(500, m.TodayBytes);

        clock.UtcNow = Local(2026, 10, 25, 0, 0, 1);   // the day the clocks go back
        Assert.Equal(0, m.TodayBytes);
        m.Add(700);
        clock.UtcNow = Local(2026, 10, 25, 23, 59);
        Assert.Equal(700, m.TodayBytes);

        clock.UtcNow = Local(2026, 10, 26, 0, 0);
        Assert.Equal(0, m.TodayBytes);
    }

    [Fact]
    public void LimitReachedOnlyAtOrAboveTheLimit()
    {
        var m = NewMeter(new TestClock(Local(2026, 10, 24, 12, 0)));
        m.Add(1_000_000);
        Assert.False(m.LimitReached);   // no limit
        m.LimitBytes = 1_000_000;
        Assert.True(m.LimitReached);
        var m2 = NewMeter(new TestClock(Local(2026, 10, 24, 12, 0)));
        m2.LimitBytes = 1_000_000;
        m2.Add(999_999);
        Assert.False(m2.LimitReached);
        m2.Add(1);
        Assert.True(m2.LimitReached);
        m2.LimitBytes = null;
        Assert.False(m2.LimitReached);
    }

    [Fact]
    public void SurvivesRestartOnTheSameDayOnly()
    {
        var file = Path.Combine(_dir, "meter.json");
        var clock = new TestClock(Local(2026, 10, 24, 12, 0));
        var m = NewMeter(clock, file);
        m.Add(1234);
        m.Save();

        Assert.Equal(1234, NewMeter(new TestClock(Local(2026, 10, 24, 20, 0)), file).TodayBytes);
        Assert.Equal(0, NewMeter(new TestClock(Local(2026, 10, 25, 0, 1)), file).TodayBytes);

        File.WriteAllText(file, "{not json");
        var log = new RecordingLog();
        Assert.Equal(0, NewMeter(clock, file, log).TodayBytes);
        Assert.Single(log.Warnings);
    }

    [Fact]
    public void FormatsToday()
    {
        Assert.Equal("Today: 0.4 MB", DownloadMeter.Format(400_000));
        Assert.Equal("Today: 123 MB", DownloadMeter.Format(123_456_789));
        Assert.Equal("Today: 1.23 GB", DownloadMeter.Format(1_234_000_000));
        Assert.Equal("Today: 10.3 MB (limit reached)", DownloadMeter.Format(10_300_000, limitReached: true));
    }

    [Fact]
    public void TextSaysWhenTheLimitIsReached()
    {
        var m = NewMeter(new TestClock(Local(2026, 10, 24, 12, 0)));
        m.Add(10_300_000);
        Assert.Equal("Today: 10.3 MB", m.Text);
        m.LimitBytes = 10_000_000;
        Assert.Equal("Today: 10.3 MB (limit reached)", m.Text);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(10, 10_000_000L)]
    [InlineData(100_000, 100_000_000_000L)]
    [InlineData(9, null)]
    [InlineData(0, null)]
    [InlineData(-5, null)]
    [InlineData(100_001, null)]
    public void HandEditedLimitOutsideTheRangeMeansNoLimit(int? mb, long? bytes)
    {
        Assert.Equal(bytes, AppSettings.DailyLimitBytes(mb));
    }
}
