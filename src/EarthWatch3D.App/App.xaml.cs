using System.Windows;
using System.Windows.Threading;
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
        var window = new MainWindow { DataContext = vm };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => vm.Tick();
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        // Diagnostic: "--face-sun" turns the camera to the Sun 2 s after load (used for screenshot checks).
        if (e.Args.Contains("--face-sun"))
            window.Loaded += (_, _) =>
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                t.Tick += (_, _) => { t.Stop(); vm.SunMoon.FaceSunCommand.Execute(null); };
                t.Start();
            };
        window.Show();
    }
}
