namespace EarthWatch3D.App.Controllers;

/// <summary>Lets an action through at most once per <c>minGap</c>; the caller supplies the time so it can be tested.</summary>
public sealed class Throttle(TimeSpan minGap)
{
    private DateTime? _last;

    /// <summary>True (and starts a new gap) if at least the minimum gap has passed since the last acquire.</summary>
    public bool TryAcquire(DateTime nowUtc)
    {
        if (_last is { } last && nowUtc >= last && nowUtc - last < minGap) return false;
        _last = nowUtc;
        return true;
    }

    /// <summary>The earliest time <see cref="TryAcquire"/> will succeed.</summary>
    public DateTime NextAllowedUtc => _last is { } last ? last + minGap : DateTime.MinValue;
}
