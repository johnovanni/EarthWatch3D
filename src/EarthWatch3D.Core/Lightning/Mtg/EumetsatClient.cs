using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Core.Lightning.Mtg;

/// <summary>
/// EUMETSAT sign-in (OAuth 2 client credentials) with a cached token. The key and secret go only into the
/// Authorization header of the token request, never into a URL, log line or message.
/// SearchAsync and DownloadAsync come with the MTG feed.
/// </summary>
public sealed class EumetsatClient(FeedHttp http, IClock clock)
{
    private static readonly Uri TokenUri = new("https://api.eumetsat.int/token");
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private EumetsatCredentials? _cachedFor;
    private string? _token;
    private DateTime _renewAtUtc;

    public async Task<string> GetTokenAsync(EumetsatCredentials c, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_token is not null && c.Equals(_cachedFor) && clock.UtcNow < _renewAtUtc) return _token;
            _token = null;
            _cachedFor = null;

            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{c.Key}:{c.Secret}"));
            string body;
            try
            {
                body = await http.PostFormAsync(TokenUri,
                    new Dictionary<string, string> { ["grant_type"] = "client_credentials" },
                    new AuthenticationHeaderValue("Basic", basic), ct).ConfigureAwait(false);
            }
            catch (FeedAuthException)
            {
                throw new EumetsatKeyRejectedException();
            }

            var (token, expiresIn) = ParseToken(body);
            _token = token;
            _cachedFor = c;
            _renewAtUtc = clock.UtcNow.AddSeconds(expiresIn) - Margin;
            return token;
        }
        finally { _gate.Release(); }
    }

    public async Task<(EumetsatTestResult Result, string? Message)> TestAsync(EumetsatCredentials c, CancellationToken ct)
    {
        try
        {
            await GetTokenAsync(c, ct).ConfigureAwait(false);
            return (EumetsatTestResult.Accepted, null);
        }
        catch (EumetsatKeyRejectedException ex)
        {
            return (EumetsatTestResult.Rejected, ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            return (EumetsatTestResult.Unreachable, ex.Message);
        }
    }

    private static (string Token, double ExpiresIn) ParseToken(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var token = root.GetProperty("access_token").GetString();
            var expiresIn = root.GetProperty("expires_in").GetDouble();
            if (string.IsNullOrEmpty(token)) throw new FormatException("empty access_token");
            return (token, expiresIn);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new HttpRequestException("EUMETSAT sign-in returned an unexpected response");
        }
    }
}
