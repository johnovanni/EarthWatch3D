using System.IO;
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
}
