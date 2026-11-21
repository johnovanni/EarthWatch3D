using System.IO;
using EarthWatch3D.App.Diagnostics;

namespace EarthWatch3D.Tests;

public class SnapshotScenesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ew3d-scenes-" + Guid.NewGuid().ToString("N"));

    public SnapshotScenesTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private string Write(string json)
    {
        string path = Path.Combine(_dir, "scenes.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void ParsesAValidFile()
    {
        var scenes = SnapshotDiagnostic.LoadScenes(Write("""
            { "scenes": [
              { "name": "a", "displayTimeUtc": "2026-11-11T20:00:00Z", "layers": ["Quakes", "coastlines"] },
              { "name": "b", "displayTimeUtc": "2026-11-12T08:00:00Z", "lookAt": [30, 100], "faceSun": true } ] }
            """));
        Assert.Equal(2, scenes.Count);
        Assert.Equal("a", scenes[0].Name);
        Assert.Equal(new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc), scenes[0].DisplayTimeUtc);
        Assert.Null(scenes[0].LookAt);
        Assert.False(scenes[0].FaceSun);
        Assert.Equal(["quakes", "coastlines"], scenes[0].Layers);
        Assert.Equal(30, scenes[1].LookAt!.Value.LatitudeDeg);
        Assert.Equal(100, scenes[1].LookAt!.Value.LongitudeDeg);
        Assert.True(scenes[1].FaceSun);
        Assert.Empty(scenes[1].Layers);
    }

    [Theory]
    [InlineData("""{ "scenes": [ { "name": "a", "displayTimeUtc": "2026-11-11T20:00:00Z", "layers": ["volcanoes"] } ] }""")]
    [InlineData("""{ "scenes": [ { "name": "a", "displayTimeUtc": "2026-11-11T20:00:00Z" }, { "name": "a", "displayTimeUtc": "2026-11-11T20:00:00Z" } ] }""")]
    [InlineData("""{ "scenes": [ { "name": "a", "displayTimeUtc": "yesterday" } ] }""")]
    [InlineData("""{ "scenes": [ { "name": "a", "displayTimeUtc": "2026-11-11T20:00:00" } ] }""")]
    [InlineData("""{ "scenes": [ ] }""")]
    [InlineData("not json")]
    public void RejectsABadFile(string json) =>
        Assert.Throws<FormatException>(() => SnapshotDiagnostic.LoadScenes(Write(json)));
}
