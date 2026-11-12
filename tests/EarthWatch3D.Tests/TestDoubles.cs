using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.Tests;

/// <summary>Records every message by level.</summary>
internal sealed class RecordingLog : IAppLog
{
    public List<string> Infos { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];
    public void Info(string m) => Infos.Add(m);
    public void Warn(string m, Exception? e = null) => Warnings.Add(m);
    public void Error(string m, Exception? e = null) => Errors.Add(m);
}

internal sealed class TestClock(DateTime t) : IClock { public DateTime UtcNow { get; set; } = t; }
