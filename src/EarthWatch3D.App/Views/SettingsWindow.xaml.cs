using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using EarthWatch3D.App.ViewModels;

namespace EarthWatch3D.App.Views;

/// <summary>The Settings dialog. DataContext: SettingsViewModel. The secret box is a PasswordBox, so it is filled by code here.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        UpdateSecretHint();
    }

    private void SecretBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _vm.ConsumerSecret = SecretBox.Password;
        UpdateSecretHint();
    }

    // "(saved)" shows while a secret is stored and nothing has been typed.
    private void UpdateSecretHint() =>
        SecretHint.Visibility = _vm.HasSavedSecret && SecretBox.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Save()) DialogResult = true;
    }

    private void GetKey_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(SettingsViewModel.RegistrationUrl) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _vm.TestStatus = "Could not open the browser. Go to " + SettingsViewModel.RegistrationUrl;
        }
    }
}
