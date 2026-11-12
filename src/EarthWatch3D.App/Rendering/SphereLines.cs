using System.Windows.Media.Media3D;
using EarthWatch3D.Core.Geo;

namespace EarthWatch3D.App.Rendering;

/// <summary>Polylines on a sphere: great-circle subdivision and dash patterns measured in degrees of arc.</summary>
public static class SphereLines
{
    // Dashes shorter than this (degrees) are rounding left-overs at the end of a path, not real dashes.
    private const double MinDashDeg = 1e-6;

    /// <summary>
    /// The line's points at <paramref name="radius"/>, with each leg split along its great circle into equal steps of
    /// at most <paramref name="maxStepDeg"/>. Repeated points are dropped.
    /// </summary>
    public static IReadOnlyList<Point3D> Densify(GeoLine line, double radius, double maxStepDeg = 1.0)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStepDeg);
        var result = new List<Point3D>();
        Vector3D? prev = null;
        foreach (var g in line.Points)
        {
            var u = g.ToUnitVector();
            var v = new Vector3D(u.X, u.Y, u.Z);
            if (prev is not { } a)
            {
                result.Add(new Point3D(u.X * radius, u.Y * radius, u.Z * radius));
                prev = v;
                continue;
            }
            double theta = Angle(a, v);
            if (theta < 1e-12) continue;
            int n = Math.Max(1, (int)Math.Ceiling(theta * 180 / Math.PI / maxStepDeg - 1e-9));
            for (int k = 1; k < n; k++) result.Add((Point3D)(Slerp(a, v, theta, (double)k / n) * radius));
            result.Add(new Point3D(u.X * radius, u.Y * radius, u.Z * radius));
            prev = v;
        }
        return result;
    }

    /// <summary>
    /// Splits a path (points on a sphere centred at the origin) into dashes of <paramref name="dashDeg"/> separated by
    /// gaps of <paramref name="gapDeg"/>, starting with a dash. Each dash keeps the path's own points inside it.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Point3D>> Dashes(IReadOnlyList<Point3D> path, double dashDeg = 1.5, double gapDeg = 1.0)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dashDeg);
        ArgumentOutOfRangeException.ThrowIfNegative(gapDeg);
        var dashes = new List<IReadOnlyList<Point3D>>();
        if (path.Count < 2) return dashes;

        double dash = dashDeg * Math.PI / 180, period = dash + gapDeg * Math.PI / 180, minDash = MinDashDeg * Math.PI / 180;
        double phase = 0;   // position in the dash/gap pattern, radians
        List<Point3D>? current = [path[0]];
        double currentLength = 0;

        void Finish()
        {
            if (current is { Count: >= 2 } && currentLength >= minDash) dashes.Add(current);
            current = null;
        }

        for (int i = 1; i < path.Count; i++)
        {
            var a = (Vector3D)path[i - 1]; var b = (Vector3D)path[i];
            double r = a.Length;
            a /= r; var bu = b / b.Length;
            double theta = Angle(a, bu);
            if (theta < 1e-15) continue;
            double done = 0;
            while (done < theta)
            {
                bool inDash = phase < dash;
                double boundary = (inDash ? dash : period) - phase;
                double left = theta - done;
                bool legEnds = left < boundary;   // the leg ends inside this dash or gap
                double step = legEnds ? left : boundary;
                done = legEnds ? theta : done + step;
                phase += step;
                var p = (Point3D)(Slerp(a, bu, theta, done / theta) * r);
                if (inDash && current is not null) { current.Add(p); currentLength += step; }
                if (legEnds) continue;
                if (inDash) Finish();
                else { phase = 0; current = [p]; currentLength = 0; }
            }
        }
        Finish();
        return dashes;
    }

    private static double Angle(Vector3D a, Vector3D b) =>
        Math.Atan2(Vector3D.CrossProduct(a, b).Length, Vector3D.DotProduct(a, b));

    // Unit vectors a, b at angle theta; t in [0, 1].
    private static Vector3D Slerp(Vector3D a, Vector3D b, double theta, double t)
    {
        double s = Math.Sin(theta);
        if (s < 1e-12)
        {
            var v = a + (b - a) * t;
            v.Normalize();
            return v;
        }
        return (a * Math.Sin((1 - t) * theta) + b * Math.Sin(t * theta)) / s;
    }
}
