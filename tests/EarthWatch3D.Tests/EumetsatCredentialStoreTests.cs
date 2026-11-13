using EarthWatch3D.Core.Lightning.Mtg;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.Tests;

internal sealed class FakeProtector : IProtector
{
    public bool CanDecrypt { get; set; } = true;
    public string Protect(string plain) => "P:" + plain;
    public string? Unprotect(string protectedBase64) =>
        CanDecrypt && protectedBase64.StartsWith("P:", StringComparison.Ordinal) ? protectedBase64[2..] : null;
}

public class EumetsatCredentialStoreTests
{
    private sealed class CountingProtector : IProtector
    {
        public int Unprotects;
        public string Protect(string plain) => "P:" + plain;
        public string? Unprotect(string protectedBase64) { Unprotects++; return protectedBase64[2..]; }
    }

    [Fact]
    public void TheDecryptedPairIsKeptUntilTheSavedValuesChange()
    {
        var settings = new FakeSettingsStore();
        var protector = new CountingProtector();
        var creds = new EumetsatCredentialStore(settings, protector);
        creds.Set(new EumetsatCredentials("k1", "s1"));
        for (int i = 0; i < 5; i++) Assert.Equal(new EumetsatCredentials("k1", "s1"), creds.Current);
        Assert.Equal(2, protector.Unprotects);   // the key and the secret, once
        creds.Set(new EumetsatCredentials("k2", "s2"));
        Assert.Equal(new EumetsatCredentials("k2", "s2"), creds.Current);
        Assert.Equal(4, protector.Unprotects);
        creds.Set(null);
        Assert.Null(creds.Current);
    }

    [Fact]
    public void UndecryptableIsTreatedAsNotSet()
    {
        var settings = new FakeSettingsStore(new AppSettings { EumetsatKeyProtected = "P:k", EumetsatSecretProtected = "P:s" });
        var creds = new EumetsatCredentialStore(settings, new FakeProtector { CanDecrypt = false });
        Assert.Null(creds.Current);
    }

    [Fact]
    public void SetStoresProtectedValuesAndNullClearsBoth()
    {
        var settings = new FakeSettingsStore();
        var creds = new EumetsatCredentialStore(settings, new FakeProtector());
        Assert.Null(creds.Current);

        creds.Set(new EumetsatCredentials("k1", "s1"));
        Assert.Equal("P:k1", settings.Load().EumetsatKeyProtected);
        Assert.Equal("P:s1", settings.Load().EumetsatSecretProtected);
        Assert.Equal(new EumetsatCredentials("k1", "s1"), creds.Current);

        creds.Set(null);
        Assert.Null(settings.Load().EumetsatKeyProtected);
        Assert.Null(settings.Load().EumetsatSecretProtected);
        Assert.Null(creds.Current);
    }

    [Fact]
    public void SetKeepsOtherSettings()
    {
        var settings = new FakeSettingsStore(new AppSettings { DailyLimitMb = 300, ShowPlates = true });
        new EumetsatCredentialStore(settings, new FakeProtector()).Set(new EumetsatCredentials("k", "s"));
        Assert.Equal(300, settings.Load().DailyLimitMb);
        Assert.True(settings.Load().ShowPlates);
    }
}
