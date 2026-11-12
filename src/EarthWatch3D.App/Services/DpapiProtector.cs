using System.Security.Cryptography;
using System.Text;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.App.Services;

/// <summary>Windows DPAPI for the current user, with an app-specific entropy, so the blob is useless on another account.</summary>
public sealed class DpapiProtector : IProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("EarthWatch3D.EUMETSAT");

    public string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));

    public string? Unprotect(string protectedBase64)
    {
        try
        {
            var blob = Convert.FromBase64String(protectedBase64);
            if (blob.Length == 0) return null;
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
