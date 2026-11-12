using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using AstroCore.Earth;
using EarthWatch3D.Core.Lightning;
using HelixToolkit.Wpf;

namespace EarthWatch3D.App.Rendering;

/// <summary>
/// Lightning flashes on the globe: one point per flash, coloured by age within the display window (four
/// <see cref="PointsVisual3D"/>, one per age bucket). Rebuilt at most once a second. UI thread only.
/// At most the newest <see cref="MaxDrawnFlashes"/> flashes of the window are drawn (the buffer keeps them all).
/// </summary>
public sealed class LightningLayer
{
    public const double PointRadius = 1.004;
    /// <summary>
    /// The most flashes drawn at once: the newest of the window. 100,000 points rotate at about 11 fps, 25,000 at about 39
    /// (docs/perf.md), and a 60-minute window in storm season can hold 100,000.
    /// </summary>
    public const int MaxDrawnFlashes = 30_000;
    /// <summary>
    /// False until the MTG (EUMETSAT) lightning feed is built: the legend, the credit line and the Settings key section then
    /// say MTG is coming rather than promise it. Turning MTG on later is this one line.
    /// </summary>
    public const bool MtgAvailable = false;
    public const string LegendTitle = MtgAvailable ? "Lightning (GOES GLM, MTG LI)" : "Lightning (GOES GLM)";
    public const string Attribution = MtgAvailable ? "GOES GLM: NOAA. MTG LI: Contains EUMETSAT data." : "GOES GLM: NOAA.";
    public const string MtgComingLine = "MTG (Europe, Africa): coming in a later version";
    /// <summary>The Settings dialog's label for the EUMETSAT key.</summary>
    public const string SettingsKeyLabel = MtgAvailable
        ? "EUMETSAT consumer key (for MTG lightning over Europe and Africa)"
        : "EUMETSAT consumer key (for the coming MTG lightning over Europe and Africa)";
    /// <summary>The Settings dialog's note under the EUMETSAT key and secret.</summary>
    public const string SettingsKeyNote =
        (MtgAvailable ? "" : "The key is stored now and used when MTG lightning arrives in a later version. ")
        + "The key stays on this computer, encrypted for your Windows account. Leave the secret empty to keep the saved one; clear the key to remove it.";
    /// <summary>A newest MTG flash older than this is called delayed (MTG data reaches the public with a lag).</summary>
    public static readonly TimeSpan MtgDelayed = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RebuildInterval = TimeSpan.FromSeconds(1);

    /// <summary>Upper age limits as percent of the window for buckets 0, 1 and 2 (bucket 3 is everything older).</summary>
    private static readonly int[] LimitPercent = [10, 35, 70];
    public static readonly Color[] BucketColors =
    [
        Color.FromRgb(0xFF, 0xF0, 0xB0), Color.FromRgb(0xFF, 0xD8, 0x4A), Color.FromRgb(0xFF, 0x9A, 0x2E), Color.FromRgb(0xB4, 0x60, 0x2A),
    ];
    public static readonly double[] BucketSizes = [4, 3.5, 3, 2.5];

    private readonly PointsVisual3D[] _visuals = new PointsVisual3D[4];
    private bool _shown;
    private TimeSpan _builtWindow;
    private DateTime _builtAtUtc;
    private bool _built;

    public LightningLayer(HelixViewport3D viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        for (int i = 0; i < _visuals.Length; i++)
        {
            _visuals[i] = new PointsVisual3D { Color = BucketColors[i], Size = BucketSizes[i] };
            viewport.Children.Add(_visuals[i]);
        }
    }

    /// <summary>Flashes currently drawn (at most <see cref="MaxDrawnFlashes"/>).</summary>
    public int PointCount { get; private set; }

    /// <summary>Flashes inside the window at the last rebuild, drawn or not (0 while hidden).</summary>
    public int FlashCountInWindow { get; private set; }

    /// <summary>Age bucket 0 (newest) to 3 for a flash of this age in a window.</summary>
    public static int Bucket(TimeSpan age, TimeSpan window)
    {
        for (int i = 0; i < LimitPercent.Length; i++)
            if (age.Ticks * 100 < window.Ticks * LimitPercent[i]) return i;
        return 3;
    }

    /// <summary>Lightning is live only: a source without history is not shown while the display is not live.</summary>
    public static bool IsShown(bool layerOn, bool live, bool supportsHistory) => layerOn && (live || supportsHistory);

    /// <summary>
    /// Draws the flashes of the last <paramref name="window"/> up to <paramref name="nowUtc"/>, or nothing if <paramref name="show"/> is false.
    /// Rebuilds at most once a second, except at once when <paramref name="show"/> or the window changed.
    /// </summary>
    public void Update(ILightningSource source, DateTime nowUtc, TimeSpan window, bool show)
    {
        ArgumentNullException.ThrowIfNull(source);
        bool force = !_built || show != _shown || window != _builtWindow;
        if (!force && (nowUtc - _builtAtUtc < RebuildInterval && nowUtc >= _builtAtUtc)) return;
        _built = true; _shown = show; _builtWindow = window; _builtAtUtc = nowUtc;

        if (!show || window <= TimeSpan.Zero)
        {
            if (PointCount > 0) foreach (var v in _visuals) v.Points = new Point3DCollection();
            PointCount = 0;
            FlashCountInWindow = 0;
            return;
        }
        var flashes = source.GetFlashes(nowUtc - window, nowUtc.AddTicks(1));   // in time order
        var buckets = new Point3DCollection[4];
        for (int i = 0; i < buckets.Length; i++) buckets[i] = new Point3DCollection();
        int count = 0;
        // Over the cap only the newest are drawn: the tail of the list. Their age buckets are still against the window.
        for (int n = Math.Max(0, flashes.Count - MaxDrawnFlashes); n < flashes.Count; n++)
        {
            var f = flashes[n];
            var age = nowUtc - f.TimeUtc;
            if (age < TimeSpan.Zero) age = TimeSpan.Zero;
            var u = new GeoPosition(f.Lat, f.Lon).ToUnitVector();
            buckets[Bucket(age, window)].Add(new Point3D(u.X * PointRadius, u.Y * PointRadius, u.Z * PointRadius));
            count++;
        }
        for (int i = 0; i < buckets.Length; i++) _visuals[i].Points = buckets[i];
        PointCount = count;
        FlashCountInWindow = flashes.Count;
    }

    /// <summary>
    /// One line per sensor: how long ago its newest flash was, for the legend tooltip. While <paramref name="mtgAvailable"/>
    /// is false (no MTG feed yet) the MTG line says it is coming. When <paramref name="flashesInWindow"/> is over
    /// <see cref="MaxDrawnFlashes"/> a last line says only the newest are drawn.
    /// </summary>
    public static string LegendNote(LightningCoverage c, DateTime nowUtc, bool mtgAvailable = MtgAvailable, int flashesInWindow = 0)
    {
        ArgumentNullException.ThrowIfNull(c);
        var lines = new[]
        {
            Line("GOES-East", c.NewestGoesEastUtc, nowUtc),
            Line("GOES-West", c.NewestGoesWestUtc, nowUtc),
            !mtgAvailable ? MtgComingLine
                : c.MtgSetUp ? Line("MTG", c.NewestMtgUtc, nowUtc, delayedAfter: MtgDelayed) : "MTG: not set up (Settings)",
        };
        var note = string.Join('\n', lines);
        return flashesInWindow > MaxDrawnFlashes
            ? note + '\n' + string.Create(CultureInfo.InvariantCulture, $"Showing the newest {MaxDrawnFlashes:N0} of {flashesInWindow:N0} flashes.")
            : note;
    }

    private static string Line(string name, DateTime? newest, DateTime nowUtc, TimeSpan? delayedAfter = null)
    {
        if (newest is not { } t) return $"{name}: no flashes yet";
        var age = nowUtc - t;
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        string text = string.Create(CultureInfo.InvariantCulture, $"{name}: newest flash {Ago(age)} ago");
        return delayedAfter is { } limit && age > limit ? text + " (delayed)" : text;
    }

    private static string Ago(TimeSpan age) =>
        age.TotalSeconds < 60 ? $"{(int)age.TotalSeconds} s"
        : age.TotalMinutes < 60 ? $"{(int)age.TotalMinutes} min"
        : $"{(int)age.TotalHours} h";
}
