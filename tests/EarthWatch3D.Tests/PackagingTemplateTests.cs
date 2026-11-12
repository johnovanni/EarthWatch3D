using System.IO;
using System.Windows.Media.Imaging;

namespace EarthWatch3D.Tests;

public class PackagingTemplateTests
{
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "EarthWatch3D.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("EarthWatch3D.sln not found above the test assembly.");
    }

    static (string File, int W, int H)[] ExpectedLogos()
    {
        var list = new List<(string, int, int)>
        {
            ("StoreLogo.scale-100.png", 50, 50), ("StoreLogo.scale-200.png", 100, 100),
            ("Square44x44Logo.scale-100.png", 44, 44), ("Square44x44Logo.scale-200.png", 88, 88),
            ("Square150x150Logo.scale-100.png", 150, 150), ("Square150x150Logo.scale-200.png", 300, 300),
            ("Wide310x150Logo.scale-100.png", 310, 150), ("Wide310x150Logo.scale-200.png", 620, 300),
        };
        foreach (var n in new[] { 16, 24, 32, 48, 256 })
        {
            list.Add(($"Square44x44Logo.targetsize-{n}.png", n, n));
            list.Add(($"Square44x44Logo.targetsize-{n}_altform-unplated.png", n, n));
        }
        return list.ToArray();
    }

    [Fact]
    public void EveryLogoExistsAtItsSize()
    {
        var dir = Path.Combine(RepoRoot(), "packaging", "Images");
        foreach (var (file, w, h) in ExpectedLogos())
        {
            var path = Path.Combine(dir, file);
            Assert.True(File.Exists(path), $"missing {file}");
            using var s = File.OpenRead(path);
            var frame = BitmapDecoder.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Assert.Equal((w, h), (frame.PixelWidth, frame.PixelHeight));
        }
    }

    [Fact]
    public void AppIconHasTheStandardSizes()
    {
        var path = Path.Combine(RepoRoot(), "src", "EarthWatch3D.App", "app.ico");
        Assert.True(File.Exists(path), "missing app.ico");
        using var s = File.OpenRead(path);
        var dec = new IconBitmapDecoder(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var sizes = dec.Frames.Select(f => f.PixelWidth).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { 16, 24, 32, 48, 64, 256 }, sizes);
    }
}
