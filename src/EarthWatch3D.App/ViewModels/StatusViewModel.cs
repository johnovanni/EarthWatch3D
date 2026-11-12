using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using EarthWatch3D.Core.Feeds;

namespace EarthWatch3D.App.ViewModels;

public sealed record StatusItem(string Name, FeedState State, string Tooltip);

/// <summary>One status dot per data source, in the order the sources first report.</summary>
public sealed partial class StatusViewModel(Dispatcher? dispatcher) : ObservableObject
{
    /// <summary>The download meter's text for the time bar, such as "Today: 12 MB"; empty when not shown.</summary>
    [ObservableProperty]
    private string _downloadText = "";

    public ObservableCollection<StatusItem> Items { get; } = [];

    /// <summary>
    /// Adds or updates the source's dot. Safe from any thread: off the dispatcher's thread it is queued with
    /// BeginInvoke (never Invoke, which could deadlock against the scheduler's Stop).
    /// </summary>
    public void Apply(FeedStatus s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => ApplyHere(s));
            return;
        }
        ApplyHere(s);
    }

    private void ApplyHere(FeedStatus s)
    {
        var item = new StatusItem(s.Name, s.State, TooltipFor(s));
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].Name != s.Name) continue;
            if (Items[i] != item) Items[i] = item;
            return;
        }
        Items.Add(item);
    }

    public static string TooltipFor(FeedStatus s)
    {
        string lastOk = s.LastSuccessUtc is { } ok ? Utc(ok) : "";
        string error = string.IsNullOrWhiteSpace(s.LastError) ? "" : " (" + Shorten(s.LastError) + ")";
        var text = s.State switch
        {
            FeedState.Ok => s.Name + ": updated" + (lastOk.Length > 0 ? " " + lastOk : ""),
            FeedState.Retrying or FeedState.Failing =>
                s.Name + (s.State == FeedState.Retrying ? ": retrying" : ": failing") + error +
                " – next try " + Utc(s.NextRunUtc) + (lastOk.Length > 0 ? "; last updated " + lastOk : ""),
            FeedState.NotSetUp => s.Name + ": not set up",
            FeedState.Paused => s.Name + ": paused",
            _ => s.Name + ": waiting for the first update",
        };
        return string.IsNullOrWhiteSpace(s.Note) ? text : text + Environment.NewLine + s.Note;
    }

    private static string Utc(DateTime t) => t.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC";

    // Exception messages can be long (a URL, a stack of causes); a tooltip line should not be.
    private static string Shorten(string text)
    {
        var oneLine = text.ReplaceLineEndings(" ").Trim();
        return oneLine.Length <= 120 ? oneLine : oneLine[..117] + "...";
    }
}
