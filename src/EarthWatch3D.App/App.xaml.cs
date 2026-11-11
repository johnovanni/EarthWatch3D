using System.Windows;
using System.Windows.Threading;
using EarthWatch3D.App.Diagnostics;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var clock = new SystemClock();
        var vm = new MainViewModel(clock);
        vm.Tick();   // show real values immediately
        var window = new MainWindow(vm, clock);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => vm.Tick();   // also drives the globe's per-minute recompose
        window.Closed += (_, _) => timer.Stop();
        timer.Start();

        var options = LaunchOptions.Parse(e.Args);
        if (options.Any) _ = SnapshotDiagnostic.RunAsync(window, window.Scene, vm, options);
#if PERF_LOG
        PerfRun.Start(window.Scene, e.Args);
#endif
        window.Show();
    }
}
