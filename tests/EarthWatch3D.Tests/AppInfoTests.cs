using System.Reflection;
using EarthWatch3D.Core;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.Tests;

public class AppInfoTests
{
    [Fact]
    public void UserAgentNamesAppAndProjectOnly()
    {
        Assert.StartsWith("EarthWatch3D/", AppInfo.UserAgent);
        Assert.EndsWith("(+https://github.com/johnovanni/EarthWatch3D)", AppInfo.UserAgent);
        Assert.DoesNotContain("@", AppInfo.UserAgent);
    }

    [Fact]
    public void VersionIsThreePartsAndMsixVersionAddsZero()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppInfo.Version);
        Assert.Equal(AppInfo.Version + ".0", AppInfo.MsixVersion);
    }

    [Fact]
    public void AppAndCoreShareTheVersion() =>
        Assert.Equal(AppInfo.Version, VersionOf(typeof(EarthWatch3D.App.App).Assembly));

    [Fact]
    public void UnpackagedRootIsUnchanged() =>
        Assert.Equal(@"C:\L\EarthWatch3D", AppPaths.ForUser(@"C:\L", null).Root);

    [Fact]
    public void PackagedRootIsTheLocalCacheFolder() =>
        Assert.Equal(@"C:\L\Packages\johnovanni.EarthWatch3D_abc123\LocalCache\Local\EarthWatch3D",
                     AppPaths.ForUser(@"C:\L", "johnovanni.EarthWatch3D_abc123").Root);

    [Fact]
    public void TestsDoNotRunFromAPackage() => Assert.Null(PackageIdentity.FamilyName);

    // The assembly's informational version without the "+commit" build metadata.
    private static string VersionOf(Assembly assembly)
    {
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var plus = info.IndexOf('+');
        return plus >= 0 ? info[..plus] : info;
    }
}
