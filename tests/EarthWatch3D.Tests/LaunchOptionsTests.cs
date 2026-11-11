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
