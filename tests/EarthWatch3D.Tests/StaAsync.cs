using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace EarthWatch3D.Tests;

/// <summary>
/// Runs async WPF code on a dedicated STA thread with a running <see cref="Dispatcher"/>, so awaits come back to that
/// thread and DispatcherTimers tick, as on the app's UI thread. Rethrows the work's exception.
/// (A copy of GlobeCore.Tests' helper.)
/// </summary>
internal static class StaAsync
{
    public static void Run(Func<Task> work, int timeoutMs = 30_000)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(async () =>
            {
                try { await work(); }
                catch (Exception ex) { error = ex; }
                finally { dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        if (!t.Join(timeoutMs)) throw new TimeoutException("The STA work did not finish in time.");
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    /// <summary>Waits (letting the dispatcher run) until <paramref name="condition"/> holds; throws after <paramref name="timeoutMs"/>.</summary>
    public static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5_000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Condition not met in time.");
            await Task.Delay(10);
        }
    }
}
