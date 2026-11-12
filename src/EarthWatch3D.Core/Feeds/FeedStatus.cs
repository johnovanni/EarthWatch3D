namespace EarthWatch3D.Core.Feeds;

public enum FeedState { NotStarted, Ok, Retrying, Failing, NotSetUp }

public sealed record FeedStatus(
    string Name, FeedState State, DateTime? LastSuccessUtc, string? LastError,
    int ConsecutiveFailures, DateTime NextRunUtc);
