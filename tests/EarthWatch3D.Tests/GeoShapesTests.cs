using AstroCore.Earth;
using EarthWatch3D.Core.Geo;

namespace EarthWatch3D.Tests;

public class GeoShapesTests
{
    [Fact]
    public void PolygonsFromEqualListsAreEqual()
    {
        static GeoPolygon Make() => new([new List<GeoPosition> { new(0, 0), new(0, 10), new(10, 10), new(0, 0) }]);
        Assert.Equal(Make(), Make());
        Assert.Equal(Make().GetHashCode(), Make().GetHashCode());
        Assert.NotEqual(Make(), new GeoPolygon([new List<GeoPosition> { new(0, 0), new(0, 11), new(10, 10), new(0, 0) }]));
        Assert.Equal(new GeoLine([new GeoPosition(1, 2)]), new GeoLine([new GeoPosition(1, 2)]));
    }

    [Fact] // Review Focus 2, real NHC cone extent
    public void SplitsNhcConeAtDateLine()
    {
        var ring = new List<(double, double)> { (177.28, 8.0), (192.56, 9.0), (190.0, 14.0), (178.0, 12.0), (177.28, 8.0) };
        var polys = DateLine.SplitPolygon([ring]);
        Assert.Equal(2, polys.Count);
        Assert.All(polys.SelectMany(p => p.Rings[0]), q => Assert.InRange(q.LongitudeDeg, -180.0, 180.0));
        Assert.Contains(polys, p => p.Bounds.MaxLon == 180.0 && p.Bounds.MinLon == 177.28);
        Assert.Contains(polys, p => p.Bounds.MinLon == -180.0 && Math.Abs(p.Bounds.MaxLon - (192.56 - 360)) < 1e-9);
    }

    [Fact] public void PolygonNotCrossingIsUnchanged()
    { var polys = DateLine.SplitPolygon([[(10, 0), (20, 0), (20, 10), (10, 0)]]); Assert.Single(polys); Assert.Equal(4, polys[0].Rings[0].Count); }

    [Fact] // Review Focus 2, real NHC track runs to -181.6
    public void SplitsUnwrappedTrack()
    {
        var lines = DateLine.SplitLine([(-167.7, 9.9), (-175.0, 10.2), (-181.6, 10.9)]);
        Assert.Equal(2, lines.Count);
        Assert.Equal(-180.0, lines[0].Points[^1].LongitudeDeg, 9);
        Assert.Equal(180.0, lines[1].Points[0].LongitudeDeg, 9);
        Assert.Equal(178.4, lines[1].Points[^1].LongitudeDeg, 9);
    }

    [Fact] public void ContainsRespectsHoles()
    {
        var p = new GeoPolygon([
            [new(0, 0), new(0, 10), new(10, 10), new(10, 0), new(0, 0)],
            [new(4, 4), new(4, 6), new(6, 6), new(6, 4), new(4, 4)]]);
        Assert.True(p.Contains(new(2, 2))); Assert.False(p.Contains(new(5, 5))); Assert.False(p.Contains(new(20, 20)));
    }

    [Fact] public void DistanceOneDegreeAtEquator() => Assert.Equal(111.19, GeoMath.DistanceKm(new(0, 0), new(0, 1)), 1);

    [Fact] public void LineWithoutCrossingStaysOnePiece()
    {
        var lines = DateLine.SplitLine([(10, 0), (20, 5), (30, 10)]);
        Assert.Single(lines);
        Assert.Equal(3, lines[0].Points.Count);
        Assert.Equal(20.0, lines[0].Points[1].LongitudeDeg);
    }

    [Fact] public void LineCrossingTwiceGivesThreePieces()
    {
        // Unwrapped: 170 -> 190 (crosses +180) -> 170 again? use eastward then westward back across.
        var lines = DateLine.SplitLine([(170, 0), (190, 10), (170, 20)]);
        Assert.Equal(3, lines.Count);
        Assert.Equal(180.0, lines[0].Points[^1].LongitudeDeg);
        Assert.Equal(-180.0, lines[1].Points[0].LongitudeDeg);
        Assert.Equal(-180.0, lines[1].Points[^1].LongitudeDeg);
        Assert.Equal(180.0, lines[2].Points[0].LongitudeDeg);
        Assert.All(lines.SelectMany(l => l.Points), q => Assert.InRange(q.LongitudeDeg, -180.0, 180.0));
    }

    [Fact] public void WrappedLongitudeInputIsNormalised()
    {
        var lines = DateLine.SplitLine([(192.6, 1), (195.0, 2)]);
        Assert.Single(lines);
        Assert.Equal(192.6 - 360, lines[0].Points[0].LongitudeDeg, 9);
    }

    [Fact] public void PolygonEntirelyBeyond180ShiftsWest()
    {
        var polys = DateLine.SplitPolygon([[(185, 0), (190, 0), (190, 5), (185, 0)]]);
        Assert.Single(polys);
        Assert.Equal(-175.0, polys[0].Bounds.MinLon, 9);
        Assert.Equal(-170.0, polys[0].Bounds.MaxLon, 9);
    }

    [Fact] public void ContainsWorksOnSplitHalf()
    {
        var ring = new List<(double, double)> { (177.28, 8.0), (192.56, 9.0), (190.0, 14.0), (178.0, 12.0), (177.28, 8.0) };
        var polys = DateLine.SplitPolygon([ring]);
        var east = polys.Single(p => p.Bounds.MinLon == -180.0);
        Assert.True(east.Contains(new(10.5, -175.0)));
    }
}
