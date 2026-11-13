using System.IO.Compression;
using EarthWatch3D.Core.Lightning.Glm;
using PureHDF;

namespace EarthWatch3D.Core.Lightning.Mtg;

/// <summary>
/// Reads the flashes of an MTG LI-2-LFL product: a zip whose CHK-BODY entry is a NetCDF-4 file (docs/data-sources.md
/// section 8). Everything is read in memory; nothing is extracted to disk.
/// </summary>
public static class MtgReader
{
    private static readonly DateTime TimeEpoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const short PositionFill = -32767;
    private const byte ConfidenceFill = 255;
    private const double ConfidenceScale = 0.004;
    /// <summary>Flashes whose scaled flash_filter_confidence is above this are left out (the product's scale runs from 0 = high confidence to 1 = low).</summary>
    private const double MaxConfidenceValue = 0.5;
    /// <summary>A body file larger than this (real ones are about 0.4 MB) is not read into memory.</summary>
    public const long MaxBodyBytes = 64L * 1024 * 1024;

    /// <summary>
    /// The flashes in file order (not time order). EnergyJ is 0: the product has radiance, not energy.
    /// </summary>
    /// <exception cref="FormatException">The bytes are not a readable MTG product.</exception>
    public static IReadOnlyList<LightningFlash> Read(byte[] product)
    {
        ArgumentNullException.ThrowIfNull(product);
        try
        {
            using var zip = new ZipArchive(new MemoryStream(product, writable: false), ZipArchiveMode.Read);
            var entry = zip.Entries.FirstOrDefault(e =>
                    e.FullName.Contains("CHK-BODY", StringComparison.Ordinal) && e.FullName.EndsWith(".nc", StringComparison.OrdinalIgnoreCase))
                ?? throw new FormatException("The MTG product has no CHK-BODY file.");
            if (entry.Length > MaxBodyBytes) throw new FormatException("The MTG body file is implausibly large.");
            var nc = new MemoryStream(checked((int)entry.Length));
            using (var s = entry.Open()) s.CopyTo(nc);
            nc.Position = 0;
            return ReadBody(nc);
        }
        catch (FormatException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new FormatException("Cannot read MTG product: " + ex.Message, ex);
        }
    }

    private static IReadOnlyList<LightningFlash> ReadBody(Stream nc)
    {
        using var file = H5File.Open(nc, leaveOpen: true);
        var latDs = file.Dataset("latitude");
        var lonDs = file.Dataset("longitude");
        var lat = latDs.Read<short[]>();
        var lon = lonDs.Read<short[]>();
        var time = file.Dataset("flash_time").Read<double[]>();
        var confidence = file.Dataset("flash_filter_confidence").Read<byte[]>();
        var latScale = (double)latDs.Attribute("scale_factor").Read<float[]>()[0];
        var lonScale = (double)lonDs.Attribute("scale_factor").Read<float[]>()[0];

        var n = lat.Length;
        if (lon.Length != n || time.Length != n || confidence.Length != n)
            throw new FormatException("MTG flash variables have different lengths.");

        var result = new List<LightningFlash>(n);
        for (var i = 0; i < n; i++)
        {
            if (lat[i] == PositionFill || lon[i] == PositionFill) continue;
            if (confidence[i] != ConfidenceFill && confidence[i] * ConfidenceScale > MaxConfidenceValue) continue;
            double la = lat[i] * latScale, lo = lon[i] * lonScale;
            if (la < -90 || la > 90 || !GlmReader.TryNormalizeLon(lo, out var wrapped)) continue;
            if (!double.IsFinite(time[i])) continue;
            var t = TimeEpoch.AddTicks((long)Math.Round(time[i] * TimeSpan.TicksPerSecond));
            result.Add(new LightningFlash(t, la, wrapped, 0, LightningSensor.Mtg));
        }
        return result;
    }
}
