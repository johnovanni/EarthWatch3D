using GlobeCore.Time;


namespace EarthWatch3D.App.Diagnostics;

/// <summary>
/// A clock that stands still at <see cref="Now"/> until it is set again. The <c>--snapshot-scenes</c> run uses one for the
/// display (moved to each scene) and one for the feeds (never moved), so each picture depends on its scene's time and not on how long the run has taken.
/// </summary>
public sealed class SettableClock(DateTime start) : IClock
{
    private long _ticks = start.Ticks;

    /// <summary>The time the clock shows (UTC). Safe to read from any thread.</summary>
    public DateTime Now
    {
        get => new(Interlocked.Read(ref _ticks), DateTimeKind.Utc);
        set => Interlocked.Exchange(ref _ticks, value.Ticks);
    }

    public DateTime UtcNow => Now;
}
