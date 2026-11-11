using EarthWatch3D.Core;

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
}
