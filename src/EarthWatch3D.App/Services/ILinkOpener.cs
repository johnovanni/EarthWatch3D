using System.Diagnostics;

namespace EarthWatch3D.App.Services;

/// <summary>Opens a web address, a file or a folder with the program Windows has chosen for it.</summary>
public interface ILinkOpener
{
    /// <summary>Opens <paramref name="target"/>. Throws if Windows cannot (no browser, missing file).</summary>
    void Open(string target);
}

/// <summary>The real opener: the shell's default handler.</summary>
public sealed class ShellLinkOpener : ILinkOpener
{
    public void Open(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
}
