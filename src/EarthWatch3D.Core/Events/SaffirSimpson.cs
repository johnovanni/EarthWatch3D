namespace EarthWatch3D.Core.Events;

public static class SaffirSimpson
{
    /// <summary>-1 = tropical depression (or unknown), 0 = tropical storm, 1..5 = hurricane category.</summary>
    public static int Category(double? windKt) => windKt switch
    {
        null => -1,
        < 34 => -1,
        < 64 => 0,
        < 83 => 1,
        < 96 => 2,
        < 113 => 3,
        < 137 => 4,
        _ => 5,
    };
}
