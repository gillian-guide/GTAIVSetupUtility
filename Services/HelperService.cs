using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GTAIVSetupUtility.Localizations;
using GTAIVSetupUtility.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using NLog;

namespace GTAIVSetupUtility.Services;

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
            not null when version.StartsWith("1, 0") => (true, true),
            not null when version.StartsWith("1.2") => (true, false),
            _ => (false, false)
        };

    public static bool IsVersionSupported(string v) => CheckVersionStatus(v).IsSupported;
    public static bool IsRetailVersion(string v) => CheckVersionStatus(v).IsRetail;

    private static readonly string[] CommandlineParamsToRemove =
    [
        "-no_3GB", "-noprecache", "-notimefix", "-novblank", "-percentvidmem",
        "-memrestrict", "-reserve", "-reservedApp", "-disableimposters",
        "-force2vb", "-minspecaudio"
    ];
    
    public static async Task CleanCommandlineFileAsync(string folderPath, params string[] additionalParams)
    {
        var toRemove = CommandlineParamsToRemove
            .Concat(additionalParams)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        string commandlineFile = Path.Combine(folderPath, "commandline.txt");
        if (!File.Exists(commandlineFile))
            return;

        string[] lines = await File.ReadAllLinesAsync(commandlineFile);
        
        if (!lines.Any(line => toRemove.Any(line.Contains))) 
            return;

        Logger.Debug($" Filtering commandline.txt from: {string.Join(", ", toRemove)}");
        var filteredLines = lines.Where(line => !toRemove.Any(line.Contains));
        await File.WriteAllLinesAsync(commandlineFile, filteredLines);
        Logger.Info(" commandline.txt has been filtered.");
    }

    public static async Task<bool> CheckOutdatedAsiLoaderAsync(string folderPath)
    {
        if (!File.Exists(Path.Combine(folderPath, "dsound.dll")))
            return false;

        Logger.Info(" dsound.dll detected, recommended to replace.");

        var result = await MessageBoxManager.GetMessageBoxStandard(
            Resources.ASILoaderOutdatedTitle,
            Resources.ASILoaderOutdatedDescription.Replace("\\n", "\n"),
            ButtonEnum.YesNo,
            Icon.Warning
        ).ShowAsync();

        if (result != ButtonResult.Yes) return false;
        
        MainWindowViewModel.OpenHyperlink("https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases/latest");
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
        await MessageBoxManager.GetMessageBoxStandard(
            Resources.RTSSConflictTitle,
            Resources.RTSSConflictDescription.Replace("\\n", "\n"),
            ButtonEnum.Ok,
            Icon.Warning
        ).ShowAsync();

        return true;
    }

    public static (string? fusionFixIni, string? fusionFixCfg, string? zolikaPatchIni) FindModConfigFiles(string folderPath)
    {
        string? fusionFixIniPath = Directory.EnumerateFiles(folderPath, "GTAIV.EFLC.FusionFix.ini", SearchOption.AllDirectories).FirstOrDefault();
        string? fusionFixCfgPath = Directory.EnumerateFiles(folderPath, "GTAIV.EFLC.FusionFix.cfg", SearchOption.AllDirectories).FirstOrDefault();
        string? zolikaPatchIniPath = Directory.EnumerateFiles(folderPath, "ZolikaPatch.ini", SearchOption.AllDirectories).FirstOrDefault();

        return (fusionFixIniPath, fusionFixCfgPath, zolikaPatchIniPath);
    }

    public static bool IsIvsdkInstalled(string folderPath)
    {
        return Directory.EnumerateFiles(folderPath, "IVSDKDotNet.asi", SearchOption.AllDirectories).Any();
    }

    public static bool zpatchLatest;

    private static async Task<bool> CheckZolikaPatchVersionAsync(IniEditorService iniParser)
    {
        if (iniParser.ReadValue(OptionsSection, "RestoreDeathMusic") != "N/A")
        {
            Logger.Info(" Latest ZolikaPatch is present");
            zpatchLatest = true;
            return true;
        }
        Logger.Info(" ZolikaPatch is present, but outdated.");

        var result = await MessageBoxManager.GetMessageBoxStandard(
            Resources.ZPatchOutdatedTitle,
            Resources.ZPatchOutdatedDescription.Replace("\\n", "\n"),
            ButtonEnum.YesNo,
            Icon.Question
        ).ShowAsync();

        if (result != ButtonResult.Yes) return false;
        
        MainWindowViewModel.OpenHyperlink("https://zolika1351.pages.dev/mods/ivpatch");

        await MessageBoxManager.GetMessageBoxStandard(
            Resources.ZPatchOutdatedTitle,
            Resources.ZPatchOutdatedDescription2.Replace("\\n", "\n"),
            ButtonEnum.Ok,
            Icon.Info
        ).ShowAsync();

        MainWindowViewModel.RestartApplication();
        return false;
    }
    
    private static bool HandleGfwlDlc(string folderPath, IniEditorService iniParser)
    {
        string gfwlDlcPath = Path.Combine(folderPath, "GFWLDLC.asi");
        bool changesMade = false;

        if (File.Exists(gfwlDlcPath))
        {
            if (iniParser.ReadValue(OptionsSection, "LoadDLCs") == "0")
            {
                iniParser.EditValue(OptionsSection, "LoadDLCs", "1");
                changesMade = true;
            }
            File.Delete(gfwlDlcPath);
        }
        else if (iniParser.ReadValue(OptionsSection, "LoadDLCs") == "0")
        {
            iniParser.EditValue(OptionsSection, "LoadDLCs", "1");
            changesMade = true;
        }

        return changesMade;
    }

    private static async Task<bool> HandleGfwlAchievementsAsync(string folderPath, IniEditorService iniParser)
    {
        string dinput8 = Path.Combine(folderPath, "dinput8.dll");
        string xlive = Path.Combine(folderPath, "xlive.dll");

        if (!File.Exists(dinput8) || File.Exists(xlive))
            return false;

        var result = await MessageBoxManager.GetMessageBoxStandard(
            Resources.GFWLAchievementsTitle,
            Resources.GFWLAchievementsDescription.Replace("\\n", "\n"),
            ButtonEnum.YesNo,
            Icon.Question
        ).ShowAsync();

        if (result == ButtonResult.Yes)
        {
            EnableGfwlAchievements(folderPath, iniParser);
        }
        else
        {
            DisableGfwlAchievements(folderPath, iniParser);
        }
        
        return true;
    }

    private static async Task<bool> FixIncompatibleZolikaPatchOptionsAsync(IniEditorService iniParser)
    { 
        string[] incompatibleOptions =
        [
            "BenchmarkFix", "BikePhoneAnimsFix", "BorderlessWindowed", "BuildingAlphaFix",
            "BuildingDynamicShadows", "CarDynamicShadowFix", "CarPartsShadowFix", "CutsceneFixes",
            "DoNotPauseOnMinimize", "DualVehicleHeadlights", "EmissiveLerpFix", "EpisodicVehicleSupport",
            "EpisodicWeaponSupport", "ForceCarHeadlightShadows", "ForceDynamicShadowsEverywhere",
            "ForceShadowsOnObjects", "HighFPSBikePhysicsFix", "HighFPSSpeedupFix", "HighQualityReflections",
            "ImprovedShaderStreaming", "MouseFix", "NewMemorySystem", "NoLiveryLimit",
            "NoLODLightHeightCutoff", "OutOfCommissionFix", "PoliceEpisodicWeaponSupport",
            "RemoveUselessChecks", "RemoveBoundingBoxCulling", "ReversingLightFix", "SkipIntro", "SkipMenu"
        ];

        var problematicOptions = incompatibleOptions
            .Where(option => iniParser.ReadValue(OptionsSection, option) == "1")
            .ToList();

        if (problematicOptions.Count == 0)
            return false;

        var result = await MessageBoxManager.GetMessageBoxStandard(
            Resources.ZPatchCompatibilityTitle,
            Resources.ZPatchCompatibilityDescription.Replace("\\n", "\n"),
            ButtonEnum.YesNo,
            Icon.Question
        ).ShowAsync();

        if (result != ButtonResult.Yes) return false;
        
        foreach (string option in problematicOptions)
        {
            iniParser.EditValue(OptionsSection, option, "0");
        }
        
        return true;
    }

    public record ModConfigurationResult(
        string? FusionFixIniPath,
        string? ZolikaPatchIniPath,
        bool IsFusionFix);

    public static async Task<ModConfigurationResult> HandleModConfigurationsAsync(string folderPath, string? fusionFixIniPath, string? fusionFixCfgPath, string? zolikaPatchIniPath)
    {
        bool hasFusionFix = !string.IsNullOrEmpty(fusionFixIniPath);
        bool hasZolikaPatch = !string.IsNullOrEmpty(zolikaPatchIniPath);

        return (hasFusionFix, hasZolikaPatch) switch
        {
            (false, false) => await HandleNoModsAsync(),
            (true, false) => await HandleFusionFixOnlyAsync(folderPath, fusionFixIniPath!, fusionFixCfgPath),
            (false, true) => await HandleZolikaPatchOnlyAsync(folderPath, zolikaPatchIniPath!),
            (true, true) => await HandleBothModsAsync(folderPath, fusionFixCfgPath!, zolikaPatchIniPath!)
        };
    }

    private static Task<ModConfigurationResult> HandleNoModsAsync()
    {
        Logger.Debug(" User has neither ZolikaPatch nor FusionFix.");
        return Task.FromResult(new ModConfigurationResult(null, null, false));
    }

    private static async Task<ModConfigurationResult> HandleFusionFixOnlyAsync(string folderPath, string fusionFixIniPath, string? fusionFixCfgPath)
    {
        string selectedIniPath = !string.IsNullOrEmpty(fusionFixCfgPath) ? fusionFixCfgPath : fusionFixIniPath;
        Logger.Debug(" User has FusionFix.");
        
        await CleanCommandlineFileAsync(folderPath, "-windowed", "-noBlockOnLostFocus");
        
        return new ModConfigurationResult(selectedIniPath, null, true);
    }

    private static async Task<ModConfigurationResult> HandleZolikaPatchOnlyAsync(string folderPath, string zolikaPatchIniPath)
    {
        Logger.Debug(" User has ZolikaPatch.");
        
        var iniParser = new IniEditorService(zolikaPatchIniPath);
        bool shouldSave = false;

        await CheckZolikaPatchVersionAsync(iniParser);
        
        // accumulate changes before saving
        if (HandleGfwlDlc(folderPath, iniParser)) shouldSave = true;
        if (await HandleGfwlAchievementsAsync(folderPath, iniParser)) shouldSave = true;

        if (!shouldSave)
        {
            iniParser.SaveFile();
            await MainWindowViewModel.ShowMessageBoxAsync(
                Resources.ZPatchIniModifiedTitle, 
                Resources.ZPatchIniModifiedDescription,
                Icon.Info
                );
        }
        
        return new ModConfigurationResult(null, zolikaPatchIniPath, false);
    }

    private static async Task<ModConfigurationResult> HandleBothModsAsync(string folderPath, string fusionFixCfgPath, string zolikaPatchIniPath)
    {
        Logger.Debug(" User has FusionFix and ZolikaPatch. Checking compatibility...");
        
        await CleanCommandlineFileAsync(folderPath, "-windowed", "-noBlockOnLostFocus");

        var iniParserZp = new IniEditorService(zolikaPatchIniPath);
        bool shouldSave = false;

        await CheckZolikaPatchVersionAsync(iniParserZp);
        
        if (HandleGfwlDlc(folderPath, iniParserZp)) shouldSave = true;
        if (await HandleGfwlAchievementsAsync(folderPath, iniParserZp)) shouldSave = true;
        if (await FixIncompatibleZolikaPatchOptionsAsync(iniParserZp)) shouldSave = true;
        
        if (shouldSave)
        {
            iniParserZp.SaveFile();
            await MainWindowViewModel.ShowMessageBoxAsync(
                Resources.ZPatchIniModifiedTitle, 
                Resources.ZPatchIniModifiedDescription,
                Icon.Info
            );
        }

        return new ModConfigurationResult(fusionFixCfgPath, zolikaPatchIniPath, true);
    }

    private static void EnableGfwlAchievements(string folderPath, IniEditorService iniParser)
    {
        string steamAchievements = Path.Combine(folderPath, "SteamAchievements.asi");
        if (File.Exists(steamAchievements))
        {
            string backupDir = Path.Combine(folderPath, "backup");
            Directory.CreateDirectory(backupDir);
            File.Move(steamAchievements, Path.Combine(backupDir, "SteamAchievements.asi"), true);
        }

        if (iniParser.ReadValue(OptionsSection, "TryToSkipAllErrors") == "1")
            iniParser.EditValue(OptionsSection, "TryToSkipAllErrors", "0");

        if (iniParser.ReadValue(OptionsSection, "VSyncFix") == "1")
            iniParser.EditValue(OptionsSection, "VSyncFix", "0");
    }

    private static void DisableGfwlAchievements(string folderPath, IniEditorService iniParser)
    {
        string backupPath = Path.Combine(folderPath, "backup", "SteamAchievements.asi");
        if (File.Exists(backupPath))
        {
            File.Move(backupPath, Path.Combine(folderPath, "SteamAchievements.asi"), true);
        }

        if (iniParser.ReadValue(OptionsSection, "TryToSkipAllErrors") == "0")
            iniParser.EditValue(OptionsSection, "TryToSkipAllErrors", "1");

        if (iniParser.ReadValue(OptionsSection, "VSyncFix") == "0")
            iniParser.EditValue(OptionsSection, "VSyncFix", "1");
    }
    
    public static async Task<int> QueryVram(bool isLinux, bool igpuOnly, bool dgpuOnly, bool dxvkOnIgpu, bool gb3)
    {
        int maxVram = gb3 ? 3072 : 4096;
        
        return await Task.Run(async () =>
        {
            try
            {
                if (isLinux) throw new PlatformNotSupportedException("Linux WMI check skipped.");

                int vram1 = 0;
                int vram2 = 0;
                bool firstGpuFound = false;

                using (var searcher = new ManagementObjectSearcher("SELECT AdapterRAM FROM Win32_VideoController"))
                using (var videoControllers = searcher.Get())
                {
                    foreach (var o in videoControllers)
                    {
                        var obj = (ManagementObject)o;
                        string? adapterRam = obj["AdapterRAM"]?.ToString();
                        if (string.IsNullOrEmpty(adapterRam) || adapterRam == "N/A") continue;

                        if (!double.TryParse(adapterRam, out double bytes)) continue;
                        int mb = (int)(bytes / 1048576) + 1;

                        if (!firstGpuFound)
                        {
                            Logger.Debug($"GPU0 has {mb}MB");
                            vram1 = mb;
                            firstGpuFound = true;
                        }
                        else if (mb > vram1 || mb > vram2)
                        {
                            Logger.Debug($"Next GPU has {mb}MB");
                            vram2 = mb;
                        }
                    }
                }

                int finalVram;
                if (igpuOnly || dgpuOnly)
                {
                    finalVram = vram1;
                }
                else
                {
                    finalVram = !dxvkOnIgpu ? Math.Max(vram1, vram2) : Math.Min(vram1, vram2);
                }

                return Math.Min(finalVram, maxVram);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "VRAM query failed; requesting manual input via UI.");
                
                return await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    int manualMax = gb3 ? 3072 : 4096;

                    string input = await ShowInputBox(Resources.VRAMManualTitle, Resources.VRAMManualDescription);
                    if (int.TryParse(input, out int manualVram))
                    {
                        if (manualVram < 512)
                        {
                            manualVram = manualMax;
                        }
                    }
                    else
                    {
                        manualVram = manualMax;
                    }
                    
                    manualVram = Math.Min(manualVram, manualMax);

                    return manualVram;
                });
            }
        });
    }

    private static async Task<string> ShowInputBox(string title, string text)
    {
        var window = new Window
        {
            Title = title,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            TransparencyLevelHint =
            [
                WindowTransparencyLevel.Mica, 
                WindowTransparencyLevel.AcrylicBlur
            ],
            Background = Brushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight
        };

        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        var label = new TextBlock { Text = text.Replace("\\n", "\n"), TextWrapping = TextWrapping.Wrap };
        var textBox = new TextBox();
        var okButton = new Button 
        { 
            Content = "OK", 
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true
        };

        okButton.Click += (_, _) => window.Close(textBox.Text);

        panel.Children.Add(label);
        panel.Children.Add(textBox);
        panel.Children.Add(okButton);
        window.Content = panel;

        // Find the main window to center this dialog over
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return await window.ShowDialog<string>(desktop.MainWindow);
        }
        
        // Fallback if no main window found
        window.Show();
        return ""; 
    }

    public static async Task DeleteFiles(string directory, List<string> filenames)
    {
        await Task.Run(() =>
        {
            foreach (string path in filenames.Select(file => Path.Combine(directory, file)).Where(File.Exists))
            {
                File.Delete(path);
            }
        });
    }
}