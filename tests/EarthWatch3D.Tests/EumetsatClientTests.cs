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
}
