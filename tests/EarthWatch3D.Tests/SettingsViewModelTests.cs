using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Lightning.Mtg;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.Tests;

public class SettingsViewModelTests
{
    private const string BothOrNeither = "Enter both the consumer key and the consumer secret, or neither.";
    private const string BadLimit = "Daily limit: a whole number of MB from 10 to 100,000, or empty for no limit.";

    private static Task<(EumetsatTestResult, string?)> Result(EumetsatTestResult r, string? m = null) =>
        Task.FromResult<(EumetsatTestResult, string?)>((r, m));

    private sealed class Rig
    {
        public FakeSettingsStore Store { get; }
        public EumetsatCredentialStore Creds { get; }
        public List<AppSettings> Applied { get; } = [];
        public List<EumetsatCredentials> Tested { get; } = [];
        public SettingsViewModel Vm { get; }

        public Rig(AppSettings? initial = null, EumetsatCredentials? saved = null, Func<EumetsatTestResult>? result = null, string? message = null)
        {
            Store = new FakeSettingsStore(initial);
            Creds = new EumetsatCredentialStore(Store, new FakeProtector());
            if (saved is not null) Creds.Set(saved);
            Vm = new SettingsViewModel(Store, Creds, (c, _) =>
            {
                Tested.Add(c);
                return Result(result?.Invoke() ?? EumetsatTestResult.Accepted, message);
            }, Applied.Add);
        }
    }

    [Fact]
    public void RejectsOneOfKeyOrSecretOrWhitespace()
    {
        var r = new Rig();
        r.Vm.ConsumerKey = "k";
        Assert.False(r.Vm.Save());
        Assert.Equal(BothOrNeither, r.Vm.Error);

        r.Vm.ConsumerKey = "";
        r.Vm.ConsumerSecret = "s";
        Assert.False(r.Vm.Save());
        Assert.Equal(BothOrNeither, r.Vm.Error);

        r.Vm.ConsumerKey = "a b";
        r.Vm.ConsumerSecret = "s";
        Assert.False(r.Vm.Save());
        Assert.NotNull(r.Vm.Error);
        Assert.Equal(0, r.Store.SaveCount);
        Assert.Empty(r.Applied);
    }

    [Theory, InlineData("5"), InlineData("abc"), InlineData("200000"), InlineData("9"), InlineData("100001"), InlineData("1.5")]
    public void RejectsBadLimit(string text)
    {
        var r = new Rig();
        r.Vm.DailyLimitText = text;
        Assert.False(r.Vm.Save());
        Assert.Equal(BadLimit, r.Vm.Error);
    }

    [Theory, InlineData("10", 10), InlineData("100000", 100000), InlineData(" 250 ", 250)]
    public void AcceptsLimitsInRange(string text, int expected)
    {
        var r = new Rig();
        r.Vm.DailyLimitText = text;
        Assert.True(r.Vm.Save());
        Assert.Equal(expected, r.Store.Load().DailyLimitMb);
    }

    [Fact]
    public void EmptyLimitIsSavedAsNull()
    {
        var r = new Rig(new AppSettings { DailyLimitMb = 500 });
        Assert.Equal("500", r.Vm.DailyLimitText);
        r.Vm.DailyLimitText = "";
        Assert.True(r.Vm.Save());
        Assert.Null(r.Store.Load().DailyLimitMb);
        Assert.Null(r.Vm.Error);
    }

    [Fact]
    public void KeyIsStoredEncrypted()
    {
        var r = new Rig();
        r.Vm.ConsumerKey = "k1";
        r.Vm.ConsumerSecret = "s1";
        Assert.True(r.Vm.Save());

        var saved = r.Store.Load();
        Assert.Equal("P:k1", saved.EumetsatKeyProtected);
        Assert.Equal("P:s1", saved.EumetsatSecretProtected);
        Assert.Equal(new EumetsatCredentials("k1", "s1"), r.Creds.Current);
        foreach (var p in typeof(AppSettings).GetProperties())
        {
            if (p.Name is nameof(AppSettings.EumetsatKeyProtected) or nameof(AppSettings.EumetsatSecretProtected)) continue;
            var text = p.GetValue(saved)?.ToString() ?? "";
            Assert.DoesNotContain("k1", text);
            Assert.DoesNotContain("s1", text);
        }
    }

    [Fact]
    public void ClearingBothClearsTheSavedKey()
    {
        var r = new Rig(saved: new EumetsatCredentials("k1", "s1"));
        Assert.Equal("k1", r.Vm.ConsumerKey);
        r.Vm.ConsumerKey = "";
        r.Vm.ConsumerSecret = "";
        Assert.True(r.Vm.Save());
        Assert.Null(r.Creds.Current);
        Assert.Null(r.Store.Load().EumetsatKeyProtected);
    }

    [Fact]
    public void OpeningPrefillsTheKeyButNotTheSecret()
    {
        var r = new Rig(saved: new EumetsatCredentials("k1", "s1"));
        Assert.Equal("k1", r.Vm.ConsumerKey);
        Assert.Equal("", r.Vm.ConsumerSecret);
        Assert.True(r.Vm.HasSavedSecret);
        Assert.False(new Rig().Vm.HasSavedSecret);
    }

    [Fact]
    public void EmptySecretKeepsTheSavedSecret()
    {
        var r = new Rig(saved: new EumetsatCredentials("k1", "s1"));

        r.Vm.ConsumerKey = "k2";   // new key, secret box left empty
        Assert.True(r.Vm.Save());
        Assert.Equal(new EumetsatCredentials("k2", "s1"), r.Creds.Current);

        r.Vm.ConsumerSecret = "s3";
        Assert.True(r.Vm.Save());
        Assert.Equal(new EumetsatCredentials("k2", "s3"), r.Creds.Current);
    }

    [Fact]
    public void EmptySecretWithNoSavedSecretIsRejected()
    {
        var r = new Rig();
        r.Vm.ConsumerKey = "k";
        Assert.False(r.Vm.Save());
        Assert.Equal(BothOrNeither, r.Vm.Error);
    }

    [Theory]
    [InlineData(EumetsatTestResult.Accepted, null, "Key accepted.")]
    [InlineData(EumetsatTestResult.Rejected, null, "Key rejected. Check the key and secret.")]
    [InlineData(EumetsatTestResult.Unreachable, "timeout", "Could not reach EUMETSAT: timeout")]
    public async Task TestShowsTheResult(EumetsatTestResult result, string? message, string expected)
    {
        var r = new Rig(result: () => result, message: message);
        r.Vm.ConsumerKey = "k1";
        r.Vm.ConsumerSecret = "s1";
        await r.Vm.TestCommand.ExecuteAsync(null);
        Assert.Equal(expected, r.Vm.TestStatus);
        Assert.Equal(new EumetsatCredentials("k1", "s1"), Assert.Single(r.Tested));
        Assert.Equal(0, r.Store.SaveCount);   // testing doesn't save
    }

    [Fact]
    public async Task TestWithoutBothValuesAsksForThem()
    {
        var r = new Rig();
        r.Vm.ConsumerKey = "k1";
        await r.Vm.TestCommand.ExecuteAsync(null);
        Assert.Empty(r.Tested);
        Assert.Equal("Enter the consumer key and the consumer secret first.", r.Vm.TestStatus);
    }

    [Fact]
    public async Task TestUsesTheSavedSecretWhenTheBoxIsEmpty()
    {
        var r = new Rig(saved: new EumetsatCredentials("k1", "s1"));
        await r.Vm.TestCommand.ExecuteAsync(null);
        Assert.Equal(new EumetsatCredentials("k1", "s1"), Assert.Single(r.Tested));
    }

    [Fact]
    public void UnitsAndLimitApplyOnSave()
    {
        var r = new Rig();
        r.Vm.Units = DistanceUnits.Miles;
        r.Vm.DailyLimitText = "500";
        Assert.True(r.Vm.Save());
        var applied = Assert.Single(r.Applied);
        Assert.Equal(DistanceUnits.Miles, applied.Units);
        Assert.Equal(500, applied.DailyLimitMb);
        Assert.Equal(DistanceUnits.Miles, r.Store.Load().Units);
        Assert.Equal(500, r.Store.Load().DailyLimitMb);
    }

    private sealed class FailingProtector : IProtector
    {
        public string Protect(string plain) => throw new System.Security.Cryptography.CryptographicException("DPAPI failed");
        public string? Unprotect(string protectedBase64) => null;
    }

    [Fact]
    public void EncryptionFailureIsReportedNotThrown()
    {
        var store = new FakeSettingsStore();
        var vm = new SettingsViewModel(store, new EumetsatCredentialStore(store, new FailingProtector()),
            (_, _) => Result(EumetsatTestResult.Accepted), _ => throw new InvalidOperationException("applied"))
        { ConsumerKey = "key", ConsumerSecret = "secret", DailyLimitText = "500" };
        Assert.False(vm.Save());
        Assert.Equal("Could not save the settings: Windows could not encrypt the key.", vm.Error);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public void InvalidSavedLimitShowsAsNoLimit()
    {
        Assert.Equal("", new Rig(new AppSettings { DailyLimitMb = 0 }).Vm.DailyLimitText);
        Assert.Equal("500", new Rig(new AppSettings { DailyLimitMb = 500 }).Vm.DailyLimitText);
    }

    [Fact]
    public void RegistrationUrlIsEumetsats() =>
        Assert.Equal("https://api.eumetsat.int/api-key/", SettingsViewModel.RegistrationUrl);
}
