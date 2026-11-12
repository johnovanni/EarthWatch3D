using System.IO;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Globe;

namespace EarthWatch3D.Tests;

public class PlateBoundariesTests
{
    private static IReadOnlyList<PlateBoundary> Load()
    {
        using var s = typeof(EarthWatch3D.App.App).Assembly.GetManifestResourceStream("EarthWatch3D.App.Assets.plate_boundaries.geojson")!;
        return PlateBoundaries.Read(s);
    }

    [Fact] public void HasAllThreeKinds()
    { var b = Load(); Assert.Contains(b, x => x.Kind == PlateBoundaryKind.Convergent); Assert.Contains(b, x => x.Kind == PlateBoundaryKind.Divergent); Assert.Contains(b, x => x.Kind == PlateBoundaryKind.Transform); }

    [Fact] public void LongitudesInRangeAfterSplit() =>
        Assert.All(Load().SelectMany(x => x.Line.Points), p => Assert.InRange(p.LongitudeDeg, -180.0, 180.0));

    [Theory] // PB2002 step lengths summed by kind (km): convergent 91,921; divergent 94,806; transform 74,068
    [InlineData(PlateBoundaryKind.Convergent, 91921)] [InlineData(PlateBoundaryKind.Divergent, 94806)] [InlineData(PlateBoundaryKind.Transform, 74068)]
    public void TotalLengthMatchesPb2002WithinTwoPercent(PlateBoundaryKind kind, double expectedKm)
    {
        double km = Load().Where(x => x.Kind == kind)
            .Sum(x => x.Line.Points.Zip(x.Line.Points.Skip(1), GeoMath.DistanceKm).Sum());
        Assert.InRange(km, expectedKm * 0.98, expectedKm * 1.02);
    }

    [Fact] public void UnknownKindSkipped()
    {
        var json = """{"type":"FeatureCollection","features":[{"type":"Feature","properties":{"kind":"mystery"},"geometry":{"type":"LineString","coordinates":[[0,0],[1,1]]}}]}""";
        Assert.Empty(PlateBoundaries.Read(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json))));
    }

    [Fact] public void LineCrossingDateLineIsSplit()
    {
        var json = """{"type":"FeatureCollection","features":[{"type":"Feature","properties":{"kind":"transform"},"geometry":{"type":"LineString","coordinates":[[179,0],[-179,0]]}}]}""";
        var b = PlateBoundaries.Read(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
        Assert.Equal(2, b.Count);
        Assert.All(b, x => Assert.Equal(PlateBoundaryKind.Transform, x.Kind));
    }

    [Fact] public void ShowPlatesDefaultsOffAndIsSaved()
    { var store = new FakeSettingsStore(); var vm = new LayersViewModel(store); Assert.False(vm.ShowPlates); vm.ShowPlates = true; Assert.True(store.Saved[^1].ShowPlates); }

    [Fact] public void ArgbStyleConstantsAre85PercentAlpha()
    {
        Assert.Equal(0xD9FF4D4D, PlateBoundaries.ConvergentArgb);
        Assert.Equal(0xD9FFD23F, PlateBoundaries.DivergentArgb);
        Assert.Equal(0xD95AC8FA, PlateBoundaries.TransformArgb);
    }

    private static SparseOverlay Sparse(params (int x, int y, byte b, byte g, byte r, byte a)[] px)
    {
        var img = Bgra32Image.Create(4, 3);
        foreach (var (x, y, b, g, r, a) in px)
        { int i = (y * 4 + x) * 4; img.Pixels[i] = b; img.Pixels[i + 1] = g; img.Pixels[i + 2] = r; img.Pixels[i + 3] = a; }
        return SparseOverlay.FromImage(img);
    }

    [Fact] public void MergeKeepsBothAndDrawsSecondOverFirst()
    {
        var a = Sparse((0, 0, 1, 2, 3, 255), (2, 1, 100, 100, 100, 255));
        var b = Sparse((2, 1, 10, 20, 30, 255), (3, 2, 5, 6, 7, 128));
        var m = SparseOverlay.Merge(a, b);
        Assert.Equal([0, 6, 11], m.PixelIndices);
        Assert.Equal([1, 2, 3, 255, 10, 20, 30, 255, 5, 6, 7, 128], m.Bgra);
        Assert.Equal([0, 1, 2, 3], m.RowStarts);
    }
}
