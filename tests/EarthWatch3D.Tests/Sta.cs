namespace EarthWatch3D.Tests;

/// <summary>Runs WPF view code on a dedicated STA thread and rethrows its exception.</summary>
internal static class Sta
{
    public static void Run(Action work)
    {
        Exception? error = null;
        var t = new Thread(() => { try { work(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
