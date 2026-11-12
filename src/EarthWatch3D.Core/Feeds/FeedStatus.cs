namespace EarthWatch3D.Core.Feeds;

public enum FeedState { NotStarted, Ok, Retrying, Failing, NotSetUp, Paused }

/// <param name="Note">Optional extra line for the tooltip, such as "Daily download limit reached".</param>
public sealed record FeedStatus(
    string Name, FeedState State, DateTime? LastSuccessUtc, string? LastError,
    int ConsecutiveFailures, DateTime NextRunUtc, string? Note = null);
