using System.Net;
using System.Net.Http;
using System.Text;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Lightning.Mtg;
using Xunit;

namespace EarthWatch3D.Tests;

public class EumetsatClientTests
{
    private static readonly DateTime T0 = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Seen(Uri Uri, string? AuthScheme, string? AuthParameter, string Body);

    private sealed class ScriptedHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct);
            Requests.Add(new Seen(r.RequestUri!, r.Headers.Authorization?.Scheme, r.Headers.Authorization?.Parameter, body));
            return respond(Requests.Count);
        }
    }

    private static HttpResponseMessage Token(string token = "tok", int expiresIn = 3600) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"access_token\":\"" + token + "\",\"expires_in\":" + expiresIn + ",\"token_type\":\"Bearer\"}",
                Encoding.UTF8, "application/json")
        };

    private static (EumetsatClient Client, ScriptedHandler Handler, TestClock Clock) Make(Func<int, HttpResponseMessage> respond)
    {
        var handler = new ScriptedHandler(respond);
        var clock = new TestClock(T0);
        return (new EumetsatClient(new FeedHttp(handler), clock), handler, clock);
    }

    private static readonly EumetsatCredentials Creds = new("K-SECRET", "S-SECRET");

    [Fact]
    public async Task TokenIsCachedUntilNearExpiry()
    {
        var (client, handler, clock) = Make(_ => Token("abc", 3600));
        Assert.Equal("abc", await client.GetTokenAsync(Creds, CancellationToken.None));
        Assert.Equal("abc", await client.GetTokenAsync(Creds, CancellationToken.None));
        Assert.Single(handler.Requests);

        clock.UtcNow = T0.AddSeconds(3541);
        await client.GetTokenAsync(Creds, CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task DifferentCredentialsGetANewToken()
    {
        var (client, handler, _) = Make(n => Token("t" + n));
        Assert.Equal("t1", await client.GetTokenAsync(Creds, CancellationToken.None));
        Assert.Equal("t2", await client.GetTokenAsync(new EumetsatCredentials("K2", "S2"), CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task TestReportsAcceptedRejectedUnreachable()
    {
        var (ok, _, _) = Make(_ => Token());
        Assert.Equal(EumetsatTestResult.Accepted, (await ok.TestAsync(Creds, CancellationToken.None)).Result);

        var (bad, _, _) = Make(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        Assert.Equal(EumetsatTestResult.Rejected, (await bad.TestAsync(Creds, CancellationToken.None)).Result);

        var (down, _, _) = Make(_ => throw new HttpRequestException("name not resolved"));
        var (result, message) = await down.TestAsync(Creds, CancellationToken.None);
        Assert.Equal(EumetsatTestResult.Unreachable, result);
        Assert.Contains("name not resolved", message);
    }

    [Fact]
    public async Task RejectedKeyThrowsKeyRejected()
    {
        var (client, _, _) = Make(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var ex = await Assert.ThrowsAsync<EumetsatKeyRejectedException>(() => client.GetTokenAsync(Creds, CancellationToken.None));
        Assert.Equal("EUMETSAT key rejected. Check it in Settings.", ex.Message);
    }

    [Fact]
    public async Task MalformedTokenResponseIsAServiceProblemNotARejection()
    {
        var (client, _, _) = Make(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>") });
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetTokenAsync(Creds, CancellationToken.None));
        var (result, _) = await client.TestAsync(Creds, CancellationToken.None);
        Assert.Equal(EumetsatTestResult.Unreachable, result);
    }

    [Fact]
    public async Task CredentialsNeverAppearInLogsOrUris()
    {
        var (client, handler, _) = Make(_ => Token());
        await client.GetTokenAsync(Creds, CancellationToken.None);
        var seen = Assert.Single(handler.Requests);
        Assert.DoesNotContain("K-SECRET", seen.Uri.ToString());
        Assert.DoesNotContain("S-SECRET", seen.Uri.ToString());
        Assert.DoesNotContain("SECRET", seen.Body);
        Assert.Contains("grant_type=client_credentials", seen.Body);
        Assert.Equal("Basic", seen.AuthScheme);
        Assert.Equal("K-SECRET:S-SECRET", Encoding.UTF8.GetString(Convert.FromBase64String(seen.AuthParameter!)));

        Assert.Equal("EumetsatCredentials(***)", Creds.ToString());

        var (bad, _, _) = Make(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var (_, message) = await bad.TestAsync(Creds, CancellationToken.None);
        Assert.DoesNotContain("SECRET", message ?? "");
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string OneProduct =
        "{\"features\":[{\"id\":\"P1\",\"properties\":{\"date\":\"2026-11-13T21:30:05Z/2026-11-13T21:40:05Z\"," +
        "\"links\":{\"data\":[{\"href\":\"https://api.eumetsat.int/data/download/1.0.0/collections/EO%3AEUM%3ADAT%3A0691/products/a%2Cb%2Bc\"}]}}}]}";

    private static bool IsToken(Seen s) => s.Uri.AbsolutePath == "/token";

    [Fact]
    public async Task SearchParsesProductsAndSendsTheBearerToken()
    {
        // Request 1 is the token, request 2 the search.
        var (client, handler, _) = Make(n => n == 1 ? Token("T1") : Json(OneProduct));
        var list = await client.SearchAsync(Creds, T0.AddMinutes(-30), CancellationToken.None);
        var p = Assert.Single(list);
        Assert.Equal("P1", p.Id);
        Assert.Equal(new DateTime(2026, 11, 13, 21, 30, 5, DateTimeKind.Utc), p.SensingStartUtc);
        Assert.Equal(new DateTime(2026, 11, 13, 21, 40, 5, DateTimeKind.Utc), p.SensingEndUtc);
        Assert.Equal(DateTimeKind.Utc, p.SensingEndUtc.Kind);
        Assert.EndsWith("/products/a%2Cb%2Bc", p.Download.AbsoluteUri);
        var search = handler.Requests[1];
        Assert.Equal("Bearer", search.AuthScheme);
        Assert.Equal("T1", search.AuthParameter);
        Assert.Contains("dtstart=2026-11-12T11%3A30%3A00Z", search.Uri.Query);
    }

    [Fact]
    public async Task MalformedSearchAnswerIsAServiceProblem()
    {
        var (client, _, _) = Make(n => n == 1 ? Token() : Json("<html>"));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchAsync(Creds, T0, CancellationToken.None));
    }

    [Fact]
    public async Task ResultsWithANullIdOrLinkAreSkipped()
    {
        const string link = "{\"data\":[{\"href\":\"https://api.eumetsat.int/data/download/1.0.0/collections/c/products/p\"}]}";
        const string date = "\"date\":\"2026-11-13T21:30:05Z/2026-11-13T21:40:05Z\"";
        var body = "{\"features\":[" +
            "{\"id\":null,\"properties\":{" + date + ",\"links\":" + link + "}}," +
            "{\"id\":\"P0\",\"properties\":{" + date + ",\"links\":{\"data\":[{\"href\":null}]}}}," +
            OneProduct["{\"features\":[".Length..^2] + "]}";
        var (client, _, _) = Make(n => n == 1 ? Token() : Json(body));
        var p = Assert.Single(await client.SearchAsync(Creds, T0, CancellationToken.None));
        Assert.Equal("P1", p.Id);
    }

    [Fact]
    public async Task DownloadReturnsTheBytesWithTheBearerToken()
    {
        var product = new MtgProduct("P1", T0, T0, new Uri("https://api.eumetsat.int/data/download/1.0.0/collections/x/products/y"));
        var (client, handler, _) = Make(n => n == 1 ? Token("T9") : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        Assert.Equal(new byte[] { 1, 2, 3 }, await client.DownloadAsync(Creds, product, CancellationToken.None));
        Assert.Equal("T9", handler.Requests[1].AuthParameter);
        Assert.Equal(product.Download, handler.Requests[1].Uri);
    }

    [Fact]
    public async Task ExpiredTokenIsRenewedOnce()
    {
        // token, search 401, token, search 200
        var (client, handler, _) = Make(n => n switch
        {
            1 => Token("old"),
            2 => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            3 => Token("new"),
            _ => Json(OneProduct),
        });
        Assert.Single(await client.SearchAsync(Creds, T0, CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count(IsToken));
        Assert.Equal("new", handler.Requests[3].AuthParameter);

        // Both answers 401 although the second token is fresh: the request is refused (the key is not what is wrong), with no
        // further loop.
        var (bad, badHandler, _) = Make(n => n % 2 == 1 ? Token("t" + n) : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var refused = await Assert.ThrowsAsync<HttpRequestException>(() => bad.SearchAsync(Creds, T0, CancellationToken.None));
        Assert.Equal("EUMETSAT refused the request (HTTP 401)", refused.Message);
        Assert.Equal(4, badHandler.Requests.Count);
        Assert.Equal(2, badHandler.Requests.Count(IsToken));
    }

    [Fact]
    public async Task ExpiredTokenIsRenewedOnceForDownloadsToo()
    {
        var product = new MtgProduct("P1", T0, T0, new Uri("https://api.eumetsat.int/data/download/1.0.0/collections/x/products/y"));
        var (bad, handler, _) = Make(n => n % 2 == 1 ? Token() : new HttpResponseMessage(HttpStatusCode.Forbidden));
        var refused = await Assert.ThrowsAsync<HttpRequestException>(() => bad.DownloadAsync(Creds, product, CancellationToken.None));
        Assert.Equal("EUMETSAT refused the request (HTTP 403)", refused.Message);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task OnlyTheTokenEndpointDecidesThatTheKeyIsRejected()
    {
        // The token request itself refused on the retry (the key was revoked meanwhile): that is a rejection.
        var (client, _, _) = Make(n => n == 1 ? Token() : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<EumetsatKeyRejectedException>(() => client.SearchAsync(Creds, T0, CancellationToken.None));
    }

    [Theory]
    [InlineData("http://api.eumetsat.int/data/download/x")]
    [InlineData("https://example.com/data/download/x")]
    [InlineData("https://api.eumetsat.int.example.com/x")]
    public async Task TheTokenGoesOnlyToEumetsatOverHttps(string link)
    {
        var (client, handler, _) = Make(_ => Token());
        var product = new MtgProduct("P", T0, T0, new Uri(link));
        await Assert.ThrowsAsync<FormatException>(() => client.DownloadAsync(Creds, product, CancellationToken.None));
        Assert.Empty(handler.Requests);   // not even a token request
    }

    [Fact]
    public async Task OneMalformedResultIsSkippedOnceAndTheOthersKept()
    {
        const string body =
            "{\"features\":[" +
            "{\"id\":\"BAD1\",\"properties\":{\"date\":\"2026-11-13T21:30:05Z/2026-11-13T21:40:05Z\",\"links\":{\"data\":[]}}}," +   // empty links
            "{\"id\":\"BAD2\",\"properties\":{\"date\":\"not a date\",\"links\":{\"data\":[{\"href\":\"https://api.eumetsat.int/x\"}]}}}," +
            "{\"properties\":{}}," +   // no id
            "{\"id\":\"GOOD\",\"properties\":{\"date\":\"2026-11-13T21:20:05Z/2026-11-13T21:30:05Z\",\"links\":{\"data\":[{\"href\":\"https://api.eumetsat.int/data/download/1.0.0/collections/c/products/g\"}]}}}]}";
        var handler = new ScriptedHandler(n => n == 1 ? Token() : Json(body));   // one token, then searches
        var log = new RecordingLog();
        var client = new EumetsatClient(new FeedHttp(handler), new TestClock(T0), log);
        var first = await client.SearchAsync(Creds, T0, CancellationToken.None);
        Assert.Equal("GOOD", Assert.Single(first).Id);
        Assert.Equal(3, log.Warnings.Count);
        await client.SearchAsync(Creds, T0, CancellationToken.None);   // the same results again: no new warnings
        Assert.Equal(3, log.Warnings.Count);
        Assert.All(log.Warnings, w => Assert.DoesNotContain("SECRET", w));
    }
}
