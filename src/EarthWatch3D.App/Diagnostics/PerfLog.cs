#if PERF_LOG
using EarthWatch3D.Core;

namespace EarthWatch3D.App.Diagnostics;

/// <summary>
/// PERF_LOG builds only: appends timestamped lines to %TEMP%\EarthWatch3D-perf.log. It writes through GlobeCore's
/// <see cref="GlobeCore.Wpf.Diagnostics.PerfLog"/>, which <see cref="Configure"/> names after the app at start-up, so the
/// app's lines and GlobeCore's (the layer host's compose timings) go to the one file through one writer and one lock.
/// </summary>
internal static class PerfLog
{
    /// <summary>Points GlobeCore's log at %TEMP%\EarthWatch3D-perf.log. Called first thing at start-up.</summary>
    public static void Configure() => GlobeCore.Wpf.Diagnostics.PerfLog.Configure(AppInfo.Name);

    public static string FilePath => GlobeCore.Wpf.Diagnostics.PerfLog.FilePath;

    public static void Write(string message) => GlobeCore.Wpf.Diagnostics.PerfLog.Write(message);

    public static string Memory() => GlobeCore.Wpf.Diagnostics.PerfLog.Memory();
}
#endif
