using System.IO;
using System.Text;
using AstroCore.Earth;
using EarthWatch3D.Core.Feeds.Gdacs;

namespace EarthWatch3D.Tests;

public sealed class GdacsHistoryTests
{
    private static DateTime U(int y, int mo, int d, int h) => new(y, mo, d, h, 0, 0, DateTimeKind.Utc);

    private static GdacsEvent Event(DateTime modified) =>
        new(1, 1, "TEST", true, "NOAA", new GeoPosition(0, 0), null, modified, new Uri("https://x.test/g"), null);

    [Fact]
    public void SamplePenaHasPastFixes()
    {
        using var list = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Samples", "gdacs_events_tc.json"));
        var pena = GdacsReader.ReadEventList(list, new RecordingLog()).Single(e => e.EventId == 1001348);
        using var s = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Samples", "gdacs_geometry.json"));
        var a = GdacsReader.ReadGeometry(s, pena, new RecordingLog());
        Assert.NotEmpty(a.History);
        Assert.Equal(U(2026, 11, 6, 21), a.History[0].TimeUtc);
        Assert.Equal(11.30, a.History[0].Position.LatitudeDeg, 2);
        Assert.Equal(-150.10, a.History[0].Position.LongitudeDeg, 2);
        Assert.True(a.History[^1].TimeUtc <= U(2026, 11, 11, 15));
        Assert.Equal(U(2026, 11, 11, 15), a.History[^1].TimeUtc);
        Assert.All(a.History, f => Assert.Null(f.WindKt));
        for (var i = 1; i < a.History.Count; i++) Assert.True(a.History[i].TimeUtc > a.History[i - 1].TimeUtc);
    }

    [Fact]
    public void YearRollsBackAcrossNewYear() =>
        Assert.Equal(U(2026, 12, 31, 18), GdacsReader.PointTime("31/12 18:00 UTC", U(2027, 1, 1, 6)));

    [Fact]
    public void YearRollsForwardAcrossNewYear() =>
        Assert.Equal(U(2027, 1, 1, 0), GdacsReader.PointTime("01/01 00:00 UTC", U(2026, 12, 31, 18)));

    private static string Feature(string label, double lon)
    {
        string C(double x, double y) => FormattableString.Invariant($"[{x},{y}]");
        var ring = string.Join(",", C(lon, 10.0), C(lon + 0.03, 10.03), C(lon, 10.06), C(lon - 0.03, 10.03), C(lon, 10.0));
        return "{\"type\":\"Feature\",\"properties\":{\"Class\":\"Point_Polygon_Point_0\",\"polygonlabel\":\"" + label
            + "\",\"todate\":\"2026-11-11T15:00:00\"},\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[" + ring + "]]}}";
    }

    [Fact]
    public void UnreadableLabelIsSkipped()
    {
        var json = "{\"features\":[" + Feature("soon", -150) + "," + Feature("10/11 03:00 UTC", -151) + "]}";
        var log = new RecordingLog();
        var a = GdacsReader.ReadGeometry(new MemoryStream(Encoding.UTF8.GetBytes(json)), Event(U(2026, 11, 11, 15)), log);
        Assert.Single(a.History);
        Assert.Equal(U(2026, 11, 10, 3), a.History[0].TimeUtc);
        Assert.Single(log.Warnings);
        Assert.Equal(PointTimeNull(), GdacsReader.PointTime("soon", U(2026, 11, 11, 15)));
    }

    private static DateTime? PointTimeNull() => null;

    [Fact]
    public void CircleOverDateLineIsCentredCorrectly()
    {
        var json = "{\"features\":[" + Feature("10/11 03:00 UTC", 179.99) + "]}";
        var a = GdacsReader.ReadGeometry(new MemoryStream(Encoding.UTF8.GetBytes(json)), Event(U(2026, 11, 11, 15)), new RecordingLog());
        Assert.Equal(179.99, a.History[0].Position.LongitudeDeg, 2);
    }
}
