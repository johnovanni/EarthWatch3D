using System.Windows;
using EarthWatch3D.Core.Logging;

namespace EarthWatch3D.App.Diagnostics;

/// <summary>
/// Catches exceptions nothing else handled (spec §11): UI-thread exceptions are marked handled and unobserved task
/// faults are marked observed, so the app keeps running. Every one is logged; only the first in a session is shown,
/// so a fault that repeats (every frame, say) cannot bury the user in message boxes.
/// </summary>
/// <param name="showMessage">Shows the message to the user. Called on whatever thread the exception arrived on
/// (unobserved task faults arrive on the finalizer thread), so it must marshal to the UI thread itself.</param>
public sealed class CrashHandler(IAppLog log, Action<string> showMessage)
{
    public const string Message =
        "Something went wrong; details are in the log file. EarthWatch3D will try to keep running, but if it " +
        "misbehaves, restart it.";

    private int _shown;

    /// <summary>Hooks the application's dispatcher, the task scheduler and (log only: the process is ending) the app domain.</summary>
    public void Install(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            Handle(e.Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            Handle(e.Exception);
        };
        // A background thread's exception still ends the process; at least the log says why.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try { log.Error("Fatal unhandled exception" + (e.IsTerminating ? " (the app is closing)" : ""), e.ExceptionObject as Exception); }
            catch (Exception) { }
        };
    }

    /// <summary>Logs the exception and, the first time only, shows the message. Never throws.</summary>
    public void Handle(Exception e)
    {
        try { log.Error("Unhandled exception: " + e.Message, e); }
        catch (Exception) { }   // the log failing must not turn one fault into two
        if (Interlocked.Exchange(ref _shown, 1) != 0) return;
        try { showMessage(Message); }
        catch (Exception) { }
    }
}
