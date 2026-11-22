using System.IO;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using EarthWatch3D.App.Diagnostics;
using EarthWatch3D.App.Services;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.App.Views;
using EarthWatch3D.Core;
using EarthWatch3D.Core.Storage;
using GlobeCore.Earth;
using GlobeCore.Feeds;
using GlobeCore.Geo;
using GlobeCore.Regions;
using GlobeCore.Storage;

namespace EarthWatch3D.Tests;

/// <summary>
/// The political boundaries (Plan 2b): the app's rules and probes in data/, the bundled set they make, the "Borders" update
/// feed, and the layer the user sees as "Boundaries" (EW-002).
/// </summary>
public class BoundariesTests
{
    private static readonly DateTime T0 = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>What the layer's tooltip says for the bundled set with the app's rules (ruling B-6).</summary>
    public const string BundledLegend =
        "Borders: Natural Earth 5.1.2 (2022-05-13); Crimea shown as part of Ukraine (UN General Assembly resolution 68/262)";

    // Parsed once for the whole class: the countries file is 3 MB.
    private static readonly Lazy<BoundaryData> Bundled = new(() => BoundaryData.Load(DataDir, EmptyCacheDir(), new RecordingLog()));

    private static string EmptyCacheDir() => Path.Combine(Path.GetTempPath(), "ew3d-boundaries-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void BoundariesLayerLabelAndAlias()
    {
        // --layers and the scenes file take "boundaries" as another name for "coastlines" (the saved key and the old
        // name keep working).
        Assert.Equal(["coastlines", "plates"], LaunchOptions.Parse(["--layers", "Boundaries,plates"]).Layers);
        Assert.Equal(["coastlines"], LaunchOptions.Parse(["--layers", "coastlines"]).Layers);
        Assert.Equal("coastlines", LaunchOptions.CanonicalLayer("boundaries"));
        var file = Path.Combine(Path.GetTempPath(), "ew3d-scenes-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, """{ "scenes": [ { "name": "a", "displayTimeUtc": "2026-11-11T20:00:00Z", "layers": ["boundaries"] } ] }""");
        try { Assert.Equal(["coastlines"], SnapshotDiagnostic.LoadScenes(file)[0].Layers); }
        finally { File.Delete(file); }

        // The button and its legend read "Boundaries"; the legend's note names the edition and the rules.
        Sta.Run(() =>
        {
            var vm = new MainViewModel(new TestClock(T0), new FakeSettingsStore());
            var strip = new LayerStrip { DataContext = vm };
            strip.Measure(new System.Windows.Size(800, 600));
            strip.Arrange(new System.Windows.Rect(0, 0, 800, 600));
            strip.UpdateLayout();
            var button = (ToggleButton)strip.FindName("BoundariesButton");
            Assert.Equal("Boundaries", button.Content);
            Assert.Equal("Layers.ShowCoastlines", BindingOperations.GetBinding(button, ToggleButton.IsCheckedProperty).Path.Path);
            var legend = (StackPanel)button.ToolTip;
            var texts = legend.Children.OfType<TextBlock>().ToList();
            Assert.Equal("Boundaries", texts[0].Text);
            Assert.Contains(texts, t => BindingOperations.GetBinding(t, TextBlock.TextProperty)?.Path.Path == "Layers.BoundariesLegend");
            Assert.DoesNotContain(texts, t => t.Text.Contains("Coastlines and borders", StringComparison.Ordinal));
        });
        Assert.Equal(BundledLegend, LayersViewModel.BoundariesLegendFor(Bundled.Value.Current.Set));
    }

    [Fact]
    public void RulesFileHoldsTheCrimeaRuleWithItsReason()
    {
        using var s = File.OpenRead(Path.Combine(DataDir, BoundaryData.RulesFile));
        var rules = BoundaryRules.Parse(s);
        var rule = Assert.Single(rules.Rules);
        Assert.Equal(("moveAdmin1", "moveAdmin1 UA-43"), (rule.Kind, rule.Name));
        Assert.Contains("68/262", rule.Reason, StringComparison.Ordinal);
        Assert.Equal(["Crimea shown as part of Ukraine (UN General Assembly resolution 68/262)"], rules.Descriptions);
    }

    [Fact]
    public void BundledSetWithTheAppsRulesPassesTheProbes()
    {
        var data = Bundled.Value;
        var set = data.Current.Set;
        Assert.Equal(("v5.1.2", "Natural Earth 5.1.2 (2022-05-13)"), (set.Tag, set.Edition));
        Assert.True(data.Validator.Validate(set).IsValid, data.Validator.Validate(set).Reason);
        using var s = File.OpenRead(Path.Combine(DataDir, BoundaryData.ProbesFile));
        var probes = BoundaryValidator.ReadProbes(s);
        Assert.Equal(["SDN", "UKR", "UKR", "UKR", "FJI", "FJI"], probes.Select(p => p.ExpectedId));
        Assert.Contains(probes, p => p.Point.LatitudeDeg == 45.0 && p.Point.LongitudeDeg == 34.1);   // Crimea
        Assert.Contains(probes, p => p.Point.LatitudeDeg == 44.6 && p.Point.LongitudeDeg == 33.5 && p.ExpectedId == "UKR");   // Sevastopol, as in EarthRelief3D
        Assert.Contains(probes, p => p.ExpectedId == "FJI" && p.Point.LongitudeDeg > 0);
        Assert.Contains(probes, p => p.ExpectedId == "FJI" && p.Point.LongitudeDeg < 0);
        Assert.Equal("UKR", set.Regions.Find(new(44.95, 34.10))?.Id);   // Simferopol
        Assert.Equal("RUS", set.Regions.Find(new(55.75, 37.62))?.Id);   // Moscow
    }

    [Fact]
    public void BordersLeaveOutThePerekopLineAndKeepTheKerchStraitLine()
    {
        // Natural Earth's own lines, as bundled, against the lines the app's set draws (the Crimea rule, B-7).
        List<IReadOnlyList<AstroCore.Earth.GeoPosition>> raw;
        using (var s = EarthAssets.Open(EarthAssets.LandBoundariesFile)) raw = [.. GeoJsonLines.Read(s)];
        var now = NaturalEarthLines.LandBoundaries(Bundled.Value.Current.Set);

        // One line fewer: the Perekop isthmus line, between Crimea and the rest of Ukraine (45.73-46.22° N, 33.59-35.00° E),
        // which lies wholly in a box around Crimea. The box stops at 36.5° E: the Kerch Strait line (36.51-36.69° E) runs
        // along Crimea's eastern edge and must stay.
        static bool InCrimea(IReadOnlyList<AstroCore.Earth.GeoPosition> line) =>
            line.All(p => p.LatitudeDeg is >= 44.0 and <= 46.3 && p.LongitudeDeg is >= 32.4 and <= 36.5);
        Assert.Equal(raw.Count - 1, now.Count);
        Assert.Single(raw, InCrimea);
        Assert.DoesNotContain(now, InCrimea);

        // The Kerch Strait line (Ukraine-Russia, near 45.3° N 36.5° E) stays.
        Assert.Contains(now, l => l.Any(p => p.LatitudeDeg is > 44.9 and < 45.7 && p.LongitudeDeg is > 36.4 and < 36.8));
    }

    [Fact]
    public void TheRejectionFingerprintIsOfTheShippedRulesAndProbes()
    {
        var expected = BoundaryUpdateFeed.Fingerprint(File.ReadAllBytes(Path.Combine(DataDir, BoundaryData.RulesFile)),
            File.ReadAllBytes(Path.Combine(DataDir, BoundaryData.ProbesFile)));
        Assert.Equal(expected, Bundled.Value.Fingerprint);
    }

    [Fact]
    public void AStoredNewerSetIsLoadedBeforeTheBundledOne()
    {
        var cache = EmptyCacheDir();
        try
        {
            // The bundled files stored as a fictional newer release v5.1.3: whole and valid, so it is used.
            var store = new BoundaryStore(cache);
            var files = NaturalEarthBoundaries.ReleaseFiles.ToDictionary(f => f, f =>
            {
                using var s = EarthAssets.Open(f == NaturalEarthBoundaries.Admin1File ? EarthAssets.CrimeaRegionsFile : f);
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            });
            Assert.True(store.Save("v5.1.3", new DateOnly(2026, 1, 2), files));
            var log = new RecordingLog();
            var data = BoundaryData.Load(DataDir, cache, log);
            Assert.Equal("Natural Earth 5.1.3 (2026-01-02)", data.Current.Set.Edition);
            Assert.Equal("UKR", data.Current.Set.Regions.Find(new(45.0, 34.1))?.Id);   // the rules apply to stored sets too
            Assert.Contains(log.Infos, m => m.Contains("5.1.3", StringComparison.Ordinal));
        }
        finally { try { Directory.Delete(cache, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task BordersFeedFromTheSamplesReportsNoNewerRelease()
    {
        var handler = OfflineSamples.CreateHandler(Path.Combine(AppContext.BaseDirectory, "Samples"));
        using var http = new FeedHttp(AppInfo.UserAgent, handler);
        var paths = new AppPaths(new AppDataRoot(Path.Combine(Path.GetTempPath(), "ew3d-borders-" + Guid.NewGuid().ToString("N"))));
        try
        {
            paths.EnsureCreated();
            var log = new RecordingLog();
            var meter = new DownloadMeter(new TestClock(T0), TimeZoneInfo.Utc, paths.DownloadMeterFile, log);
            var data = BoundaryData.Load(DataDir, paths.BoundariesDir, log);
            var feed = data.CreateFeed(http, meter, log);
            Assert.Equal(BoundaryData.FeedName, feed.Name);
            Assert.Equal(FeedRunResult.Ok, await feed.RunOnceAsync(CancellationToken.None));
            Assert.Equal("Using Natural Earth 5.1.2 (2022-05-13); no newer release", feed.StatusNote);
            // Only the release description was asked for (from the sample, never the network); nothing was downloaded.
            Assert.Equal([NaturalEarthBoundarySource.LatestReleaseUrl.ToString()], handler.Requests);
            Assert.Empty(log.Errors);
            // GlobeCore's feed deletes a remembered rejection after each "no newer release" answer, which logs a warning
            // when the folder is missing; the app makes the folder with the others.
            Assert.Empty(log.Warnings);

            // The daily download limit pauses it, as it does lightning.
            meter.LimitBytes = 1;
            meter.Add(10);
            Assert.Equal(FeedRunResult.Paused, await feed.RunOnceAsync(CancellationToken.None));
        }
        finally { try { Directory.Delete(paths.Root, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task DeferredFeedWaitsForItsFeedAndPassesItsNoteOn()
    {
        var made = new TaskCompletionSource<IFeed>();
        var feed = new DeferredFeed("Borders", TimeSpan.FromHours(24), made.Task);
        Assert.Equal(("Borders", TimeSpan.FromHours(24)), (feed.Name, feed.Interval));
        Assert.Null(feed.StatusNote);
        var run = feed.RunOnceAsync(CancellationToken.None);
        Assert.False(run.IsCompleted);
        made.SetResult(new NotedFeed());
        Assert.Equal(FeedRunResult.Attention, await run);
        Assert.Equal("a note", feed.StatusNote);

        // A feed that could not be made fails each run with the reason, so its dot shows it.
        var broken = new DeferredFeed("Borders", TimeSpan.FromHours(24), Task.FromException<IFeed>(new InvalidDataException("no rules")));
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => broken.RunOnceAsync(CancellationToken.None));
        Assert.Equal("no rules", ex.Message);
    }

    private sealed class NotedFeed : IFeed, IFeedStatusNote
    {
        public string Name => "Borders";
        public TimeSpan Interval => TimeSpan.FromHours(24);
        public string? StatusNote => "a note";
        public Task<FeedRunResult> RunOnceAsync(CancellationToken ct) => Task.FromResult(FeedRunResult.Attention);
    }
}
