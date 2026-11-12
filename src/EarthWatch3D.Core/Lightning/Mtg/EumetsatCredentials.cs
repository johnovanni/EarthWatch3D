namespace EarthWatch3D.Core.Lightning.Mtg;

/// <summary>EUMETSAT Data Store consumer key and secret. ToString never shows the values.</summary>
public sealed record EumetsatCredentials(string Key, string Secret)
{
    public override string ToString() => "EumetsatCredentials(***)";
}

public enum EumetsatTestResult { Accepted, Rejected, Unreachable }

/// <summary>EUMETSAT refused the key and secret.</summary>
public sealed class EumetsatKeyRejectedException()
    : Exception("EUMETSAT key rejected. Check it in Settings.");
