using PureHDF;

namespace EarthWatch3D.Core.Lightning.Glm;

/// <summary>Reads the flash-level variables of a GOES GLM L2 LCFA file (NetCDF-4 / HDF5) with PureHDF.</summary>
public static class GlmReader
{
    private static readonly DateTime ProductEpoch = new(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private const ushort Unsigned16Fill = 65535;

    /// <exception cref="FormatException">The stream is not a readable GLM file.</exception>
    public static IReadOnlyList<LightningFlash> Read(Stream nc, LightningSensor sensor)
    {
        try
        {
            using var file = H5File.Open(nc, leaveOpen: true);
            var lat = file.Dataset("flash_lat").Read<float[]>();
            var lon = file.Dataset("flash_lon").Read<float[]>();
            var energyDs = file.Dataset("flash_energy");
            var timeDs = file.Dataset("flash_time_offset_of_first_event");
            var energy = energyDs.Read<ushort[]>();
            var offset = timeDs.Read<ushort[]>();
            var productTime = file.Dataset("product_time").Read<double>();

            var eScale = (double)energyDs.Attribute("scale_factor").Read<float[]>()[0];
            var eAdd = (double)energyDs.Attribute("add_offset").Read<float[]>()[0];
            var tScale = (double)timeDs.Attribute("scale_factor").Read<float[]>()[0];
            var tAdd = (double)timeDs.Attribute("add_offset").Read<float[]>()[0];

            var n = lat.Length;
            if (lon.Length != n || energy.Length != n || offset.Length != n)
                throw new FormatException("GLM flash variables have different lengths.");

            var result = new List<LightningFlash>(n);
            for (var i = 0; i < n; i++)
            {
                double la = lat[i], lo = lon[i];
                if (!double.IsFinite(la) || !double.IsFinite(lo) || la < -90 || la > 90) continue;
                if (offset[i] == Unsigned16Fill) continue; // no time: cannot be placed
                var seconds = productTime + offset[i] * tScale + tAdd;
                var time = ProductEpoch.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
                var joules = energy[i] == Unsigned16Fill ? 0 : energy[i] * eScale + eAdd;
                result.Add(new LightningFlash(time, la, NormalizeLon(lo), joules, sensor));
            }
            return result;
        }
        catch (FormatException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new FormatException("Cannot read GLM file: " + ex.Message, ex);
        }
    }

    /// <summary>Wraps to (-180, 180].</summary>
    private static double NormalizeLon(double lon)
    {
        while (lon > 180) lon -= 360;
        while (lon <= -180) lon += 360;
        return lon;
    }
}
