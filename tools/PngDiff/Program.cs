using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// PngDiff <dirA> <dirB> [--tolerance D]
// Compares every *.png in dirA with the same-named file in dirB, byte for byte after converting both to Bgra32.
// --bbox adds the box (pixels) around the differing pixels to each differing line.
// One line per file: "name: identical" or "name: N pixels differ, max channel delta D". Exits 0 only when every file is
// identical or within the tolerance (largest per-channel difference, default 0); 1 when not; 2 for a usage error.
// WPF imaging only (no System.Drawing).
return PngDiff.Run(args);

static class PngDiff
{
    public static int Run(string[] args)
    {
        int tolerance = 0;
        bool bbox = false;
        var dirs = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--tolerance" && i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int t)
                && t <= 255)
                tolerance = t;
            else if (args[i] == "--bbox") bbox = true;
            else if (args[i].StartsWith("--", StringComparison.Ordinal)) return Usage();
            else dirs.Add(args[i]);
        }
        if (dirs.Count != 2 || !Directory.Exists(dirs[0]) || !Directory.Exists(dirs[1])) return Usage();

        var namesA = Names(dirs[0]);
        var namesB = Names(dirs[1]);
        bool ok = true;
        foreach (var name in namesA.Union(namesB).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!namesA.Contains(name) || !namesB.Contains(name))
            {
                Console.WriteLine($"{name}: only in {(namesA.Contains(name) ? dirs[0] : dirs[1])}");
                ok = false;
                continue;
            }
            try
            {
                var (diff, max, note, box) = Compare(Path.Combine(dirs[0], name + ".png"), Path.Combine(dirs[1], name + ".png"));
                if (note is not null) { Console.WriteLine($"{name}: {note}"); ok = false; }
                else if (diff == 0) Console.WriteLine($"{name}: identical");
                else
                {
                    Console.WriteLine($"{name}: {diff} pixels differ, max channel delta {max}" + (bbox ? $", within x {box.X0}-{box.X1}, y {box.Y0}-{box.Y1}" : ""));
                    if (max > tolerance) ok = false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{name}: cannot compare ({ex.Message})");
                ok = false;
            }
        }
        return ok ? 0 : 1;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: PngDiff <dirA> <dirB> [--tolerance D] [--bbox]");
        return 2;
    }

    private static SortedSet<string> Names(string dir) =>
        new(Directory.GetFiles(dir, "*.png").Select(f => Path.GetFileNameWithoutExtension(f)), StringComparer.OrdinalIgnoreCase);

    private static (int Pixels, int MaxDelta, string? Note, (int X0, int Y0, int X1, int Y1) Box) Compare(string a, string b)
    {
        var (pa, wa, ha) = Load(a);
        var (pb, wb, hb) = Load(b);
        if (wa != wb || ha != hb) return (0, 0, $"different sizes ({wa}x{ha} and {wb}x{hb})", default);
        int pixels = 0, max = 0, x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int p = 0; p < pa.Length; p += 4)
        {
            int worst = 0;
            for (int c = 0; c < 4; c++) worst = Math.Max(worst, Math.Abs(pa[p + c] - pb[p + c]));
            if (worst == 0) continue;
            pixels++;
            max = Math.Max(max, worst);
            int x = p / 4 % wa, y = p / 4 / wa;
            x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
        }
        return (pixels, max, null, (x0, y0, x1, y1));
    }

    private static (byte[] Bgra, int Width, int Height) Load(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var bytes = new byte[w * h * 4];
        bgra.CopyPixels(bytes, w * 4, 0);
        return (bytes, w, h);
    }
}
