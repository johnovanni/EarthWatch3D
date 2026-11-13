using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Core.Lightning.Mtg;

/// <summary>
/// EUMETSAT sign-in (OAuth 2 client credentials) with a cached token. The key and secret go only into the
/// Authorization header of the token request, never into a URL, log line or message. SearchAsync and DownloadAsync call the
/// Data Store with that token (docs/data-sources.md section 8).
/// </summary>
public sealed class EumetsatClient(FeedHttp http, IClock clock, IAppLog? log = null)
{
    private static readonly Uri TokenUri = new("https://api.eumetsat.int/token");
    private const string SearchBase = "https://api.eumetsat.int/data/search-products/1.0.0/os";
    private const string DownloadHost = "api.eumetsat.int";
    private const string ProductType = "EO:EUM:DAT:0691";   // MTG LI-2-LFL, lightning flashes
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _warnedIds = [];   // search results already reported as unreadable (one line each)
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

    /// <summary>
    /// The newest products (at most 20, newest first) whose sensing period ends at or after <paramref name="sinceUtc"/>.
    /// </summary>
    /// <exception cref="EumetsatKeyRejectedException">The token request was refused.</exception>
    /// <exception cref="HttpRequestException">The service is unreachable, refused the request twice or answered something unexpected.</exception>
    public async Task<IReadOnlyList<MtgProduct>> SearchAsync(EumetsatCredentials c, DateTime sinceUtc, CancellationToken ct)
    {
        var since = sinceUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var uri = new Uri($"{SearchBase}?format=json&pi={Uri.EscapeDataString(ProductType)}&dtstart={Uri.EscapeDataString(since)}" +
                          "&sort=start,time,0&c=20&si=0");
        var bytes = await WithTokenAsync(c, auth => http.GetBytesAsync(uri, ct, auth), ct).ConfigureAwait(false);
        return ParseProducts(Encoding.UTF8.GetString(bytes));
    }

    /// <summary>The product's zip file.</summary>
    /// <exception cref="EumetsatKeyRejectedException">The token request was refused.</exception>
    /// <exception cref="FormatException">The link is not on EUMETSAT's https address.</exception>
    public Task<byte[]> DownloadAsync(EumetsatCredentials c, MtgProduct p, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(p);
        // The token goes only to EUMETSAT's own https address, whatever link the search answered with.
        if (p.Download.Scheme != Uri.UriSchemeHttps || !string.Equals(p.Download.Host, DownloadHost, StringComparison.OrdinalIgnoreCase))
            throw new FormatException("The product's download link is not an https://" + DownloadHost + " address.");
        return WithTokenAsync(c, auth => http.GetBytesAsync(p.Download, ct, auth), ct);
    }

    /// <summary>Runs the request with a bearer token; a 401/403 gets one retry with a fresh token, a second one means the key is rejected.</summary>
    private async Task<byte[]> WithTokenAsync(EumetsatCredentials c, Func<AuthenticationHeaderValue, Task<byte[]>> send, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await GetTokenAsync(c, ct).ConfigureAwait(false);
            try
            {
                return await send(new AuthenticationHeaderValue("Bearer", token)).ConfigureAwait(false);
            }
            catch (FeedAuthException e)
            {
                // Only the token endpoint says the key is wrong. A data request refused with a token it has just issued
                // is a problem with that request.
                if (attempt >= 1) throw new HttpRequestException($"EUMETSAT refused the request (HTTP {(int)(e.StatusCode ?? 0)})", null, e.StatusCode);
                await ForgetTokenAsync(token, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task ForgetTokenAsync(string token, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { if (_token == token) { _token = null; _cachedFor = null; } }
        finally { _gate.Release(); }
    }

    private IReadOnlyList<MtgProduct> ParseProducts(string body)
    {
        JsonElement features;
        try
        {
            using var doc = JsonDocument.Parse(body);
            features = doc.RootElement.GetProperty("features").Clone();
            if (features.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("features is not an array");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new HttpRequestException("EUMETSAT search returned an unexpected response");
        }

        var list = new List<MtgProduct>();
        var index = 0;
        foreach (var f in features.EnumerateArray())
        {
            var label = "#" + index++;
            try
            {
                if (f.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String) label = idProp.GetString()!;
                var props = f.GetProperty("properties");
                var period = props.GetProperty("date").GetString()!.Split('/');
                if (period.Length != 2) throw new FormatException("bad period");
                var href = props.GetProperty("links").GetProperty("data")[0].GetProperty("href").GetString()!;
                list.Add(new MtgProduct(f.GetProperty("id").GetString()!, ParseUtc(period[0]), ParseUtc(period[1]), new Uri(href)));
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException
                                           or ArgumentOutOfRangeException or NullReferenceException or UriFormatException
                                           or IndexOutOfRangeException)
            {
                // One unreadable result is skipped, once; the others still count.
                if (_warnedIds.Add(label)) log?.Warn($"EUMETSAT search: skipped an unreadable result ({label}): {ex.Message}");
            }
        }
        return list;
    }

    private static DateTime ParseUtc(string s) =>
        DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

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
