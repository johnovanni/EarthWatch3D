using System.Windows;
using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.App.Diagnostics;

/// <summary>
/// Catches exceptions nothing else handled (spec §11): UI-thread exceptions are marked handled and unobserved task
/// faults are marked observed, so the app keeps running. Only the first message in a session is shown, so a fault that
/// repeats (every frame, say) cannot bury the user in message boxes, and it cannot flood the log either:
/// <list type="bullet">
/// <item>Each distinct fault (exception type, message and top stack frame) is logged in full the first time. After
/// that its repeats are only counted, and one summary line ("repeated N times") is written at most once a minute, and
/// on <see cref="FlushRepeats"/>.</item>
/// <item>If more than <see cref="FloodLimit"/> UI-thread exceptions arrive within <see cref="FloodWindow"/>, the app is
/// clearly stuck: they are no longer marked handled, so WPF ends the process ("too many errors, closing").</item>
/// </list>
/// </summary>
/// <param name="showMessage">Shows the message to the user. Called on whatever thread the exception arrived on
/// (unobserved task faults arrive on the finalizer thread), so it must marshal to the UI thread itself; on the UI thread
/// it should show the message at once, because after "too many errors" the process ends straight afterwards.</param>
public sealed class CrashHandler(IAppLog log, Action<string> showMessage, IClock? clock = null)
{
    public const string Message =
        "Something went wrong; details are in the log file. EarthWatch3D will try to keep running, but if it " +
        "misbehaves, restart it.";
    public const string ClosingMessage =
        "Something went wrong; details are in the log file. Errors keep happening, so EarthWatch3D has to close.";
    public const int FloodLimit = 50;
    public static readonly TimeSpan FloodWindow = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan SummaryInterval = TimeSpan.FromMinutes(1);

    private sealed class Repeats(string description, DateTime since)
    {
        public string Description { get; } = description;
        public DateTime Since { get; set; } = since;
        public int Count { get; set; }
    }

    private readonly IClock _clock = clock ?? new SystemClock();
    private readonly object _gate = new();
    private readonly Dictionary<string, Repeats> _seen = new(StringComparer.Ordinal);
    private readonly Queue<DateTime> _recentDispatcher = new();
    private bool _shown, _closing;

    /// <summary>Hooks the application's dispatcher, the task scheduler and (log only: the process is ending) the app domain.</summary>
    public void Install(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.DispatcherUnhandledException += (_, e) => e.Handled = HandleDispatcher(e.Exception);
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

    /// <summary>
    /// A UI-thread exception: handled as <see cref="Handle"/>, and returns whether to mark it handled. False once more
    /// than <see cref="FloodLimit"/> have arrived within <see cref="FloodWindow"/> (and from then on). Never throws.
    /// </summary>
    public bool HandleDispatcher(Exception e)
    {
        bool tripped = false;
        lock (_gate)
        {
            if (!_closing)
            {
                var now = _clock.UtcNow;
                _recentDispatcher.Enqueue(now);
                while (_recentDispatcher.Count > 0 && now - _recentDispatcher.Peek() > FloodWindow) _recentDispatcher.Dequeue();
                if (_recentDispatcher.Count > FloodLimit) _closing = tripped = true;
            }
        }
        if (!tripped)
        {
            Handle(e);
            return !Volatile.Read(ref _closing);
        }
        FlushRepeats();
        Log(() => log.Error($"Unhandled exception: too many errors, closing ({FloodLimit}+ within {FloodWindow.TotalSeconds:0} s): {e.Message}", e));
        Show(ClosingMessage);
        return false;
    }

    /// <summary>Logs the exception (in full once per distinct fault, then counted) and, the first time only, shows the message. Never throws.</summary>
    public void Handle(Exception e)
    {
        var (key, description) = Describe(e);
        bool first;
        List<string> summaries;
        lock (_gate)
        {
            var now = _clock.UtcNow;
            first = !_seen.TryGetValue(key, out var r);
            if (first) _seen[key] = new Repeats(description, now);
            else r!.Count++;
            summaries = DueSummaries(now, force: false);
        }
        if (first) Log(() => log.Error("Unhandled exception: " + e.Message, e));
        foreach (var s in summaries) Log(() => log.Warn(s));
        Show(Message);
    }

    /// <summary>Writes the summary line of every fault that has repeated since its last line (the app calls this on exit).</summary>
    public void FlushRepeats()
    {
        List<string> summaries;
        lock (_gate) summaries = DueSummaries(_clock.UtcNow, force: true);
        foreach (var s in summaries) Log(() => log.Warn(s));
    }

    // Under _gate.
    private List<string> DueSummaries(DateTime now, bool force)
    {
        var due = new List<string>();
        foreach (var r in _seen.Values)
        {
            if (r.Count == 0 || (!force && now - r.Since < SummaryInterval)) continue;
            due.Add($"Unhandled exception repeated {r.Count} {(r.Count == 1 ? "time" : "times")} since {r.Since:HH:mm:ss} UTC: {r.Description}");
            r.Count = 0;
            r.Since = now;
        }
        return due;
    }

    private void Show(string message)
    {
        lock (_gate)
        {
            if (_shown) return;
            _shown = true;
        }
        try { showMessage(message); }
        catch (Exception)
        {
            lock (_gate) _shown = false;   // not seen: a later fault (or the closing message) may still show it
        }
    }

    private static void Log(Action write)
    {
        try { write(); }
        catch (Exception) { }   // the log failing must not turn one fault into two
    }

    /// <summary>The identity of a fault: type, message and top stack frame (of the single inner exception for a task fault).</summary>
    private static (string Key, string Description) Describe(Exception e)
    {
        var x = e is AggregateException { InnerExceptions.Count: 1 } a ? a.InnerExceptions[0] : e;
        string top = "";
        try
        {
            var trace = x.StackTrace;
            if (trace is not null)
            {
                int nl = trace.IndexOf('\n');
                top = (nl < 0 ? trace : trace[..nl]).Trim();
            }
        }
        catch (Exception) { }
        var description = $"{x.GetType().FullName}: {x.Message}";
        return (description + " @ " + top, description);
    }
}
