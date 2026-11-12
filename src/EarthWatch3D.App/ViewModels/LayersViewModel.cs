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
    [ObservableProperty] private bool _showPlates;
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
        _showPlates = s.ShowPlates;
        _isExpanded = s.LayerStripExpanded;
    }

    partial void OnShowQuakesChanged(bool value) => Save();
    partial void OnShowCyclonesChanged(bool value) => Save();
    partial void OnShowAlertsChanged(bool value) => Save();
    partial void OnShowEonetChanged(bool value) => Save();
    partial void OnShowCoastlinesChanged(bool value) => Save();
    partial void OnShowPlatesChanged(bool value) => Save();
    partial void OnIsExpandedChanged(bool value) => Save();

    private bool _transient;

    /// <summary>
    /// Switches the named layers on for this run only (the <c>--layers</c> diagnostic): nothing is saved.
    /// Names: quakes, cyclones, alerts, eonet, coastlines, plates.
    /// </summary>
    public void TurnOnForThisRun(IEnumerable<string> names)
    {
        _transient = true;
        try
        {
            foreach (var n in names)
                switch (n.ToLowerInvariant())
                {
                    case "quakes": ShowQuakes = true; break;
                    case "cyclones": ShowCyclones = true; break;
                    case "alerts": ShowAlerts = true; break;
                    case "eonet": ShowEonet = true; break;
                    case "coastlines": ShowCoastlines = true; break;
                    case "plates": ShowPlates = true; break;
                }
        }
        finally { _transient = false; }
    }

    private void Save()
    {
        if (_transient) return;
        _store.Save(_store.Load() with
        {
            ShowQuakes = ShowQuakes,
            ShowCyclones = ShowCyclones,
            ShowAlerts = ShowAlerts,
            ShowEonet = ShowEonet,
            ShowCoastlines = ShowCoastlines,
            ShowPlates = ShowPlates,
            LayerStripExpanded = IsExpanded,
        });
    }
}
