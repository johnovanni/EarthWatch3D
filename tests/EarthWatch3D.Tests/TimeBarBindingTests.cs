using System.Windows;
using System.Windows.Controls;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.App.Views;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Tests;

public class TimeBarBindingTests
{
    private sealed class FakeClock(DateTime t) : IClock { public DateTime UtcNow { get; set; } = t; }

    [Fact]
    public void TimeBarBindsToTimeline()
    {
        Sta.Run(() =>
        {
            var vm = new TimelineViewModel(new FakeClock(new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc)));
            var bar = new TimeBar { DataContext = vm };
            bar.Measure(new Size(1000, 100));
            bar.Arrange(new Rect(0, 0, 1000, 100));
            bar.UpdateLayout();
            var slider = (Slider)bar.FindName("TimeSlider");
            Assert.NotNull(slider);
            Assert.NotNull(bar.FindName("StatusHost"));
            Assert.Equal(60, slider.SmallChange);
            Assert.Equal(3600, slider.LargeChange);
            Assert.True(vm.IsLive);
            slider.Value = 0;
            Assert.False(vm.IsLive);
        });
    }
}
