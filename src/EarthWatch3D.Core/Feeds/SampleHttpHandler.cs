using System.Net;

namespace EarthWatch3D.Core.Feeds;

/// <summary>Serves canned responses by URL prefix (longest wins; 404 otherwise). For tests and offline runs.</summary>
public sealed class SampleHttpHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Func<byte[]>> _map = new(StringComparer.Ordinal);
    private readonly List<string> _requests = [];

    public IReadOnlyList<string> Requests { get { lock (_gate) return _requests.ToArray(); } }

    public void Map(string urlPrefix, string filePath)
    {
        lock (_gate) _map[urlPrefix] = () => File.ReadAllBytes(filePath);
    }

    public void MapText(string urlPrefix, string body)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        lock (_gate) _map[urlPrefix] = () => bytes;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri?.ToString() ?? "";
        Func<byte[]>? source = null;
        lock (_gate)
        {
            _requests.Add(url);
            var best = -1;
            foreach (var (prefix, f) in _map)
                if (url.StartsWith(prefix, StringComparison.Ordinal) && prefix.Length > best)
                { best = prefix.Length; source = f; }
        }
        if (source is null)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
        try
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(source()),
                RequestMessage = request,
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Task.FromException<HttpResponseMessage>(new HttpRequestException(e.Message, e));
        }
    }
}
