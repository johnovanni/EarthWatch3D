using System.Net;

namespace EarthWatch3D.Core.Feeds;

/// <summary>
/// Shared HTTP client for all feeds: User-Agent, 30 s timeout (headers and body), gzip/deflate, byte counting.
/// A caller-supplied handler is owned and disposed by FeedHttp.
/// </summary>
public sealed class FeedHttp : IDisposable
{
    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;
    private readonly long _maxBytes;

    /// <summary>Raised with the number of body bytes read (after decompression).</summary>
    public event Action<long>? BytesReceived;

    public FeedHttp(HttpMessageHandler? handler = null, TimeSpan? timeout = null, long maxBodyBytes = 50L * 1024 * 1024)
    {
        _maxBytes = maxBodyBytes;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        handler ??= new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", AppInfo.UserAgent);
    }

    public async Task<byte[]> GetBytesAsync(Uri u, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);
        try
        {
            return await ReadAsync(u, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException($"{u.Host} did not respond within {_timeout.TotalSeconds:0.###} s");
        }
    }

    private async Task<byte[]> ReadAsync(Uri u, CancellationToken ct)
    {
        using var resp = await _client.GetAsync(u, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = await s.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            if (ms.Length + n > _maxBytes) throw new HttpRequestException($"response larger than {_maxBytes / (1024.0 * 1024):0.###} MB");
            ms.Write(buf, 0, n);
            BytesReceived?.Invoke(n);
        }
        return ms.ToArray();
    }

    public async Task<string> GetStringAsync(Uri u, CancellationToken ct)
        => System.Text.Encoding.UTF8.GetString(await GetBytesAsync(u, ct).ConfigureAwait(false));

    public void Dispose() => _client.Dispose();
}
