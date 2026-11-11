using System.Globalization;
using AstroCore.Earth;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.App.ViewModels;

public partial class SunMoonViewModel : ObservableObject
{
    private readonly IClock _clock;

    public SunMoonViewModel(IClock clock) => _clock = clock;

    public event EventHandler<GeoPosition>? FaceRequested;

    [ObservableProperty] private GeoPosition _subSolar;
    [ObservableProperty] private GeoPosition _subLunar;
    [ObservableProperty] private string _subSolarText = "";
    [ObservableProperty] private string _subLunarText = "";
    [ObservableProperty] private double _moonDistanceKm;
    [ObservableProperty] private string _moonDistanceText = "";
    [ObservableProperty] private int _moonIlluminatedPercent;
    [ObservableProperty] private bool _moonWaxing;
    [ObservableProperty] private bool _isCollapsed;

    public void Refresh()
    {
        var t = _clock.UtcNow;
        SubSolar = SubPoints.SubSolarPoint(t);
        SubLunar = SubPoints.SubLunarPoint(t);
        SubSolarText = GeoFormat.LatLon(SubSolar);
        SubLunarText = GeoFormat.LatLon(SubLunar);
        MoonDistanceKm = SubPoints.Moon(t).DistanceKm;
        MoonDistanceText = string.Format(CultureInfo.InvariantCulture, "{0:N0} km", MoonDistanceKm);
        MoonIlluminatedPercent = (int)Math.Round(SubPoints.MoonIlluminatedFraction(t) * 100);
        MoonWaxing = SubPoints.MoonIsWaxing(t);
    }

    [RelayCommand] private void FaceSun() => FaceRequested?.Invoke(this, SubSolar);
    [RelayCommand] private void FaceMoon() => FaceRequested?.Invoke(this, SubLunar);
    [RelayCommand] private void ToggleCollapsed() => IsCollapsed = !IsCollapsed;
}
