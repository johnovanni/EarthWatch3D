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
    public void SunDistanceAndMilesTooltips()
    {
        var t = new DateTime(2026, 12, 21, 12, 0, 0, DateTimeKind.Utc);
        var vm = new SunMoonViewModel(); vm.Refresh(t);
        double sunKm = SubPoints.Sun(t).DistanceKm, moonKm = SubPoints.Moon(t).DistanceKm;
        Assert.Equal(sunKm, vm.SunDistanceKm);
        Assert.InRange(sunKm, 147.0e6, 148.0e6);   // near perihelion (early January)
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Equal((sunKm / 1e6).ToString("F1", inv) + " million km", vm.SunDistanceText);
        Assert.Equal("From Earth's centre to the Sun's centre:\n"
            + (sunKm / 1e6).ToString("F2", inv) + " million km\n"
            + (sunKm / 1.609344 / 1e6).ToString("F2", inv) + " million miles\n"
            + (sunKm / 149_597_870.7).ToString("F5", inv) + " AU", vm.SunDistanceToolTip);
        Assert.Equal("From Earth's centre to the Moon's centre:\n"
            + moonKm.ToString("N0", inv) + " km\n"
            + (moonKm / 1.609344).ToString("N0", inv) + " miles\n"
            + "The point where the Moon is overhead is 6,371 km (3,959 miles) closer.", vm.MoonDistanceToolTip);
    }

    [Fact]
    public void DistancesFollowUnits()
    {
        var vm = new SunMoonViewModel { Units = DistanceUnits.Miles };
        vm.Refresh(new DateTime(2026, 12, 21, 12, 0, 0, DateTimeKind.Utc));
        Assert.EndsWith(" mi", vm.MoonDistanceText);
        Assert.Equal(Units.Distance(vm.MoonDistanceKm, DistanceUnits.Miles), vm.MoonDistanceText);
        Assert.Equal(Units.Large(vm.SunDistanceKm, DistanceUnits.Miles), vm.SunDistanceText);
        Assert.Contains("million miles", vm.SunDistanceToolTip);   // tooltips keep both systems
        Assert.Contains(" km", vm.MoonDistanceToolTip);
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
                                  nameof(vm.MoonDistanceText), nameof(vm.MoonDistanceKm), nameof(vm.MoonIlluminatedPercent),
                                  nameof(vm.SunDistanceKm), nameof(vm.SunDistanceText), nameof(vm.SunDistanceToolTip), nameof(vm.MoonDistanceToolTip) })
            Assert.Contains(n, names);
    }
}
