namespace EarthWatch3D.Core.Lightning;

/// <summary>What the MTG feed is doing, so the legend can say why it has nothing new (set up and not rejected is Running).</summary>
public enum MtgFeedState { Running, KeyRejected, Paused, NotInView }

/// <summary>Newest flash time per sensor, whether the MTG feed is set up, and what it is doing (spec §9).</summary>
public sealed record LightningCoverage(DateTime? NewestGoesEastUtc, DateTime? NewestGoesWestUtc, DateTime? NewestMtgUtc, bool MtgSetUp,
    MtgFeedState MtgState = MtgFeedState.Running);

/// <summary>Where the globe layer gets lightning flashes from (spec §9).</summary>
public interface ILightningSource
{
    LightningCoverage Coverage { get; }
    IReadOnlyList<LightningFlash> GetFlashes(DateTime fromUtc, DateTime toUtc);
    bool SupportsHistory { get; }
    event EventHandler? FlashesChanged;
}
