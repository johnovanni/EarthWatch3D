using EarthWatch3D.Core.Geo;
using EarthWatch3D.App.Diagnostics;
using EarthWatch3D.App.Services;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.Tests;

internal sealed class RecordingDialogService : IDialogService
{
    public List<string> Calls { get; } = [];
    public Action? OnShowSettings { get; set; }
    public bool ShowSettings() { Calls.Add("settings"); OnShowSettings?.Invoke(); return false; }
    public void ShowAbout() => Calls.Add("about");
    public void ShowWelcome() => Calls.Add("welcome");
}

public class WelcomeViewModelTests
{
    private static WelcomeViewModel Make(FakeSettingsStore? store = null, IDialogService? dialogs = null,
        ILinkOpener? opener = null, bool mtg = false) =>
        new(store ?? new FakeSettingsStore(), dialogs ?? new RecordingDialogService(), opener ?? new RecordingLinkOpener(), mtg);

    [Fact]
    public void ShownOnlyOnFirstRun()
    {
        var none = LaunchOptions.Parse([]);
        Assert.True(WelcomeViewModel.ShouldShow(new AppSettings(), none));
        Assert.False(WelcomeViewModel.ShouldShow(new AppSettings { WelcomeShown = true }, none));
    }

    [Theory]
    [InlineData("--snapshot", "x.png")]
    [InlineData("--offline-samples", "d")]
    [InlineData("--bench")]
    [InlineData("--dialog", "about")]
    [InlineData("--look-at", "0,0")]
    [InlineData("--face-sun")]
    public void NotShownInDiagnosticOrOfflineRuns(params string[] args)
    {
        var options = LaunchOptions.Parse(args);
        Assert.True(options.IsDiagnosticRun);
        Assert.False(WelcomeViewModel.ShouldShow(new AppSettings(), options));
    }

    [Fact]
    public void ClosingMarksItShownAndKeepsOtherSettings()
    {
        var store = new FakeSettingsStore(new AppSettings { Units = DistanceUnits.Miles });
        Make(store).Close();
        var saved = store.Load();
        Assert.True(saved.WelcomeShown);
        Assert.Equal(DistanceUnits.Miles, saved.Units);
    }

    [Fact]
    public void ClosingTwiceSavesOnce()
    {
        var store = new FakeSettingsStore();
        var vm = Make(store);
        vm.Close();
        vm.Close();
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void OpenSettingsClosesWelcomeThenOpensSettings()
    {
        var dialogs = new RecordingDialogService();
        var vm = Make(dialogs: dialogs);
        bool closedFirst = false, closeRaised = false;
        vm.RequestClose += (_, _) => closeRaised = true;
        dialogs.OnShowSettings = () => closedFirst = closeRaised;
        vm.OpenSettingsCommand.Execute(null);
        Assert.Equal(["settings"], dialogs.Calls);
        Assert.True(closedFirst);
    }

    [Fact]
    public void StartRaisesRequestClose()
    {
        var vm = Make();
        int n = 0;
        vm.RequestClose += (_, _) => n++;
        vm.StartCommand.Execute(null);
        Assert.Equal(1, n);
    }

    [Fact]
    public void TheAppAsBuiltOffersTheKeyForEuropeAndAfrica()
    {
        var vm = new WelcomeViewModel(new FakeSettingsStore(), new RecordingDialogService(), new RecordingLinkOpener());
        Assert.Contains("adds Europe and Africa", vm.KeyText);
        Assert.DoesNotContain("coming version", vm.KeyText);
    }

    [Fact]
    public void KeyTextFollowsMtgAvailability()
    {
        var without = Make(mtg: false).KeyText;
        Assert.Contains("coming version", without);
        Assert.DoesNotContain("now", without.Replace("store it in Settings now", ""));
        var with = Make(mtg: true).KeyText;
        Assert.Contains("Europe and Africa", with);
        Assert.Contains("Lightning covers the Americas", with);
    }

    [Fact]
    public void KeyLinkOpensTheRegistrationPage()
    {
        var opener = new RecordingLinkOpener();
        var vm = Make(opener: opener);
        vm.OpenKeyLinkCommand.Execute(null);
        Assert.Equal([SettingsViewModel.RegistrationUrl], opener.Opened);
        Assert.Null(vm.Message);
    }

    [Fact]
    public void KeyLinkFailureShowsWhereToGo()
    {
        var vm = Make(opener: new RecordingLinkOpener { Throw = new InvalidOperationException("no browser") });
        vm.OpenKeyLinkCommand.Execute(null);
        Assert.StartsWith("Could not open " + SettingsViewModel.RegistrationUrl, vm.Message);
    }
}
