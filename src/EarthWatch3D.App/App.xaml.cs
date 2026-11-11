using System.Windows;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var vm = new MainViewModel(new SystemClock());
        vm.Tick();   // show real values immediately
        new MainWindow { DataContext = vm }.Show();
    }
}
