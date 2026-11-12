namespace EarthWatch3D.Core.Feeds;

public interface IEventFeed
{
    string Name { get; }
    TimeSpan Interval { get; }
    Task RunOnceAsync(CancellationToken ct);
}
