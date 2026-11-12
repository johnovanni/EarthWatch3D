using System.IO;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Threading;
using System.Net.Http;
using System.Net;
using System.Text;
using EarthWatch3D.Core;
using EarthWatch3D.Core.Feeds;

namespace EarthWatch3D.Tests;

public sealed class FeedHttpTests
{
    private sealed class RecordingHandler(string body) : HttpMessageHandler
    {
        public string? UserAgent;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task OversizedBodyRejected()
    {
        using var http = new FeedHttp(new RecordingHandler(new string('a', 2048)), maxBodyBytes: 1024);
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetBytesAsync(new Uri("http://x/y"), CancellationToken.None));
    }

    [Fact]
    public async Task FeedHttpSendsUserAgentAndCountsBytes()
    {
        var h = new RecordingHandler("hello wörld");
        using var http = new FeedHttp(h);
        long total = 0;
        http.BytesReceived += n => Interlocked.Add(ref total, n);
        var text = await http.GetStringAsync(new Uri("http://x/y"), CancellationToken.None);
        Assert.Equal("hello wörld", text);
        Assert.Equal(AppInfo.UserAgent, h.UserAgent);
        Assert.Equal(Encoding.UTF8.GetByteCount("hello wörld"), total);
    }

    [Fact]
    public async Task NonSuccessThrows()
    {
        using var http = new FeedHttp(new SampleHttpHandler());
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetBytesAsync(new Uri("http://nope/"), CancellationToken.None));
    }

    [Fact]
    public async Task SampleHandlerLongestPrefixAnd404()
    {
        var h = new SampleHttpHandler();
        h.MapText("http://a/", "short");
        h.MapText("http://a/b/", "long");
        using var http = new FeedHttp(h);
        Assert.Equal("long", await http.GetStringAsync(new Uri("http://a/b/c"), CancellationToken.None));
        Assert.Equal("short", await http.GetStringAsync(new Uri("http://a/z"), CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetStringAsync(new Uri("http://other/"), CancellationToken.None));
        Assert.Equal(3, h.Requests.Count);
        Assert.Equal("http://a/b/c", h.Requests[0]);
    }

    private sealed class StallStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }
    }

    private sealed class StallHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallStream()) });
    }

    [Fact]
    public async Task StalledBodyTimesOut()
    {
        using var http = new FeedHttp(new StallHandler(), TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<TimeoutException>(() => http.GetBytesAsync(new Uri("http://host.example/x"), CancellationToken.None));
    }

    private sealed class FuncHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return respond(request, body);
        }
    }

    [Fact]
    public async Task CountsCompressedBytesAndDecompresses()
    {
        var text = string.Concat(Enumerable.Range(0, 20_000).Select(i => "line " + (i % 7) + "\n"))[..100_000];
        var raw = Encoding.UTF8.GetBytes(text);
        Assert.Equal(100_000, raw.Length);
        byte[] gz;
        using (var ms = new MemoryStream())
        {
            using (var z = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw);
            gz = ms.ToArray();
        }
        Assert.True(gz.Length < raw.Length / 4);
        var handler = new FuncHandler((_, _) =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(gz) };
            r.Content.Headers.ContentEncoding.Add("gzip");
            return r;
        });
        using var http = new FeedHttp(handler);
        long total = 0;
        http.BytesReceived += n => Interlocked.Add(ref total, n);
        var got = await http.GetBytesAsync(new Uri("http://x/y"), CancellationToken.None);
        Assert.Equal(raw, got);
        Assert.Equal(gz.Length, total);
    }

    [Fact]
    public async Task SendsAcceptEncoding()
    {
        string? accept = null;
        var handler = new FuncHandler((req, _) =>
        {
            accept = req.Headers.AcceptEncoding.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("x") };
        });
        using var http = new FeedHttp(handler);
        await http.GetBytesAsync(new Uri("http://x/y"), CancellationToken.None);
        Assert.Equal("gzip, deflate", accept);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task UnauthorizedThrowsFeedAuthException(HttpStatusCode code)
    {
        using var http = new FeedHttp(new FuncHandler((_, _) => new HttpResponseMessage(code)));
        var e = await Assert.ThrowsAsync<FeedAuthException>(() => http.GetBytesAsync(new Uri("http://x/y"), CancellationToken.None));
        // Keyless feeds get these too (a 403 from S3), so the message does not talk about credentials.
        Assert.Equal($"HTTP {(int)code}: access denied", e.Message);
    }

    [Fact]
    public async Task PostFormSendsBasicAuthAndForm()
    {
        HttpMethod? method = null; AuthenticationHeaderValue? auth = null; string? sent = null;
        var handler = new FuncHandler((req, body) =>
        {
            method = req.Method; auth = req.Headers.Authorization; sent = body;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":1}") };
        });
        using var http = new FeedHttp(handler);
        var reply = await http.PostFormAsync(new Uri("http://x/token"),
            new Dictionary<string, string> { ["grant_type"] = "client_credentials" },
            new AuthenticationHeaderValue("Basic", "dTpw"), CancellationToken.None);
        Assert.Equal("{\"ok\":1}", reply);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("Basic", auth?.Scheme);
        Assert.Equal("dTpw", auth?.Parameter);
        Assert.Equal("grant_type=client_credentials", sent);
    }
}
