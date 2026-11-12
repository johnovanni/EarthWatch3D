namespace EarthWatch3D.App.Services;

/// <summary>Opens the app's dialogs, so view models need no window types.</summary>
public interface IDialogService
{
    /// <summary>Shows the Settings dialog; true if the user saved changes.</summary>
    bool ShowSettings();
}
