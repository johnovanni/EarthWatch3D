using EarthWatch3D.App.ViewModels;

namespace EarthWatch3D.Tests;

/// <summary>
/// The PB2002 reader is tested in GlobeCore (GlobeCore.Tests.PlateBoundariesTests); the plates layer switch stays here.
/// </summary>
public class PlateBoundariesTests
{
    [Fact] public void ShowPlatesDefaultsOffAndIsSaved()
    { var store = new FakeSettingsStore(); var vm = new LayersViewModel(store); Assert.False(vm.ShowPlates); vm.ShowPlates = true; Assert.True(store.Saved[^1].ShowPlates); }

    [Fact] public void LayersTurnedOnForRunAreNotSavedUnlessUserToggles()
    {
        var store = new FakeSettingsStore(); var vm = new LayersViewModel(store);
        vm.TurnOnForThisRun(["plates"]);
        Assert.True(vm.ShowPlates); Assert.Equal(0, store.SaveCount);
        vm.ShowCoastlines = false;   // another layer saved: plates still not persisted
        Assert.False(store.Saved[^1].ShowPlates);
        vm.ShowPlates = false; vm.ShowPlates = true;   // the user's own toggle persists
        Assert.True(store.Saved[^1].ShowPlates);
    }
}
