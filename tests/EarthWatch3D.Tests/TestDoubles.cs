using EarthWatch3D.Core.Logging;
using EarthWatch3D.Core.Storage;
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

/// <summary>In-memory, synchronous settings store: Load returns the last saved value.</summary>
internal sealed class FakeSettingsStore(AppSettings? initial = null) : ISettingsStore
{
    private readonly object _gate = new();
    private AppSettings _current = initial ?? new();
    public List<AppSettings> Saved { get; } = [];
    public List<int> SaveThreadIds { get; } = [];
    public int SaveCount { get { lock (_gate) return Saved.Count; } }
    public AppSettings Load() { lock (_gate) return _current; }
    public void Save(AppSettings s)
    {
        lock (_gate) { _current = s; Saved.Add(s); SaveThreadIds.Add(Environment.CurrentManagedThreadId); }
    }
}
