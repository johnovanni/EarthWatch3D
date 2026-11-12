using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Geo;
using UnitFormat = EarthWatch3D.Core.Geo.Units;

namespace EarthWatch3D.App.ViewModels;

/// <summary>The selected event and the text the details panel and the hover tooltip show for it (distances and winds in the chosen units).</summary>
public sealed partial class SelectionViewModel : ObservableObject
{
    public const int MaxDescriptionChars = 600;
    private static readonly IReadOnlyList<(string Label, string Value)> NoLines = [];
    private readonly Func<DateTime> _nowUtc;
    private readonly Func<DistanceUnits> _units;

    public SelectionViewModel(Func<DateTime>? nowUtc = null, Func<DistanceUnits>? units = null)
    {
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
        _units = units ?? (() => DistanceUnits.Kilometres);
    }

    [ObservableProperty] private EarthEvent? _selected;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private IReadOnlyList<(string Label, string Value)> _lines = NoLines;
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private Uri? _link;
    [ObservableProperty] private bool _isOpen;

    public void Select(EarthEvent? e)
    {
        Selected = e;
        IsOpen = e is not null;
        Title = e is null ? "" : TitleFor(e);
        Lines = e is null ? NoLines : LinesFor(e, _nowUtc(), _units());
        Description = e is WeatherAlert a ? Truncate(a.Description) : "";
        Link = e?.Link;
    }

    [RelayCommand]
    private void Close() => Select(null);

    /// <summary>Closes the panel if the selected event is gone; shows the newer record if it was updated.</summary>
    public void ClearIfNotVisible(VisibleEvents v)
    {
        ArgumentNullException.ThrowIfNull(v);
        if (Selected is not { } s) return;
        EarthEvent? now = s switch
        {
            Earthquake => v.Quakes.Select(q => q.Quake).FirstOrDefault(q => Same(q, s)),
            CycloneAdvisory => v.Cyclones.FirstOrDefault(c => Same(c, s)),
            WeatherAlert => v.Alerts.FirstOrDefault(a => Same(a, s)),
            EonetStorm => v.Eonet.Select(x => x.Storm).FirstOrDefault(x => Same(x, s)),
            _ => null,
        };
        if (now is null) Select(null);
        else Select(now);   // also rebuilds the lines, whose relative ages move with the display time
    }

    private static bool Same(EarthEvent a, EarthEvent b) => a.Source == b.Source && a.Id == b.Id;

    // ---------------------------------------------------------------- text

    private static string TitleFor(EarthEvent e) => e switch
    {
        Earthquake q => string.IsNullOrWhiteSpace(q.Title) ? "Earthquake " + Magnitude(q) : q.Title,
        CycloneAdvisory c => string.IsNullOrWhiteSpace(c.Name) ? c.Title : c.Name,
        WeatherAlert a => a.EventType,
        _ => e.Title,
    };

    public static IReadOnlyList<(string Label, string Value)> LinesFor(EarthEvent e, DateTime nowUtc, DistanceUnits units)
    {
        switch (e)
        {
            case Earthquake q:
                return
                [
                    ("Magnitude", Magnitude(q)),
                    ("Depth", UnitFormat.Distance(q.DepthKm, units)),
                    ("Time", TimeWithAge(q.TimeUtc, nowUtc)),
                    ("Place", q.Place),
                ];
            case CycloneAdvisory c:
                return
                [
                    ("Category", StormNames.CategoryText(c.Category, c.Location)),
                    ("Wind", Wind(c.WindKt, units)),
                    ("Pressure", c.PressureHpa is { } p ? Num(p, 0) + " hPa" : "unknown"),
                    ("Advisory time", TimeWithAge(c.TimeUtc, nowUtc)),
                    ("Source", SourceName(c.Source)),
                ];
            case WeatherAlert a:
                return
                [
                    ("Event", a.EventType),
                    ("Severity", a.Severity),
                    ("From", Utc(a.OnsetUtc)),
                    ("Until", Utc(EndOf(a)) + EndNote(a)),
                    ("Headline", a.Title),
                ];
            case EonetStorm s:
                var fix = EventVisibility.FixAt(s, nowUtc);   // the fix the globe shows at the display time
                return
                [
                    ("Wind", Wind(fix?.WindKt, units)),
                    ("Last fix", fix is null ? "unknown" : TimeWithAge(fix.TimeUtc, nowUtc)),
                    ("Source", "NASA EONET"),
                ];
            default:
                return [("Time", TimeWithAge(e.TimeUtc, nowUtc))];
        }
    }

    /// <summary>The one-line tooltip, e.g. "M6.1 · 35 km deep · 2 h ago".</summary>
    public static string HoverText(EarthEvent e, DateTime nowUtc, DistanceUnits units)
    {
        switch (e)
        {
            case Earthquake q:
                return $"{Magnitude(q)} · {UnitFormat.Distance(q.DepthKm, units)} deep · {Age(q.TimeUtc, nowUtc)}";
            case CycloneAdvisory c:
                return $"{TitleFor(c)} · {StormNames.CategoryText(c.Category, c.Location)}" + (c.WindKt is { } w ? $" · {Num(w, 0)} kt" : "");
            case WeatherAlert a:
                var end = EndOf(a);
                return $"{a.EventType} · until " + (end.Date == nowUtc.Date
                    ? end.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC"
                    : Utc(end)) + EndNote(a);
            case EonetStorm s:
                return s.Title + (EventVisibility.FixAt(s, nowUtc)?.WindKt is { } fw ? $" · {Num(fw, 0)} kt" : "");
            default:
                return e.Title;
        }
    }

    /// <summary>min(EndsUtc, CancelledUtc): when the alert really stops showing.</summary>
    private static DateTime EndOf(WeatherAlert a) => a.CancelledUtc is { } c && c < a.EndsUtc ? c : a.EndsUtc;

    /// <summary>" (superseded)" or " (cancelled)" when a later message ended the alert early; otherwise empty.</summary>
    private static string EndNote(WeatherAlert a) =>
        a.CancelledUtc is { } c && c < a.EndsUtc
            ? a.EndReason == AlertEndReason.Cancelled ? " (cancelled)" : " (superseded)"
            : "";

    private static string Magnitude(Earthquake q) => "M" + Num(q.Magnitude, 1);
    private static string Wind(double? kt, DistanceUnits units) => kt is { } w ? UnitFormat.WindFromKnots(w, units) : "unknown";
    private static string Num(double v, int digits) => v.ToString("F" + digits, CultureInfo.InvariantCulture);
    private static string Utc(DateTime t) => t.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
    private static string TimeWithAge(DateTime t, DateTime now) => $"{Utc(t)} ({Age(t, now)})";

    private static string SourceName(EventSource s) => s switch
    {
        EventSource.Gdacs => "GDACS",
        EventSource.Nhc => "NHC",
        EventSource.Usgs => "USGS",
        EventSource.Nws => "NWS",
        _ => "NASA EONET",
    };

    /// <summary>"just now", "35 min ago", "2 h ago", "3 d ago" (or "in 2 h" for a time ahead).</summary>
    public static string Age(DateTime t, DateTime now)
    {
        var d = now - t;
        bool future = d < TimeSpan.Zero;
        if (future) d = -d;
        string span = d.TotalMinutes < 1 ? "" :
                      d.TotalMinutes < 60 ? $"{(int)d.TotalMinutes} min" :
                      d.TotalHours < 48 ? $"{(int)d.TotalHours} h" :
                      $"{(int)d.TotalDays} d";
        if (span.Length == 0) return "just now";
        return future ? "in " + span : span + " ago";
    }

    private static string Truncate(string text)
    {
        text = text?.Trim() ?? "";
        return text.Length <= MaxDescriptionChars ? text : text[..MaxDescriptionChars] + "…";
    }
}
