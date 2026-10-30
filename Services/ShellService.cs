using System.Diagnostics;

namespace GTAIVSetupUtility.Services;

/// <summary>
/// Small shell helpers. Kept separate from the view models so services can open links/restart
/// without taking a dependency on the UI layer.
/// </summary>
public static class ShellService
{
    public static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public static void Restart()
    {
        string? executable = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(executable))
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });

        Environment.Exit(0);
    }
}
