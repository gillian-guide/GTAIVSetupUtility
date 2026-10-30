using System.Collections.Frozen;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using GTAIVSetupUtility.Localizations;
using MsBox.Avalonia.Enums;
using NLog;

namespace GTAIVSetupUtility.Services;

public enum PatchIniKind
{
    None,
    ZolikaPatch,
    OpenPreCE
}

public static class HelperService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private const string DefaultVersion = "0.0.0.0";

    private const string OptionsSection = "Options";
    private const string HookingSection = "Hooking";

    public static string GetFileVersion(string filePath)
    {
        if (!File.Exists(filePath)) return DefaultVersion;

        try
        {
            return FileVersionInfo.GetVersionInfo(filePath).FileVersion ?? DefaultVersion;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $" Error retrieving file version for {filePath}", filePath);
            return DefaultVersion;
        }
    }

    private static (bool IsSupported, bool IsRetail) CheckVersionStatus(string version) =>
        version switch
        {
            not null when version.StartsWith("1, 0", StringComparison.Ordinal) => (true, true),
            not null when version.StartsWith("1.2", StringComparison.Ordinal) => (true, false),
            _ => (false, false)
        };

    public static bool IsVersionSupported(string v) => CheckVersionStatus(v).IsSupported;
    public static bool IsRetailVersion(string v) => CheckVersionStatus(v).IsRetail;

    private static readonly FrozenSet<string> CommandlineParamsToRemove = FrozenSet.ToFrozenSet(
    [
        "-no_3GB", "-noprecache", "-notimefix", "-novblank", "-percentvidmem",
        "-memrestrict", "-reserve", "-reservedApp", "-disableimposters",
        "-force2vb", "-minspecaudio"
    ], StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> IncompatiblePatchOptions = FrozenSet.ToFrozenSet(
    [
        "BenchmarkFix", "BikePhoneAnimsFix", "BorderlessWindowed", "BuildingAlphaFix",
        "BuildingDynamicShadows", "CarDynamicShadowFix", "CarPartsShadowFix", "CutsceneFixes",
        "DoNotPauseOnMinimize", "DualVehicleHeadlights", "EmissiveLerpFix", "EpisodicVehicleSupport",
        "EpisodicWeaponSupport", "ForceCarHeadlightShadows", "ForceDynamicShadowsEverywhere",
        "ForceShadowsOnObjects", "HighFPSBikePhysicsFix", "HighFPSSpeedupFix", "HighQualityReflections",
        "ImprovedShaderStreaming", "MouseFix", "NewMemorySystem", "NoLiveryLimit",
        "NoLODLightHeightCutoff", "OutOfCommissionFix", "PoliceEpisodicWeaponSupport",
        "RemoveUselessChecks", "RemoveBoundingBoxCulling", "ReversingLightFix", "SkipIntro", "SkipMenu"
    ], StringComparer.OrdinalIgnoreCase);

    public static async Task CleanCommandlineFileAsync(string folderPath, params string[] additionalParams)
    {
        var toRemove = new HashSet<string>(CommandlineParamsToRemove, StringComparer.OrdinalIgnoreCase);
        toRemove.UnionWith(additionalParams);

        string commandlineFile = Path.Combine(folderPath, "commandline.txt");
        if (!File.Exists(commandlineFile))
            return;

        string[] lines = await File.ReadAllLinesAsync(commandlineFile);

        string[] filteredLines = lines
            .Where(line => !toRemove.Any(parameter =>
                line.Contains(parameter, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (filteredLines.Length == lines.Length)
            return;

        Logger.Debug($" Filtering commandline.txt from: {string.Join(", ", toRemove)}");
        await File.WriteAllLinesAsync(commandlineFile, filteredLines);
        Logger.Info(" commandline.txt has been filtered.");
    }

    public static async Task<bool> CheckOutdatedAsiLoaderAsync(string folderPath)
    {
        if (!File.Exists(Path.Combine(folderPath, "dsound.dll")))
            return false;

        Logger.Info(" dsound.dll detected, recommended to replace.");

        if (!await MessageBoxService.ConfirmAsync(
                Resources.ASILoaderOutdatedTitle,
                Resources.ASILoaderOutdatedDescription))
        {
            return false;
        }

        ShellService.OpenUrl("https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases/latest");
        return true;
    }

    public static (bool isDxvkInstalled, bool isFusionFixLatest) DetectDxvkInstallation(string folderPath)
    {
        bool hasVulkan = File.Exists(Path.Combine(folderPath, "vulkan.dll"));
        bool hasD3d9 = File.Exists(Path.Combine(folderPath, "d3d9.dll"));

        if (hasVulkan) Logger.Info(" Folder has vulkan.dll.");
        if (hasD3d9) Logger.Info(" Folder has d3d9.dll.");

        return (hasVulkan || hasD3d9, hasVulkan && hasD3d9);
    }

    public static async Task<bool> CheckRtssConflictAsync(bool isIvsdkInstalled, string rtssConfig)
    {
        if (!isIvsdkInstalled || string.IsNullOrEmpty(rtssConfig))
            return false;

        var rtssGtaivConfig = new IniEditorService(rtssConfig);
        if (rtssGtaivConfig.ReadValue(HookingSection, "EnableHooking") != "1") return false;
        Logger.Info(" User has DXVK and IVSDK .NET and RTSS enabled at the same time.");
        await MessageBoxService.ShowAsync(
            Resources.RTSSConflictTitle,
            Resources.RTSSConflictDescription,
            Icon.Warning);

        return true;
    }

    public static (string? fusionFixIni, string? fusionFixCfg, string? patchIni, PatchIniKind patchKind, string? xlivelessIni) FindModConfigFiles(string folderPath)
    {
        string? fusionFixIniPath = Directory.EnumerateFiles(folderPath, "GTAIV.EFLC.FusionFix.ini", SearchOption.AllDirectories).FirstOrDefault();
        string? fusionFixCfgPath = Directory.EnumerateFiles(folderPath, "GTAIV.EFLC.FusionFix.cfg", SearchOption.AllDirectories).FirstOrDefault();
        string? zolikaPatchIniPath = Directory.EnumerateFiles(folderPath, "ZolikaPatch.ini", SearchOption.AllDirectories).FirstOrDefault();
        string? openPreCeIniPath = Directory.EnumerateFiles(folderPath, "IV.EFLC.OpenPreCEPatch.ini", SearchOption.AllDirectories).FirstOrDefault();

        // OpenPreCE succeeded ZolikaPatch and keeps its options in its own grouped ini. A leftover
        // ZolikaPatch.ini is only used by OpenPreCE to port values once, so it must stay untouched.
        if (!string.IsNullOrEmpty(openPreCeIniPath))
        {
            string? xlivelessIniPath = Directory.EnumerateFiles(folderPath, "XLivelessAddon.ini", SearchOption.AllDirectories).FirstOrDefault();

            if (!string.IsNullOrEmpty(zolikaPatchIniPath))
            {
                Logger.Info(" OpenPreCEPatch is present - leaving the leftover ZolikaPatch.ini alone.");
            }

            Logger.Info(" OpenPreCEPatch is present.");
            return (fusionFixIniPath, fusionFixCfgPath, openPreCeIniPath, PatchIniKind.OpenPreCE, xlivelessIniPath);
        }

        if (!string.IsNullOrEmpty(zolikaPatchIniPath))
        {
            Logger.Info(" ZolikaPatch is present.");
            return (fusionFixIniPath, fusionFixCfgPath, zolikaPatchIniPath, PatchIniKind.ZolikaPatch, null);
        }

        return (fusionFixIniPath, fusionFixCfgPath, null, PatchIniKind.None, null);
    }

    public static bool IsIvsdkInstalled(string folderPath)
    {
        return Directory.EnumerateFiles(folderPath, "IVSDKDotNet.asi", SearchOption.AllDirectories).Any();
    }

    private static async Task<bool> CheckPatchVersionAsync(IniEditorService iniParser, PatchIniKind patchKind)
    {
        // OpenPreCE is a different, actively maintained project; there is nothing sensible to point
        // the user at and its presence already means they are on the patch they want.
        if (patchKind == PatchIniKind.OpenPreCE)
        {
            Logger.Info(" OpenPreCEPatch is present - version check skipped.");
            return true;
        }

        if (iniParser.ReadValueAnySection("RestoreDeathMusic") != "N/A")
        {
            Logger.Info(" Latest ZolikaPatch is present");
            return true;
        }
        Logger.Info(" ZolikaPatch is present, but outdated.");

        if (!await MessageBoxService.ConfirmAsync(
                Resources.ZPatchOutdatedTitle,
                Resources.ZPatchOutdatedDescription))
        {
            return false;
        }

        ShellService.OpenUrl("https://zolika1351.pages.dev/mods/ivpatch");

        await MessageBoxService.ShowAsync(
            Resources.ZPatchOutdatedTitle,
            Resources.ZPatchOutdatedDescription2,
            Icon.Info);

        ShellService.Restart();
        return false;
    }

    private static string PatchDisplayName(PatchIniKind patchKind)
        => patchKind == PatchIniKind.OpenPreCE ? "OpenPreCEPatch" : "ZolikaPatch";

    // ZolikaPatch keeps every option in [Options] while OpenPreCE groups the very same option names
    // under [STABILITY], [GRAPHICS], [VEHICLES], ... so patch options are always looked up by key.
    private static string ReadPatchOption(IniEditorService iniParser, string key)
        => iniParser.ReadValueAnySection(key);

    private static bool SetPatchOption(IniEditorService iniParser, PatchIniKind patchKind, string key, string value)
    {
        // Options added by newer ZolikaPatch builds belong in [Options]. OpenPreCE categories are
        // fixed, so a missing key means the installed build predates that option and adding it to an
        // arbitrary section would not do anything.
        string? fallbackSection = patchKind == PatchIniKind.ZolikaPatch ? OptionsSection : null;

        if (iniParser.EditValueAnySection(key, value, fallbackSection))
            return true;

        Logger.Warn($" Patch option '{key}' is missing from the ini - skipped.");
        return false;
    }

    private static bool HandleGfwlDlc(string folderPath, IniEditorService iniParser, PatchIniKind patchKind)
    {
        string gfwlDlcPath = Path.Combine(folderPath, "GFWLDLC.asi");
        bool changesMade = false;

        if (ReadPatchOption(iniParser, "LoadDLCs") == "0")
        {
            changesMade = SetPatchOption(iniParser, patchKind, "LoadDLCs", "1");
        }

        if (File.Exists(gfwlDlcPath))
        {
            File.Delete(gfwlDlcPath);
        }

        return changesMade;
    }

    private static async Task<bool> HandleGfwlAchievementsAsync(string folderPath, IniEditorService iniParser, PatchIniKind patchKind)
    {
        string dinput8 = Path.Combine(folderPath, "dinput8.dll");
        string xlive = Path.Combine(folderPath, "xlive.dll");

        if (!File.Exists(dinput8) || File.Exists(xlive))
            return false;

        string description = patchKind == PatchIniKind.OpenPreCE
            // OpenPreCE mirrors achievements to Steam without replacing the GFWL call, so both can
            // stay enabled and there is no Steam Achievements plugin to remove.
            ? Resources.GFWLAchievementsDescriptionOpenPreCE
            : string.Format(Resources.GFWLAchievementsDescription, PatchDisplayName(patchKind));

        bool enable = await MessageBoxService.ConfirmAsync(Resources.GFWLAchievementsTitle, description);

        if (enable)
            EnableGfwlAchievements(folderPath, iniParser, patchKind);
        else
            DisableGfwlAchievements(folderPath, iniParser, patchKind);

        return true;
    }

    private static async Task<bool> FixIncompatiblePatchOptionsAsync(IniEditorService iniParser, PatchIniKind patchKind)
    {
        // OpenPreCE notices FusionFix on its own and disables the overlapping options while loading
        // ("compat: FusionFix present, N overlapping options disabled"). Second-guessing that here
        // would only nag the user about options the mod already ignores.
        if (patchKind == PatchIniKind.OpenPreCE)
        {
            Logger.Info(" OpenPreCEPatch handles FusionFix compatibility on its own - compatibility check skipped.");
            return false;
        }

        var problematicOptions = IncompatiblePatchOptions
            .Where(option => ReadPatchOption(iniParser, option) == "1")
            .ToList();

        if (problematicOptions.Count == 0)
            return false;

        if (!await MessageBoxService.ConfirmAsync(
                string.Format(Resources.ZPatchCompatibilityTitle, PatchDisplayName(patchKind)),
                string.Format(Resources.ZPatchCompatibilityDescription, PatchDisplayName(patchKind))))
        {
            return false;
        }

        foreach (string option in problematicOptions)
        {
            SetPatchOption(iniParser, patchKind, option, "0");
        }

        return true;
    }

    public record ModConfigurationResult(
        string? FusionFixIniPath,
        string? PatchIniPath,
        PatchIniKind PatchKind,
        bool IsFusionFix,
        string? XlivelessIniPath,
        bool PatchLatest);

    public static async Task<ModConfigurationResult> HandleModConfigurationsAsync(
        string folderPath,
        string? fusionFixIniPath,
        string? fusionFixCfgPath,
        string? patchIniPath,
        PatchIniKind patchKind,
        string? xlivelessIniPath)
    {
        bool hasFusionFix = !string.IsNullOrEmpty(fusionFixIniPath) || !string.IsNullOrEmpty(fusionFixCfgPath);
        bool hasPatch = !string.IsNullOrEmpty(patchIniPath);

        return (hasFusionFix, hasPatch) switch
        {
            (false, false) => await HandleNoModsAsync(),
            (true, false) => await HandleFusionFixOnlyAsync(folderPath, fusionFixIniPath, fusionFixCfgPath, xlivelessIniPath),
            (false, true) => await HandlePatchOnlyAsync(folderPath, patchIniPath!, patchKind, xlivelessIniPath),
            (true, true) => await HandleBothModsAsync(folderPath, fusionFixCfgPath, fusionFixIniPath, patchIniPath!, patchKind, xlivelessIniPath)
        };
    }

    private static Task<ModConfigurationResult> HandleNoModsAsync()
    {
        Logger.Debug(" User has neither a patch ini nor FusionFix.");
        return Task.FromResult(new ModConfigurationResult(null, null, PatchIniKind.None, false, null, false));
    }

    private static async Task<ModConfigurationResult> HandleFusionFixOnlyAsync(
        string folderPath,
        string? fusionFixIniPath,
        string? fusionFixCfgPath,
        string? xlivelessIniPath)
    {
        string selectedIniPath = !string.IsNullOrEmpty(fusionFixCfgPath) ? fusionFixCfgPath : fusionFixIniPath!;
        Logger.Debug(" User has FusionFix.");

        await CleanCommandlineFileAsync(folderPath, "-windowed", "-noBlockOnLostFocus");

        return new ModConfigurationResult(selectedIniPath, null, PatchIniKind.None, true, xlivelessIniPath, false);
    }

    private static async Task<ModConfigurationResult> HandlePatchOnlyAsync(
        string folderPath,
        string patchIniPath,
        PatchIniKind patchKind,
        string? xlivelessIniPath)
    {
        Logger.Debug($" User has {PatchDisplayName(patchKind)}.");

        var iniParser = new IniEditorService(patchIniPath);
        bool shouldSave = false;

        bool latest = await CheckPatchVersionAsync(iniParser, patchKind);

        // accumulate changes before saving
        if (HandleGfwlDlc(folderPath, iniParser, patchKind)) shouldSave = true;
        if (await HandleGfwlAchievementsAsync(folderPath, iniParser, patchKind)) shouldSave = true;

        if (shouldSave)
        {
            iniParser.SaveFile();
            await ShowIniModifiedMessageAsync(patchIniPath);
        }

        return new ModConfigurationResult(null, patchIniPath, patchKind, false, xlivelessIniPath, latest);
    }

    private static async Task<ModConfigurationResult> HandleBothModsAsync(
        string folderPath,
        string? fusionFixCfgPath,
        string? fusionFixIniPath,
        string patchIniPath,
        PatchIniKind patchKind,
        string? xlivelessIniPath)
    {
        Logger.Debug($" User has FusionFix and {PatchDisplayName(patchKind)}. Checking compatibility...");

        await CleanCommandlineFileAsync(folderPath, "-windowed", "-noBlockOnLostFocus");

        string selectedFusionFixPath = !string.IsNullOrEmpty(fusionFixCfgPath) ? fusionFixCfgPath : fusionFixIniPath!;
        var iniParserZp = new IniEditorService(patchIniPath);
        bool shouldSave = false;

        bool latest = await CheckPatchVersionAsync(iniParserZp, patchKind);

        if (HandleGfwlDlc(folderPath, iniParserZp, patchKind)) shouldSave = true;
        if (await HandleGfwlAchievementsAsync(folderPath, iniParserZp, patchKind)) shouldSave = true;
        if (await FixIncompatiblePatchOptionsAsync(iniParserZp, patchKind)) shouldSave = true;

        if (shouldSave)
        {
            iniParserZp.SaveFile();
            await ShowIniModifiedMessageAsync(patchIniPath);
        }

        return new ModConfigurationResult(selectedFusionFixPath, patchIniPath, patchKind, true, xlivelessIniPath, latest);
    }

    private static async Task ShowIniModifiedMessageAsync(string patchIniPath)
    {
        string fileName = Path.GetFileName(patchIniPath);
        await MessageBoxService.ShowAsync(
            string.Format(Resources.ZPatchIniModifiedTitle, fileName),
            string.Format(Resources.ZPatchIniModifiedDescription, fileName),
            Icon.Info);
    }

    private static void EnableGfwlAchievements(string folderPath, IniEditorService iniParser, PatchIniKind patchKind)
    {
        // Only the standalone SteamAchievements plugin conflicts with GFWL. OpenPreCE mirrors
        // achievements to Steam on its own and is designed to run both at once.
        string steamAchievements = Path.Combine(folderPath, "SteamAchievements.asi");
        if (patchKind == PatchIniKind.ZolikaPatch && File.Exists(steamAchievements))
        {
            string backupDir = Path.Combine(folderPath, "backup");
            Directory.CreateDirectory(backupDir);
            File.Move(steamAchievements, Path.Combine(backupDir, "SteamAchievements.asi"), true);
        }

        if (ReadPatchOption(iniParser, "TryToSkipAllErrors") == "1")
            SetPatchOption(iniParser, patchKind, "TryToSkipAllErrors", "0");

        if (ReadPatchOption(iniParser, "VSyncFix") == "1")
            SetPatchOption(iniParser, patchKind, "VSyncFix", "0");
    }

    private static void DisableGfwlAchievements(string folderPath, IniEditorService iniParser, PatchIniKind patchKind)
    {
        string backupPath = Path.Combine(folderPath, "backup", "SteamAchievements.asi");
        if (patchKind == PatchIniKind.ZolikaPatch && File.Exists(backupPath))
        {
            File.Move(backupPath, Path.Combine(folderPath, "SteamAchievements.asi"), true);
        }

        if (ReadPatchOption(iniParser, "TryToSkipAllErrors") == "0")
            SetPatchOption(iniParser, patchKind, "TryToSkipAllErrors", "1");

        if (ReadPatchOption(iniParser, "VSyncFix") == "0")
            SetPatchOption(iniParser, patchKind, "VSyncFix", "1");
    }

    public static async Task<int> QueryVramAsync(
        bool isLinux, bool igpuOnly, bool dgpuOnly, bool dxvkOnIgpu, bool gb3)
    {
        int maxVram = gb3 ? 3072 : 4096;

        if (!isLinux)
        {
            try
            {
                int detected = await Task.Run(() => QueryVramFromWmi(igpuOnly, dgpuOnly, dxvkOnIgpu));
                if (detected > 0)
                    return Math.Min(detected, maxVram);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, " VRAM query failed; requesting manual input via UI.");
            }
        }

        string? input = await MessageBoxService.ShowInputAsync(
            Resources.VRAMManualTitle, Resources.VRAMManualDescription);

        if (int.TryParse(input, NumberStyles.Integer, CultureInfo.CurrentCulture, out int manualVram) &&
            manualVram >= 512)
        {
            return Math.Min(manualVram, maxVram);
        }

        return maxVram;
    }

    private static int QueryVramFromWmi(bool igpuOnly, bool dgpuOnly, bool dxvkOnIgpu)
    {
        int[] sizes;

        using (var searcher = new ManagementObjectSearcher("SELECT AdapterRAM FROM Win32_VideoController"))
        using (var videoControllers = searcher.Get())
        {
            sizes = videoControllers
                .Cast<ManagementObject>()
                .Select(controller => controller["AdapterRAM"]?.ToString())
                .Where(value => !string.IsNullOrEmpty(value) && value != "N/A")
                .Select(value => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double bytes)
                    ? (int)(bytes / 1048576) + 1
                    : 0)
                .Where(megabytes => megabytes > 0)
                .ToArray();
        }

        if (sizes.Length == 0)
            throw new InvalidOperationException("WMI returned no video controller memory information.");

        int firstGpu = sizes[0];
        int bestOther = sizes.Skip(1).DefaultIfEmpty(0).Max();
        Logger.Debug($" GPU0 has {firstGpu}MB, best other GPU has {bestOther}MB.");

        if (igpuOnly || dgpuOnly)
            return firstGpu;

        return dxvkOnIgpu ? Math.Min(firstGpu, bestOther) : Math.Max(firstGpu, bestOther);
    }

    public static async Task DeleteFilesAsync(string directory, IEnumerable<string> fileNames)
    {
        await Task.Run(() =>
        {
            foreach (string path in fileNames.Select(file => Path.Combine(directory, file)).Where(File.Exists))
                File.Delete(path);
        });
    }
}
