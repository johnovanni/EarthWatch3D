using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;

namespace EarthWatch3D.Core.Feeds;

/// <summary>
/// HTTP 401 or 403, so a caller that sent credentials can say "key rejected". Keyless feeds can get a 403 too (S3 does),
/// so the message itself does not talk about credentials.
/// </summary>
public sealed class FeedAuthException(HttpStatusCode status)
    : HttpRequestException($"HTTP {(int)status}: access denied", null, status);

/// <summary>
/// Shared HTTP client for all feeds: User-Agent, 30 s timeout (headers and body), gzip/deflate, byte counting.
/// A caller-supplied handler is owned and disposed by FeedHttp.
/// </summary>
public sealed class FeedHttp : IDisposable
{
    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;
    private readonly long _maxBytes;

    /// <summary>Raised with the number of body bytes read off the wire (compressed, as downloaded).</summary>
    public event Action<long>? BytesReceived;

    public FeedHttp(HttpMessageHandler? handler = null, TimeSpan? timeout = null, long maxBodyBytes = 50L * 1024 * 1024)
    {
        _maxBytes = maxBodyBytes;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        // Decompression is done here, so the bytes counted are the bytes that crossed the network.
        handler ??= new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.None };
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", AppInfo.UserAgent);
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
    }

    public Task<byte[]> GetBytesAsync(Uri u, CancellationToken ct) => GetBytesAsync(u, ct, null);

    public Task<byte[]> GetBytesAsync(Uri u, CancellationToken ct, AuthenticationHeaderValue? auth)
        => SendAsync(u, () => NewRequest(HttpMethod.Get, u, auth), ct);

    /// <summary>POSTs an application/x-www-form-urlencoded body and returns the response text.</summary>
    public async Task<string> PostFormAsync(Uri u, IReadOnlyDictionary<string, string> form,
        AuthenticationHeaderValue? auth, CancellationToken ct)
    {
        var bytes = await SendAsync(u, () =>
        {
            var req = NewRequest(HttpMethod.Post, u, auth);
            req.Content = new FormUrlEncodedContent(form);
            return req;
        }, ct).ConfigureAwait(false);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    public async Task<string> GetStringAsync(Uri u, CancellationToken ct)
        => System.Text.Encoding.UTF8.GetString(await GetBytesAsync(u, ct).ConfigureAwait(false));

    private static HttpRequestMessage NewRequest(HttpMethod method, Uri u, AuthenticationHeaderValue? auth)
    {
        var req = new HttpRequestMessage(method, u);
        if (auth is not null) req.Headers.Authorization = auth;
        return req;
    }

    private async Task<byte[]> SendAsync(Uri u, Func<HttpRequestMessage> makeRequest, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);
        try
        {
            using var req = makeRequest();
            return await ReadAsync(req, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException($"{u.Host} did not respond within {_timeout.TotalSeconds:0.###} s");
        }
    }

    private async Task<byte[]> ReadAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new FeedAuthException(resp.StatusCode);
        resp.EnsureSuccessStatusCode();
        await using var wire = new CountingStream(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false),
            n => BytesReceived?.Invoke(n));
        await using var s = Decoded(wire, resp.Content.Headers.ContentEncoding);
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        // The cap is on the decompressed size: it is there to protect memory.
        while ((n = await s.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            if (ms.Length + n > _maxBytes) throw new HttpRequestException($"response larger than {_maxBytes / (1024.0 * 1024):0.###} MB");
            ms.Write(buf, 0, n);
        }
        return ms.ToArray();
    }

    private static Stream Decoded(Stream wire, ICollection<string> encodings)
    {
        if (encodings.Contains("gzip", StringComparer.OrdinalIgnoreCase) || encodings.Contains("x-gzip", StringComparer.OrdinalIgnoreCase))
            return new GZipStream(wire, CompressionMode.Decompress);
        if (encodings.Contains("deflate", StringComparer.OrdinalIgnoreCase))
            return new ZLibStream(wire, CompressionMode.Decompress);
        return wire;
    }

    /// <summary>Passes reads through and reports how many bytes they returned.</summary>
    private sealed class CountingStream(Stream inner, Action<long> counted) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => Count(await inner.ReadAsync(buffer, ct).ConfigureAwait(false));

        private int Count(int n)
        {
            if (n > 0) counted(n);
            return n;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    public void Dispose() => _client.Dispose();
}
