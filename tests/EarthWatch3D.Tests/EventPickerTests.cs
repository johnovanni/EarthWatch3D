using System.Windows;
using System.Windows.Media.Media3D;
using AstroCore.Earth;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Geo;

namespace EarthWatch3D.Tests;

public class EventPickerTests
{   // Camera at (0,-5,0) looking +Y, up +Z, FOV 60, viewport 800x600: the point (0,-90) is at the screen centre.
    private static readonly DateTime T = new(2026, 11, 12, 6, 0, 0, DateTimeKind.Utc);
    private static Projector P() =>
        new(new PerspectiveCamera(new Point3D(0, -5, 0), new Vector3D(0, 5, 0), new Vector3D(0, 0, 1), 60), 800, 600);

    private static VisibleQuake Quake(string id, double lat, double lon) =>
        new(new Earthquake(id, T, new GeoPosition(lat, lon), id, null, 5, 10, "", T), 0);

    private static VisibleEvents Of(params VisibleQuake[] q) => new(q, [], [], []);

    private static WeatherAlert Alert(string id, string severity, DateTime sent, params GeoPolygon[] areas) =>
        new(id, sent, new GeoPosition(0, 0), id, null, "Tornado Warning", severity, T, T.AddHours(1), null, areas, [], "");

    private static GeoPolygon Box(double lat0, double lon0, double lat1, double lon1) =>
        new([[new(lat0, lon0), new(lat0, lon1), new(lat1, lon1), new(lat1, lon0), new(lat0, lon0)]]);

    [Fact] // about 1.66 deg of longitude is 5 px here, 2.65 deg is 8 px
    public void PicksNearestPointWithinRadius()
    {
        var v = Of(Quake("far", 0, -90 - 2.65), Quake("near", 0, -90 + 1.66));
        var hit = EventPicker.Pick(new Point(400, 300), P(), v, null);
        Assert.Equal("near", hit?.Id);
    }

    [Fact]
    public void MissesPointsBeyondTheRadius()
    {
        var v = Of(Quake("q", 0, -90 + 4));   // about 12 px
        Assert.Null(EventPicker.Pick(new Point(400, 300), P(), v, null));
    }

    [Fact]
    public void IgnoresFarSidePoints()
    {
        var v = Of(Quake("behind", 0, 90));   // projects to the screen centre but is hidden by the globe
        Assert.Null(EventPicker.Pick(new Point(400, 300), P(), v, null));
    }

    [Fact]
    public void PointEventBeatsAlertUnderneath()
    {
        var alert = Alert("a", "Extreme", T, Box(-10, -100, 10, -80));
        var v = new VisibleEvents([Quake("q", 0, -90)], [], [alert], []);
        Assert.Equal("q", EventPicker.Pick(new Point(400, 300), P(), v, new GeoPosition(0, -90))?.Id);
    }

    [Fact]
    public void PicksAlertContainingSurfacePoint()
    {
        var v = new VisibleEvents([], [], [Alert("in", "Severe", T, Box(-10, -100, 10, -80)),
                                           Alert("out", "Extreme", T, Box(20, 20, 30, 30))], []);
        Assert.Equal("in", EventPicker.Pick(new Point(400, 300), P(), v, new GeoPosition(0, -90))?.Id);
        Assert.Null(EventPicker.Pick(new Point(400, 300), P(), v, new GeoPosition(50, -90)));
        Assert.Null(EventPicker.Pick(new Point(400, 300), P(), v, null));
    }

    [Fact]
    public void OverlappingAlertsPickMostSevereThenNewest()
    {
        var box = Box(-10, -100, 10, -80);
        var v = new VisibleEvents([], [], [Alert("minor", "Minor", T.AddHours(3), box), Alert("severeOld", "Severe", T, box),
            Alert("severeNew", "Severe", T.AddHours(1), box), Alert("unknown", "Unknown", T.AddHours(5), box)], []);
        Assert.Equal("severeNew", EventPicker.Pick(new Point(400, 300), P(), v, new GeoPosition(0, -90))?.Id);
    }

    [Fact]
    public void AlertWithOnlyZonesUsesTheZoneLookup()
    {
        var a = Alert("z", "Moderate", T) with { ZoneIds = ["zone1"] };
        var v = new VisibleEvents([], [], [a], []);
        IReadOnlyList<GeoPolygon>? Lookup(string id) => id == "zone1" ? [Box(-10, -100, 10, -80)] : null;
        Assert.Equal("z", EventPicker.Pick(new Point(400, 300), P(), v, new GeoPosition(0, -90), Lookup)?.Id);
        Assert.Null(EventPicker.Pick(new Point(400, 300), P(), v, new GeoPosition(0, -90), _ => null));
    }

    [Fact]
    public void PicksCycloneCentreAndEonetFix()
    {
        var c = new CycloneAdvisory(EventSource.Nhc, "nhc:1", T, new GeoPosition(0, -90), "t", null, "PENA", 40, 1002, [], [], [], []);
        var storm = new EonetStorm("e1", T, new GeoPosition(0, -90), "Storm", null, [new StormFix(T, new GeoPosition(0, -90), 50)], null);
        Assert.Same(c, EventPicker.Pick(new Point(400, 300), P(), new VisibleEvents([], [c], [], []), null));
        Assert.Same(storm, EventPicker.Pick(new Point(400, 300), P(),
            new VisibleEvents([], [], [], [new VisibleEonet(storm, storm.Fixes[0])]), null));
    }
}
