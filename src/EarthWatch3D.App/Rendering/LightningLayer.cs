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
/// </summary>
public sealed class LightningLayer
{
    public const double PointRadius = 1.004;
    public const string Attribution = "GOES GLM: NOAA. MTG LI: Contains EUMETSAT data.";
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

    /// <summary>Flashes currently drawn.</summary>
    public int PointCount { get; private set; }

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
            return;
        }
        var flashes = source.GetFlashes(nowUtc - window, nowUtc.AddTicks(1));
        var buckets = new Point3DCollection[4];
        for (int i = 0; i < buckets.Length; i++) buckets[i] = new Point3DCollection();
        int count = 0;
        foreach (var f in flashes)
        {
            var age = nowUtc - f.TimeUtc;
            if (age < TimeSpan.Zero) age = TimeSpan.Zero;
            var u = new GeoPosition(f.Lat, f.Lon).ToUnitVector();
            buckets[Bucket(age, window)].Add(new Point3D(u.X * PointRadius, u.Y * PointRadius, u.Z * PointRadius));
            count++;
        }
        for (int i = 0; i < buckets.Length; i++) _visuals[i].Points = buckets[i];
        PointCount = count;
    }

    /// <summary>One line per sensor: how long ago its newest flash was, for the legend tooltip.</summary>
    public static string LegendNote(LightningCoverage c, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(c);
        var lines = new[]
        {
            Line("GOES-East", c.NewestGoesEastUtc, nowUtc),
            Line("GOES-West", c.NewestGoesWestUtc, nowUtc),
            c.MtgSetUp ? Line("MTG", c.NewestMtgUtc, nowUtc, delayedAfter: MtgDelayed) : "MTG: not set up (Settings)",
        };
        return string.Join('\n', lines);
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
