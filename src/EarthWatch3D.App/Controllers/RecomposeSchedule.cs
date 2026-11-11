namespace EarthWatch3D.App.Controllers;

/// <summary>
/// Decides when the globe image needs composing again. It is due before the first compose, once the interval
/// has passed, or when the clock has gone backwards (clock change, or resume from sleep).
/// </summary>
public sealed class RecomposeSchedule
{
    private readonly TimeSpan _interval;
    private DateTime? _last;

    public RecomposeSchedule(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        _interval = interval;
    }

    public bool IsDue(DateTime nowUtc) =>
        _last is not { } last || nowUtc < last || nowUtc - last >= _interval;

    public void MarkComposed(DateTime nowUtc) => _last = nowUtc;
}
