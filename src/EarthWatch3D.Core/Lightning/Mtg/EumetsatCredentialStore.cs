using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.Core.Lightning.Mtg;

/// <summary>
/// The user's EUMETSAT consumer key and secret, kept in the settings file only in encrypted form. A pair that cannot
/// be decrypted (another Windows user's, or damaged) counts as not set.
/// </summary>
public sealed class EumetsatCredentialStore(ISettingsStore store, IProtector protector)
{
    private readonly object _gate = new();
    // The decrypted pair, kept for as long as the encrypted values in the settings are the ones it came from.
    private string? _cachedKey, _cachedSecret;
    private EumetsatCredentials? _cached;

    /// <summary>The saved credentials, or null when none are set or they cannot be decrypted. Safe from any thread.</summary>
    public EumetsatCredentials? Current
    {
        get
        {
            lock (_gate)
            {
                var s = store.Load();
                if (s.EumetsatKeyProtected is not { } k || s.EumetsatSecretProtected is not { } sec) return null;
                if (k == _cachedKey && sec == _cachedSecret) return _cached;
                _cachedKey = k;
                _cachedSecret = sec;
                _cached = protector.Unprotect(k) is { Length: > 0 } key && protector.Unprotect(sec) is { Length: > 0 } secret
                    ? new EumetsatCredentials(key, secret) : null;
                return _cached;
            }
        }
    }

    /// <summary>Saves the credentials encrypted; null clears both.</summary>
    public void Set(EumetsatCredentials? c)
    {
        lock (_gate)
        {
            store.Save(store.Load() with
            {
                EumetsatKeyProtected = c is null ? null : protector.Protect(c.Key),
                EumetsatSecretProtected = c is null ? null : protector.Protect(c.Secret),
            });
        }
    }
}
