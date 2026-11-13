using System.Globalization;
using System.IO;
using System.IO.Compression;
using EarthWatch3D.Core.Lightning;
using EarthWatch3D.Core.Lightning.Mtg;

namespace EarthWatch3D.Tests;

public class MtgReaderTests
{
    private static string Sample(string f) => Path.Combine(AppContext.BaseDirectory, "Samples", f);

    [Fact]
    public void MatchesNetCdf4Dump()
    {
        var flashes = MtgReader.Read(File.ReadAllBytes(Sample("mtg_lfl_sample.zip")));
        // 13,702 flashes in the file; the 873 the product marks low confidence (value 1.0) are left out.
        Assert.Equal(12_829, flashes.Count);

        var rows = File.ReadAllLines(Sample("mtg_lfl_sample.csv")).Skip(1).Take(10).ToArray();
        Assert.Equal(10, rows.Length);
        for (var i = 0; i < rows.Length; i++)
        {
            var c = rows[i].Split(',');
            var t = DateTime.Parse(c[0], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            var lat = double.Parse(c[1], CultureInfo.InvariantCulture);
            var lon = double.Parse(c[2], CultureInfo.InvariantCulture);
            var f = flashes[i];
            Assert.Equal(DateTimeKind.Utc, f.TimeUtc.Kind);
            Assert.True(Math.Abs((f.TimeUtc - t).TotalMilliseconds) <= 1, $"row {i} time {f.TimeUtc:O} vs {t:O}");
            Assert.InRange(f.Lat - lat, -1e-4, 1e-4);
            Assert.InRange(f.Lon - lon, -1e-4, 1e-4);
        }
        Assert.All(flashes, f =>
        {
            Assert.Equal(LightningSensor.Mtg, f.Sensor);
            Assert.Equal(0, f.EnergyJ);
            Assert.InRange(f.Lat, -90, 90);
            Assert.InRange(f.Lon, -180, 180);
            Assert.NotEqual(-180, f.Lon);
        });
    }

    [Fact]
    public void FlashesLieInsideTheProductPeriod()
    {
        var flashes = MtgReader.Read(File.ReadAllBytes(Sample("mtg_lfl_sample.zip")));
        var start = new DateTime(2026, 11, 13, 21, 30, 0, DateTimeKind.Utc);
        Assert.All(flashes, f => Assert.InRange(f.TimeUtc, start, start.AddMinutes(11)));
    }

    [Fact]
    public void GarbageThrowsFormatException() =>
        Assert.Throws<FormatException>(() => MtgReader.Read([1, 2, 3, 4, 5]));

    [Fact]
    public void ZipWithoutABodyFileThrowsFormatException()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            zip.CreateEntry("manifest.xml");
        Assert.Throws<FormatException>(() => MtgReader.Read(ms.ToArray()));
    }

    [Fact]
    public void BodyFileThatIsNotNetCdfThrowsFormatException()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var w = new StreamWriter(zip.CreateEntry("x--CHK-BODY--y_0001.nc").Open());
            w.Write("not a netcdf file");
        }
        Assert.Throws<FormatException>(() => MtgReader.Read(ms.ToArray()));
    }
}
