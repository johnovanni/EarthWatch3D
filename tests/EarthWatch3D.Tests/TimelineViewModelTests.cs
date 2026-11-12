using EarthWatch3D.App.Controllers;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Tests;

public class TimelineViewModelTests
{
    private sealed class FakeClock(DateTime t) : IClock { public DateTime UtcNow { get; set; } = t; }
    private static readonly DateTime T0 = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact] public void StartsLiveAtClockTime()
    { var c = new FakeClock(T0); var vm = new TimelineViewModel(c); vm.Advance(TimeSpan.Zero); Assert.True(vm.IsLive); Assert.Equal(T0, vm.DisplayTimeUtc); Assert.StartsWith("LIVE", vm.ReadoutText); }

    [Fact] public void LiveFollowsClock()
    { var c = new FakeClock(T0); var vm = new TimelineViewModel(c); c.UtcNow = T0.AddMinutes(3); vm.Advance(TimeSpan.FromMinutes(3)); Assert.Equal(T0.AddMinutes(3), vm.DisplayTimeUtc); }

    [Fact] public void MovingSliderLeavesLiveAndShowsReplay()
    { var vm = new TimelineViewModel(new FakeClock(T0)); vm.SliderSeconds = TimeSpan.FromDays(6).TotalSeconds;
      Assert.False(vm.IsLive); Assert.Equal(T0 - TimeSpan.FromDays(1), vm.DisplayTimeUtc); Assert.StartsWith("REPLAY", vm.ReadoutText); }

    [Fact] public void SliderClampsToSevenDays()
    { var vm = new TimelineViewModel(new FakeClock(T0)); vm.SliderSeconds = -5; Assert.Equal(T0 - TimeSpan.FromDays(7), vm.DisplayTimeUtc);
      vm.SliderSeconds = 1e12; Assert.Equal(T0, vm.DisplayTimeUtc); }

    [Fact] public void PlayAdvancesAtSpeedAndBecomesLiveAtNow()
    { var c = new FakeClock(T0); var vm = new TimelineViewModel(c); vm.StepMinus1HourCommand.Execute(null);
      vm.SetSpeedCommand.Execute(600.0); vm.TogglePlayCommand.Execute(null);
      vm.Advance(TimeSpan.FromSeconds(3)); Assert.Equal(T0.AddHours(-1).AddMinutes(30), vm.DisplayTimeUtc);
      vm.Advance(TimeSpan.FromSeconds(10)); Assert.True(vm.IsLive); Assert.Equal(T0, vm.DisplayTimeUtc); }

    [Fact] // Review Focus 1
    public void GoLiveAfterReplayRestores()
    { var vm = new TimelineViewModel(new FakeClock(T0)); vm.SliderSeconds = 0; vm.GoLiveCommand.Execute(null); Assert.True(vm.IsLive); Assert.Equal(T0, vm.DisplayTimeUtc); }

    [Fact] public void ReplayAtLeavesLiveAndClampsToTheRange()
    { var vm = new TimelineViewModel(new FakeClock(T0)); vm.IsPlaying = true;
      vm.ReplayAt(T0.AddHours(-5)); Assert.False(vm.IsLive); Assert.False(vm.IsPlaying); Assert.Equal(T0.AddHours(-5), vm.DisplayTimeUtc);
      Assert.StartsWith("REPLAY", vm.ReadoutText);
      vm.ReplayAt(T0.AddDays(-30)); Assert.Equal(T0 - TimelineViewModel.Range, vm.DisplayTimeUtc);
      vm.Advance(TimeSpan.FromSeconds(1)); Assert.False(vm.IsLive); }

    [Fact] public void SampleDataNeverReadsLive()
    { var c = new FakeClock(T0); var vm = new TimelineViewModel(c) { IsSampleData = true };
      Assert.True(vm.IsLive); Assert.Equal("SAMPLE 12 Nov 12:00 UTC", vm.ReadoutText);
      c.UtcNow = T0.AddMinutes(5); vm.Advance(TimeSpan.FromMinutes(5)); Assert.Equal("SAMPLE 12 Nov 12:05 UTC", vm.ReadoutText);
      vm.StepMinus1HourCommand.Execute(null); Assert.Equal("REPLAY 12 Nov 11:05 UTC", vm.ReadoutText);
      vm.GoLiveCommand.Execute(null); Assert.StartsWith("SAMPLE ", vm.ReadoutText); Assert.DoesNotContain("LIVE", vm.ReadoutText); }

    [Fact] public void SettingSampleDataRaisesReadoutChange()
    { var vm = new TimelineViewModel(new FakeClock(T0)); var changed = new List<string?>(); vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
      vm.IsSampleData = true; Assert.Contains(nameof(TimelineViewModel.ReadoutText), changed); }

    [Fact] public void DisplayTimeChangedRaisedOnStep()
    { var vm = new TimelineViewModel(new FakeClock(T0)); int n = 0; vm.DisplayTimeChanged += (_, _) => n++; vm.StepMinus10MinCommand.Execute(null); Assert.Equal(1, n); }

    [Fact] public void SliderSecondsTracksMinTimeInReplay()
    {
        var c = new FakeClock(T0); var vm = new TimelineViewModel(c);
        vm.SliderSeconds = 1000;   // replay at a fixed time
        var before = vm.SliderSeconds;
        var fired = new List<string?>(); vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);
        c.UtcNow = T0.AddSeconds(10); vm.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(before - 10, vm.SliderSeconds, 3);
        Assert.Contains("SliderSeconds", fired);
    }
}

public class ThrottleTests
{
    private static readonly DateTime T = new(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);
    [Fact] public void AllowsThenBlocksWithinGap()
    { var t = new Throttle(TimeSpan.FromMilliseconds(250)); Assert.True(t.TryAcquire(T)); Assert.False(t.TryAcquire(T.AddMilliseconds(100))); Assert.True(t.TryAcquire(T.AddMilliseconds(250))); }
}
