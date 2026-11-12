using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Core.Events;

public sealed record EventSnapshot(IReadOnlyList<Earthquake> Earthquakes, IReadOnlyList<CycloneAdvisory> Cyclones,
    IReadOnlyList<WeatherAlert> Alerts, IReadOnlyList<EonetStorm> Eonet, long Version)
{
    public static readonly EventSnapshot Empty = new([], [], [], [], 0);
}

/// <summary>
/// Thread-safe store. Writers serialise on one lock; readers just read <see cref="Snapshot"/>, an immutable
/// value replaced atomically. Lists inside a published snapshot are never mutated.
/// </summary>
public sealed class EventStore(IClock clock)
{
    private static readonly TimeSpan QuakeRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan CycloneRetention = TimeSpan.FromHours(24);
    private static readonly TimeSpan AlertRetention = TimeSpan.FromHours(24);
    private static readonly TimeSpan EonetRetention = TimeSpan.FromDays(7);

    private readonly object _gate = new();
    private volatile EventSnapshot _snapshot = EventSnapshot.Empty;

    public EventSnapshot Snapshot => _snapshot;

    /// <summary>Raised on the writing thread after <see cref="Snapshot"/> has been replaced.</summary>
    public event EventHandler? Changed;

    public void Load(EventSnapshot cached) => Write(s => cached with { Version = s.Version });

    public void UpsertEarthquakes(IEnumerable<Earthquake> quakes) => Write(s => WithQuakes(s, MergeQuakes(s.Earthquakes, quakes, null)));

    public void ReplaceEarthquakeWeek(IReadOnlyCollection<Earthquake> week, DateTime weekStartUtc) =>
        Write(s => WithQuakes(s, MergeQuakes(s.Earthquakes, week, weekStartUtc)));

    public void AddCycloneAdvisories(IEnumerable<CycloneAdvisory> advisories) => Write(s =>
    {
        var map = s.Cyclones.ToDictionary(a => (a.Id, a.TimeUtc));
        var changed = false;
        foreach (var a in advisories)
        {
            if (map.TryGetValue((a.Id, a.TimeUtc), out var old) && old == a) continue;
            map[(a.Id, a.TimeUtc)] = a;
            changed = true;
        }
        return changed ? s with { Cyclones = [.. map.Values] } : s;
    });

    public void UpsertAlerts(IEnumerable<WeatherAlert> alerts) => Write(s =>
    {
        var merged = MergeById(s.Alerts, alerts, a => a.Id);
        return ReferenceEquals(merged, s.Alerts) ? s : s with { Alerts = merged };
    });

    public void UpsertEonet(IEnumerable<EonetStorm> storms) => Write(s =>
    {
        var merged = MergeById(s.Eonet, storms, e => e.Id);
        return ReferenceEquals(merged, s.Eonet) ? s : s with { Eonet = merged };
    });

    /// <summary>Retention: quakes 7 d; advisories 24 h; alerts 24 h after they end or are cancelled; EONET 7 d after last fix or close. Items exactly at a limit are kept.</summary>
    public void Prune() => Write(s =>
    {
        var now = clock.UtcNow;
        var quakes = s.Earthquakes.Where(q => q.TimeUtc >= now - QuakeRetention).ToList();
        var cyclones = s.Cyclones.Where(c => c.TimeUtc >= now - CycloneRetention).ToList();
        var alerts = s.Alerts.Where(a => EndOf(a) >= now - AlertRetention).ToList();
        var eonet = s.Eonet.Where(e => LastFix(e) >= now - EonetRetention && !(e.ClosedUtc is { } c && c < now - EonetRetention)).ToList();
        if (quakes.Count == s.Earthquakes.Count && cyclones.Count == s.Cyclones.Count
            && alerts.Count == s.Alerts.Count && eonet.Count == s.Eonet.Count) return s;
        return s with { Earthquakes = quakes, Cyclones = cyclones, Alerts = alerts, Eonet = eonet };
    });

    private static EventSnapshot WithQuakes(EventSnapshot s, IReadOnlyList<Earthquake> quakes) =>
        ReferenceEquals(quakes, s.Earthquakes) ? s : s with { Earthquakes = quakes };

    private static DateTime EndOf(WeatherAlert a) =>
        a.CancelledUtc is { } c && c < a.EndsUtc ? c : a.EndsUtc;

    private static DateTime LastFix(EonetStorm e) =>
        e.Fixes.Count > 0 ? e.Fixes.Max(f => f.TimeUtc) : e.TimeUtc;

    private static IReadOnlyList<Earthquake> MergeQuakes(IReadOnlyList<Earthquake> current, IEnumerable<Earthquake> incoming, DateTime? weekStartUtc)
    {
        var map = current.ToDictionary(q => q.Id);
        var changed = false;
        HashSet<string>? keep = weekStartUtc is null ? null : [];
        foreach (var q in incoming)
        {
            keep?.Add(q.Id);
            if (map.TryGetValue(q.Id, out var old) && q.UpdatedUtc <= old.UpdatedUtc) continue;
            map[q.Id] = q;
            changed = true;
        }
        if (weekStartUtc is { } start)
        {
            foreach (var q in map.Values.Where(q => q.TimeUtc >= start && !keep!.Contains(q.Id)).ToList())
            {
                map.Remove(q.Id);
                changed = true;
            }
        }
        return changed ? [.. map.Values] : current;
    }

    private static IReadOnlyList<T> MergeById<T>(IReadOnlyList<T> current, IEnumerable<T> incoming, Func<T, string> id)
    {
        var map = current.ToDictionary(id);
        var changed = false;
        foreach (var item in incoming)
        {
            if (map.TryGetValue(id(item), out var old) && EqualityComparer<T>.Default.Equals(old, item)) continue;
            map[id(item)] = item;
            changed = true;
        }
        return changed ? [.. map.Values] : current;
    }

    private void Write(Func<EventSnapshot, EventSnapshot> update)
    {
        lock (_gate)
        {
            var before = _snapshot;
            var after = update(before);
            if (ReferenceEquals(after, before)) return;
            _snapshot = after with { Version = before.Version + 1 };
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
