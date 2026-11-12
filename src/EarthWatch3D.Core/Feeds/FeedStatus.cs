namespace EarthWatch3D.Core.Feeds;

/// <summary>
/// A feed's state for its status dot. <see cref="Idle"/>: its last run was skipped (layer off, replay, or the satellite
/// not in view), so nothing is being fetched right now.
/// </summary>
public enum FeedState { NotStarted, Ok, Retrying, Failing, NotSetUp, Paused, Idle }

/// <param name="Note">Optional extra line for the tooltip, such as "Daily download limit reached".</param>
public sealed record FeedStatus(
    string Name, FeedState State, DateTime? LastSuccessUtc, string? LastError,
    int ConsecutiveFailures, DateTime NextRunUtc, string? Note = null)
{
    /// <summary>The note on an <see cref="FeedState.Idle"/> feed (true for the lightning and the clouds feeds alike).</summary>
    public const string IdleNote = "Not polled right now (layer off, replay, or not in view)";
}
