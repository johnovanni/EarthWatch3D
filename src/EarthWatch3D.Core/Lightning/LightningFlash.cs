namespace EarthWatch3D.Core.Lightning;

/// <summary>Which lightning imager a flash came from.</summary>
public enum LightningSensor { GoesEast, GoesWest, Mtg }

/// <summary>One lightning flash. <see cref="TimeUtc"/> is UTC, <see cref="Lon"/> is in (-180, 180] degrees, <see cref="EnergyJ"/> is radiant energy in joules (0 if unknown).</summary>
public readonly record struct LightningFlash(DateTime TimeUtc, double Lat, double Lon, double EnergyJ, LightningSensor Sensor);
