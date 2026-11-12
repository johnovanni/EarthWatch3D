using CommunityToolkit.Mvvm.ComponentModel;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.App.ViewModels;

/// <summary>The ⋯ Display flyout's choices. Starts from the saved settings; any change is saved.</summary>
public partial class DisplayOptionsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;
    private double _minMagnitude;
    private int _quakeWindowDays;
    private double _nightStrength;
    private int _lightningWindowMinutes;

    [ObservableProperty] private bool _showWarnings;
    [ObservableProperty] private bool _showWatches;
    [ObservableProperty] private bool _showAdvisories;
    [ObservableProperty] private bool _showStatements;
    /// <summary>The soft gold day/night line.</summary>
    [ObservableProperty] private bool _showTerminator;

    public DisplayOptionsViewModel(ISettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        var s = store.Load();
        _minMagnitude = s.MinMagnitude;
        _quakeWindowDays = s.QuakeWindowDays == 7 ? 7 : 1;
        _nightStrength = Math.Clamp(double.IsNaN(s.NightStrength) ? 0.85 : s.NightStrength, 0, 1);
        _showWarnings = s.AlertTypes.HasFlag(AlertTypes.Warnings);
        _showWatches = s.AlertTypes.HasFlag(AlertTypes.Watches);
        _showAdvisories = s.AlertTypes.HasFlag(AlertTypes.Advisories);
        _showStatements = s.AlertTypes.HasFlag(AlertTypes.Statements);
        _showTerminator = s.ShowTerminator;
        _lightningWindowMinutes = LightningWindowChoices.Contains(s.LightningWindowMinutes) ? s.LightningWindowMinutes : DefaultLightningWindowMinutes;
    }

    /// <summary>Minimum-magnitude choices; NaN is "All".</summary>
    public IReadOnlyList<(string Label, double Value)> MagnitudeChoices { get; } =
        [("All", double.NaN), ("1", 1), ("2.5", 2.5), ("4", 4), ("5", 5), ("6", 6)];

    /// <summary>Minimum earthquake magnitude; NaN means "All".</summary>
    public double MinMagnitude
    {
        get => _minMagnitude;
        set
        {
            if (!SetProperty(ref _minMagnitude, value)) return;
            OnPropertyChanged(nameof(MagnitudeIndex));
            Save();
        }
    }

    /// <summary>Index of <see cref="MinMagnitude"/> in <see cref="MagnitudeChoices"/>, or −1 if it is not one of them.</summary>
    public int MagnitudeIndex
    {
        get
        {
            for (int i = 0; i < MagnitudeChoices.Count; i++)
                if (MagnitudeChoices[i].Value.Equals(MinMagnitude)) return i;   // Equals treats NaN as equal to NaN
            return -1;
        }
        set
        {
            if (value >= 0 && value < MagnitudeChoices.Count) MinMagnitude = MagnitudeChoices[value].Value;
        }
    }

    /// <summary>Earthquake display window in days: 1 or 7 (anything else becomes 1).</summary>
    public int QuakeWindowDays
    {
        get => _quakeWindowDays;
        set
        {
            if (SetProperty(ref _quakeWindowDays, value == 7 ? 7 : 1)) Save();
        }
    }

    public const int DefaultLightningWindowMinutes = 15;

    /// <summary>Minutes of lightning kept on the globe.</summary>
    public IReadOnlyList<int> LightningWindowChoices { get; } = [5, 10, 15, 30, 60];

    /// <summary>How long a flash stays on the globe, in minutes: one of <see cref="LightningWindowChoices"/> (anything else becomes 15).</summary>
    public int LightningWindowMinutes
    {
        get => _lightningWindowMinutes;
        set
        {
            if (!SetProperty(ref _lightningWindowMinutes, LightningWindowChoices.Contains(value) ? value : DefaultLightningWindowMinutes)) return;
            Save();
        }
    }

    /// <summary>Night-shade strength, 0 to 1 (clamped).</summary>
    public double NightStrength
    {
        get => _nightStrength;
        set
        {
            if (double.IsNaN(value)) return;
            if (SetProperty(ref _nightStrength, Math.Clamp(value, 0, 1))) Save();
        }
    }

    public AlertTypes AlertTypes =>
        (ShowWarnings ? AlertTypes.Warnings : AlertTypes.None) |
        (ShowWatches ? AlertTypes.Watches : AlertTypes.None) |
        (ShowAdvisories ? AlertTypes.Advisories : AlertTypes.None) |
        (ShowStatements ? AlertTypes.Statements : AlertTypes.None);

    partial void OnShowWarningsChanged(bool value) => AlertTypesChanged();
    partial void OnShowWatchesChanged(bool value) => AlertTypesChanged();
    partial void OnShowAdvisoriesChanged(bool value) => AlertTypesChanged();
    partial void OnShowStatementsChanged(bool value) => AlertTypesChanged();
    partial void OnShowTerminatorChanged(bool value) => Save();

    private void AlertTypesChanged()
    {
        OnPropertyChanged(nameof(AlertTypes));
        Save();
    }

    private void Save() => _store.Save(_store.Load() with
    {
        MinMagnitude = MinMagnitude,
        QuakeWindowDays = QuakeWindowDays,
        AlertTypes = AlertTypes,
        NightStrength = NightStrength,
        ShowTerminator = ShowTerminator,
        LightningWindowMinutes = LightningWindowMinutes,
    });
}
