using System.Windows;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.App.Views;
using EarthWatch3D.Core.Lightning.Mtg;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.App.Services;

/// <summary>Opens the app's dialogs modally over the main window.</summary>
public sealed class DialogService(Func<Window?> owner, ISettingsStore settings, EumetsatCredentialStore credentials,
    Func<EumetsatCredentials, CancellationToken, Task<(EumetsatTestResult, string?)>> test, Action<AppSettings> applied) : IDialogService
{
    public bool ShowSettings()
    {
        var window = new SettingsWindow(new SettingsViewModel(settings, credentials, test, applied));
        if (owner() is { IsLoaded: true } o) window.Owner = o;
        return window.ShowDialog() == true;
    }
}
