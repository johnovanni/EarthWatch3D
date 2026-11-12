namespace EarthWatch3D.Core.Lightning;

/// <summary>Newest flash time per sensor, and whether the MTG feed is set up (spec §9).</summary>
public sealed record LightningCoverage(DateTime? NewestGoesEastUtc, DateTime? NewestGoesWestUtc, DateTime? NewestMtgUtc, bool MtgSetUp);

/// <summary>Where the globe layer gets lightning flashes from (spec §9).</summary>
public interface ILightningSource
{
    LightningCoverage Coverage { get; }
    IReadOnlyList<LightningFlash> GetFlashes(DateTime fromUtc, DateTime toUtc);
    bool SupportsHistory { get; }
    event EventHandler? FlashesChanged;
}
