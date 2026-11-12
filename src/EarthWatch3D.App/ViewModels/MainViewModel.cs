using CommunityToolkit.Mvvm.ComponentModel;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // Wall time handed to playback per Tick is capped, so a sleep/resume gap does not leap a replay forward.
    private static readonly TimeSpan MaxTickElapsed = TimeSpan.FromSeconds(5);

    private readonly IClock _clock;
    private DateTime? _lastTick;

    public MainViewModel(IClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Timeline = new TimelineViewModel(clock);
        SunMoon = new SunMoonViewModel();
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

    public DateTime DisplayTimeUtc => Timeline.DisplayTimeUtc;

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
}
