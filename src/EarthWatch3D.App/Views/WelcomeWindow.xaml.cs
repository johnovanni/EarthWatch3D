using System.Windows;
using EarthWatch3D.App.ViewModels;

namespace EarthWatch3D.App.Views;

/// <summary>The first-run Welcome window. DataContext: WelcomeViewModel.</summary>
public partial class WelcomeWindow : Window
{
    private readonly WelcomeViewModel _vm;

    public WelcomeWindow(WelcomeViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        vm.RequestClose += (_, _) => Close();
        // Any way of closing (a button, the window's close box, Alt+F4) counts as seen.
        Closed += (_, _) => _vm.Close();
        MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 40);
    }
}
