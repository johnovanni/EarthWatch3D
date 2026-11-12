using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.App.Views;
using EarthWatch3D.Core.Lightning;
using HelixToolkit.Wpf;

namespace EarthWatch3D.Tests;

public class LightningLayerTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    [Theory]
    [InlineData(0, 0), InlineData(89, 0), InlineData(90, 1), InlineData(314, 1), InlineData(315, 2), InlineData(629, 2), InlineData(630, 3)]
    public void BucketsByAgeFraction(int ageSeconds, int bucket) =>
        Assert.Equal(bucket, LightningLayer.Bucket(TimeSpan.FromSeconds(ageSeconds), Window));

    [Fact]
    public void HiddenInReplayWithoutHistory()
    {
        Assert.False(LightningLayer.IsShown(true, live: false, supportsHistory: false));
        Assert.True(LightningLayer.IsShown(true, live: true, supportsHistory: false));
        Assert.True(LightningLayer.IsShown(true, live: false, supportsHistory: true));
        Assert.False(LightningLayer.IsShown(false, live: true, supportsHistory: true));
    }

    [Fact]
    public void LegendNoteDescribesEachSensor()
    {
        var c = new LightningCoverage(Now.AddSeconds(-25), null, null, MtgSetUp: false);
        var lines = LightningLayer.LegendNote(c, Now, mtgAvailable: true).Split('\n');
        Assert.Equal(["GOES-East: newest flash 25 s ago", "GOES-West: no flashes yet", "MTG: not set up (Settings)"], lines);
    }

    [Fact]
    public void LegendNoteSaysMtgIsComingUntilItsFeedExists()
    {
        // A saved key does not change that: there is no MTG feed yet.
        foreach (var setUp in new[] { false, true })
        {
            var c = new LightningCoverage(Now.AddSeconds(-25), null, null, MtgSetUp: setUp);
            var lines = LightningLayer.LegendNote(c, Now, mtgAvailable: false).Split('\n');
            Assert.Equal(["GOES-East: newest flash 25 s ago", "GOES-West: no flashes yet", "MTG (Europe, Africa): coming in a later version"], lines);
        }
    }

    [Fact]
    public void MtgIsNotPromisedWhileItsFeedDoesNotExist()
    {
        Assert.False(LightningLayer.MtgAvailable);   // flip this (one line in LightningLayer) when the MTG feed is built
        var c = new LightningCoverage(null, null, null, MtgSetUp: true);
        Assert.EndsWith("MTG (Europe, Africa): coming in a later version", LightningLayer.LegendNote(c, Now));
        Assert.Equal("Lightning (GOES GLM)", LightningLayer.LegendTitle);
        Assert.Equal("GOES GLM: NOAA.", LightningLayer.Attribution);
        Assert.Contains("The key is stored now and used when MTG lightning arrives in a later version.", LightningLayer.SettingsKeyNote);
        Assert.DoesNotContain("clear both", LightningLayer.SettingsKeyNote);
        Assert.Contains("clear the key to remove it", LightningLayer.SettingsKeyNote);
    }

    [Fact]
    public void LegendNoteFlagsDelayedMtg()
    {
        var c = new LightningCoverage(null, null, Now.AddMinutes(-12), MtgSetUp: true);
        Assert.Contains("MTG: newest flash 12 min ago (delayed)", LightningLayer.LegendNote(c, Now, mtgAvailable: true));
        var fresh = new LightningCoverage(null, null, Now.AddMinutes(-4), MtgSetUp: true);
        Assert.Contains("MTG: newest flash 4 min ago" + "\n", LightningLayer.LegendNote(fresh, Now, mtgAvailable: true) + "\n");
        Assert.DoesNotContain("delayed", LightningLayer.LegendNote(fresh, Now, mtgAvailable: true));
    }

    [Fact]
    public void UpdateDrawsOnlyFlashesInsideTheWindow()
    {
        Sta.Run(() =>
        {
            var buffer = new LightningBuffer();
            for (int i = 0; i < 10; i++) buffer.Add([new LightningFlash(Now.AddSeconds(-60 * i - 5), 10 + i, 20 + i, 0, LightningSensor.GoesEast)]);
            for (int i = 0; i < 5; i++) buffer.Add([new LightningFlash(Now.AddMinutes(-20 - i), -10 - i, 30 + i, 0, LightningSensor.GoesWest)]);
            var source = new LiveLightningSource(buffer, () => false);
            var viewport = new HelixViewport3D();
            var layer = new LightningLayer(viewport);

            layer.Update(source, Now, Window, show: true);
            Assert.Equal(10, layer.PointCount);
            var points = viewport.Children.OfType<PointsVisual3D>().ToList();
            Assert.Equal(4, points.Count);
            Assert.Equal(10, points.Sum(p => p.Points.Count));
            Assert.All(points.SelectMany(p => p.Points), p => Assert.Equal(1.004, new System.Windows.Media.Media3D.Vector3D(p.X, p.Y, p.Z).Length, 9));

            layer.Update(source, Now.AddSeconds(5), Window, show: false);   // show changed: at once
            Assert.Equal(0, layer.PointCount);
            Assert.Equal(0, points.Sum(p => p.Points.Count));
        });
    }

    [Fact]
    public void RebuildsAtMostOnceASecondUnlessShowChanges()
    {
        Sta.Run(() =>
        {
            var buffer = new LightningBuffer();
            buffer.Add([new LightningFlash(Now.AddSeconds(-5), 1, 1, 0, LightningSensor.GoesEast)]);
            var source = new LiveLightningSource(buffer, () => false);
            var layer = new LightningLayer(new HelixViewport3D());
            layer.Update(source, Now, Window, true);
            buffer.Add([new LightningFlash(Now.AddSeconds(-4), 2, 2, 0, LightningSensor.GoesEast)]);
            layer.Update(source, Now.AddMilliseconds(400), Window, true);
            Assert.Equal(1, layer.PointCount);
            layer.Update(source, Now.AddMilliseconds(1100), Window, true);
            Assert.Equal(2, layer.PointCount);
        });
    }

    [Fact]
    public void DrawsOnlyTheNewestFlashesAboveTheCap()
    {
        Sta.Run(() =>
        {
            const int total = 40_000;
            Assert.Equal(30_000, LightningLayer.MaxDrawnFlashes);
            // Flash i (0 = oldest) sits at its own latitude, so the drawn points say which flashes were drawn.
            static double Lat(int i) => -80 + i * 0.004;
            var buffer = new LightningBuffer();
            var batch = new List<LightningFlash>(total);
            for (int i = 0; i < total; i++)
                batch.Add(new LightningFlash(Now.AddMilliseconds(-12 * 60_000 + i * 15), Lat(i), 10, 0, LightningSensor.GoesEast));
            buffer.Add(batch);
            var source = new LiveLightningSource(buffer, () => false);
            var viewport = new HelixViewport3D();
            var layer = new LightningLayer(viewport);

            layer.Update(source, Now, Window, show: true);

            Assert.Equal(30_000, layer.PointCount);
            Assert.Equal(total, layer.FlashCountInWindow);
            var points = viewport.Children.OfType<PointsVisual3D>().SelectMany(p => p.Points).ToList();
            Assert.Equal(30_000, points.Count);
            // The oldest drawn flash is the 10,001st oldest (index 10,000): the lowest latitude drawn is its latitude.
            double minLat = points.Min(p => GeoLat(p));
            Assert.Equal(Lat(total - 30_000), minLat, 6);
            Assert.Equal(Lat(total - 1), points.Max(p => GeoLat(p)), 6);
            // Age colours still follow the window: the drawn flashes are 2 to 9.5 minutes old in a 15-minute window, so the
            // oldest drawn is in bucket 2 (under 70 %), not re-coloured as if it were the oldest of the window.
            var visuals = viewport.Children.OfType<PointsVisual3D>().ToList();
            Assert.Empty(visuals[3].Points);
            Assert.True(visuals[2].Points.Count > 0);
        });
    }

    [Fact]
    public void BelowTheCapEveryFlashIsDrawn()
    {
        Sta.Run(() =>
        {
            var buffer = new LightningBuffer();
            var batch = new List<LightningFlash>();
            for (int i = 0; i < 1_000; i++) batch.Add(new LightningFlash(Now.AddMilliseconds(-500 * i), i * 0.05, 10, 0, LightningSensor.GoesEast));
            buffer.Add(batch);
            var layer = new LightningLayer(new HelixViewport3D());
            layer.Update(new LiveLightningSource(buffer, () => false), Now, Window, show: true);
            Assert.Equal(1_000, layer.PointCount);
            Assert.Equal(1_000, layer.FlashCountInWindow);
        });
    }

    [Fact]
    public void LegendSaysSoOnlyWhenTheCapApplies()
    {
        var c = new LightningCoverage(Now.AddSeconds(-25), null, null, MtgSetUp: false);
        var capped = LightningLayer.LegendNote(c, Now, flashesInWindow: 84_312).Split('\n');
        Assert.Equal("Showing the newest 30,000 of 84,312 flashes.", capped[^1]);
        Assert.Equal(4, capped.Length);
        foreach (int n in new[] { 0, 1_000, 30_000 })
            Assert.DoesNotContain("Showing", LightningLayer.LegendNote(c, Now, flashesInWindow: n));
        Assert.Equal(3, LightningLayer.LegendNote(c, Now, flashesInWindow: 30_000).Split('\n').Length);
    }

    private static double GeoLat(System.Windows.Media.Media3D.Point3D p) =>
        AstroCore.Earth.GeoPosition.FromVector(new AstroCore.Astronomy.Vector3d(p.X, p.Y, p.Z)).LatitudeDeg;

    [Fact]
    public void AgeColoursAreTheSpecifiedOnes()
    {
        Assert.Equal([Color.FromRgb(0xFF, 0xF0, 0xB0), Color.FromRgb(0xFF, 0xD8, 0x4A), Color.FromRgb(0xFF, 0x9A, 0x2E), Color.FromRgb(0xB4, 0x60, 0x2A)],
            LightningLayer.BucketColors);
        Assert.Equal([4, 3.5, 3, 2.5], LightningLayer.BucketSizes);
    }

    [Fact]
    public void LegendTextIsLightOnTheDarkTooltip()
    {
        Sta.Run(() =>
        {
            var vm = new EarthWatch3D.App.ViewModels.MainViewModel(new TestClock(Now), new FakeSettingsStore());
            vm.Layers.LightningLegend = "GOES-East: newest flash 25 s ago";
            var strip = new LayerStrip { DataContext = vm };
            strip.Measure(new Size(800, 600)); strip.Arrange(new Rect(0, 0, 800, 600)); strip.UpdateLayout();
            var button = (ToggleButton)strip.FindName("LightningButton");
            var tip = Assert.IsType<StackPanel>(button.ToolTip);
            // A shown tooltip finds the strip's styles through its owner; reproduce that by hosting it under the strip's resources.
            var host = new Border { Child = tip };
            host.Resources.MergedDictionaries.Add(strip.Resources);
            host.Measure(new Size(400, 400)); host.Arrange(new Rect(0, 0, 400, 400)); host.UpdateLayout();
            var texts = new List<TextBlock>();
            void Walk(DependencyObject d)
            {
                if (d is TextBlock t) texts.Add(t);
                foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(child);
            }
            Walk(tip);
            // The per-sensor note is bound to the view model (a tooltip only connects its bindings once it is shown).
            Assert.Contains(texts, t => System.Windows.Data.BindingOperations.GetBinding(t, TextBlock.TextProperty)?.Path.Path == "Layers.LightningLegend");
            Assert.Contains(texts, t => t.Text == LightningLayer.Attribution);
            Assert.Contains(texts, t => t.Text == LightningLayer.LegendTitle);
            Assert.DoesNotContain(texts, t => t.Text.Contains("EUMETSAT", StringComparison.Ordinal) || t.Text.Contains("MTG LI", StringComparison.Ordinal));
            foreach (var t in texts)
            {
                var c = Assert.IsType<SolidColorBrush>(t.Foreground).Color;
                Assert.True(c.R >= 0x90 && c.G >= 0x90 && c.B >= 0x90, $"'{t.Text}' is too dark: {c}");
            }
        });
    }
}
