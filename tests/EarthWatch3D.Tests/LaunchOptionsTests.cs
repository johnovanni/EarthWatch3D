using EarthWatch3D.App.Diagnostics;

namespace EarthWatch3D.Tests;

public class LaunchOptionsTests
{
    [Fact]
    public void NoArgumentsMeansNoDiagnostics()
    {
        var o = LaunchOptions.Parse([]);
        Assert.Equal(new LaunchOptions(null, false, LaunchOptions.DefaultDelayMs), o);
        Assert.False(o.Any);
    }

    [Fact]
    public void ParsesSnapshotFaceSunAndDelay()
    {
        var o = LaunchOptions.Parse(["--face-sun", "--snapshot", @"C:\tmp\a b.png", "--delay-ms", "2500"]);
        Assert.Equal(new LaunchOptions(@"C:\tmp\a b.png", true, 2500), o);
        Assert.True(o.Any);
    }

    [Fact]
    public void IgnoresUnknownAndIncompleteArguments()
    {
        var o = LaunchOptions.Parse(["--bench", "--delay-ms", "soon", "--snapshot"]);
        Assert.Equal(new LaunchOptions(null, false, LaunchOptions.DefaultDelayMs), o);
    }

    [Fact]
    public void ParsesSnapshotRecompose()
    {
        var o = LaunchOptions.Parse(["--snapshot", "x.png", "--snapshot-recompose"]);
        Assert.Equal(new LaunchOptions("x.png", false, LaunchOptions.DefaultDelayMs, SnapshotRecompose: true), o);
        Assert.False(LaunchOptions.Parse(["--snapshot", "x.png"]).SnapshotRecompose);
    }

    [Theory]
    [InlineData(@"C:\tmp\earth.png", "a", @"C:\tmp\earth-a.png")]
    [InlineData("shot", "b", "shot-b")]
    public void SuffixedPathGoesBeforeTheExtension(string path, string suffix, string expected) =>
        Assert.Equal(expected, LaunchOptions.WithSuffix(path, suffix));
}

public class LaunchOptionsOfflineTests
{
    [Fact]
    public void ParsesOfflineSamplesAndDisplayTime()
    {
        var o = LaunchOptions.Parse(["--offline-samples", @"tests\Samples", "--display-time", "2026-11-11T20:00:00Z"]);
        Assert.Null(o.Error);
        Assert.Equal(@"tests\Samples", o.OfflineSamplesDir);
        Assert.Equal(new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc), o.DisplayTimeUtc);
        Assert.Equal(DateTimeKind.Utc, o.DisplayTimeUtc!.Value.Kind);
    }

    [Theory]
    [InlineData("2026-11-11T20:00Z", 0)]
    [InlineData("2026-11-11T20:00:00.5Z", 500)]
    [InlineData("2026-11-11T21:00:00+01:00", 0)]
    public void AcceptsOtherIsoUtcForms(string text, int ms)
    {
        var o = LaunchOptions.Parse(["--display-time", text]);
        Assert.Null(o.Error);
        Assert.Equal(new DateTime(2026, 11, 11, 20, 0, 0, ms, DateTimeKind.Utc), o.DisplayTimeUtc);
        Assert.Equal(DateTimeKind.Utc, o.DisplayTimeUtc!.Value.Kind);
    }

    [Theory]
    [InlineData("2026-13-11T20:00:00Z")]
    [InlineData("yesterday")]
    [InlineData("2026-11-11T20:00:00")]   // no zone: local or UTC? Refused rather than guessed
    public void InvalidDisplayTimeIsAClearError(string text)
    {
        var o = LaunchOptions.Parse(["--display-time", text]);
        Assert.Null(o.DisplayTimeUtc);
        Assert.NotNull(o.Error);
        Assert.Contains("--display-time", o.Error);
        Assert.Contains(text, o.Error);
        Assert.Contains("2026-11-11T20:00:00Z", o.Error);   // shows the expected form
    }

    [Theory]
    [InlineData("--display-time")]
    [InlineData("--offline-samples")]
    [InlineData("--look-at")]
    public void MissingValueIsAnError(string option)
    {
        var o = LaunchOptions.Parse([option]);
        Assert.NotNull(o.Error);
        Assert.Contains(option, o.Error);
    }

    [Fact]
    public void ParsesLookAt()
    {
        var o = LaunchOptions.Parse(["--look-at", "10,-165"]);
        Assert.Null(o.Error);
        Assert.Equal(new AstroCore.Earth.GeoPosition(10, -165), o.LookAt);
        Assert.True(o.Any);
        Assert.Equal(new AstroCore.Earth.GeoPosition(-33.5, 151.25), LaunchOptions.Parse(["--look-at", "-33.5, 151.25"]).LookAt);
    }

    [Theory]
    [InlineData("10")]
    [InlineData("91,0")]
    [InlineData("0,181")]
    [InlineData("north,west")]
    public void InvalidLookAtIsAnError(string text)
    {
        var o = LaunchOptions.Parse(["--look-at", text]);
        Assert.Null(o.LookAt);
        Assert.NotNull(o.Error);
        Assert.Contains("--look-at", o.Error);
    }

    [Fact]
    public void ErrorsAreCollected()
    {
        var o = LaunchOptions.Parse(["--display-time", "soon", "--look-at", "x"]);
        Assert.Contains("--display-time", o.Error);
        Assert.Contains("--look-at", o.Error);
    }
}
