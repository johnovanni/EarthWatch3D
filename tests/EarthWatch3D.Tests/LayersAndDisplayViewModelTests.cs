using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.App.Views;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.Tests;

public class LayersAndDisplayViewModelTests
{
    private static readonly DateTime T0 = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TogglingLayerSavesSettings()
    {
        var store = new FakeSettingsStore(new AppSettings { MinMagnitude = 4 });
        var layers = new LayersViewModel(store);
        Assert.True(layers.ShowQuakes);
        Assert.False(layers.IsExpanded);

        layers.ShowQuakes = false;
        Assert.Equal(1, store.SaveCount);
        Assert.False(store.Saved[^1].ShowQuakes);
        Assert.Equal(4, store.Saved[^1].MinMagnitude);   // other settings are kept

        layers.ShowCoastlines = false;
        layers.IsExpanded = true;
        layers.ShowEonet = false;
        var last = store.Saved[^1];
        Assert.Equal(4, store.SaveCount);
        Assert.False(last.ShowQuakes);
        Assert.False(last.ShowCoastlines);
        Assert.True(last.LayerStripExpanded);
        Assert.False(last.ShowEonet);
        Assert.True(last.ShowCyclones);

        layers.ShowEonet = false;   // no change, no save
        Assert.Equal(4, store.SaveCount);
    }

    [Fact]
    public void LayersAndDisplayStartFromSavedSettings()
    {
        var store = new FakeSettingsStore(new AppSettings
        {
            ShowAlerts = false, LayerStripExpanded = true, MinMagnitude = double.NaN, QuakeWindowDays = 7,
            AlertTypes = AlertTypes.Watches | AlertTypes.Statements, NightStrength = 0.4, ShowTerminator = false,
        });
        var layers = new LayersViewModel(store);
        var display = new DisplayOptionsViewModel(store);
        Assert.False(layers.ShowAlerts);
        Assert.True(layers.IsExpanded);
        Assert.True(double.IsNaN(display.MinMagnitude));
        Assert.Equal(7, display.QuakeWindowDays);
        Assert.False(display.ShowWarnings);
        Assert.True(display.ShowWatches);
        Assert.False(display.ShowAdvisories);
        Assert.True(display.ShowStatements);
        Assert.Equal(0.4, display.NightStrength);
        Assert.False(display.ShowTerminator);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void DisplayOptionsSaveAndClamp()
    {
        var store = new FakeSettingsStore();
        var display = new DisplayOptionsViewModel(store);
        Assert.Equal(["All", "1", "2.5", "4", "5", "6"], display.MagnitudeChoices.Select(c => c.Label));
        Assert.True(double.IsNaN(display.MagnitudeChoices[0].Value));
        Assert.Equal([1, 2.5, 4, 5, 6], display.MagnitudeChoices.Skip(1).Select(c => c.Value));
        Assert.Equal(2, display.MagnitudeIndex);   // default M2.5

        display.ShowAdvisories = true;
        Assert.Equal(AlertTypes.Warnings | AlertTypes.Advisories, store.Saved[^1].AlertTypes);
        display.NightStrength = 1.7;
        Assert.Equal(1.0, display.NightStrength);
        Assert.Equal(1.0, store.Saved[^1].NightStrength);
        display.ShowTerminator = false;
        Assert.False(store.Saved[^1].ShowTerminator);
        display.QuakeWindowDays = 3;   // only 1 or 7
        Assert.Equal(1, display.QuakeWindowDays);
        display.MagnitudeIndex = 0;
        Assert.True(double.IsNaN(display.MinMagnitude));
        Assert.True(double.IsNaN(store.Saved[^1].MinMagnitude));
        display.MinMagnitude = 5;
        Assert.Equal(4, display.MagnitudeIndex);
    }

    [Fact]
    public void FilterReflectsDisplayOptions()
    {
        var vm = new MainViewModel(new TestClock(T0), new FakeSettingsStore());
        Assert.Equal(DisplayFilter.Default, vm.CurrentFilter);

        vm.Display.MinMagnitude = double.NaN;   // "All"
        Assert.Equal(double.NegativeInfinity, vm.CurrentFilter.MinMagnitude);
        vm.Display.ShowWatches = true;
        Assert.Equal(AlertTypes.Warnings | AlertTypes.Watches, vm.CurrentFilter.AlertTypes);
        vm.Display.QuakeWindowDays = 7;
        Assert.Equal(TimeSpan.FromDays(7), vm.CurrentFilter.QuakeWindow);
        vm.Layers.ShowCyclones = false;
        Assert.False(vm.CurrentFilter.ShowCyclones);
        Assert.True(vm.CurrentFilter.ShowQuakes);
    }

    [Fact]
    public void FilterChangedRaised()
    {
        var vm = new MainViewModel(new TestClock(T0), new FakeSettingsStore());
        int raised = 0;
        vm.FilterChanged += (_, _) => raised++;

        vm.Layers.ShowQuakes = false;
        Assert.Equal(1, raised);
        vm.Display.MinMagnitude = 6;
        Assert.Equal(2, raised);
        vm.Display.ShowStatements = true;
        Assert.Equal(3, raised);
        vm.Display.QuakeWindowDays = 7;
        Assert.Equal(4, raised);

        // Settings that don't change which events are visible don't raise it.
        vm.Display.NightStrength = 0.3;
        vm.Display.ShowTerminator = false;
        vm.Layers.ShowCoastlines = false;
        vm.Layers.IsExpanded = true;
        Assert.Equal(4, raised);
    }

    [Fact]
    public void LayerStripFlyoutAndStatusDotsBind()
    {
        Sta.Run(() =>
        {
            var vm = new MainViewModel(new TestClock(T0), new FakeSettingsStore());
            var strip = new LayerStrip { DataContext = vm };
            Layout(strip);
            var quakes = (ToggleButton)strip.FindName("QuakesButton");
            Assert.True(quakes.IsChecked);
            quakes.IsChecked = false;
            Assert.False(vm.Layers.ShowQuakes);

            var flyout = new DisplayFlyout { DataContext = vm.Display };
            Layout(flyout);
            var magnitude = (ListBox)flyout.FindName("MagnitudeChips");
            Assert.Equal(6, magnitude.Items.Count);
            Assert.Equal(2, magnitude.SelectedIndex);   // M2.5
            magnitude.SelectedIndex = 0;
            Assert.True(double.IsNaN(vm.Display.MinMagnitude));
            var window = (ListBox)flyout.FindName("WindowChips");
            Assert.Equal(0, window.SelectedIndex);
            window.SelectedIndex = 1;
            Assert.Equal(7, vm.Display.QuakeWindowDays);
            ((Slider)flyout.FindName("NightSlider")).Value = 0.25;
            Assert.Equal(0.25, vm.Display.NightStrength);

            vm.Status.Apply(new FeedStatus("USGS", FeedState.Ok, T0, null, 0, T0));
            vm.Status.Apply(new FeedStatus("NWS", FeedState.Failing, null, "x", 3, T0));
            var dots = new StatusStrip { DataContext = vm.Status };
            Layout(dots);
            Assert.Equal(2, ((ItemsControl)dots.FindName("Dots")).Items.Count);
            Assert.Equal(Color.FromRgb(0x34, 0xC7, 0x59), FeedStateBrushConverter.For(FeedState.Ok).Color);
            Assert.Equal(Color.FromRgb(0xFF, 0xCC, 0x00), FeedStateBrushConverter.For(FeedState.Retrying).Color);
            Assert.Equal(Color.FromRgb(0xFF, 0x3B, 0x30), FeedStateBrushConverter.For(FeedState.Failing).Color);
            Assert.Equal(Color.FromRgb(0x8E, 0x8E, 0x93), FeedStateBrushConverter.For(FeedState.NotSetUp).Color);
            Assert.Equal(Color.FromRgb(0x8E, 0x8E, 0x93), FeedStateBrushConverter.For(FeedState.NotStarted).Color);
        });
    }

    private static void Layout(FrameworkElement e)
    {
        e.Measure(new Size(800, 600));
        e.Arrange(new Rect(0, 0, 800, 600));
        e.UpdateLayout();
    }
}
