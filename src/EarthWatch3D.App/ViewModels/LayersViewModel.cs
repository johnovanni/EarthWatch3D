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

    partial void OnShowQuakesChanged(bool value) => Save("quakes");
    partial void OnShowCyclonesChanged(bool value) => Save("cyclones");
    partial void OnShowAlertsChanged(bool value) => Save("alerts");
    partial void OnShowEonetChanged(bool value) => Save("eonet");
    partial void OnShowCoastlinesChanged(bool value) => Save("coastlines");
    partial void OnShowPlatesChanged(bool value) => Save("plates");
    partial void OnIsExpandedChanged(bool value) => Save(null);

    // Layers turned on for this run only (--layers) and the saved value each must keep until the user toggles it.
    private readonly Dictionary<string, bool> _transient = [];
    private bool _applying;

    /// <summary>
    /// Switches the named layers on for this run only (the <c>--layers</c> diagnostic): their saved values are kept, so nothing
    /// persists unless the user toggles that layer. Names: quakes, cyclones, alerts, eonet, coastlines, plates.
    /// </summary>
    public void TurnOnForThisRun(IEnumerable<string> names)
    {
        _applying = true;
        try
        {
            foreach (var n in names.Select(x => x.ToLowerInvariant()))
            {
                bool? saved = n switch
                {
                    "quakes" => ShowQuakes, "cyclones" => ShowCyclones, "alerts" => ShowAlerts,
                    "eonet" => ShowEonet, "coastlines" => ShowCoastlines, "plates" => ShowPlates, _ => null,
                };
                if (saved is null) continue;
                _transient.TryAdd(n, saved.Value);
                switch (n)
                {
                    case "quakes": ShowQuakes = true; break;
                    case "cyclones": ShowCyclones = true; break;
                    case "alerts": ShowAlerts = true; break;
                    case "eonet": ShowEonet = true; break;
                    case "coastlines": ShowCoastlines = true; break;
                    case "plates": ShowPlates = true; break;
                }
            }
        }
        finally { _applying = false; }
    }

    private void Save(string? changed)
    {
        if (_applying) return;
        if (changed is not null) _transient.Remove(changed);
        bool P(string n, bool current) => _transient.TryGetValue(n, out var saved) ? saved : current;
        _store.Save(_store.Load() with
        {
            ShowQuakes = P("quakes", ShowQuakes),
            ShowCyclones = P("cyclones", ShowCyclones),
            ShowAlerts = P("alerts", ShowAlerts),
            ShowEonet = P("eonet", ShowEonet),
            ShowCoastlines = P("coastlines", ShowCoastlines),
            ShowPlates = P("plates", ShowPlates),
            LayerStripExpanded = IsExpanded,
        });
    }
}
