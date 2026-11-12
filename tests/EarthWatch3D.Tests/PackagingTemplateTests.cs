using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

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

    static readonly XNamespace Foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    static readonly XNamespace Rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";
    static readonly XNamespace AppInstaller2018 = "http://schemas.microsoft.com/appx/appinstaller/2018";

    static XDocument Manifest() => XDocument.Load(Path.Combine(RepoRoot(), "packaging", "AppxManifest.xml"));
    static XDocument AppInstallerTemplate() => XDocument.Load(Path.Combine(RepoRoot(), "packaging", "EarthWatch3D.appinstaller"));

    [Fact]
    public void ManifestHasTheIdentityAndRequirements()
    {
        var root = Manifest().Root!;
        Assert.Equal(Foundation + "Package", root.Name);

        var id = root.Element(Foundation + "Identity")!;
        Assert.Equal("johnovanni.EarthWatch3D", (string?)id.Attribute("Name"));
        Assert.Equal("{Version}", (string?)id.Attribute("Version"));
        Assert.Equal("{Publisher}", (string?)id.Attribute("Publisher"));
        Assert.Equal("x64", (string?)id.Attribute("ProcessorArchitecture"));

        var props = root.Element(Foundation + "Properties")!;
        Assert.Equal("EarthWatch3D", (string?)props.Element(Foundation + "DisplayName"));
        Assert.Equal("{PublisherDisplayName}", (string?)props.Element(Foundation + "PublisherDisplayName"));

        var family = root.Element(Foundation + "Dependencies")!.Element(Foundation + "TargetDeviceFamily")!;
        Assert.Equal("Windows.Desktop", (string?)family.Attribute("Name"));
        Assert.Equal("10.0.17763.0", (string?)family.Attribute("MinVersion"));
        Assert.Equal("10.0.26100.0", (string?)family.Attribute("MaxVersionTested"));

        var app = Assert.Single(root.Element(Foundation + "Applications")!.Elements(Foundation + "Application"));
        Assert.Equal("EarthWatch3D", (string?)app.Attribute("Id"));
        Assert.Equal("EarthWatch3D.App.exe", (string?)app.Attribute("Executable"));
        Assert.Equal("Windows.FullTrustApplication", (string?)app.Attribute("EntryPoint"));

        var caps = root.Element(Foundation + "Capabilities")!;
        Assert.Contains(caps.Elements(Foundation + "Capability"), c => (string?)c.Attribute("Name") == "internetClient");
        Assert.Contains(caps.Elements(Rescap + "Capability"), c => (string?)c.Attribute("Name") == "runFullTrust");
    }

    [Fact]
    public void ManifestLogosExist()
    {
        var root = Manifest().Root!;
        // Every *Logo attribute (uap:VisualElements, uap:DefaultTile) plus the Properties/Logo element.
        var logos = root.Descendants().Attributes()
            .Where(a => a.Name.LocalName.EndsWith("Logo", StringComparison.Ordinal))
            .Select(a => a.Value)
            .Concat(root.Descendants(Foundation + "Logo").Select(e => e.Value))
            .ToList();
        Assert.True(logos.Count >= 4, $"expected at least 4 logos, found {logos.Count}");

        var packaging = Path.Combine(RepoRoot(), "packaging");
        foreach (var logo in logos)
        {
            // The manifest names "Images\X.png"; resources.pri resolves it to X.scale-100.png and the other qualifiers.
            var rel = logo.Replace('\\', Path.DirectorySeparatorChar);
            Assert.StartsWith("Images" + Path.DirectorySeparatorChar, rel);
            var scaled = Path.Combine(packaging, Path.ChangeExtension(rel, null) + ".scale-100.png");
            Assert.True(File.Exists(scaled), $"{logo}: missing {scaled}");
        }
    }

    [Fact]
    public void ExecutableMatchesTheAssemblyName()
    {
        var csprojPath = Path.Combine(RepoRoot(), "src", "EarthWatch3D.App", "EarthWatch3D.App.csproj");
        var assemblyName = XDocument.Load(csprojPath).Descendants("AssemblyName").Select(e => e.Value.Trim()).LastOrDefault()
                           ?? Path.GetFileNameWithoutExtension(csprojPath);

        var app = Manifest().Root!.Descendants(Foundation + "Application").Single();
        Assert.Equal(assemblyName + ".exe", (string?)app.Attribute("Executable"));
    }

    [Fact]
    public void AppInstallerUsesStableNamesAndUpdateSettings()
    {
        var root = AppInstallerTemplate().Root!;
        Assert.Equal(AppInstaller2018 + "AppInstaller", root.Name);
        Assert.Equal("{AppInstallerUri}", (string?)root.Attribute("Uri"));
        Assert.Equal("{Version}", (string?)root.Attribute("Version"));

        var identityName = (string?)Manifest().Root!.Element(Foundation + "Identity")!.Attribute("Name");
        var main = root.Element(AppInstaller2018 + "MainPackage")!;
        Assert.Equal(identityName, (string?)main.Attribute("Name"));
        Assert.Equal("{Publisher}", (string?)main.Attribute("Publisher"));
        Assert.Equal("{Version}", (string?)main.Attribute("Version"));
        Assert.Equal("x64", (string?)main.Attribute("ProcessorArchitecture"));
        Assert.Equal("{MsixUri}", (string?)main.Attribute("Uri"));

        var update = root.Element(AppInstaller2018 + "UpdateSettings")!;
        var onLaunch = update.Element(AppInstaller2018 + "OnLaunch")!;
        Assert.Equal("24", (string?)onLaunch.Attribute("HoursBetweenUpdateChecks"));
        Assert.Equal("true", (string?)onLaunch.Attribute("ShowPrompt"));
        Assert.Equal("false", (string?)onLaunch.Attribute("UpdateBlocksActivation"));
        Assert.NotNull(update.Element(AppInstaller2018 + "AutomaticBackgroundTask"));
    }

    [Theory]
    [InlineData("packaging/metadata.json")]
    [InlineData("packaging/signing.local.json")]
    [InlineData("artifacts/release/EarthWatch3D.msix")]
    [InlineData("artifacts/release/EarthWatch3D-test.cer")]
    [InlineData("packaging/some-key.pfx")]
    public void SigningFilesAreIgnored(string path)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepoRoot(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "check-ignore", "--no-index", "-q", path })
            psi.ArgumentList.Add(arg);
        using var git = Process.Start(psi)!;
        var err = git.StandardError.ReadToEnd();
        git.WaitForExit();
        Assert.True(git.ExitCode == 0, $"{path} is not git-ignored (git check-ignore exit {git.ExitCode}) {err}");
    }

    [Fact]
    public void ToolsProjectPinsExactVersionsAndIsNotInTheSolution()
    {
        var repo = RepoRoot();
        var refs = XDocument.Load(Path.Combine(repo, "packaging", "tools.proj")).Descendants("PackageReference")
            .ToDictionary(e => (string)e.Attribute("Include")!, e => (string?)e.Attribute("Version"));
        Assert.True(refs.ContainsKey("Microsoft.Windows.SDK.BuildTools"), "Microsoft.Windows.SDK.BuildTools not referenced");
        Assert.True(refs.ContainsKey("Microsoft.ArtifactSigning.Client"), "Microsoft.ArtifactSigning.Client not referenced");
        foreach (var (name, version) in refs)
            Assert.Matches(@"^\d+(\.\d+){2,3}$", version ?? $"{name}: no version"); // exact: no ranges, no wildcards

        Assert.DoesNotContain("tools.proj", File.ReadAllText(Path.Combine(repo, "EarthWatch3D.sln")));
        Assert.True(File.Exists(Path.Combine(repo, "packaging", "priconfig.xml")), "packaging/priconfig.xml is missing");
    }
}
