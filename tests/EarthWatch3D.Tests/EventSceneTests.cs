using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using AstroCore.Earth;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Geo;
using HelixToolkit.Wpf;

namespace EarthWatch3D.Tests;

public class EventSceneTests
{
    private static readonly DateTime T = new(2026, 11, 12, 6, 0, 0, DateTimeKind.Utc);

    private static Earthquake Quake(string id, double lat, double lon, double mag, TimeSpan age) =>
        new(id, T - age, new GeoPosition(lat, lon), id, null, mag, 10, "", T - age);

    private static CycloneAdvisory Storm(string name, double windKt, GeoPosition at) =>
        new(EventSource.Nhc, "nhc:" + name, T, at, name, null, name, windKt, null,
            [new GeoLine([new GeoPosition(at.LatitudeDeg - 5, at.LongitudeDeg + 5), at])],
            [new GeoLine([at, new GeoPosition(at.LatitudeDeg + 5, at.LongitudeDeg - 10)])], [], []);

    [Theory]
    [InlineData(90.0)]
    [InlineData(-90.0)]
    [InlineData(89.9999999)]
    [InlineData(0.0)]
    public void TangentFrameIsOrthonormalAndFacesOutEvenAtThePoles(double lat)
    {
        var (centre, e1, e2) = EventScene.TangentFrame(new GeoPosition(lat, 30), 1.002);
        var n = (Vector3D)centre / 1.002;

        Assert.Equal(1.002, ((Vector3D)centre).Length, 12);
        Assert.Equal(1, e1.Length, 12);
        Assert.Equal(1, e2.Length, 12);
        Assert.Equal(0, Vector3D.DotProduct(e1, e2), 12);
        Assert.Equal(0, Vector3D.DotProduct(e1, n), 12);
        Assert.Equal(0, Vector3D.DotProduct(e2, n), 12);
        // e1 × e2 points outward, so the disc's front face (counter-clockwise) faces the camera.
        Assert.Equal(1, Vector3D.DotProduct(Vector3D.CrossProduct(e1, e2), n), 12);
    }

    private static (EventScene Scene, HelixViewport3D Viewport, PerspectiveCamera Camera) NewScene()
    {
        var camera = new PerspectiveCamera(new Point3D(3, 0, 0), new Vector3D(-1, 0, 0), new Vector3D(0, 0, 1), 45);
        var viewport = new HelixViewport3D { Camera = camera };
        return (new EventScene(viewport), viewport, camera);
    }

    private static VisibleEvents Quakes(params (double Mag, TimeSpan Age)[] qs) => new(
        qs.Select((q, i) => new VisibleQuake(Quake("q" + i, i, i, q.Mag, q.Age), q.Age / TimeSpan.FromHours(24))).ToList(),
        [], [], []);

    [Fact]
    public void ZoomDoesNotRebuildDiscsWhenNoDiscIsAtTheMinimumSize()
    {
        Sta.Run(() =>
        {
            var (scene, _, camera) = NewScene();
            var projector = new Projector(camera, 1000, 700);
            var big = Quakes((7.0, TimeSpan.FromHours(2)), (6.5, TimeSpan.FromHours(10)));   // 0.025 and 0.020 Earth radii
            scene.Update(big, T, false, projector, 3);   // minimum 0.005
            int builds = scene.DiscMeshBuilds;
            scene.Update(big, T, false, projector, 2);   // minimum halves: no disc is at the minimum before or after
            Assert.Equal(builds, scene.DiscMeshBuilds);

            var small = Quakes((2.5, TimeSpan.FromHours(2)));   // 0.004: at the minimum (0.0025 → 0.005)
            scene.Update(small, T, false, projector, 2);
            builds = scene.DiscMeshBuilds;
            scene.Update(small, T, false, projector, 3);
            Assert.True(scene.DiscMeshBuilds > builds);
        });
    }

    [Fact]
    public void RingsAnimateAtMost20HzAndReuseTwoBuffers()
    {
        Sta.Run(() =>
        {
            var (scene, viewport, camera) = NewScene();
            var v = Quakes((4.0, TimeSpan.FromMinutes(5)), (3.0, TimeSpan.FromMinutes(30)));
            scene.Update(v, T, live: true, new Projector(camera, 1000, 700), 3);
            Assert.True(scene.HasRings);
            var rings = scene.RingVisual;
            Assert.Contains(rings, Descendants(viewport.Children));

            Assert.True(scene.AnimateRings(TimeSpan.FromSeconds(1.00)));
            var a = rings.Points;
            Assert.Equal(2 * 32 * 2, a.Count);
            Assert.False(scene.AnimateRings(TimeSpan.FromSeconds(1.03)));   // under 50 ms later: skipped
            Assert.Same(a, rings.Points);
            Assert.True(scene.AnimateRings(TimeSpan.FromSeconds(1.06)));
            var b = rings.Points;
            Assert.NotSame(a, b);
            Assert.True(scene.AnimateRings(TimeSpan.FromSeconds(1.12)));
            Assert.Same(a, rings.Points);   // the two buffers alternate; nothing new is allocated
        });
    }

    private static IEnumerable<Visual3D> Descendants(IEnumerable<Visual3D> visuals)
    {
        foreach (var v in visuals)
        {
            yield return v;
            if (v is ModelVisual3D m)
                foreach (var c in Descendants(m.Children)) yield return c;
        }
    }

    [Fact]
    public void UpdateBuildsDiscsGlyphsTracksAndLabels()
    {
        Sta.Run(() =>
        {
            var camera = new PerspectiveCamera(new Point3D(3, 0, 0), new Vector3D(-1, 0, 0), new Vector3D(0, 0, 1), 45);
            var viewport = new HelixViewport3D { Camera = camera };
            var scene = new EventScene(viewport);
            var projector = new Projector(camera, 1000, 700);
            var window = TimeSpan.FromHours(24);
            var quakes = new[]
            {
                Quake("a", 0, 0, 6.14, TimeSpan.FromMinutes(10)),    // newest: ring and label
                Quake("b", 10, 5, 3.0, TimeSpan.FromHours(20)),      // oldest bucket, no label
                Quake("c", 0, 180, 7.0, TimeSpan.FromHours(5)),      // far side: label hidden
            }.Select(q => new VisibleQuake(q, (T - q.TimeUtc) / window)).ToList();
            var v = new VisibleEvents(quakes, [Storm("Pena", 40, new GeoPosition(10, -10))], [], []);

            scene.Update(v, T, live: true, projector, 3);
            Assert.Equal(3, scene.DiscCount);
            Assert.True(scene.HasRings);
            scene.AnimateRings(TimeSpan.FromSeconds(0.75));

            var canvas = new Canvas { Width = 1000, Height = 700 };
            canvas.Measure(new System.Windows.Size(1000, 700));
            canvas.Arrange(new System.Windows.Rect(0, 0, 1000, 700));
            var overlay = new LabelOverlay(canvas);
            scene.DrawLabels(overlay, projector);
            var shown = canvas.Children.OfType<TextBlock>().Where(t => t.Visibility == System.Windows.Visibility.Visible)
                .Select(t => t.Text).ToList();
            Assert.Contains("PENA TS", shown);
            Assert.Contains("M6.1", shown);
            Assert.DoesNotContain("M7.0", shown);

            // Not live: no rings.
            scene.Update(v with { }, T, live: false, projector, 3);
            Assert.False(scene.HasRings);
        });
    }
}
