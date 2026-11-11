using System.Windows.Media.Media3D;
using AstroCore.Earth;

namespace EarthWatch3D.App.Rendering;

/// <summary>Distance limits, rotation sensitivity and canonical views for the globe camera (Earth radius = 1).</summary>
public static class CameraRules
{
    public const double MinDistance = 1.047, MaxDistance = 20.0, ZoomPerNotch = 1.15;

    public static Point3D ClampDistance(Point3D position)
    {
        var v = (Vector3D)position;
        double len = v.Length;
        if (len < 1e-12) return new Point3D(0, -MinDistance, 0);
        double target = Math.Clamp(len, MinDistance, MaxDistance);
        if (target == len) return position;
        v *= target / len;
        return new Point3D(v.X, v.Y, v.Z);
    }

    public static double RotationSensitivity(double distance) => Math.Clamp((distance - 1) / 2, 0.02, 1.0);

    public static (Point3D Position, Vector3D LookDirection, Vector3D UpDirection) LookDownOn(GeoPosition p, double distance)
    {
        var u = p.ToUnitVector();
        var n = new Vector3D(u.X, u.Y, u.Z);
        var position = new Point3D(n.X * distance, n.Y * distance, n.Z * distance);
        var look = -(Vector3D)position;
        var up = Perp(new Vector3D(0, 0, 1), n);
        if (up.Length < 1e-6) up = Perp(new Vector3D(0, 1, 0), n);
        up.Normalize();
        return (position, look, up);
    }

    private static Vector3D Perp(Vector3D v, Vector3D n) => v - Vector3D.DotProduct(v, n) * n;
}
