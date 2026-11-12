namespace EarthWatch3D.Core.Feeds;

public static class Backoff
{
    /// <summary>Wait before the next attempt: 30 s, 1 min, 2 min, 5 min, then 10 min.</summary>
    public static TimeSpan Delay(int consecutiveFailures) => consecutiveFailures switch
    {
        <= 1 => TimeSpan.FromSeconds(30),
        2 => TimeSpan.FromMinutes(1),
        3 => TimeSpan.FromMinutes(2),
        4 => TimeSpan.FromMinutes(5),
        _ => TimeSpan.FromMinutes(10),
    };
}
