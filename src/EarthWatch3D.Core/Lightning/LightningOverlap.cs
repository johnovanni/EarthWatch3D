namespace EarthWatch3D.Core.Lightning;

/// <summary>Decides which imager "owns" each longitude where their fields of view overlap, so every flash is kept once.</summary>
public static class LightningOverlap
{
    public const double WestEastBoundary = -106.2, EastMtgBoundary = -37.6;

    /// <summary>
    /// GoesWest keeps lon &lt; -106.2; GoesEast keeps -106.2 &lt;= lon &lt; -37.6 (every lon >= -106.2 when MTG is not active);
    /// Mtg keeps lon >= -37.6.
    /// </summary>
    public static bool Keep(in LightningFlash f, bool mtgActive) => f.Sensor switch
    {
        LightningSensor.GoesWest => f.Lon < WestEastBoundary,
        LightningSensor.GoesEast => f.Lon >= WestEastBoundary && (!mtgActive || f.Lon < EastMtgBoundary),
        LightningSensor.Mtg => f.Lon >= EastMtgBoundary,
        _ => false,
    };
}
