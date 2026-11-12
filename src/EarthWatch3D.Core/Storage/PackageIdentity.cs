using System.Runtime.InteropServices;

namespace EarthWatch3D.Core.Storage;

/// <summary>Identity of the MSIX package the app is running from, if any.</summary>
public static class PackageIdentity
{
    private const int ErrorInsufficientBuffer = 122;
    private const int AppModelErrorNoPackage = 15700;

    private static readonly Lazy<string?> Family = new(Query);

    /// <summary>The package family name, or null when not running from a package (or not on Windows).</summary>
    public static string? FamilyName => Family.Value;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, char[]? packageFamilyName);

    private static string? Query()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            uint length = 0;
            var rc = GetCurrentPackageFamilyName(ref length, null);
            if (rc == AppModelErrorNoPackage || rc != ErrorInsufficientBuffer) return null;
            var buffer = new char[length];
            rc = GetCurrentPackageFamilyName(ref length, buffer);
            return rc == 0 && length > 1 ? new string(buffer, 0, (int)length - 1) : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }
}
