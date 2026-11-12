using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Logging;

namespace EarthWatch3D.Core.Feeds.Eonet;

/// <summary>Polls NASA EONET severe storms every 30 min.</summary>
public sealed class EonetFeed(FeedHttp http, EventStore store, IAppLog log) : IEventFeed
{
    private static readonly Uri Url = new("https://eonet.gsfc.nasa.gov/api/v3/events?category=severeStorms&status=all&days=7");

    public string Name => "EONET";
    public TimeSpan Interval => TimeSpan.FromMinutes(30);

    public async Task<FeedRunResult> RunOnceAsync(CancellationToken ct)
    {
        var bytes = await http.GetBytesAsync(Url, ct).ConfigureAwait(false);
        store.UpsertEonet(EonetReader.Read(new MemoryStream(bytes), log));
        return FeedRunResult.Ok;
    }
}
