using System.IO;
using AstroCore.Earth;
using EarthWatch3D.Core.Geo;

namespace EarthWatch3D.Tests;

public class GeoJsonLinesTests
{
    private static Stream S(string json) => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

    [Fact]
    public void ReadsLineStringAndMultiLineStringSkipsOthers()
    {
        var lines = GeoJsonLines.Read(S("""
        {"type":"FeatureCollection","features":[
          {"type":"Feature","properties":{},"geometry":{"type":"LineString","coordinates":[[10,20],[11,21]]}},
          {"type":"Feature","properties":{},"geometry":{"type":"MultiLineString","coordinates":[[[0,0],[1,1]],[[2,2],[3,3],[4,4]]]}},
          {"type":"Feature","properties":{},"geometry":{"type":"Point","coordinates":[5,5]}},
          {"type":"Feature","properties":{},"geometry":null}
        ]}
        """));
        Assert.Equal(3, lines.Count);
        Assert.Equal(new GeoPosition(20, 10), lines[0][0]);   // GeoJSON order is [lon, lat]
        Assert.Equal(3, lines[2].Count);
    }

    [Fact]
    public void EmbeddedCoastlineLoads()
    {
        using var s = typeof(EarthWatch3D.App.App).Assembly.GetManifestResourceStream("EarthWatch3D.App.Assets.ne_50m_coastline.geojson")!;
        Assert.True(GeoJsonLines.Read(s).Count > 1000);
    }
}
