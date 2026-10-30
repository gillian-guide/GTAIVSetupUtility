using System.Diagnostics;
using GTAIVSetupUtility.Services.Network;
using NLog;

namespace GTAIVSetupUtility.Services;

/// <summary>
/// Reads the DXVK version actually installed in the game folder and compares it with the
/// latest upstream release. Used to avoid downloading/reinstalling an already-current DXVK.
/// </summary>
public static class DxvkVersionService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private const string LatestReleaseUrl = "https://api.github.com/repos/doitsujin/dxvk/releases/latest";

    public static string? GetInstalledVersion(string gameDirectory)
    {
        foreach (string name in new[] { "vulkan.dll", "d3d9.dll" })
        {
            string path = Path.Combine(gameDirectory, name);
            if (!File.Exists(path))
                continue;

            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                string? version = Normalize(info.ProductVersion) ?? Normalize(info.FileVersion);
                if (version != null)
                    return version;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, $" Could not read the version of {name}.");
            }
        }

        return null;
    }

    public static async Task<string?> GetLatestDxvkVersionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            GitHubRelease? release = await HttpService
                .GetJsonAsync(LatestReleaseUrl, GitHubJsonContext.Default.GitHubRelease, cancellationToken)
                .ConfigureAwait(false);

            return Normalize(release?.TagName);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, " Could not query the latest DXVK release.");
            return null;
        }
    }

    public static bool IsAtLeast(string installed, string target)
    {
        if (!Version.TryParse(Clean(installed), out Version? installedVersion))
            return false;
        if (!Version.TryParse(Clean(target), out Version? targetVersion))
            return false;

        return installedVersion >= targetVersion;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string cleaned = Clean(value);
        return Version.TryParse(cleaned, out _) ? cleaned : null;
    }

    private static string Clean(string value)
    {
        string cleaned = value.Trim();
        if (cleaned.StartsWith('v') || cleaned.StartsWith('V'))
            cleaned = cleaned[1..];

        int dash = cleaned.IndexOf('-');
        if (dash >= 0)
            cleaned = cleaned[..dash];

        return cleaned;
    }
}
