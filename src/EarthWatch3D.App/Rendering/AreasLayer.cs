using System.Runtime.CompilerServices;
using System.Windows.Media;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Globe;

namespace EarthWatch3D.App.Rendering;

/// <summary>What the Areas layer would draw, and a key that changes whenever that does.</summary>
public sealed record AreasPlan(int Key, IReadOnlyList<(GeoPolygon Area, Color Color)> Shapes);

/// <summary>
/// The Areas layer (alert zones, cyclone cones and wind bands): planned cheaply on the UI thread, rasterized on an
/// STA thread only when the plan's key changed.
/// </summary>
public static class AreasLayer
{
    /// <summary>
    /// The shapes to draw, cones and wind bands first and alerts on top, and their content key. Never touches the
    /// disk: <paramref name="zoneLookup"/> must answer from memory (null while a zone is not loaded), and each zone
    /// it could not answer is added to <paramref name="unresolved"/> so the caller can have it loaded.
    /// </summary>
    public static AreasPlan Plan(VisibleEvents visible, DisplayFilter filter,
                                 Func<string, IReadOnlyList<GeoPolygon>?> zoneLookup, ICollection<string>? unresolved = null)
    {
        ArgumentNullException.ThrowIfNull(visible);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(zoneLookup);
        var key = new HashCode();
        key.Add(filter.ShowAlerts); key.Add(filter.ShowCyclones);
        var shapes = new List<(GeoPolygon, Color)>();
        foreach (var c in visible.Cyclones)
        {
            key.Add(c.Id); key.Add(c.TimeUtc);
            // The same advisory can come back with new shapes (a GDACS merge, a re-read): the lists are immutable,
            // so a new list means new content.
            key.Add(RuntimeHelpers.GetHashCode(c.Cone));
            key.Add(RuntimeHelpers.GetHashCode(c.WindAreas));
            key.Add(RuntimeHelpers.GetHashCode(c.PastTrack));
            foreach (var wa in c.WindAreas) shapes.Add((wa.Area, AlertColors.ForWind(wa.ThresholdKmh)));
            foreach (var p in c.Cone) shapes.Add((p, AlertColors.Cone));
        }
        foreach (var a in visible.Alerts)   // alerts on top
        {
            key.Add(a.Id); key.Add(a.CancelledUtc);
            var color = AlertColors.For(a.EventType);
            if (a.Areas.Count > 0) { foreach (var p in a.Areas) shapes.Add((p, color)); continue; }
            int resolved = 0;
            foreach (var z in a.ZoneIds)
            {
                if (zoneLookup(z) is not { } polys) { unresolved?.Add(z); continue; }   // drawn once ZonesLoaded fires
                resolved++;
                foreach (var p in polys) shapes.Add((p, color));
            }
            key.Add(resolved);
        }
        return new AreasPlan(key.ToHashCode(), shapes);
    }

    /// <summary>The plan's shapes as a new image, or null when there is nothing to draw. Call it on an STA thread.</summary>
    public static Bgra32Image? Rasterize(AreasPlan plan, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Shapes.Count == 0 ? null : AreaRasterizer.Rasterize(plan.Shapes, width, height);
    }
}
