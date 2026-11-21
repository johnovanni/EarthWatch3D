using System.Reflection;
using EarthWatch3D.Core;
using EarthWatch3D.Core.Lightning.Mtg;
using EarthWatch3D.Core.Storage;
using GlobeCore.Storage;

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

    [Theory]   // every data path, byte for byte as before GlobeCore (AppPaths now wraps GlobeCore's AppDataRoot)
    [InlineData(null, @"C:\L\EarthWatch3D")]
    [InlineData("johnovanni.EarthWatch3D_abc123", @"C:\L\Packages\johnovanni.EarthWatch3D_abc123\LocalCache\Local\EarthWatch3D")]
    public void EveryDataPathIsUnchanged(string? packageFamilyName, string root)
    {
        var p = AppPaths.ForUser(@"C:\L", packageFamilyName);
        Assert.Equal(root, p.Root);
        Assert.Equal(root + @"\cache", p.CacheDir);
        Assert.Equal(root + @"\cache\zones", p.ZonesDir);
        Assert.Equal(root + @"\logs", p.LogsDir);
        Assert.Equal(root + @"\settings.json", p.SettingsFile);
        Assert.Equal(root + @"\cache\events.json", p.EventsFile);
        Assert.Equal(root + @"\cache\clouds", p.CloudsDir);
        Assert.Equal(root + @"\download-meter.json", p.DownloadMeterFile);
    }

    [Fact]   // the values EarthWatch3D now passes to GlobeCore; changing one would rename files or lose saved keys
    public void ValuesPassedToGlobeCoreAreUnchanged()
    {
        Assert.Equal("EarthWatch3D", AppInfo.Name);                               // crash message wording
        Assert.Equal("earthwatch3d-", AppInfo.LogFilePrefix);                     // log file names
        Assert.Equal("EarthWatch3D.EUMETSAT", EumetsatCredentialStore.ProtectorEntropy);   // saved EUMETSAT keys decrypt
    }

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
