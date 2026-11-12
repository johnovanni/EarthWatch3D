using EarthWatch3D.Core.Geo;

namespace EarthWatch3D.Core.Events;

public sealed record DisplayFilter(bool ShowQuakes, bool ShowCyclones, bool ShowAlerts, bool ShowEonet,
    double MinMagnitude /* double.NegativeInfinity = All */, TimeSpan QuakeWindow, AlertTypes AlertTypes)
{
    public static readonly DisplayFilter Default = new(true, true, true, true, 2.5, TimeSpan.FromHours(24), AlertTypes.Warnings);
}

public sealed record VisibleQuake(Earthquake Quake, double AgeFraction);   // 0 = now, 1 = end of window
public sealed record VisibleEonet(EonetStorm Storm, StormFix Fix);
public sealed record VisibleEvents(IReadOnlyList<VisibleQuake> Quakes, IReadOnlyList<CycloneAdvisory> Cyclones,
    IReadOnlyList<WeatherAlert> Alerts, IReadOnlyList<VisibleEonet> Eonet)
{
    public static readonly VisibleEvents None = new([], [], [], []);
}

public static class EventVisibility
{
    private static readonly TimeSpan CycloneWindow = TimeSpan.FromHours(24);

    public static VisibleEvents At(EventSnapshot s, DateTime t, DisplayFilter f)
    {
        var quakes = new List<VisibleQuake>();
        if (f.ShowQuakes)
            foreach (var q in s.Earthquakes)
            {
                var age = t - q.TimeUtc;
                if (age < TimeSpan.Zero || age > f.QuakeWindow || q.Magnitude < f.MinMagnitude) continue;
                var frac = f.QuakeWindow > TimeSpan.Zero ? age / f.QuakeWindow : 0;
                quakes.Add(new VisibleQuake(q, frac));
            }

        // Cyclones are only needed when shown, or when EONET storms must be de-duplicated against them.
        IReadOnlyList<CycloneAdvisory> merged = [];
        if (f.ShowCyclones)
        {
            var latest = s.Cyclones
                .Where(a => a.TimeUtc <= t && t - a.TimeUtc <= CycloneWindow)
                .GroupBy(a => a.Id)
                .Select(g => g.MaxBy(a => a.TimeUtc)!)
                .ToList();
            merged = CycloneMerger.Merge(latest);
        }

        var alerts = new List<WeatherAlert>();
        if (f.ShowAlerts)
            foreach (var a in s.Alerts)
            {
                var end = a.CancelledUtc is { } c && c < a.EndsUtc ? c : a.EndsUtc;
                if (a.OnsetUtc <= t && t < end && (AlertTypeClassifier.Classify(a.EventType) & f.AlertTypes) != 0)
                    alerts.Add(a);
            }

        var eonet = new List<VisibleEonet>();
        if (f.ShowEonet)
            foreach (var e in s.Eonet)
            {
                if (e.ClosedUtc is { } closed && closed < t) continue;
                if (FixAt(e, t) is not { } fix) continue;
                if (merged.Any(c => GeoMath.DistanceKm(c.Location, fix.Position) <= CycloneMerger.SameStormKm)) continue;
                eonet.Add(new VisibleEonet(e, fix));
            }

        return new VisibleEvents(quakes, merged, alerts, eonet);
    }

    /// <summary>The storm's latest fix at or before <paramref name="t"/>, or null if it has none yet.</summary>
    public static StormFix? FixAt(EonetStorm e, DateTime t)
    {
        StormFix? fix = null;
        foreach (var x in e.Fixes)
            if (x.TimeUtc <= t && (fix is null || x.TimeUtc > fix.TimeUtc)) fix = x;
        return fix;
    }
}
