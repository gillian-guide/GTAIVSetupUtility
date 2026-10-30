namespace GTAIVSetupUtility.Modules.DlssIv;

/// <summary>
/// Locates the user-installed DLSS-IV files. The mod is never installed by this tool: the
/// user already has DLSS-IV.asi / DLSS-IV.cfg / DLSSIV next to GTAIV.exe (or in plugins\).
/// </summary>
public sealed class DlssIvInstallation
{
    public required string GameDirectory { get; init; }
    public required string DataDirectory { get; init; }
    public string? ConfigPath { get; init; }
    public IReadOnlyList<string> EnabledAsiPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DisabledAsiPaths { get; init; } = Array.Empty<string>();

    public bool IsEnabled => EnabledAsiPaths.Count > 0;
    public bool IsDisabled => !IsEnabled && DisabledAsiPaths.Count > 0;
    public bool HasConfig => !string.IsNullOrEmpty(ConfigPath) && File.Exists(ConfigPath);
    public bool HasDataDirectory => Directory.Exists(DataDirectory);

    /// <summary>
    /// The ASI is the mod. A leftover DLSS-IV.cfg or DLSSIV folder alone must not make the
    /// panel appear, because there is nothing the user could enable.
    /// </summary>
    public bool IsPresent => IsEnabled || IsDisabled;

    public string StatePath =>
        HasDataDirectory
            ? Path.Combine(DataDirectory, ".gtaiv-utility.json")
            : Path.Combine(GameDirectory, ".gtaiv-utility-dlssiv.json");

    public string BackupDirectory => Path.Combine(DataDirectory, ".gtaiv-backup");

    public static DlssIvInstallation Find(string gameDirectory)
    {
        string plugins = Path.Combine(gameDirectory, "plugins");
        string[] directories = [gameDirectory, plugins];

        var enabled = new List<string>();
        var disabled = new List<string>();

        foreach (string directory in directories)
        {
            string asi = Path.Combine(directory, "DLSS-IV.asi");
            if (File.Exists(asi)) enabled.Add(asi);

            string disabledAsi = asi + ".disabled";
            if (File.Exists(disabledAsi)) disabled.Add(disabledAsi);
        }

        string? config = null;
        string preferredDirectory = enabled.Count > 0
            ? Path.GetDirectoryName(enabled[0])!
            : disabled.Count > 0
                ? Path.GetDirectoryName(disabled[0])!
                : gameDirectory;

        string preferredConfig = Path.Combine(preferredDirectory, "DLSS-IV.cfg");
        if (File.Exists(preferredConfig))
        {
            config = preferredConfig;
        }
        else
        {
            config = directories
                .Select(directory => Path.Combine(directory, "DLSS-IV.cfg"))
                .FirstOrDefault(File.Exists);
            config ??= preferredConfig;
        }

        return new DlssIvInstallation
        {
            GameDirectory = gameDirectory,
            DataDirectory = Path.Combine(gameDirectory, "DLSSIV"),
            ConfigPath = config,
            EnabledAsiPaths = enabled,
            DisabledAsiPaths = disabled
        };
    }
}
