using System.Net;

namespace EarthWatch3D.Core.Feeds;

/// <summary>Shared HTTP client for all feeds: User-Agent, 30 s timeout, gzip/deflate, byte counting.</summary>
public sealed class FeedHttp : IDisposable
{
    private readonly HttpClient _client;

    /// <summary>Raised with the number of body bytes read (after decompression).</summary>
    public event Action<long>? BytesReceived;

    public FeedHttp(HttpMessageHandler? handler = null)
    {
        handler ??= new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", AppInfo.UserAgent);
    }

    public async Task<byte[]> GetBytesAsync(Uri u, CancellationToken ct)
    {
        using var resp = await _client.GetAsync(u, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = await s.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            ms.Write(buf, 0, n);
            BytesReceived?.Invoke(n);
        }
        return ms.ToArray();
    }

    public async Task<string> GetStringAsync(Uri u, CancellationToken ct)
        => System.Text.Encoding.UTF8.GetString(await GetBytesAsync(u, ct).ConfigureAwait(false));

    public void Dispose() => _client.Dispose();
}
