using AstroCore.Earth;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Tests;

public class SunMoonViewModelTests
{
    private sealed class FakeClock(DateTime t) : IClock { public DateTime UtcNow { get; set; } = t; }

    [Fact]
    public void MatchesAstroCoreAtFixedTime()
    {
        var t = new DateTime(2026, 12, 21, 12, 0, 0, DateTimeKind.Utc);
        var vm = new SunMoonViewModel(new FakeClock(t)); vm.Refresh();
        Assert.Equal(SubPoints.SubSolarPoint(t), vm.SubSolar);
        Assert.Equal(SubPoints.SubLunarPoint(t), vm.SubLunar);
        Assert.Equal((int)Math.Round(SubPoints.MoonIlluminatedFraction(t) * 100), vm.MoonIlluminatedPercent);
        Assert.Equal(GeoFormat.LatLon(vm.SubSolar), vm.SubSolarText);
        Assert.EndsWith(" km", vm.MoonDistanceText);
        Assert.InRange(vm.MoonDistanceKm, 350000, 410000);
    }

    [Fact] // Review Focus 5: a clock jump (sleep/resume) shows the new time immediately
    public void RefreshAfterClockJumpUsesCurrentTime()
    {
        var clock = new FakeClock(new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc));
        var vm = new SunMoonViewModel(clock); vm.Refresh();
        clock.UtcNow = clock.UtcNow.AddHours(5); vm.Refresh();
        Assert.Equal(SubPoints.SubSolarPoint(clock.UtcNow), vm.SubSolar);
    }

    [Fact]
    public void FaceSunRaisesWithSubSolarPoint()
    {
        var vm = new SunMoonViewModel(new FakeClock(new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc))); vm.Refresh();
        GeoPosition? got = null; vm.FaceRequested += (_, p) => got = p;
        vm.FaceSunCommand.Execute(null);
        Assert.Equal(vm.SubSolar, got);
    }
}
