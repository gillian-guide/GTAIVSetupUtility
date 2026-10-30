using System.Text;
using NLog;

namespace GTAIVSetupUtility.Modules.DlssIv;

/// <summary>
/// Reads and writes only the four DLSS-IV.cfg values the tool owns, plus the FusionFix
/// anti-aliasing toggle. Everything else (comments, unknown keys, formatting) is preserved.
/// </summary>
public static class DlssIvConfigService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly string[] AntialiasingSections = ["MISC", "MAIN", "Options", "GRAPHICS"];

    public static DlssIvConfigValues LoadSettings(DlssIvInstallation installation)
    {
        if (!installation.HasConfig)
            return DlssIvConfigValues.Default;

        try
        {
            string text = File.ReadAllText(installation.ConfigPath!);
            return new DlssIvConfigValues(
                ReadInt(text, "upscaler", -1),
                ReadInt(text, "fsr_version", 0),
                ReadInt(text, "quality", 1),
                ReadInt(text, "hdr", 0),
                ReadInt(text, "preset", 11));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, " Failed to read DLSS-IV.cfg.");
            return DlssIvConfigValues.Default;
        }
    }

    public static void ApplySettings(DlssIvInstallation installation, DlssIvSettings settings)
    {
        string path = installation.ConfigPath ?? Path.Combine(installation.GameDirectory, "DLSS-IV.cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
        {
            Logger.Info(" Creating a minimal DLSS-IV.cfg (the user's mod normally provides one).");
            File.WriteAllText(path,
                "# DLSS-IV configuration\n" +
                "# Created by GTAIVSetupUtility. The mod's own settings keep their defaults.\n" +
                "quality=1\nupscaler=-1\nfsr_version=0\nhdr=0\n",
                Utf8NoBom);
        }

        BackupOnce(path);

        string text = File.ReadAllText(path);
        text = IniTextEditor.SetValue(text, null, "upscaler", settings.Upscaler == DlssIvUpscaler.Dlss ? "0" : "1");
        text = IniTextEditor.SetValue(text, null, "fsr_version", settings.FsrProfile switch
        {
            DlssIvFsrProfile.Fsr31 => "3",
            DlssIvFsrProfile.Fsr4 => "4",
            DlssIvFsrProfile.Fsr4Lite => "4",
            _ => "0"
        });
        text = IniTextEditor.SetValue(text, null, "quality", settings.Quality == DlssIvQuality.Dlaa ? "0" : "1");
        text = IniTextEditor.SetValue(text, null, "preset", settings.DlssPreset.ToString());
        text = IniTextEditor.SetValue(text, null, "hdr", settings.Hdr ? "1" : "0");

        File.WriteAllText(path, text, Utf8NoBom);
        Logger.Info($" DLSS-IV.cfg updated: upscaler={settings.Upscaler}, fsr={settings.FsrProfile}, preset={settings.DlssPreset}, quality={settings.Quality}, hdr={settings.Hdr}");
    }

    /// <summary>
    /// Enables/disables the whole mod by renaming DLSS-IV.asi to *.asi.disabled and back.
    /// DLSS-IV has no "upscaler off" config value, and its HDR/performance features are
    /// inseparable from the ASI, so this is the only honest on/off switch.
    /// </summary>
    public static void SetModEnabled(DlssIvInstallation installation, bool enabled, DlssIvState state,
        string? fusionFixConfigPath, bool updateFusionFixAntialiasing = true)
    {
        if (enabled)
        {
            foreach (string disabledPath in installation.DisabledAsiPaths)
            {
                string target = disabledPath[..^".disabled".Length];
                if (File.Exists(target))
                    continue;

                File.Move(disabledPath, target);
                Logger.Info($" Enabled DLSS-IV by renaming {Path.GetFileName(disabledPath)} back to {Path.GetFileName(target)}.");
            }
        }
        else
        {
            foreach (string enabledPath in installation.EnabledAsiPaths)
            {
                string target = enabledPath + ".disabled";
                if (File.Exists(target))
                    continue;

                File.Move(enabledPath, target);
                Logger.Info($" Disabled DLSS-IV by renaming {Path.GetFileName(enabledPath)} to {Path.GetFileName(target)}.");
            }
        }

        state.ModEnabled = enabled;

        if (updateFusionFixAntialiasing)
            SetFusionFixAntialiasing(state, fusionFixConfigPath, disable: enabled);
    }

    /// <summary>
    /// DLSS-IV only runs through FusionFix's Vulkan renderer. When Vulkan is usable the two
    /// switches are forced on; when it is not, they are forced back to Direct3D so the game
    /// does not try to load a renderer that cannot work.
    /// </summary>
    public static void SetVulkanEnabled(
        DlssIvInstallation installation,
        string? fusionFixConfigPath,
        bool enabled)
    {
        string value = enabled ? "1" : "0";

        if (!string.IsNullOrWhiteSpace(fusionFixConfigPath) &&
            File.Exists(fusionFixConfigPath) &&
            fusionFixConfigPath.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase))
        {
            SetIniValueIfNeeded(fusionFixConfigPath, "MAIN", "GraphicsAPI", value, "FusionFix");
        }

        string d3d9ConfigPath = Path.Combine(installation.GameDirectory, "d3d9.cfg");
        if (enabled)
        {
            if (!File.Exists(d3d9ConfigPath))
            {
                File.WriteAllText(d3d9ConfigPath, $"[MAIN]{Environment.NewLine}API = 1{Environment.NewLine}", Utf8NoBom);
                Logger.Info(" Created d3d9.cfg with [MAIN] API = 1.");
                return;
            }

            SetIniValueIfNeeded(d3d9ConfigPath, "MAIN", "API", value, "d3d9.cfg");
        }
        else if (File.Exists(d3d9ConfigPath))
        {
            SetIniValueIfNeeded(d3d9ConfigPath, "MAIN", "API", value, "d3d9.cfg");
        }
    }

    private static void SetIniValueIfNeeded(
        string path,
        string section,
        string key,
        string value,
        string displayName)
    {
        try
        {
            string text = File.ReadAllText(path);
            if (IniTextEditor.ReadValue(text, section, key) == value)
                return;

            File.WriteAllText(path, IniTextEditor.SetValue(text, section, key, value), Utf8NoBom);
            Logger.Info($" Set {displayName} [{section}] {key}={value}.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $" Failed to set {displayName} [{section}] {key}.");
        }
    }

    public static void SetFusionFixAntialiasing(DlssIvState state, string? configPath, bool disable)
    {
        if (!string.IsNullOrWhiteSpace(configPath))
            state.FusionFixConfigPath = configPath;

        string? path = state.FusionFixConfigPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
            !path.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            string text = File.ReadAllText(path);

            if (disable)
            {
                if (state.FusionFixAntialiasing == null)
                {
                    string? section = FindAntialiasingSection(text);
                    string? current = section == null ? null : IniTextEditor.ReadValue(text, section, "Antialiasing");
                    state.FusionFixAntialiasingSection = section ?? "MISC";
                    state.FusionFixAntialiasing = current ?? "N/A";
                }

                string targetSection = state.FusionFixAntialiasingSection ?? "MISC";
                text = IniTextEditor.SetValue(text, targetSection, "Antialiasing", "0");
                File.WriteAllText(path, text, Utf8NoBom);
                Logger.Info($" Set FusionFix Antialiasing=0 in [{targetSection}] (was {state.FusionFixAntialiasing}).");
            }
            else if (state.FusionFixAntialiasing != null)
            {
                string targetSection = state.FusionFixAntialiasingSection ?? "MISC";
                text = state.FusionFixAntialiasing == "N/A"
                    ? IniTextEditor.RemoveValue(text, targetSection, "Antialiasing")
                    : IniTextEditor.SetValue(text, targetSection, "Antialiasing", state.FusionFixAntialiasing);

                File.WriteAllText(path, text, Utf8NoBom);
                Logger.Info($" Restored FusionFix Antialiasing={state.FusionFixAntialiasing} in [{targetSection}].");
                state.FusionFixAntialiasing = null;
                state.FusionFixAntialiasingSection = null;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, " Failed to update FusionFix's anti-aliasing setting.");
        }
    }

    private static string? FindAntialiasingSection(string text)
    {
        foreach (string section in AntialiasingSections)
        {
            if (IniTextEditor.ReadValue(text, section, "Antialiasing") != null)
                return section;
        }

        return null;
    }

    private static int ReadInt(string text, string key, int fallback)
    {
        string? value = IniTextEditor.ReadValue(text, null, key);
        return int.TryParse(value, out int parsed) ? parsed : fallback;
    }

    private static void BackupOnce(string path)
    {
        string backup = path + ".gtaiv-utility.bak";
        if (File.Exists(backup) || !File.Exists(path))
            return;

        try
        {
            File.Copy(path, backup);
            Logger.Debug($" Backed up DLSS-IV.cfg to {Path.GetFileName(backup)}.");
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, " Could not back up DLSS-IV.cfg.");
        }
    }
}
