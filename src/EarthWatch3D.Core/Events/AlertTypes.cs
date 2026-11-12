namespace EarthWatch3D.Core.Events;

[Flags]
public enum AlertTypes { None = 0, Warnings = 1, Watches = 2, Advisories = 4, Statements = 8 }

public static class AlertTypeClassifier
{
    /// <summary>Warning/Watch/Advisory by the event-type suffix; anything else is a statement.</summary>
    public static AlertTypes Classify(string eventType)
    {
        var t = eventType.AsSpan().Trim();
        if (t.EndsWith("Warning", StringComparison.OrdinalIgnoreCase)) return AlertTypes.Warnings;
        if (t.EndsWith("Watch", StringComparison.OrdinalIgnoreCase)) return AlertTypes.Watches;
        if (t.EndsWith("Advisory", StringComparison.OrdinalIgnoreCase)) return AlertTypes.Advisories;
        return AlertTypes.Statements;
    }
}
