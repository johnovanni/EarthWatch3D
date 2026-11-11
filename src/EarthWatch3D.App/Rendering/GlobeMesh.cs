using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace EarthWatch3D.App.Rendering;

public static class GlobeMesh
{
    /// <summary>
    /// Unit sphere, Z up, +X at (0N, 0E). Texture u = (lon+180)/360, v = (90-lat)/180. The seam column
    /// is duplicated (u = 0 and u = 1) so the texture never wraps inside a triangle.
    /// </summary>
    public static MeshGeometry3D Create(int lonSegments = 360, int latSegments = 180)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lonSegments, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(latSegments, 2);
        var positions = new Point3DCollection((lonSegments + 1) * (latSegments + 1));
        var tex = new PointCollection((lonSegments + 1) * (latSegments + 1));
        for (int i = 0; i <= latSegments; i++)
        {
            double v = (double)i / latSegments;
            double lat = (90 - 180 * v) * Math.PI / 180;
            double cl = Math.Cos(lat), sl = Math.Sin(lat);
            for (int j = 0; j <= lonSegments; j++)
            {
                double u = (double)j / lonSegments;
                double lon = (360 * u - 180) * Math.PI / 180;
                positions.Add(new Point3D(cl * Math.Cos(lon), cl * Math.Sin(lon), sl));
                tex.Add(new Point(u, v));
            }
        }
        var idx = new Int32Collection(lonSegments * latSegments * 6);
        int stride = lonSegments + 1;
        for (int i = 0; i < latSegments; i++)
            for (int j = 0; j < lonSegments; j++)
            {
                int a = i * stride + j, b = a + stride, c = a + 1, d = b + 1;
                idx.Add(a); idx.Add(b); idx.Add(c);   // counter-clockwise seen from outside
                idx.Add(c); idx.Add(b); idx.Add(d);
            }
        return new MeshGeometry3D { Positions = positions, TextureCoordinates = tex, TriangleIndices = idx };
    }
}
