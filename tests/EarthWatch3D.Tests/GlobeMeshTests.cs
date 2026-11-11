using System.Windows.Media.Media3D;
using AstroCore.Earth;
using EarthWatch3D.App.Rendering;

namespace EarthWatch3D.Tests;

public class GlobeMeshTests
{
    [Fact]
    public void VerticesOnUnitSphereAndTextureMatchesLatLon()
    {
        var m = GlobeMesh.Create(36, 18);
        Assert.All(m.Positions, p => Assert.Equal(1, ((Vector3D)p).Length, 9));
        for (int i = 0; i < m.Positions.Count; i++)
        {
            var g = GeoPosition.FromVector(new(m.Positions[i].X, m.Positions[i].Y, m.Positions[i].Z));
            if (Math.Abs(g.LatitudeDeg) > 89.9) continue;
            Assert.Equal((90 - g.LatitudeDeg) / 180, m.TextureCoordinates[i].Y, 6);
            double du = (m.TextureCoordinates[i].X - (g.LongitudeDeg + 180) / 360) % 1;   // seam: u=0 and u=1 are both lon ±180
            Assert.True(Math.Abs(du) < 1e-6 || Math.Abs(Math.Abs(du) - 1) < 1e-6);
        }
    }
}
