namespace EarthWatch3D.Core.Geo;

/// <summary>
/// Earth as the app measures it: a sphere of radius 6371 km. The same value as GlobeCore.Earth's
/// <c>EarthBody.RadiusKm</c>; the Earth pack is a WPF project, so this no-UI project keeps its own copy (a test pins
/// the two equal).
/// </summary>
public static class EarthSphere
{
    public const double RadiusKm = 6371.0;
}
