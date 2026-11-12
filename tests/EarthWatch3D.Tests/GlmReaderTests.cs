using System.IO;
using System.Globalization;
using EarthWatch3D.Core.Lightning;
using EarthWatch3D.Core.Lightning.Glm;

namespace EarthWatch3D.Tests;

public class GlmReaderTests
{
    private static string Sample(string f) => Path.Combine(AppContext.BaseDirectory, "Samples", f);

    [Fact]
    public void MatchesNetCdf4Dump()
    {
        using var s = File.OpenRead(Sample("glm_g19_sample.nc"));
        var flashes = GlmReader.Read(s, LightningSensor.GoesEast);
        Assert.Equal(757, flashes.Count);

        var rows = File.ReadAllLines(Sample("glm_g19_sample.csv")).Skip(1).Take(10).ToArray();
        Assert.Equal(10, rows.Length);
        for (var i = 0; i < rows.Length; i++)
        {
            var c = rows[i].Split(',');
            var t = DateTime.Parse(c[0], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            var lat = double.Parse(c[1], CultureInfo.InvariantCulture);
            var lon = double.Parse(c[2], CultureInfo.InvariantCulture);
            var energy = double.Parse(c[3], CultureInfo.InvariantCulture);
            var f = flashes[i];
            Assert.Equal(DateTimeKind.Utc, f.TimeUtc.Kind);
            Assert.True(Math.Abs((f.TimeUtc - t).TotalMilliseconds) <= 1, $"row {i} time {f.TimeUtc:O} vs {t:O}");
            Assert.InRange(f.Lat - lat, -1e-5, 1e-5);
            Assert.InRange(f.Lon - lon, -1e-5, 1e-5);
            Assert.InRange(Math.Abs(f.EnergyJ - energy) / energy, 0, 1e-5);
            Assert.Equal(LightningSensor.GoesEast, f.Sensor);
        }
    }

    [Fact]
    public void ReadsGoesWestSample()
    {
        using var s = File.OpenRead(Sample("glm_g18_sample.nc"));
        var flashes = GlmReader.Read(s, LightningSensor.GoesWest);
        Assert.NotEmpty(flashes);
        Assert.All(flashes, f =>
        {
            Assert.Equal(LightningSensor.GoesWest, f.Sensor);
            Assert.InRange(f.Lat, -90, 90);
            Assert.InRange(f.Lon, -180, 180);
            Assert.NotEqual(-180, f.Lon);
        });
    }

    [Fact]
    public void TruncatedFileThrowsFormatException()
    {
        var bytes = File.ReadAllBytes(Sample("glm_g19_sample.nc")).Take(50_000).ToArray();
        using var s = new MemoryStream(bytes);
        Assert.Throws<FormatException>(() => GlmReader.Read(s, LightningSensor.GoesEast));
    }

    [Fact]
    public void GarbageThrowsFormatException()
    {
        using var s = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        Assert.Throws<FormatException>(() => GlmReader.Read(s, LightningSensor.GoesEast));
    }
}

public class GlmLongitudeTests
{
    [Theory]
    [InlineData(540.0)]
    [InlineData(1e30)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-361.0)]
    public void RejectsCorruptLongitudes(double lon) => Assert.False(GlmReader.TryNormalizeLon(lon, out _));

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(180.0, 180.0)]
    [InlineData(-180.0, 180.0)]
    [InlineData(190.0, -170.0)]
    [InlineData(-190.0, 170.0)]
    [InlineData(360.0, 0.0)]
    [InlineData(-360.0, 0.0)]
    public void WrapsLongitude(double lon, double expected)
    {
        Assert.True(GlmReader.TryNormalizeLon(lon, out var r));
        Assert.Equal(expected, r);
    }
}
