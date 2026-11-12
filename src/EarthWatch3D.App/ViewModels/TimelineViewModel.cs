using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.App.ViewModels;

/// <summary>
/// Owns the single "display time" that everything time-dependent follows: the live clock, or a replay time
/// within the last <see cref="Range"/> that the user scrubs or plays.
/// </summary>
public partial class TimelineViewModel : ObservableObject
{
    public static readonly TimeSpan Range = TimeSpan.FromDays(7);
    public static IReadOnlyList<double> Speeds { get; } = [1, 60, 600, 3600];

    private readonly IClock _clock;

    public TimelineViewModel(IClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _displayTimeUtc = clock.UtcNow;
    }

    [ObservableProperty] private DateTime _displayTimeUtc;
    [ObservableProperty] private bool _isLive = true;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _speed = 1;

    public event EventHandler? DisplayTimeChanged;

    public DateTime MinTimeUtc => _clock.UtcNow - Range;
    public DateTime MaxTimeUtc => _clock.UtcNow;

    /// <summary>Seconds since <see cref="MinTimeUtc"/>. Setting it leaves live and clamps to the range.</summary>
    public double SliderSeconds
    {
        get => (DisplayTimeUtc - MinTimeUtc).TotalSeconds;
        set
        {
            if (IsLive && Math.Abs(value - SliderSeconds) < 1) return;   // the slider echoing back live updates
            IsLive = false;
            SetDisplay(MinTimeUtc.AddSeconds(Math.Clamp(value, 0, Range.TotalSeconds)));
        }
    }

    /// <summary>
    /// True when the events come from the offline samples (--offline-samples): the readout then says SAMPLE, never
    /// LIVE, so sample data can't be mistaken for what is happening now.
    /// </summary>
    public bool IsSampleData
    {
        get => _isSampleData;
        set
        {
            if (!SetProperty(ref _isSampleData, value)) return;
            OnPropertyChanged(nameof(ReadoutText));
            OnPropertyChanged(nameof(ShowsLive));
            OnPropertyChanged(nameof(LiveButtonText));
            OnPropertyChanged(nameof(LiveButtonToolTip));
        }
    }
    private bool _isSampleData;

    /// <summary>True when the LIVE button should light up: live, and real data (never for the samples).</summary>
    public bool ShowsLive => IsLive && !IsSampleData;

    /// <summary>LIVE, or NOW on the samples (it jumps to the newest sample time, which is not live).</summary>
    public string LiveButtonText => IsSampleData ? "NOW" : "LIVE";

    public string LiveButtonToolTip => IsSampleData ? "Jump to the newest time on the sample clock (offline sample data)" : "Jump back to the live view";

    public string ReadoutText => !IsLive
        ? "REPLAY " + DisplayTimeUtc.ToString("d MMM HH:mm", CultureInfo.InvariantCulture) + " UTC"
        : IsSampleData
            ? "SAMPLE " + DisplayTimeUtc.ToString("d MMM HH:mm", CultureInfo.InvariantCulture) + " UTC"
            : "LIVE " + DisplayTimeUtc.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";

    public string LocalTimeText
    {
        get
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(DisplayTimeUtc, DateTimeKind.Utc), TimeZoneInfo.Local);
            return local.ToString("d MMM HH:mm", CultureInfo.InvariantCulture) + " local";
        }
    }

    /// <summary>Moves the display time on by one tick of wall-clock time.</summary>
    public void Advance(TimeSpan wallElapsed)
    {
        AdvanceCore(wallElapsed);
        OnPropertyChanged(nameof(SliderSeconds));   // MinTimeUtc has moved, so the thumb position has too
    }

    private void AdvanceCore(TimeSpan wallElapsed)
    {
        var max = MaxTimeUtc;
        if (IsLive) SetDisplay(max);
        else if (IsPlaying)
        {
            var next = DisplayTimeUtc + TimeSpan.FromSeconds(wallElapsed.TotalSeconds * Speed);
            if (next >= max) { MakeLive(); return; }
            SetDisplay(next);
        }
        else SetDisplay(DisplayTimeUtc);   // re-clamp as the window slides forward
    }

    [RelayCommand] private void GoLive() => MakeLive();

    [RelayCommand]
    private void TogglePlay()
    {
        if (IsLive) return;   // nothing to play forward to
        IsPlaying = !IsPlaying;
    }

    [RelayCommand] private void StepMinus10Min() => Step(TimeSpan.FromMinutes(-10));
    [RelayCommand] private void StepPlus10Min() => Step(TimeSpan.FromMinutes(10));
    [RelayCommand] private void StepMinus1Hour() => Step(TimeSpan.FromHours(-1));
    [RelayCommand] private void StepPlus1Hour() => Step(TimeSpan.FromHours(1));
    [RelayCommand] private void SetSpeed(double speed) => Speed = speed;

    /// <summary>Leaves live and stops playback at <paramref name="utc"/>, clamped to the range (the --display-time option).</summary>
    public void ReplayAt(DateTime utc)
    {
        IsLive = false;
        IsPlaying = false;
        SetDisplay(utc);
    }

    /// <summary>Moves the display time by <paramref name="delta"/>, leaving live. A step forward while live is a no-op: live is already now.</summary>
    private void Step(TimeSpan delta)
    {
        if (IsLive && delta > TimeSpan.Zero) return;
        var from = DisplayTimeUtc;
        IsLive = false;
        SetDisplay(from + delta);
    }

    private void MakeLive()
    {
        IsLive = true;
        IsPlaying = false;
        SetDisplay(MaxTimeUtc);
    }

    private void SetDisplay(DateTime utc)
    {
        var min = MinTimeUtc; var max = MaxTimeUtc;
        if (utc < min) utc = min;
        if (utc > max) utc = max;
        if (utc == DisplayTimeUtc) return;
        DisplayTimeUtc = utc;
        OnPropertyChanged(nameof(SliderSeconds));
        OnPropertyChanged(nameof(ReadoutText));
        OnPropertyChanged(nameof(LocalTimeText));
        DisplayTimeChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsLiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ReadoutText));
        OnPropertyChanged(nameof(ShowsLive));
    }
}
