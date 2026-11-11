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
        var options = LaunchOptions.Parse(e.Args);
        // --snapshot-recompose moves the app's time forward part-way through, so it runs on an adjustable clock.
        OffsetClock? offsetClock = options.SnapshotRecompose ? new OffsetClock(new SystemClock()) : null;
        IClock clock = offsetClock is null ? new SystemClock() : offsetClock;
        var vm = new MainViewModel(clock);
        vm.Tick();   // show real values immediately
        var window = new MainWindow(vm, clock);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => vm.Tick();   // also drives the globe's per-minute recompose
        window.Closed += (_, _) => timer.Stop();
        timer.Start();

        if (options.Any) _ = SnapshotDiagnostic.RunAsync(window, window.Scene, vm, options, offsetClock);
#if PERF_LOG
        PerfRun.Start(window.Scene, e.Args);
#endif
        window.Show();
    }
}
