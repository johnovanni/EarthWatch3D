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
