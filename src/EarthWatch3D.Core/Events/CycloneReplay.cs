using AstroCore.Earth;
using GlobeCore.Geo;

namespace EarthWatch3D.Core.Events;

/// <summary>
/// Where a tropical cyclone was at a past display time, worked out from its observed fixes only: advisory positions and
/// the timed history (NHC best track, GDACS track points). Never extrapolated, and never from the forecast track.
/// </summary>
public static class CycloneReplay
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <param name="storm">All stored advisories of one storm (one <see cref="EarthEvent.Id"/>).</param>
    /// <returns>The advisory to draw at <paramref name="t"/>, or null when the storm is not shown then.</returns>
    public static CycloneAdvisory? At(IReadOnlyList<CycloneAdvisory> storm, DateTime t)
    {
        if (storm.Count == 0) return null;
        var newest = storm.MaxBy(a => a.TimeUtc)!;
        var latestAdv = storm.Where(a => a.TimeUtc <= t).MaxBy(a => a.TimeUtc);

        // At or after the newest advisory the storm draws exactly as it always did.
        if (latestAdv is not null && latestAdv.TimeUtc == newest.TimeUtc)
            return t - latestAdv.TimeUtc <= Window ? latestAdv : null;

        // Known fixes, oldest advisory first so that for a duplicate time the newest advisory's value stays. A GDACS
        // advisory's own position applies at the episode's todate, which its History already ends with, so GDACS
        // advisories with a History add no fix at their (later) TimeUtc.
        var byTime = new SortedDictionary<DateTime, (StormFix Fix, bool FromAdvisory)>();
        foreach (var a in storm.OrderBy(a => a.TimeUtc))
        {
            foreach (var h in a.History) byTime[h.TimeUtc] = (h, false);
            if (!(a.Source == EventSource.Gdacs && a.History.Count > 0))
                byTime[a.TimeUtc] = (new StormFix(a.TimeUtc, a.Location, a.WindKt), true);
        }
        var fixes = byTime.Values.Select(v => v.Fix).ToList();

        var e = fixes.LastOrDefault(f => f.TimeUtc <= t);
        if (e is null || t - e.TimeUtc > Window) return null;
        var l = fixes.FirstOrDefault(f => f.TimeUtc > t);   // null only when a GDACS advisory is stored later than its todate fix

        var position = l is null ? e.Position : Slerp(e.Position, l.Position, (t - e.TimeUtc) / (l.TimeUtc - e.TimeUtc));

        double? wind = e.WindKt is { } we && l?.WindKt is { } wl
            ? we + (wl - we) * ((t - e.TimeUtc) / (l.TimeUtc - e.TimeUtc))
            : latestAdv?.WindKt;
        var known = wind is not null;

        var past = fixes.Where(f => f.TimeUtc <= t).Select(f => f.Position).ToList();
        if (past.Count == 0 || past[^1] != position) past.Add(position);
        var pastTrack = DateLine.SplitLine(past.Select(p => (p.LongitudeDeg, p.LatitudeDeg)).ToList())
            .Where(line => line.Points.Any(q => q != line.Points[0])).ToList();

        // Before the first stored advisory there is no pressure, and the title must not borrow a later classification.
        var basis = latestAdv ?? storm.MinBy(a => a.TimeUtc)!;
        var title = latestAdv is not null ? basis.Title
            : ("Tropical cyclone " + CycloneMerger.NormalizeName(basis.Name)).Trim();
        return new CycloneAdvisory(basis.Source, basis.Id, e.TimeUtc, position, title, basis.Link, basis.Name, wind,
            latestAdv?.PressureHpa, pastTrack,
            latestAdv?.ForecastTrack ?? [], latestAdv?.Cone ?? [], latestAdv?.WindAreas ?? [])
        {
            History = fixes.Where(f => f.TimeUtc <= t).ToList(),
            Replay = new ReplayInfo(e.TimeUtc, l?.TimeUtc, SourceLabel(basis.Source, byTime[e.TimeUtc].FromAdvisory, byTime[(l ?? e).TimeUtc].FromAdvisory), known),
        };
    }

    /// <summary>Where the two neighbouring fixes came from, e.g. "NHC best track" or "NHC advisories and best track".</summary>
    private static string SourceLabel(EventSource source, bool earlierFromAdvisory, bool laterFromAdvisory)
    {
        if (source == EventSource.Nhc)
            return earlierFromAdvisory && laterFromAdvisory ? "NHC advisories"
                : earlierFromAdvisory || laterFromAdvisory ? "NHC advisories and best track" : "NHC best track";
        return earlierFromAdvisory && laterFromAdvisory ? "GDACS advisories" : "GDACS track";
    }

    /// <summary>Point a fraction of the way along the great circle from a to b.</summary>
    private static GeoPosition Slerp(GeoPosition a, GeoPosition b, double f)
    {
        var va = a.ToUnitVector();
        var vb = b.ToUnitVector();
        var theta = Math.Acos(Math.Clamp(AstroCore.Astronomy.Vector3d.Dot(va, vb), -1, 1));
        if (theta < 1e-9 || Math.PI - theta < 1e-9) return a;
        var s = Math.Sin(theta);
        return GeoPosition.FromVector((va * (Math.Sin((1 - f) * theta) / s)) + (vb * (Math.Sin(f * theta) / s)));
    }
}
