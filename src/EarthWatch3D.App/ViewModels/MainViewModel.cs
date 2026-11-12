using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EarthWatch3D.App.Services;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Storage;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // Wall time handed to playback per Tick is capped, so a sleep/resume gap does not leap a replay forward.
    private static readonly TimeSpan MaxTickElapsed = TimeSpan.FromSeconds(5);

    private readonly IClock _clock;
    private readonly IDialogService? _dialogs;
    private DateTime? _lastTick;
    // Replaced whole, never mutated, so feeds may read it from any thread.
    private volatile DisplayFilter _currentFilter;

    /// <param name="settings">Where layer and display choices are loaded from and saved to. In the app this is a
    /// <see cref="SettingsSaver"/>, so saves never block the UI thread.</param>
    /// <param name="dispatcher">The UI thread's dispatcher, used by the status dots; null in tests, where updates
    /// apply directly.</param>
    /// <param name="dialogs">Opens the Settings dialog; null in tests.</param>
    public MainViewModel(IClock clock, ISettingsStore settings, Dispatcher? dispatcher = null, IDialogService? dialogs = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _dialogs = dialogs;
        ArgumentNullException.ThrowIfNull(settings);
        Timeline = new TimelineViewModel(clock);
        SunMoon = new SunMoonViewModel();
        Layers = new LayersViewModel(settings);
        Display = new DisplayOptionsViewModel(settings);
        Status = new StatusViewModel(dispatcher);
        Selection = new SelectionViewModel(() => DisplayTimeUtc);
        _currentFilter = BuildFilter();
        Layers.PropertyChanged += OnFilterInputChanged;
        Display.PropertyChanged += OnFilterInputChanged;
        // Subscribed first, so the Sun/Moon card is current before any other listener runs.
        Timeline.DisplayTimeChanged += (_, _) =>
        {
            SunMoon.Refresh(DisplayTimeUtc);
            DisplayTimeChanged?.Invoke(this, EventArgs.Empty);
        };
        SunMoon.Refresh(DisplayTimeUtc);
    }

    public TimelineViewModel Timeline { get; }
    public SunMoonViewModel SunMoon { get; }
    public LayersViewModel Layers { get; }
    public DisplayOptionsViewModel Display { get; }
    public StatusViewModel Status { get; }
    public SelectionViewModel Selection { get; }

    /// <summary>Which events are shown, from the layer toggles and display options. Safe to read from any thread.</summary>
    public DisplayFilter CurrentFilter => _currentFilter;

    /// <summary>Raised on the UI thread when <see cref="CurrentFilter"/> changes.</summary>
    public event EventHandler? FilterChanged;

    public DateTime DisplayTimeUtc => Timeline.DisplayTimeUtc;

    /// <summary>Opens the Settings dialog (nothing is bound to it yet).</summary>
    [RelayCommand]
    private void OpenSettings() => _dialogs?.ShowSettings();

    /// <summary>Raised whenever the display time changes (live tick, step, scrub or playback).</summary>
    public event EventHandler? DisplayTimeChanged;

    /// <summary>Raised after each <see cref="Tick"/> has advanced the timeline.</summary>
    public event EventHandler? Ticked;

    public void Tick()
    {
        var now = _clock.UtcNow;
        var elapsed = _lastTick is { } last ? now - last : TimeSpan.Zero;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        if (elapsed > MaxTickElapsed) elapsed = MaxTickElapsed;
        _lastTick = now;
        Timeline.Advance(elapsed);
        Ticked?.Invoke(this, EventArgs.Empty);
    }

    // Through AppSettings.ToFilter, the one place that maps NaN ("All") to −∞ and days to a window.
    private DisplayFilter BuildFilter() => new AppSettings
    {
        ShowQuakes = Layers.ShowQuakes,
        ShowCyclones = Layers.ShowCyclones,
        ShowAlerts = Layers.ShowAlerts,
        ShowEonet = Layers.ShowEonet,
        MinMagnitude = Display.MinMagnitude,
        QuakeWindowDays = Display.QuakeWindowDays,
        AlertTypes = Display.AlertTypes,
    }.ToFilter();

    private void OnFilterInputChanged(object? sender, PropertyChangedEventArgs e)
    {
        var next = BuildFilter();
        if (next == _currentFilter) return;   // e.g. night strength or the strip's labels
        _currentFilter = next;
        OnPropertyChanged(nameof(CurrentFilter));
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }
}
