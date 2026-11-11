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
}
