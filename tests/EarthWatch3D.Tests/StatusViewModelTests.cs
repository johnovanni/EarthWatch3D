using System.Windows.Threading;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.App.Views;
using EarthWatch3D.Core.Feeds;

namespace EarthWatch3D.Tests;

public class StatusViewModelTests
{
    private static readonly DateTime T0 = new(2026, 11, 12, 14, 3, 0, DateTimeKind.Utc);

    [Fact]
    public void StatusItemsAddedAndUpdated()
    {
        var vm = new StatusViewModel(null);
        vm.Apply(new FeedStatus("USGS", FeedState.NotStarted, null, null, 0, T0));
        vm.Apply(new FeedStatus("NWS", FeedState.NotStarted, null, null, 0, T0));
        Assert.Equal(["USGS", "NWS"], vm.Items.Select(i => i.Name));

        vm.Apply(new FeedStatus("USGS", FeedState.Ok, T0, null, 0, T0.AddMinutes(1)));
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("USGS", vm.Items[0].Name);
        Assert.Equal(FeedState.Ok, vm.Items[0].State);
        Assert.Equal("USGS: updated 14:03 UTC", vm.Items[0].Tooltip);
        Assert.Equal(FeedState.NotStarted, vm.Items[1].State);
    }

    [Fact]
    public void TooltipTextForEachState()
    {
        var next = new DateTime(2026, 11, 12, 14, 5, 0, DateTimeKind.Utc);
        Assert.Equal("USGS: updated 14:03 UTC",
            StatusViewModel.TooltipFor(new FeedStatus("USGS", FeedState.Ok, T0, null, 0, T0.AddMinutes(1))));
        Assert.Equal("NWS: retrying (timeout) – next try 14:05 UTC",
            StatusViewModel.TooltipFor(new FeedStatus("NWS", FeedState.Retrying, null, "timeout", 1, next)));
        Assert.Equal("NWS: retrying (timeout) – next try 14:05 UTC; last updated 14:03 UTC",
            StatusViewModel.TooltipFor(new FeedStatus("NWS", FeedState.Retrying, T0, "timeout", 1, next)));
        Assert.Equal("GDACS: failing (HTTP 503) – next try 14:05 UTC",
            StatusViewModel.TooltipFor(new FeedStatus("GDACS", FeedState.Failing, null, "HTTP 503", 3, next)));
        Assert.Equal("EONET: waiting for the first update",
            StatusViewModel.TooltipFor(new FeedStatus("EONET", FeedState.NotStarted, null, null, 0, next)));
        Assert.Equal("MTG: not set up",
            StatusViewModel.TooltipFor(new FeedStatus("MTG", FeedState.NotSetUp, null, null, 0, next)));
    }

    [Fact]
    public void PausedTooltipAddsTheNoteOnItsOwnLine()
    {
        var tip = StatusViewModel.TooltipFor(new FeedStatus("MTG", FeedState.Paused, T0, null, 0, T0.AddMinutes(1),
            "Daily download limit reached"));
        Assert.Equal("MTG: paused" + Environment.NewLine + "Daily download limit reached", tip);
        Assert.Equal(FeedStateBrushConverter.Paused, FeedStateBrushConverter.For(FeedState.Paused));
    }

    [Fact]
    public void DownloadTextIsObservable()
    {
        var vm = new StatusViewModel(null);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Equal("", vm.DownloadText);
        vm.DownloadText = "Today: 1 MB";
        Assert.Contains(nameof(StatusViewModel.DownloadText), changed);
    }

    [Fact]
    public void ApplyFromWorkerThreadIsMarshalledWithBeginInvoke()
    {
        Sta.Run(() =>
        {
            var vm = new StatusViewModel(Dispatcher.CurrentDispatcher);
            // The UI thread is blocked here, so a synchronous Invoke would never return.
            var worker = Task.Run(() => vm.Apply(new FeedStatus("USGS", FeedState.Ok, T0, null, 0, T0)));
            Assert.True(worker.Wait(TimeSpan.FromSeconds(5)));
            Assert.Empty(vm.Items);

            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Assert.Single(vm.Items);
        });
    }

    [Fact]
    public void OnlyANotSetUpOrKeyRejectedMtgDotOpensSettings()
    {
        var vm = new StatusViewModel(null);
        vm.Apply(new FeedStatus("MTG", FeedState.NotSetUp, null, null, 0, T0));
        vm.Apply(new FeedStatus("USGS", FeedState.NotSetUp, null, null, 0, T0));
        Assert.True(vm.OpensSettings(vm.Items[0]));
        Assert.False(vm.OpensSettings(vm.Items[1]));   // not the MTG dot

        vm.Apply(new FeedStatus("MTG", FeedState.Failing, null, "EUMETSAT key rejected. Check it in Settings.", 3, T0));
        Assert.True(vm.OpensSettings(vm.Items[0]));

        vm.Apply(new FeedStatus("MTG", FeedState.Failing, null, "HTTP 503", 3, T0));
        Assert.False(vm.OpensSettings(vm.Items[0]));
        vm.Apply(new FeedStatus("MTG", FeedState.Ok, T0, null, 0, T0));
        Assert.False(vm.OpensSettings(vm.Items[0]));
    }
}
