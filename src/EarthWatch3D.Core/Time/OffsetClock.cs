namespace EarthWatch3D.Core.Time;

/// <summary>
/// A clock running <see cref="Offset"/> ahead of another (behind if negative). Used by the
/// <c>--snapshot-recompose</c> diagnostic to move the app's time forward while it runs.
/// </summary>
public sealed class OffsetClock : IClock
{
    private readonly IClock _inner;

    public OffsetClock(IClock inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>Added to the inner clock's time. Set it on the UI thread.</summary>
    public TimeSpan Offset { get; set; }

    public DateTime UtcNow => _inner.UtcNow + Offset;
}
