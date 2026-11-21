using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.Tests;

/// <summary>
/// The download meter itself is tested in GlobeCore (GlobeCore.Tests.DownloadMeterTests); the daily limit read from the
/// app's settings stays here.
/// </summary>
public sealed class DownloadMeterTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(10, 10_000_000L)]
    [InlineData(100_000, 100_000_000_000L)]
    [InlineData(9, null)]
    [InlineData(0, null)]
    [InlineData(-5, null)]
    [InlineData(100_001, null)]
    public void HandEditedLimitOutsideTheRangeMeansNoLimit(int? mb, long? bytes)
    {
        Assert.Equal(bytes, AppSettings.DailyLimitBytes(mb));
    }
}
