using CommunityToolkit.Mvvm.ComponentModel;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.App.ViewModels;

/// <summary>The layer strip's toggles. Starts from the saved settings; any change is saved.</summary>
public partial class LayersViewModel : ObservableObject
{
    private readonly ISettingsStore _store;

    [ObservableProperty] private bool _showQuakes;
    [ObservableProperty] private bool _showCyclones;
    [ObservableProperty] private bool _showAlerts;
    [ObservableProperty] private bool _showEonet;
    [ObservableProperty] private bool _showCoastlines;
    /// <summary>The strip shows text labels beside its icons.</summary>
    [ObservableProperty] private bool _isExpanded;

    public LayersViewModel(ISettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        var s = store.Load();
        _showQuakes = s.ShowQuakes;
        _showCyclones = s.ShowCyclones;
        _showAlerts = s.ShowAlerts;
        _showEonet = s.ShowEonet;
        _showCoastlines = s.ShowCoastlines;
        _isExpanded = s.LayerStripExpanded;
    }

    partial void OnShowQuakesChanged(bool value) => Save();
    partial void OnShowCyclonesChanged(bool value) => Save();
    partial void OnShowAlertsChanged(bool value) => Save();
    partial void OnShowEonetChanged(bool value) => Save();
    partial void OnShowCoastlinesChanged(bool value) => Save();
    partial void OnIsExpandedChanged(bool value) => Save();

    private void Save() => _store.Save(_store.Load() with
    {
        ShowQuakes = ShowQuakes,
        ShowCyclones = ShowCyclones,
        ShowAlerts = ShowAlerts,
        ShowEonet = ShowEonet,
        ShowCoastlines = ShowCoastlines,
        LayerStripExpanded = IsExpanded,
    });
}
