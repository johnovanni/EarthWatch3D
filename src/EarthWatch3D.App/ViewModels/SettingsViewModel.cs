using System.IO;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EarthWatch3D.Core.Geo;
using EarthWatch3D.Core.Lightning.Mtg;
using EarthWatch3D.Core.Storage;

namespace EarthWatch3D.App.ViewModels;

/// <summary>
/// The Settings dialog: the user's own EUMETSAT consumer key and secret, an optional daily download limit and the
/// distance units. The secret is never shown again once saved; leaving its box empty keeps the saved one.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public const string RegistrationUrl = "https://api.eumetsat.int/api-key/";

    private const string BothOrNeither = "Enter both the consumer key and the consumer secret, or neither.";
    private const string NoSpaces = "The consumer key and secret cannot contain spaces.";
    private const string BadLimit = "Daily limit: a whole number of MB from 10 to 100,000, or empty for no limit.";
    private const int MinLimitMb = 10, MaxLimitMb = 100_000;

    private readonly ISettingsStore _store;
    private readonly EumetsatCredentialStore _creds;
    private readonly Func<EumetsatCredentials, CancellationToken, Task<(EumetsatTestResult, string?)>> _test;
    private readonly Action<AppSettings> _applied;
    private string? _savedSecret;

    public SettingsViewModel(ISettingsStore store, EumetsatCredentialStore creds,
        Func<EumetsatCredentials, CancellationToken, Task<(EumetsatTestResult, string?)>> test, Action<AppSettings> applied)
    {
        _store = store;
        _creds = creds;
        _test = test;
        _applied = applied;
        var current = creds.Current;
        _savedSecret = current?.Secret;
        _consumerKey = current?.Key ?? "";
        var s = store.Load();
        _dailyLimitText = s.DailyLimitMb?.ToString(CultureInfo.InvariantCulture) ?? "";
        _units = s.Units;
    }

    [ObservableProperty] private string _consumerKey;

    /// <summary>What was typed in the secret box; empty keeps the saved secret. Never pre-filled.</summary>
    [ObservableProperty] private string _consumerSecret = "";

    [ObservableProperty] private string _dailyLimitText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseMiles))]
    private DistanceUnits _units;

    [ObservableProperty] private string _testStatus = "";
    [ObservableProperty] private string? _error;

    /// <summary>True when a secret is already saved, so an empty secret box means "keep it".</summary>
    public bool HasSavedSecret => _savedSecret is not null;

    public bool UseMiles
    {
        get => Units == DistanceUnits.Miles;
        set => Units = value ? DistanceUnits.Miles : DistanceUnits.Kilometres;
    }

    partial void OnConsumerKeyChanged(string value) => TestStatus = "";
    partial void OnConsumerSecretChanged(string value) => TestStatus = "";

    [RelayCommand]
    private async Task TestAsync(CancellationToken ct)
    {
        string key = ConsumerKey, secret = EffectiveSecret();
        if (key.Length == 0 || secret.Length == 0)
        {
            TestStatus = "Enter the consumer key and the consumer secret first.";
            return;
        }
        if (HasWhitespace(key) || HasWhitespace(secret))
        {
            TestStatus = NoSpaces;
            return;
        }
        TestStatus = "Testing…";
        try
        {
            var (result, message) = await _test(new EumetsatCredentials(key, secret), ct);
            TestStatus = result switch
            {
                EumetsatTestResult.Accepted => "Key accepted.",
                EumetsatTestResult.Rejected => "Key rejected. Check the key and secret.",
                _ => "Could not reach EUMETSAT: " + message,
            };
        }
        catch (OperationCanceledException)
        {
            TestStatus = "";
        }
        catch (Exception ex)
        {
            TestStatus = "Could not reach EUMETSAT: " + ex.Message;
        }
    }

    /// <summary>
    /// Validates and saves. Returns false with <see cref="Error"/> set when something is invalid or the settings
    /// could not be written; nothing is saved or applied then.
    /// </summary>
    public bool Save()
    {
        Error = null;
        string key = ConsumerKey, secret = EffectiveSecret();
        EumetsatCredentials? credentials = null;
        if (key.Length == 0 && ConsumerSecret.Length == 0)
        {
            // Both emptied: clears the MTG key and secret.
        }
        else if (key.Length == 0 || secret.Length == 0)
        {
            Error = BothOrNeither;
            return false;
        }
        else if (HasWhitespace(key) || HasWhitespace(secret))
        {
            Error = NoSpaces;
            return false;
        }
        else
        {
            credentials = new EumetsatCredentials(key, secret);
        }

        int? limit = null;
        string limitText = DailyLimitText.Trim();
        if (limitText.Length > 0)
        {
            if (!int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out int mb) || mb < MinLimitMb || mb > MaxLimitMb)
            {
                Error = BadLimit;
                return false;
            }
            limit = mb;
        }

        try
        {
            _creds.Set(credentials);
            var settings = _store.Load() with { DailyLimitMb = limit, Units = Units };
            _store.Save(settings);
            _savedSecret = credentials?.Secret;
            OnPropertyChanged(nameof(HasSavedSecret));
            _applied(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = "Could not save the settings.";
            return false;
        }
        return true;
    }

    // What is typed, or else the saved secret: an empty secret box keeps the saved one.
    private string EffectiveSecret() => ConsumerSecret.Length > 0 ? ConsumerSecret : _savedSecret ?? "";

    private static bool HasWhitespace(string s) => s.Any(char.IsWhiteSpace);
}
