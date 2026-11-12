using System.Windows.Threading;

namespace EarthWatch3D.App.Rendering;

/// <summary>
/// One long-lived STA thread with its own WPF <see cref="Dispatcher"/>, for the WPF drawing that must not run on the
/// UI thread (rasterizing lines, plate boundaries and alert areas). Work runs one item at a time, in order.
/// <see cref="Dispose"/> shuts the dispatcher down: work still queued is cancelled, and later calls are cancelled.
/// </summary>
public sealed class StaWorker : IDisposable
{
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(2);
    private readonly Thread _thread;
    private readonly Dispatcher _dispatcher;

    public StaWorker(string name)
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();   // returns once the dispatcher has shut down
        })
        { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
        _dispatcher = dispatcher!;
    }

    /// <summary>Runs <paramref name="work"/> on the worker thread. Its exception, if any, is the task's.</summary>
    public Task<T> InvokeAsync<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var op = _dispatcher.BeginInvoke(() =>
        {
            try { tcs.TrySetResult(work()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        // Aborted is raised when shutdown drops queued work; an operation queued after shutdown is aborted already.
        op.Aborted += (_, _) => tcs.TrySetCanceled();
        if (op.Status == DispatcherOperationStatus.Aborted) tcs.TrySetCanceled();
        return tcs.Task;
    }

    /// <summary>Shuts the dispatcher down (after the item running now, if any) and waits briefly for the thread to end.</summary>
    public void Dispose()
    {
        if (!_dispatcher.HasShutdownStarted) _dispatcher.InvokeShutdown();
        if (Thread.CurrentThread != _thread) _thread.Join(JoinTimeout);
    }
}
