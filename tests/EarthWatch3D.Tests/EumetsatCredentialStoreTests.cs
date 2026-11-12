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
