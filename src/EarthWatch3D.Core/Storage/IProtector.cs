namespace EarthWatch3D.Core.Storage;

/// <summary>Encrypts small secrets for storage in the settings file.</summary>
public interface IProtector
{
    /// <summary>Returns the encrypted text as base64.</summary>
    string Protect(string plain);

    /// <summary>Returns the original text, or null if the blob cannot be decrypted (for example another Windows user's).</summary>
    string? Unprotect(string protectedBase64);
}
