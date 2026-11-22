using GlobeCore.Feeds;

namespace EarthWatch3D.App.Services;

/// <summary>
/// A feed whose real feed is made in the background (the "Borders" feed needs the boundary set, which takes a moment to
/// load), so the scheduler and its status dot can have it from the start. A run waits for the feed, then runs it; if the
/// feed could not be made, each run throws that reason, which the dot shows.
/// </summary>
public sealed class DeferredFeed(string name, TimeSpan interval, Task<IFeed> feed) : IFeed, IFeedStatusNote
{
    private readonly Task<IFeed> _feed = feed ?? throw new ArgumentNullException(nameof(feed));

    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    public TimeSpan Interval { get; } = interval;

    /// <summary>The real feed's note, once it is made.</summary>
    public string? StatusNote => _feed.IsCompletedSuccessfully && _feed.Result is IFeedStatusNote n ? n.StatusNote : null;

    public async Task<FeedRunResult> RunOnceAsync(CancellationToken ct)
    {
        var f = await _feed.WaitAsync(ct).ConfigureAwait(false);
        return await f.RunOnceAsync(ct).ConfigureAwait(false);
    }
}
