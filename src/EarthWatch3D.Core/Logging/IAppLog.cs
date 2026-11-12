namespace EarthWatch3D.Core.Logging;

public interface IAppLog
{
    void Info(string m);
    void Warn(string m, Exception? e = null);
    void Error(string m, Exception? e = null);
}

public sealed class NullLog : IAppLog
{
    public void Info(string m) { }
    public void Warn(string m, Exception? e = null) { }
    public void Error(string m, Exception? e = null) { }
}
