using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Logging;

namespace EarthWatch3D.Core.Feeds.Nhc;

/// <summary>Polls NHC CurrentStorms.json every 15 min; each storm's track and cone KMZ is fetched only when its advisory number changes.</summary>
public sealed class NhcFeed(FeedHttp http, EventStore store, IAppLog log) : IEventFeed
{
    private static readonly Uri StormsUrl = new("https://www.nhc.noaa.gov/CurrentStorms.json");
    private static readonly Uri HomeUrl = new("https://www.nhc.noaa.gov/");

    private sealed class Cache
    {
        public string? TrackAdv, ConeAdv;
        public IReadOnlyList<GeoLine> Track = [];
        public IReadOnlyList<GeoPolygon> Cone = [];
    }

    private readonly Dictionary<string, Cache> _cache = new(StringComparer.Ordinal);

    public string Name => "NHC";
    public TimeSpan Interval => TimeSpan.FromMinutes(15);

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var bytes = await http.GetBytesAsync(StormsUrl, ct).ConfigureAwait(false);
        var storms = NhcReader.ReadCurrentStorms(new MemoryStream(bytes), log);

        var advisories = new List<CycloneAdvisory>();
        foreach (var s in storms)
        {
            if (!_cache.TryGetValue(s.Id, out var c)) _cache[s.Id] = c = new Cache();
            if (s.TrackKmz is not null && c.TrackAdv != (s.TrackAdv ?? ""))
            {
                var kmz = await TryReadAsync(s, s.TrackKmz, ct).ConfigureAwait(false);
                if (kmz is not null) { c.Track = kmz.Value.Lines; c.TrackAdv = s.TrackAdv ?? ""; }
            }
            if (s.ConeKmz is not null && c.ConeAdv != (s.ConeAdv ?? ""))
            {
                var kmz = await TryReadAsync(s, s.ConeKmz, ct).ConfigureAwait(false);
                if (kmz is not null) { c.Cone = kmz.Value.Polygons; c.ConeAdv = s.ConeAdv ?? ""; }
            }
            advisories.Add(new CycloneAdvisory(EventSource.Nhc, "nhc:" + s.Id, s.LastUpdateUtc, s.Position,
                $"{s.Classification} {s.Name}", HomeUrl, s.Name, s.WindKt, s.PressureMb, [], c.Track, c.Cone, []));
        }
        // Forget storms that are no longer active.
        foreach (var id in _cache.Keys.Where(k => storms.All(s => s.Id != k)).ToList()) _cache.Remove(id);
        if (advisories.Count > 0) store.AddCycloneAdvisories(advisories);
    }

    private async Task<(IReadOnlyList<GeoLine> Lines, IReadOnlyList<GeoPolygon> Polygons)?> TryReadAsync(NhcStorm s, Uri url, CancellationToken ct)
    {
        byte[] data;
        try { data = await http.GetBytesAsync(url, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            log.Warn($"NHC: KMZ for {s.Id} failed; will retry", ex);
            return null;
        }
        try
        {
            var (lines, _, polys) = KmlReader.ReadKmz(new MemoryStream(data), log);
            return (lines, polys);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException or FormatException or ArgumentException)
        {
            log.Warn($"NHC: KMZ for {s.Id} unreadable; will retry", ex);
            return null;
        }
    }
}
