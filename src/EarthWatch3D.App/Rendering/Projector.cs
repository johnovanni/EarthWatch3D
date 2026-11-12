using System.Windows;
using System.Windows.Media.Media3D;

namespace EarthWatch3D.App.Rendering;

/// <summary>
/// Snapshot of a perspective camera that projects world points to screen pixels. Used for
/// picking, label placement and level-of-detail decisions.
/// </summary>
public readonly struct Projector
{
    private readonly Point3D _eye;
    private readonly Vector3D _forward, _right, _up;
    private readonly double _focal, _halfW, _halfH;

    public Projector(PerspectiveCamera camera, double width, double height)
    {
        _eye = camera.Position;
        _forward = camera.LookDirection; _forward.Normalize();
        _right = Vector3D.CrossProduct(_forward, camera.UpDirection);
        if (_right.LengthSquared < 1e-20) _right = Vector3D.CrossProduct(_forward, new Vector3D(0, 1, 0));
        _right.Normalize();
        _up = Vector3D.CrossProduct(_right, _forward);
        _halfW = Math.Max(1, width) / 2;
        _halfH = Math.Max(1, height) / 2;
        // WPF's FieldOfView is the horizontal angle.
        _focal = _halfW / Math.Tan(camera.FieldOfView * Math.PI / 360.0);
        Width = width; Height = height;
    }

    public double Width { get; }
    public double Height { get; }
    public Point3D Eye => _eye;

    /// <summary>Distance along the view direction (negative = behind the camera).</summary>
    public double Depth(Point3D p) => Vector3D.DotProduct(p - _eye, _forward);

    /// <summary>Projects a point; returns false if it is behind the camera.</summary>
    public bool TryProject(Point3D p, out Point screen)
    {
        var d = p - _eye;
        double z = Vector3D.DotProduct(d, _forward);
        if (z <= 1e-12) { screen = default; return false; }
        screen = new Point(_halfW + Vector3D.DotProduct(d, _right) / z * _focal,
                           _halfH - Vector3D.DotProduct(d, _up) / z * _focal);
        return true;
    }

    /// <summary>
    /// Where the ray from the eye through the screen point <paramref name="screen"/> first meets the unit sphere (the
    /// globe's surface); false if it misses. Pure camera math: the inverse of <see cref="TryProject"/>.
    /// </summary>
    public bool TryHitUnitSphere(Point screen, out Point3D hit)
    {
        var dir = _forward + _right * ((screen.X - _halfW) / _focal) + _up * ((_halfH - screen.Y) / _focal);
        return RayHitsUnitSphere(_eye, dir, out hit);
    }

    /// <summary>The nearest point ahead of <paramref name="origin"/> along <paramref name="direction"/> on the unit sphere.</summary>
    public static bool RayHitsUnitSphere(Point3D origin, Vector3D direction, out Point3D hit)
    {
        hit = default;
        var o = (Vector3D)origin;
        double a = direction.LengthSquared;
        if (a < 1e-24) return false;
        // |o + t d|² = 1  →  a t² + 2 b t + c = 0
        double b = Vector3D.DotProduct(o, direction);
        double c = o.LengthSquared - 1;
        double disc = b * b - a * c;
        if (disc < 0) return false;
        double root = Math.Sqrt(disc);
        double t = (-b - root) / a;            // the entry point
        if (t < 0) t = (-b + root) / a;        // the eye is inside the sphere: the exit point
        if (t < 0) return false;               // the sphere is behind the eye
        hit = origin + direction * t;
        return true;
    }

    public bool IsOnScreen(Point s, double margin = 0) =>
        s.X >= -margin && s.Y >= -margin && s.X <= Width + margin && s.Y <= Height + margin;

    /// <summary>Expresses a world direction in camera axes: X = right, Y = up, Z = forward.</summary>
    public Vector3D ToCameraSpace(Vector3D worldDirection) => new(
        Vector3D.DotProduct(worldDirection, _right),
        Vector3D.DotProduct(worldDirection, _up),
        Vector3D.DotProduct(worldDirection, _forward));

    /// <summary>Approximate on-screen size in pixels of a length placed at <paramref name="at"/>.</summary>
    public double PixelSize(double worldLength, Point3D at)
    {
        double dist = (at - _eye).Length;
        return dist <= 0 ? double.PositiveInfinity : worldLength / dist * _focal;
    }
}
