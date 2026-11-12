using AstroCore.Earth;
using EarthWatch3D.Core.Geo;

namespace EarthWatch3D.Core.Events;

public enum EventSource { Usgs, Gdacs, Nhc, Nws, Eonet }

public abstract record EarthEvent(EventSource Source, string Id, DateTime TimeUtc, GeoPosition Location, string Title, Uri? Link);

public sealed record Earthquake(string Id, DateTime TimeUtc, GeoPosition Location, string Title, Uri? Link,
    double Magnitude, double DepthKm, string Place, DateTime UpdatedUtc) : EarthEvent(EventSource.Usgs, Id, TimeUtc, Location, Title, Link);

public sealed record WindArea(double ThresholdKmh, GeoPolygon Area);

/// <summary>Id = storm key: "gdacs:&lt;eventid&gt;" or "nhc:&lt;id&gt;". TimeUtc = advisory issue time.</summary>
public sealed record CycloneAdvisory(EventSource Source, string Id, DateTime TimeUtc, GeoPosition Location, string Title, Uri? Link,
    string Name, double? WindKt, double? PressureHpa, IReadOnlyList<GeoLine> PastTrack, IReadOnlyList<GeoLine> ForecastTrack,
    IReadOnlyList<GeoPolygon> Cone, IReadOnlyList<WindArea> WindAreas) : EarthEvent(Source, Id, TimeUtc, Location, Title, Link)
{
    public int Category => SaffirSimpson.Category(WindKt);

    public bool Equals(CycloneAdvisory? o) => o is not null && Source == o.Source && Id == o.Id && TimeUtc == o.TimeUtc
        && Location == o.Location && Title == o.Title && Link == o.Link && Name == o.Name && WindKt == o.WindKt
        && PressureHpa == o.PressureHpa && ListEquality.Equal(PastTrack, o.PastTrack)
        && ListEquality.Equal(ForecastTrack, o.ForecastTrack) && ListEquality.Equal(Cone, o.Cone)
        && ListEquality.Equal(WindAreas, o.WindAreas);

    public override int GetHashCode() => HashCode.Combine(Source, Id, TimeUtc, Name, WindKt,
        ListEquality.Hash(PastTrack), ListEquality.Hash(ForecastTrack), ListEquality.Hash(Cone));
}

/// <summary>TimeUtc = sent. Location = centroid of Areas, or (0,0) until zones load. Areas empty when only ZoneIds are given.</summary>
public sealed record WeatherAlert(string Id, DateTime TimeUtc, GeoPosition Location, string Title, Uri? Link,
    string EventType, string Severity, DateTime OnsetUtc, DateTime EndsUtc, DateTime? CancelledUtc,
    IReadOnlyList<GeoPolygon> Areas, IReadOnlyList<string> ZoneIds, string Description, IReadOnlyList<string>? SupersedesIds = null) : EarthEvent(EventSource.Nws, Id, TimeUtc, Location, Title, Link)
{
    public bool Equals(WeatherAlert? o) => o is not null && Id == o.Id && TimeUtc == o.TimeUtc && Location == o.Location
        && Title == o.Title && Link == o.Link && EventType == o.EventType && Severity == o.Severity
        && OnsetUtc == o.OnsetUtc && EndsUtc == o.EndsUtc && CancelledUtc == o.CancelledUtc
        && ListEquality.Equal(Areas, o.Areas) && ListEquality.Equal(ZoneIds, o.ZoneIds) && Description == o.Description
        && ListEquality.Equal(SupersedesIds ?? [], o.SupersedesIds ?? []);

    public override int GetHashCode() => HashCode.Combine(Id, TimeUtc, EventType, Severity, EndsUtc, CancelledUtc,
        ListEquality.Hash(Areas), ListEquality.Hash(ZoneIds) ^ ListEquality.Hash(SupersedesIds ?? []));
}

public sealed record StormFix(DateTime TimeUtc, GeoPosition Position, double? WindKt);

/// <summary>TimeUtc = first fix; Location = last fix.</summary>
public sealed record EonetStorm(string Id, DateTime TimeUtc, GeoPosition Location, string Title, Uri? Link,
    IReadOnlyList<StormFix> Fixes, DateTime? ClosedUtc) : EarthEvent(EventSource.Eonet, Id, TimeUtc, Location, Title, Link)
{
    public bool Equals(EonetStorm? o) => o is not null && Id == o.Id && TimeUtc == o.TimeUtc && Location == o.Location
        && Title == o.Title && Link == o.Link && ClosedUtc == o.ClosedUtc && ListEquality.Equal(Fixes, o.Fixes);

    public override int GetHashCode() => HashCode.Combine(Id, TimeUtc, Title, ClosedUtc, ListEquality.Hash(Fixes));
}
