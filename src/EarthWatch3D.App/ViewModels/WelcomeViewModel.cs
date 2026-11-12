using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EarthWatch3D.App.Diagnostics;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.App.Services;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.App.ViewModels;

/// <summary>The first-run Welcome window: a short tour, and a way to Settings. Closing it in any way marks it as shown.</summary>
public sealed partial class WelcomeViewModel : ObservableObject
{
    private readonly ISettingsStore _store;
    private readonly IDialogService _dialogs;
    private readonly ILinkOpener _opener;
    private bool _closed;

    public WelcomeViewModel(ISettingsStore store, IDialogService dialogs, ILinkOpener opener,
        bool mtgAvailable = LightningLayer.MtgAvailable)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _opener = opener ?? throw new ArgumentNullException(nameof(opener));
        KeyText = mtgAvailable
            ? "Lightning covers the Americas. A free EUMETSAT key, entered in Settings, adds Europe and Africa."
            : "A free EUMETSAT key will add lightning over Europe and Africa in a coming version. You can store it in Settings now.";
    }

    /// <summary>The paragraph about the EUMETSAT key; the window shows the "Get a free key" link after it.</summary>
    public string KeyText { get; }

    /// <summary>Why the key page could not be opened; null otherwise.</summary>
    [ObservableProperty] private string? _message;

    /// <summary>Asks the window to close itself.</summary>
    public event EventHandler? RequestClose;

    /// <summary>Only the very first run, and never a diagnostic or offline run (a modal window would hang a --snapshot run).</summary>
    public static bool ShouldShow(AppSettings settings, LaunchOptions options) =>
        !settings.WelcomeShown && !options.IsDiagnosticRun;

    /// <summary>Marks the Welcome as shown (once), keeping every other setting. The window calls this when it closes, so the close button counts too.</summary>
    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _store.Save(_store.Load() with { WelcomeShown = true });
    }

    [RelayCommand]
    private void Start() => RequestClose?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenSettings()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
        _dialogs.ShowSettings();
    }

    [RelayCommand]
    private void OpenKeyLink()
    {
        try
        {
            _opener.Open(SettingsViewModel.RegistrationUrl);
            Message = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Message = $"Could not open {SettingsViewModel.RegistrationUrl}: {ex.Message}";
        }
    }
}
