using AstroCore.Earth;

namespace EarthWatch3D.Core.Geo;

/// <summary>Camera state shared between the UI thread (writer) and background feeds (readers).</summary>
public sealed class ViewState
{
    private readonly object _lock = new();
    private GeoPosition _cameraSubPoint;

    /// <summary>The point on Earth directly beneath the camera. Reads and writes are atomic. Defaults to (0, 0).</summary>
    public GeoPosition CameraSubPoint
    {
        get { lock (_lock) return _cameraSubPoint; }
        set { lock (_lock) _cameraSubPoint = value; }
    }

    /// <summary>True when a geostationary satellite at <paramref name="subSatelliteLon"/> (on the equator) is strictly less than
    /// <paramref name="maxAngleDeg"/> of great-circle angle from the camera's sub-point.</summary>
    public static bool Faces(GeoPosition cameraSubPoint, double subSatelliteLon, double maxAngleDeg = 100)
    {
        double angleDeg = GeoMath.DistanceKm(cameraSubPoint, new GeoPosition(0, subSatelliteLon)) / GeoMath.EarthRadiusKm * 180 / Math.PI;
        return Math.Round(angleDeg, 9) < maxAngleDeg;   // rounding absorbs haversine round-off so an exact limit is "not facing"
    }
}
