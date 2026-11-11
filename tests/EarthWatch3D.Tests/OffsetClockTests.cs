using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Tests;

public class OffsetClockTests
{
    private sealed class FixedClock(DateTime t) : IClock { public DateTime UtcNow { get; set; } = t; }

    private static readonly DateTime T0 = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void StartsWithNoOffset()
    {
        var c = new OffsetClock(new FixedClock(T0));
        Assert.Equal(TimeSpan.Zero, c.Offset);
        Assert.Equal(T0, c.UtcNow);
    }

    [Fact]
    public void AddsTheOffsetToTheInnerClock()
    {
        var inner = new FixedClock(T0);
        var c = new OffsetClock(inner) { Offset = TimeSpan.FromHours(6) };
        Assert.Equal(T0.AddHours(6), c.UtcNow);
        inner.UtcNow = T0.AddMinutes(1);
        Assert.Equal(T0.AddHours(6).AddMinutes(1), c.UtcNow);
        Assert.Equal(DateTimeKind.Utc, c.UtcNow.Kind);
    }

    [Fact]
    public void RejectsANullClock() => Assert.Throws<ArgumentNullException>(() => new OffsetClock(null!));
}
