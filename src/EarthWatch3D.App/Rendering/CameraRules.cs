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

    /// <summary>Distance at which the globe fits the window (WPF FieldOfView is horizontal), with margin.</summary>
    public static double FitDistance(double horizontalFovDeg, double aspect, double margin = 0.9)
    {
        double hHalf = horizontalFovDeg * Math.PI / 360;
        double vHalf = Math.Atan(Math.Tan(hHalf) / aspect);
        double d = 1 / Math.Sin(margin * Math.Min(hHalf, vHalf));
        return Math.Clamp(d, MinDistance, MaxDistance);
    }

    /// <summary>True if <paramref name="look"/> points from <paramref name="position"/> at the origin within <paramref name="toleranceDeg"/>.</summary>
    public static bool AimsAtOrigin(Point3D position, Vector3D look, double toleranceDeg = 0.5)
    {
        var toOrigin = -(Vector3D)position;
        double denom = toOrigin.Length * look.Length;
        if (!(denom > 1e-24)) return false;
        double cos = Vector3D.DotProduct(toOrigin, look) / denom;
        return cos >= Math.Cos(toleranceDeg * Math.PI / 180);
    }

    public static double RotationSensitivity(double distance) => Math.Clamp((distance - 1) / 2, 0.02, 1.0);

    public static (Point3D Position, Vector3D LookDirection, Vector3D UpDirection) LookDownOn(GeoPosition p, double distance)
    {
        var u = p.ToUnitVector();
        var n = new Vector3D(u.X, u.Y, u.Z);
        var position = new Point3D(n.X * distance, n.Y * distance, n.Z * distance);
        var look = -(Vector3D)position;
        return (position, look, UpFor(n));
    }

    private static Vector3D UpFor(Vector3D n)
    {
        var up = Perp(new Vector3D(0, 0, 1), n);
        if (up.Length < 1e-6) up = Perp(new Vector3D(0, 1, 0), n);
        up.Normalize();
        return up;
    }

    /// <summary>One frame of an orbit between two camera positions: slerped direction, linear distance, smoothstep easing.</summary>
    public static (Point3D Position, Vector3D LookDirection, Vector3D UpDirection) OrbitStep(Vector3D fromPos, Vector3D toPos, double t)
    {
        t = Math.Clamp(t, 0, 1);
        double s = t * t * (3 - 2 * t);
        double d0 = fromPos.Length, d1 = toPos.Length;
        var a = fromPos; a.Normalize();
        var b = toPos; b.Normalize();
        double dot = Math.Clamp(Vector3D.DotProduct(a, b), -1, 1);
        Vector3D dir;
        if (dot > 1 - 1e-12) dir = b;
        else if (dot < -1 + 1e-9)
        {
            var axis = Vector3D.CrossProduct(a, new Vector3D(0, 0, 1));
            if (axis.Length < 1e-6) axis = Vector3D.CrossProduct(a, new Vector3D(1, 0, 0));
            axis.Normalize();
            double ang = Math.PI * s;
            dir = a * Math.Cos(ang) + Vector3D.CrossProduct(axis, a) * Math.Sin(ang);
        }
        else
        {
            double th = Math.Acos(dot), sin = Math.Sin(th);
            dir = (a * Math.Sin((1 - s) * th) + b * Math.Sin(s * th)) / sin;
        }
        dir.Normalize();
        double d = d0 + (d1 - d0) * s;
        var pos = dir * d;
        return (new Point3D(pos.X, pos.Y, pos.Z), -pos, UpFor(dir));
    }

    private static Vector3D Perp(Vector3D v, Vector3D n) => v - Vector3D.DotProduct(v, n) * n;
}
