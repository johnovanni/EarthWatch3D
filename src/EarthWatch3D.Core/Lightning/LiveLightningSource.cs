namespace EarthWatch3D.Core.Lightning;

/// <summary>The live feeds' <see cref="LightningBuffer"/> seen as an <see cref="ILightningSource"/>.</summary>
public sealed class LiveLightningSource(LightningBuffer buffer, Func<bool> mtgSetUp, Func<MtgFeedState>? mtgState = null) : ILightningSource
{
    public LightningCoverage Coverage => new(
        buffer.Newest(LightningSensor.GoesEast), buffer.Newest(LightningSensor.GoesWest), buffer.Newest(LightningSensor.Mtg), mtgSetUp(), mtgState?.Invoke() ?? MtgFeedState.Running);

    public IReadOnlyList<LightningFlash> GetFlashes(DateTime fromUtc, DateTime toUtc) => buffer.Between(fromUtc, toUtc);

    public bool SupportsHistory => false;

    public event EventHandler? FlashesChanged
    {
        add => buffer.Changed += value;
        remove => buffer.Changed -= value;
    }
}
