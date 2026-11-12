using System.Globalization;
using AstroCore.Earth;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EarthWatch3D.Core.Geo;

namespace EarthWatch3D.App.ViewModels;

public partial class SunMoonViewModel : ObservableObject
{
    public event EventHandler<GeoPosition>? FaceRequested;

    [ObservableProperty] private GeoPosition _subSolar;
    [ObservableProperty] private GeoPosition _subLunar;
    [ObservableProperty] private string _subSolarText = "";
    [ObservableProperty] private string _subLunarText = "";
    [ObservableProperty] private double _moonDistanceKm;
    [ObservableProperty] private string _moonDistanceText = "";
    [ObservableProperty] private string _moonDistanceToolTip = "";
    [ObservableProperty] private double _sunDistanceKm;
    [ObservableProperty] private string _sunDistanceText = "";
    [ObservableProperty] private string _sunDistanceToolTip = "";
    [ObservableProperty] private int _moonIlluminatedPercent;
    [ObservableProperty] private bool _moonWaxing;
    [ObservableProperty] private bool _isCollapsed;

    /// <summary>Recomputes the card and markers for the given (display) time.</summary>
    public void Refresh(DateTime t)
    {
        SubSolar = SubPoints.SubSolarPoint(t);
        SubLunar = SubPoints.SubLunarPoint(t);
        SubSolarText = GeoFormat.LatLon(SubSolar);
        SubLunarText = GeoFormat.LatLon(SubLunar);
        MoonDistanceKm = SubPoints.Moon(t).DistanceKm;
        MoonDistanceText = string.Format(CultureInfo.InvariantCulture, "{0:N0} km", MoonDistanceKm);
        MoonDistanceToolTip = string.Format(CultureInfo.InvariantCulture,
            "From Earth's centre to the Moon's centre:\n{0:N0} km\n{1:N0} miles\n"
            + "The point where the Moon is overhead is 6,371 km (3,959 miles) closer.", MoonDistanceKm, MoonDistanceKm / KmPerMile);
        // Both distances are geocentric (centre to centre), as in JPL Horizons with Earth's centre as the observer.
        SunDistanceKm = SubPoints.Sun(t).DistanceKm;
        SunDistanceText = string.Format(CultureInfo.InvariantCulture, "{0:F1} million km", SunDistanceKm / 1e6);
        SunDistanceToolTip = string.Format(CultureInfo.InvariantCulture,
            "From Earth's centre to the Sun's centre:\n{0:F2} million km\n{1:F2} million miles\n{2:F5} AU",
            SunDistanceKm / 1e6, SunDistanceKm / KmPerMile / 1e6, SunDistanceKm / KmPerAu);
        MoonIlluminatedPercent = (int)Math.Round(SubPoints.MoonIlluminatedFraction(t) * 100);
        MoonWaxing = SubPoints.MoonIsWaxing(t);
    }

    private const double KmPerMile = 1.609344;        // international mile
    private const double KmPerAu = 149_597_870.7;     // IAU 2012

    [RelayCommand] private void FaceSun() => FaceRequested?.Invoke(this, SubSolar);
    [RelayCommand] private void FaceMoon() => FaceRequested?.Invoke(this, SubLunar);
    [RelayCommand] private void ToggleCollapsed() => IsCollapsed = !IsCollapsed;
}
