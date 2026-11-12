using AstroCore.Earth;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Tests;

public class SunMoonViewModelTests
{
    [Fact]
    public void MatchesAstroCoreAtFixedTime()
    {
        var t = new DateTime(2026, 12, 21, 12, 0, 0, DateTimeKind.Utc);
        var vm = new SunMoonViewModel(); vm.Refresh(t);
        Assert.Equal(SubPoints.SubSolarPoint(t), vm.SubSolar);
        Assert.Equal(SubPoints.SubLunarPoint(t), vm.SubLunar);
        Assert.Equal((int)Math.Round(SubPoints.MoonIlluminatedFraction(t) * 100), vm.MoonIlluminatedPercent);
        Assert.Equal(GeoFormat.LatLon(vm.SubSolar), vm.SubSolarText);
        Assert.EndsWith(" km", vm.MoonDistanceText);
        Assert.InRange(vm.MoonDistanceKm, 350000, 410000);
    }

    [Fact]
    public void RefreshUsesTheGivenTime()
    {
        var t = new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);
        var vm = new SunMoonViewModel(); vm.Refresh(t);
        var later = t.AddHours(5); vm.Refresh(later);
        Assert.Equal(SubPoints.SubSolarPoint(later), vm.SubSolar);
    }

    [Fact]
    public void FaceSunRaisesWithSubSolarPoint()
    {
        var vm = new SunMoonViewModel(); vm.Refresh(new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc));
        GeoPosition? got = null; vm.FaceRequested += (_, p) => got = p;
        vm.FaceSunCommand.Execute(null);
        Assert.Equal(vm.SubSolar, got);
    }

    [Fact]
    public void FaceMoonRaisesWithSubLunarPoint()
    {
        var vm = new SunMoonViewModel(); vm.Refresh(new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc));
        GeoPosition? got = null; vm.FaceRequested += (_, p) => got = p;
        vm.FaceMoonCommand.Execute(null);
        Assert.Equal(vm.SubLunar, got);
    }

    [Fact]
    public void RefreshRaisesPropertyChanged()
    {
        var vm = new SunMoonViewModel();
        var names = new List<string?>();
        vm.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        vm.Refresh(new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc));
        foreach (var n in new[] { nameof(vm.SubSolar), nameof(vm.SubLunar), nameof(vm.SubSolarText), nameof(vm.SubLunarText),
                                  nameof(vm.MoonDistanceText), nameof(vm.MoonDistanceKm), nameof(vm.MoonIlluminatedPercent) })
            Assert.Contains(n, names);
    }
}
