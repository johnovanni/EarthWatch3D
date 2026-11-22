using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using AstroCore.Earth;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.App.ViewModels;
using GlobeCore.Earth;
using GlobeCore.Wpf.Layers;
using GlobeCore.Wpf.Rendering;

namespace EarthWatch3D.App.Layers;

/// <summary>
/// The Sun and Moon on the overlay canvas (<see cref="SunMoonOverlay"/>): an icon where each is in view, or an edge
/// arrow pointing to it, kept clear of the Sun &amp; Moon card. (Moved from SceneController: the overlay half of
/// UpdateSunMoon, CardBounds, and HideAll while minimized.)
/// </summary>
public sealed class SunMoonOverlayLayer : OverlayLayer
{
    public const string LayerId = "sun-moon-arrows";
    public const int Order = 210;

    private readonly SunMoonOverlay _overlay;
    private readonly Canvas _canvas;
    private readonly FrameworkElement _card;
    private readonly SunMoonViewModel _sm;

    /// <param name="overlay">The canvas; the icons, arrows and labels are added to it now.</param>
    /// <param name="card">The Sun &amp; Moon card, which the edge arrows keep clear of. It and <paramref name="overlay"/> must share a parent.</param>
    public SunMoonOverlayLayer(Canvas overlay, FrameworkElement card, SunMoonViewModel sunMoon) : base(LayerId, "Sun and Moon arrows", Order)
    {
        _canvas = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _card = card ?? throw new ArgumentNullException(nameof(card));
        _sm = sunMoon ?? throw new ArgumentNullException(nameof(sunMoon));
        _overlay = new SunMoonOverlay(overlay);
        _sm.PropertyChanged += OnSunMoonChanged;
        _card.SizeChanged += OnCardSizeChanged;   // collapsing the card changes the area the arrows avoid
    }

    public override void Render(Canvas overlay, Projector projector)
    {
        var sm = _sm;
        double fraction = sm.MoonIlluminatedPercent / 100.0;
        var sun = sm.SubSolar.ToUnitVector();
        var moon = sm.SubLunar.ToUnitVector();
        double moonR = sm.MoonDistanceKm / EarthBody.RadiusKm;
        _overlay.Update(projector, new Vector3D(sun.X, sun.Y, sun.Z),
            new Point3D(moon.X * moonR, moon.Y * moonR, moon.Z * moonR), fraction, sm.MoonWaxing, CardBounds());
    }

    /// <summary>Hides every element (the window was minimized).</summary>
    public void HideAll() => _overlay.HideAll();

    protected override void Clear(Canvas overlay) => _overlay.HideAll();

    /// <summary>Stops listening to the view model and the card (the window has closed).</summary>
    public void Close()
    {
        _sm.PropertyChanged -= OnSunMoonChanged;
        _card.SizeChanged -= OnCardSizeChanged;
    }

    private void OnSunMoonChanged(object? sender, PropertyChangedEventArgs e) => Invalidate();
    private void OnCardSizeChanged(object sender, SizeChangedEventArgs e) => Invalidate();

    /// <summary>The card's rectangle in overlay coordinates, or Rect.Empty if it is not shown. Allocation-free.</summary>
    private Rect CardBounds()
    {
        if (!_card.IsVisible || _card.ActualWidth <= 0 || _card.ActualHeight <= 0) return Rect.Empty;
        // Both are children of the same grid, so the difference of their offsets in it is the card's offset in the overlay.
        var o = VisualTreeHelper.GetOffset(_card) - VisualTreeHelper.GetOffset(_canvas);
        return new Rect(o.X, o.Y, _card.ActualWidth, _card.ActualHeight);
    }
}
