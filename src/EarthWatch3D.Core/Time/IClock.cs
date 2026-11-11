namespace EarthWatch3D.Core.Time;

public interface IClock { DateTime UtcNow { get; } }

public sealed class SystemClock : IClock { public DateTime UtcNow => DateTime.UtcNow; }
