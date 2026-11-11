using EarthWatch3D.App.Controllers;

namespace EarthWatch3D.Tests;

public class RecomposeScheduleTests
{
    private static readonly DateTime T0 = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact] public void DueInitially() => Assert.True(new RecomposeSchedule(TimeSpan.FromMinutes(1)).IsDue(T0));

    [Fact] public void NotDueWithinInterval()
    { var s = new RecomposeSchedule(TimeSpan.FromMinutes(1)); s.MarkComposed(T0); Assert.False(s.IsDue(T0.AddSeconds(59))); Assert.True(s.IsDue(T0.AddSeconds(60))); }

    [Fact] // Review Focus 5: sleep/resume or clock change
    public void DueAfterLargeJumpOrBackwardsClock()
    { var s = new RecomposeSchedule(TimeSpan.FromMinutes(1)); s.MarkComposed(T0); Assert.True(s.IsDue(T0.AddHours(6))); Assert.True(s.IsDue(T0.AddMinutes(-5))); }
}
