using System.ComponentModel;
using System.Windows;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.App.ViewModels;
using GlobeCore.Wpf.Layers;
using GlobeCore.Wpf.Rendering;

namespace EarthWatch3D.App.Layers;

/// <summary>Which Sun/Moon globe marker a pick hit.</summary>
public enum SunMoonMarker { Sun, Moon }

/// <summary>
/// The small camera-facing Sun and Moon markers on the globe, at the points directly under them
/// (<see cref="SunMoonGlobeMarkers"/>), and their hover tooltips (<see cref="MarkerHover"/>). A marker is picked before
/// any event under it. (Moved from SceneController: the markers half of UpdateSunMoon and the marker half of UpdateHover.)
/// </summary>
public sealed class SunMoonSceneLayer : SceneLayer, IPickable
{
    public const string LayerId = "sun-moon-markers";
    public const int Order = 120;

    private readonly SunMoonGlobeMarkers _markers = new();
    private readonly SunMoonViewModel _sm;

    public SunMoonSceneLayer(SunMoonViewModel sunMoon) : base(LayerId, "Sun and Moon markers", Order)
    {
        _sm = sunMoon ?? throw new ArgumentNullException(nameof(sunMoon));
        foreach (var v in _markers.Visuals) Root.Children.Add(v);
        _sm.PropertyChanged += OnSunMoonChanged;
    }

    public override void Update(SceneContext ctx) =>
        _markers.Update(_sm.SubSolar, _sm.SubLunar, _sm.MoonIlluminatedPercent / 100.0, _sm.MoonWaxing);

    public PickResult? HitTest(Point screen, Projector projector, bool hover) =>
        MarkerHover.Hits(screen, projector, _sm.SubSolar) ? new PickResult(LayerId, SunMoonMarker.Sun, MarkerHover.SunText(_sm.SubSolar))
        : MarkerHover.Hits(screen, projector, _sm.SubLunar) ? new PickResult(LayerId, SunMoonMarker.Moon, MarkerHover.MoonText(_sm.SubLunar))
        : null;

    /// <summary>Stops listening to the view model (the window has closed).</summary>
    public void Close() => _sm.PropertyChanged -= OnSunMoonChanged;

    private void OnSunMoonChanged(object? sender, PropertyChangedEventArgs e) => Invalidate();
}
