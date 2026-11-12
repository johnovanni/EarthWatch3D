using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AstroCore.Earth;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.App.Views;
using EarthWatch3D.Core.Events;

namespace EarthWatch3D.Tests;

/// <summary>Every piece of text shown on the dark cards must be light, so it reads against the dark background.</summary>
public class CardReadabilityTests
{
    private static readonly DateTime T = new(2026, 11, 12, 6, 0, 0, DateTimeKind.Utc);

    public static TheoryData<string> Kinds => ["storm", "quake", "alert", "eonet"];

    private static EarthEvent Make(string kind) => kind switch
    {
        "storm" => new CycloneAdvisory(EventSource.Nhc, "nhc:1", T, new GeoPosition(15, -50), "Hurricane Pena", new Uri("https://example.org/s"),
            "PENA", 90, 970, [], [], [], []),
        "quake" => new Earthquake("q1", T, new GeoPosition(10, 20), "M 6.1 - Somewhere", new Uri("https://example.org/q"), 6.1, 35,
            "10 km N of Somewhere", T),
        "alert" => new WeatherAlert("a1", T, new GeoPosition(0, 0), "Tornado Warning issued for Foo County", null, "Tornado Warning",
            "Extreme", T, T.AddHours(3), null, [], [], "Take cover."),
        _ => new EonetStorm("e1", T, new GeoPosition(5, 5), "Typhoon Foo", new Uri("https://example.org/e"), [new StormFix(T, new GeoPosition(5, 5), 80)], null),
    };

    [Theory, MemberData(nameof(Kinds))]
    public void AllDetailTextIsLight(string kind)
    {
        Sta.Run(() =>
        {
            var vm = new SelectionViewModel(() => T.AddHours(1));
            vm.Select(Make(kind));
            var panel = new DetailsPanel { DataContext = vm };
            panel.Measure(new Size(400, 800));
            panel.Arrange(new Rect(0, 0, 400, 800));
            panel.UpdateLayout();

            AssertAllLight(panel, 4);
        });
    }

    [Theory, InlineData(false), InlineData(true)]
    public void AllSunMoonCardTextIsLight(bool collapsed)
    {
        Sta.Run(() =>
        {
            var vm = new SunMoonViewModel { IsCollapsed = collapsed };
            vm.Refresh(T);
            var card = new SunMoonCard { DataContext = vm };
            card.Measure(new Size(400, 800));
            card.Arrange(new Rect(0, 0, 400, 800));
            card.UpdateLayout();
            AssertAllLight(card, collapsed ? 2 : 8);
        });
    }

    [Fact]
    public void AllSettingsWindowTextIsLight()
    {
        Sta.Run(() =>
        {
            var store = new FakeSettingsStore(new EarthWatch3D.Core.Storage.AppSettings { EumetsatKeyProtected = "P:k", EumetsatSecretProtected = "P:s" });
            var creds = new EarthWatch3D.Core.Lightning.Mtg.EumetsatCredentialStore(store, new FakeProtector());
            var vm = new SettingsViewModel(store, creds, (_, _) => throw new InvalidOperationException(), _ => { });
            vm.Error = "Daily limit: a whole number of MB from 10 to 100,000, or empty for no limit.";
            vm.TestStatus = "Key accepted.";
            var window = new SettingsWindow(vm);
            // A window that was never shown has no visual tree of its own; its content is measured instead.
            var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
            content.Measure(new Size(440, 900));
            content.Arrange(new Rect(0, 0, 440, 900));
            content.UpdateLayout();
            AssertAllLight(content, 10);
            // Until the MTG feed exists the key section says the key is kept for it, and how to remove the key.
            var texts = Descendants(content).OfType<TextBlock>().Select(TextOf).ToList();
            Assert.Contains(EarthWatch3D.App.Rendering.LightningLayer.SettingsKeyNote, texts);
            Assert.Contains(EarthWatch3D.App.Rendering.LightningLayer.SettingsKeyLabel, texts);
            Assert.DoesNotContain(texts, t => t.Contains("clear both", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void AllAboutWindowTextIsLight()
    {
        Sta.Run(() =>
        {
            var vm = new AboutViewModel(new RecordingLinkOpener(), @"C:\Users\x\AppData\Local\EarthWatch3D", mtgAvailable: true);
            var window = new AboutWindow(vm);
            var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
            content.Measure(new Size(480, 3000));
            content.Arrange(new Rect(0, 0, 480, 3000));
            content.UpdateLayout();
            AssertAllLight(content, 20);
            var texts = Descendants(content).OfType<TextBlock>().Select(TextOf).ToList();
            Assert.Contains("Live earthquakes, storms and lightning on a 3D globe.", texts);
            Assert.Contains("Shown for interest, not for safety decisions: follow your local weather service and authorities.", texts);
        });
    }

    [Fact]
    public void HoverTipUsesTheDarkCardStyle()
    {
        Sta.Run(() =>
        {
            var tip = new ToolTip { Style = EarthWatch3D.App.Controllers.SceneController.DarkToolTipStyle(), Content = "Hurricane Pena" };
            Assert.Equal(Colors.White, ((SolidColorBrush)tip.Foreground).Color);
            Assert.True(((SolidColorBrush)tip.Background).Color.R < 0x40, "background should be dark");
        });
    }

    private static void AssertAllLight(FrameworkElement root, int atLeast)
    {
        // Only text that is shown: a collapsed parent hides its children.
        var texts = Descendants(root).OfType<TextBlock>()
            .Where(t => TextOf(t).Length > 0 && Shown(t, root)).ToList();
        Assert.True(texts.Count >= atLeast, $"only {texts.Count} texts found");
        foreach (var t in texts)
        {
            var c = Assert.IsType<SolidColorBrush>(t.Foreground).Color;
            var luminance = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
            Assert.True(luminance > 0.5, $"'{TextOf(t)}' is drawn in {c}, too dark for the dark card");
        }
    }

    private static bool Shown(DependencyObject d, DependencyObject root)
    {
        for (; d is not null && d != root; d = VisualTreeHelper.GetParent(d))
            if (d is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }

    private static string TextOf(TextBlock t) =>
        t.Text.Length > 0 ? t.Text : string.Concat(t.Inlines.OfType<Run>().Select(r => r.Text));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }
}
