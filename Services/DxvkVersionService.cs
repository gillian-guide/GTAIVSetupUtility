using System.Text;
using System.Text.RegularExpressions;
using NLog;

namespace GTAIVSetupUtility.Services;

/// <summary>
/// Reads the DXVK version actually installed in the game folder. The target version comes from
/// the DXVK source the user selected (upstream, gplasync, Sarek or the legacy async fork).
/// </summary>
public static class DxvkVersionService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static string? GetInstalledVersion(string gameDirectory)
    {
        foreach (string name in new[] { "vulkan.dll", "d3d9.dll" })
        {
            string path = Path.Combine(gameDirectory, name);
            if (!File.Exists(path))
                continue;

            try
            {
                // DXVK masquerades its PE version as a Windows 10 build, so the real version has
                // to come from the "DXVK: vX.Y.Z" string it embeds in the binary. Without that
                // string the file is not a DXVK build (for example the game's original d3d9.dll),
                // and reporting its Windows version as a DXVK version would make the updater skip.
                string? version = ReadEmbeddedVersion(path);
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

    private static string? ReadEmbeddedVersion(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string ascii = Encoding.ASCII.GetString(bytes);
        Match match = Regex.Match(
            ascii,
            @"DXVK[\s\S]{0,24}?v(?<version>\d+\.\d+\.\d+)",
            RegexOptions.CultureInvariant);

        return match.Success ? Normalize(match.Groups["version"].Value) : null;
    }

    public static bool IsAtLeast(string installed, string target)
    {
        if (!Version.TryParse(Clean(installed), out Version? installedVersion))
            return false;
        if (!Version.TryParse(Clean(target), out Version? targetVersion))
            return false;

        return installedVersion >= targetVersion;
    }

    /// <summary>
    /// Compares up to the build component, so "2.6.2" and "2.6.2.0" are the same release.
    /// Used for fixed DXVK builds where a newer major build is not necessarily usable.
    /// </summary>
    public static bool IsSameVersion(string installed, string target)
    {
        if (!Version.TryParse(Clean(installed), out Version? installedVersion))
            return false;
        if (!Version.TryParse(Clean(target), out Version? targetVersion))
            return false;

        return installedVersion.Major == targetVersion.Major &&
               installedVersion.Minor == targetVersion.Minor &&
               installedVersion.Build == targetVersion.Build;
    }

    public static string? Normalize(string? value)
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
