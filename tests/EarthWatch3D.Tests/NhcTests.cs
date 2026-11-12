using System.IO;
using System.Text;
using AstroCore.Earth;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Feeds.Nhc;
using EarthWatch3D.Core.Geo;

namespace EarthWatch3D.Tests;

public sealed class NhcTests
{
    private static string Sample(string f) => Path.Combine(AppContext.BaseDirectory, "Samples", f);

    private static IReadOnlyList<NhcStorm> Read(string json, RecordingLog? log = null) =>
        NhcReader.ReadCurrentStorms(new MemoryStream(Encoding.UTF8.GetBytes(json)), log ?? new RecordingLog());

    [Fact]
    public void ReadsCurrentStorms()
    {
        using var s = File.OpenRead(Sample("nhc_CurrentStorms.json"));
        var storms = NhcReader.ReadCurrentStorms(s, new RecordingLog());
        var p = Assert.Single(storms);
        Assert.Equal(("cp042026", "Pena", "TS"), (p.Id, p.Name, p.Classification));
        Assert.Equal(40, p.WindKt);
        Assert.Equal(1002, p.PressureMb);
        Assert.Equal(new GeoPosition(9.9, -167.7), p.Position);
        Assert.Equal(new DateTime(2026, 11, 11, 15, 0, 0, DateTimeKind.Utc), p.LastUpdateUtc);
        Assert.Equal("020", p.TrackAdv);
        Assert.Equal("020", p.ConeAdv);
        Assert.NotNull(p.TrackKmz);
        Assert.NotNull(p.ConeKmz);
    }

    [Fact]
    public void ConeKmzSplitsAtDateLine()
    {
        using var s = File.OpenRead(Sample("nhc_cone.kmz"));
        var (_, _, polys) = KmlReader.ReadKmz(s);
        Assert.Equal(2, polys.Count);
        var lons = polys.SelectMany(p => p.Rings.SelectMany(r => r)).Select(q => q.LongitudeDeg).ToList();
        Assert.All(lons, l => Assert.InRange(l, -180, 180));
        Assert.Contains(polys, p => p.Rings.SelectMany(r => r).Any(q => q.LongitudeDeg == 180));
        Assert.Contains(polys, p => p.Rings.SelectMany(r => r).Any(q => q.LongitudeDeg == -180));
    }

    [Fact]
    public void TrackKmzSplitsAtDateLine()
    {
        using var s = File.OpenRead(Sample("nhc_track.kmz"));
        var (lines, points, _) = KmlReader.ReadKmz(s);
        Assert.Contains(lines, l => l.Points[^1].LongitudeDeg == -180);
        Assert.Contains(lines, l => l.Points[0].LongitudeDeg == 180 && l.Points.Any(q => Math.Abs(q.LongitudeDeg - 178.4) < 1e-9));
        Assert.All(lines, l => Assert.Contains(l.Points, q => q != l.Points[0]));
        Assert.NotEmpty(points);
        Assert.All(points, q => Assert.InRange(q.LongitudeDeg, -180, 180));
    }

    [Fact]
    public void KmzWithoutKmlGivesEmptyAndWarns()
    {
        var ms = new MemoryStream();
        using (var z = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
            using (var w = new StreamWriter(z.CreateEntry("readme.txt").Open())) w.Write("x");
        ms.Position = 0;
        var log = new RecordingLog();
        var (l, p, g) = KmlReader.ReadKmz(ms, log);
        Assert.Empty(l); Assert.Empty(p); Assert.Empty(g);
        Assert.Single(log.Warnings);
    }

    private static MemoryStream Kmz(string kml)
    {
        var ms = new MemoryStream();
        using (var z = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        using (var w = new StreamWriter(z.CreateEntry("a.kml").Open())) w.Write(kml);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void OversizedKmlEntryRejected()
    {
        var big = "<kml><!--" + new string('x', 21 * 1024 * 1024) + "--></kml>";
        Assert.Throws<InvalidDataException>(() => KmlReader.ReadKmz(Kmz(big)));
    }

    [Fact]
    public void NonFiniteKmlCoordinateSkipped()
    {
        var log = new RecordingLog();
        var (lines, points, _) = KmlReader.ReadKmz(Kmz(
            "<kml xmlns=\"http://www.opengis.net/kml/2.2\"><LineString><coordinates>1,2 Infinity,3 NaN,4</coordinates></LineString>"
            + "<Point><coordinates>10,20</coordinates></Point></kml>"), log);
        Assert.Empty(lines);
        Assert.Single(points);
        Assert.Single(log.Warnings);
    }

    [Fact]
    public void DateLineRejectsNonFiniteInput()
    {
        Assert.Throws<ArgumentException>(() => DateLine.SplitLine([(0, 0), (double.PositiveInfinity, 1)]));
        Assert.Throws<ArgumentException>(() => DateLine.SplitLine([(0, 0), (1e300, 1)]));
        Assert.Throws<ArgumentException>(() => DateLine.SplitPolygon([[(0, 0), (double.NaN, 1), (1, 1)]]));
    }

    [Fact]
    public void EmptyActiveStormsGivesNoStorms() => Assert.Empty(Read("""{"activeStorms":[]}"""));

    [Fact]
    public void SkipsMalformedRecords()
    {
        var log = new RecordingLog();
        var storms = Read("""
            {"activeStorms":[
              {"id":"al012026","name":"A","classification":"TD","intensity":"","pressure":"x","latitudeNumeric":20,"longitudeNumeric":-50,"lastUpdate":"2026-11-11T15:00:00.000Z"},
              {"name":"NoId","latitudeNumeric":1,"longitudeNumeric":1,"lastUpdate":"2026-11-11T15:00:00.000Z"}
            ]}
            """, log);
        var a = Assert.Single(storms);
        Assert.Equal("al012026", a.Id);
        Assert.Null(a.WindKt);
        Assert.Null(a.PressureMb);
        Assert.Null(a.TrackKmz);
        Assert.NotEmpty(log.Warnings);
    }

    [Fact]
    public async Task FeedDownloadsKmzOnlyWhenAdvisoryChanges()
    {
        var handler = new SampleHttpHandler();
        handler.Map("https://www.nhc.noaa.gov/CurrentStorms.json", Sample("nhc_CurrentStorms.json"));
        handler.Map("https://www.nhc.noaa.gov/storm_graphics/api/CP042026_020adv_TRACK.kmz", Sample("nhc_track.kmz"));
        handler.Map("https://www.nhc.noaa.gov/storm_graphics/api/CP042026_020adv_CONE.kmz", Sample("nhc_cone.kmz"));
        var store = new EventStore(new TestClock(new DateTime(2026, 11, 11, 15, 30, 0, DateTimeKind.Utc)));
        using var http = new FeedHttp(handler);
        var feed = new NhcFeed(http, store, new RecordingLog());
        Assert.Equal("NHC", feed.Name);
        await feed.RunOnceAsync(CancellationToken.None);
        await feed.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, handler.Requests.Count(r => r.EndsWith("TRACK.kmz")));
        Assert.Equal(1, handler.Requests.Count(r => r.EndsWith("CONE.kmz")));
        var c = Assert.Single(store.Snapshot.Cyclones);
        Assert.Equal(("nhc:cp042026", "TS Pena", EventSource.Nhc), (c.Id, c.Title, c.Source));
        Assert.Equal(2, c.Cone.Count);
        Assert.NotEmpty(c.ForecastTrack);
        Assert.Empty(c.PastTrack);
        Assert.Equal(40, c.WindKt);
        Assert.Equal(1002, c.PressureHpa);
    }

    [Fact]
    public async Task FeedKeepsStormAndRetriesWhenKmzFails()
    {
        var handler = new SampleHttpHandler();
        handler.Map("https://www.nhc.noaa.gov/CurrentStorms.json", Sample("nhc_CurrentStorms.json"));
        handler.Map("https://www.nhc.noaa.gov/storm_graphics/api/CP042026_020adv_CONE.kmz", Sample("nhc_cone.kmz"));
        var store = new EventStore(new TestClock(new DateTime(2026, 11, 11, 15, 30, 0, DateTimeKind.Utc)));
        using var http = new FeedHttp(handler);
        var log = new RecordingLog();
        var feed = new NhcFeed(http, store, log);
        await feed.RunOnceAsync(CancellationToken.None);
        var c = Assert.Single(store.Snapshot.Cyclones);
        Assert.Empty(c.ForecastTrack);
        Assert.Equal(2, c.Cone.Count);
        Assert.NotEmpty(log.Warnings);
        await feed.RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count(r => r.EndsWith("TRACK.kmz")));
        Assert.Equal(1, handler.Requests.Count(r => r.EndsWith("CONE.kmz")));
    }
}
