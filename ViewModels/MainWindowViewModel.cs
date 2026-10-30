using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTAIVSetupUtility.Localizations;
using GTAIVSetupUtility.Models;
using GTAIVSetupUtility.Modules.DlssIv;
using GTAIVSetupUtility.Services;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using NLog;
using FontWeight = Avalonia.Media.FontWeight;
using TextDecorationCollection = Avalonia.Media.TextDecorationCollection;
using TextDecorations = Avalonia.Media.TextDecorations;

namespace GTAIVSetupUtility.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectoryLabelText))]
    public partial string GameDirectoryPath { get; set; } = string.Empty;

    public string DirectoryLabelText => string.IsNullOrWhiteSpace(GameDirectoryPath)
        ? Resources.DirectoryLabelUnselected
        : Resources.DirectoryLabelSelected;

    [ObservableProperty]
    public partial FontWeight DirectoryLabelFontWeight { get; set; } = FontWeight.SemiBold;

    [ObservableProperty]
    public partial TextDecorationCollection? DirectoryLabelDecorations { get; set; } = TextDecorations.Underline;

    [ObservableProperty]
    public partial TextDecorationCollection? TipsNoteDecorations { get; set; }

    [ObservableProperty]
    public partial bool IsDirectoryButtonDefault { get; set; } = true;

    [ObservableProperty]
    public partial bool IsDirectoryButtonEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowTips { get; set; } = true;

    [ObservableProperty]
    public partial bool IsDxvkPanelEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsLaunchOptionsPanelEnabled { get; set; }

    [ObservableProperty]
    public partial bool InstallAsync { get; set; }

    [ObservableProperty]
    public partial bool IsAsyncEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsAsyncVisible { get; set; }

    [ObservableProperty]
    public partial bool EnableVSync { get; set; } = true;

    [ObservableProperty]
    public partial bool SetMaxFrameLatency { get; set; } = true;

    [ObservableProperty]
    public partial bool NoRestrictions { get; set; } = true;

    [ObservableProperty]
    public partial bool NoMemRestrict { get; set; } = true;

    [ObservableProperty]
    public partial bool AvailableVidMem { get; set; } = true;

    [ObservableProperty]
    public partial bool IsVidMemCheckboxEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsVidMem3GB { get; set; } = true;

    [ObservableProperty]
    public partial bool IsVidMemRadioEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool BorderlessWindowed { get; set; } = true;

    [ObservableProperty]
    public partial bool IsBorderlessWindowedEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool MonitorDetails { get; set; }

    [ObservableProperty]
    public partial bool IsUninstallDxvkVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DxvkButtonText))]
    public partial DxvkState InstallationState { get; set; } = DxvkState.NotInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DxvkButtonText))]
    public partial int DownloadProgress { get; set; }

    public string DxvkButtonText => InstallationState switch
    {
        DxvkState.Installed => Resources.DXVKTextInstalled,
        DxvkState.NotInstalled => Resources.DXVKTextNotInstalled,
        DxvkState.Downloading => string.Format(Resources.DXVKTextDownloading, DownloadProgress),
        DxvkState.Installing => Resources.DXVKTextInstalling,
        _ => Resources.DXVKTextNotInstalled
    };
    [ObservableProperty]
    public partial double DxvkButtonWidth { get; set; } = 200;

    [ObservableProperty]
    public partial FontWeight DxvkButtonFontWeight { get; set; } = FontWeight.SemiBold;

    [ObservableProperty]
    public partial bool IsDxvkButtonDefault { get; set; }

    [ObservableProperty]
    public partial FontWeight LaunchOptionsButtonFontWeight { get; set; } = FontWeight.Normal;

    [ObservableProperty]
    public partial bool IsLaunchOptionsButtonDefault { get; set; }

    public bool IsLinux { get; set; }
    public DlssIvViewModel DlssIv { get; } = new();
    private GpuInfo? _defaultGpu;
    private IReadOnlyList<GpuInfo> _gpus = [];
    private bool _ffix;
    private bool _ffixLatest;
    private bool _zpatch;
    private bool _zpatchLatest;
    private PatchIniKind _patchKind;
    private string? _patchIniPath;
    private string? _xlivelessIniPath;
    private bool _isRetail;
    private bool _isIvsdkInstalled;
    private bool _dxvkOnIgpu;
    private readonly string _rtssConfig = File.Exists(@"C:\Program Files (x86)\RivaTuner Statistics Server\Profiles\GTAIV.exe.cfg")
        ? @"C:\Program Files (x86)\RivaTuner Statistics Server\Profiles\GTAIV.exe.cfg"
        : File.Exists(@"C:\Program Files (x86)\RivaTuner Statistics Server\Profiles\Global")
            ? @"C:\Program Files (x86)\RivaTuner Statistics Server\Profiles\Global"
            : string.Empty;

    private bool _rtssConflict;
    private string? _iniPath = string.Empty;

    public int InstallDxvk { get; set; }
    private int _vkDgpuDxvkSupport;
    private int _vkIgpuDxvkSupport;
    private int _gplSupport;
    private bool _igpuOnly = true;
    private bool _dgpuOnly = true;
    private bool _intelIgpu;
    private bool _enableAsync;

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public MainWindowViewModel()
    {
        DlssIv.ModEnabledChanged += OnDlssIvModEnabledChanged;
        Logger.Info(" Application sucessfully initialized!");
    }

    private async void OnDlssIvModEnabledChanged(object? sender, EventArgs e)
    {
        try
        {
            await EnsureDlssIvPrerequisitesAsync();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, " Could not prepare the DLSS-IV prerequisites.");
        }
    }

    private static string GetAssemblyVersion()
    {
        return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
               ?? string.Empty;
    }

    /// <summary>
    /// Stores the probe result and configures the DLSS-IV module. Used on Linux/Wine, where
    /// DXVK installation is disabled but DLSS/FSR still work through the host's DXVK.
    /// </summary>
    public Task ReceiveHardwareInfoAsync(VulkanCheckResult vulkanInfo)
    {
        ApplyVulkanHardwareInfo(vulkanInfo);
        if (vulkanInfo.Gpus.Count == 0)
            DlssIv.SetHardwareUnavailable();
        else
            DlssIv.SetHardware(_defaultGpu, DxvkFeatureTier);
        return Task.CompletedTask;
    }

    public async Task ReceiveVulkanInfoAsync(VulkanCheckResult vulkanInfo)
    {
        ApplyVulkanHardwareInfo(vulkanInfo);

        switch (_igpuOnly)
        {
            case true when !_dgpuOnly:
                Logger.Debug($" User's PC only has an iGPU. Setting Install DXVK to {_vkIgpuDxvkSupport}.");
                InstallDxvk = _vkIgpuDxvkSupport;
                break;
            case false when _dgpuOnly:
                Logger.Debug($" User's PC only has a dGPU. Setting Install DXVK to {_vkDgpuDxvkSupport}.");
                InstallDxvk = _vkDgpuDxvkSupport;
                break;
            case false when !_dgpuOnly:
                Logger.Debug(" User's PC has both an iGPU and a dGPU. Doing further checks...");
                switch (_vkDgpuDxvkSupport, _vkIgpuDxvkSupport)
                {
                    case (0, 1):
                    case (0, 2):
                    case (0, 3):
                        Logger.Debug(" User PC's iGPU supports DXVK, but their dGPU does not - asking them what to do...");
                        if (await MessageBoxService.ConfirmAsync(
                                Resources.InstallDXVKPrompt,
                                Resources.InstallDXVKPromptDescription))
                        {
                            Logger.Debug(" User chose to install DXVK for the iGPU");
                            _dxvkOnIgpu = true;
                            Logger.Debug($" Setting Install DXVK to {_vkIgpuDxvkSupport}");
                            InstallDxvk = _vkIgpuDxvkSupport;
                        }
                        else
                        {
                            Logger.Debug(" User chose not to install DXVK.");
                        }
                        break;
                    case (1, 2):
                    case (1, 3):
                    case (2, 3):
                        Logger.Debug(" User PC's iGPU supports DXVK, but their dGPU supports an inferior version - asking them what to do...");
                        if (await MessageBoxService.ConfirmAsync(
                                Resources.WhichDXVKPrompt,
                                Resources.WhichDXVKPromptDescription))
                        {
                            Logger.Debug($" User chose to install DXVK for the dGPU. Setting Install DXVK to {_vkDgpuDxvkSupport}.");
                            InstallDxvk = _vkDgpuDxvkSupport;
                        }
                        else
                        {
                            Logger.Debug($" User chose to install DXVK for the iGPU. Setting Install DXVK to {_vkIgpuDxvkSupport}.");
                            _dxvkOnIgpu = true;
                            InstallDxvk = _vkIgpuDxvkSupport;
                        }
                        break;
                    case (3, 3):
                    case (2, 2):
                    case (1, 1):
                    case (2, 1):
                    case (3, 1):
                    case (3, 2):
                    case (3, 0):
                    case (2, 0):
                    case (1, 0):
                        Logger.Debug($" User's GPU supports the same or a better version of DXVK as the iGPU. Setting Install DXVK to {_vkDgpuDxvkSupport}");
                        InstallDxvk = _vkDgpuDxvkSupport;
                        break;
                }

                break;
        }

        if (_intelIgpu && _igpuOnly)
        {
            Logger.Debug(" User's PC only has an Intel iGPU. Prompting them to install DXVK 1.10.1.");
            if (await MessageBoxService.ConfirmAsync(
                    Resources.InteliGPUDXVKPrompt,
                    Resources.InteliGPUDXVKPromptDescription))
            {
                Logger.Debug(" Setting Install DXVK to -1 - a special case to install 1.10.1 for Intel iGPU's.");
                InstallDxvk = -1;
            }
        }

        if (_gplSupport != 2 || InstallDxvk < 2)
        {
            IsAsyncVisible = true;
            IsAsyncEnabled = true;
            InstallAsync = true;
            Logger.Debug(" User's GPU doesn't support GPL in full, enable async toggle.");
        }
        else if (_enableAsync)
        {
            IsAsyncVisible = true;
            IsAsyncEnabled = true;
            Logger.Debug(" One of user's GPU doesn't support GPL in full, allow enabling async for an edge case scenario.");
        }

        if (vulkanInfo.Gpus.Count == 0)
            DlssIv.SetHardwareUnavailable();
        else
            DlssIv.SetHardware(_defaultGpu, DxvkFeatureTier);

        Logger.Info(" Vulkan check finished!");
    }

    private void ApplyVulkanHardwareInfo(VulkanCheckResult vulkanInfo)
    {
        _vkDgpuDxvkSupport = vulkanInfo.VkDgpuDxvkSupport;
        _vkIgpuDxvkSupport = vulkanInfo.VkIgpuDxvkSupport;
        _gplSupport = vulkanInfo.GplSupport;
        _igpuOnly = vulkanInfo.IgpuOnly;
        _dgpuOnly = vulkanInfo.DgpuOnly;
        _intelIgpu = vulkanInfo.IntelIgpu;
        _enableAsync = vulkanInfo.EnableAsync;
        _gpus = vulkanInfo.Gpus;
        _defaultGpu = vulkanInfo.DefaultGpu;
    }

    /// <summary>
    /// The DXVK feature tier of the GPU Windows (or Wine) actually uses for the game.
    /// This is what gates DLSS-IV, independently of which DXVK build we install.
    /// </summary>
    private int DxvkFeatureTier => _defaultGpu?.DxvkSupport ?? 0;

    private static async Task<IStorageFolder?> ShowFolderPickerAsync()
    {
        var topLevel = TopLevel.GetTopLevel(
            Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null);

        if (topLevel == null)
            return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Resources.SelectGTAIVFolder,
            AllowMultiple = false,
            SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(
                @"C:\Program Files (x86)\Steam\steamapps\Grand Theft Auto IV\GTAIV")
        });

        return folders.Count > 0 ? folders[0] : null;
    }

    [RelayCommand]
    private async Task SelectDirectoryAsync()
    {
        Logger.Info(" User is selecting the game folder...");

        while (true)
        {
            var folder = await ShowFolderPickerAsync();
            if (folder == null) break; // User cancelled

            if (await TryOpenGameDirectoryAsync(folder.Path.LocalPath))
                break;
        }
    }

    /// <summary>
    /// Opens a game folder without the picker. Used by the DEBUG-only --game-dir hook.
    /// </summary>
    public async Task OpenGameDirectoryAsync(string folderPath) =>
        await TryOpenGameDirectoryAsync(folderPath);

    private async Task<bool> TryOpenGameDirectoryAsync(string folderPath)
    {
        string gameExePath = Path.Combine(folderPath, "GTAIV.exe");

        Logger.Info(" Checking the selected game folder...");

        if (!File.Exists(gameExePath))
        {
            await MessageBoxService.ShowAsync(
                Resources.InvalidFolderTitle,
                Resources.InvalidFolderNoGTAIV,
                Icon.Error);
            return false;
        }

        string gameVersion = HelperService.GetFileVersion(gameExePath);

        if (!HelperService.IsVersionSupported(gameVersion))
        {
            await MessageBoxService.ShowAsync(
                Resources.InvalidFolderTitle,
                Resources.InvalidFolderWrongVersion,
                Icon.Error);
            return false;
        }

        Logger.Info($" Selected valid game folder. Version: {gameVersion}");
        IsDirectoryButtonDefault = false;
        await ProcessGameDirectoryAsync(folderPath, gameVersion);
        return true;
    }

    private async Task ProcessGameDirectoryAsync(string folderPath, string gameVersion)
    {
        DlssIv.ClearGame();
        await HelperService.CleanCommandlineFileAsync(folderPath);

        _isRetail = HelperService.IsRetailVersion(gameVersion);
        bool isSupported = HelperService.IsVersionSupported(gameVersion);

        switch (_isRetail)
        {
            case true when !isSupported:
                Logger.Info(" Folder contains a retail exe, but of an unsupported version. Disabling -availablevidmem toggle.");
                IsVidMemCheckboxEnabled = false;
                IsVidMemRadioEnabled = false;
                break;
            case false:
                {
                    Logger.Info(" Folder contains Steam/Rockstar Launcher version.");
                    if (File.Exists(Path.Combine(folderPath, "commandline.txt")))
                    {
                        Logger.Info(" commandline.txt detected on Steam Version...");
                        await MessageBoxService.ShowAsync(
                            Resources.SteamVersionNoticeTitle,
                            Resources.SteamVersionNoticeDescription,
                            Icon.Info);
                    }

                    break;
                }
        }

        await HelperService.CheckOutdatedAsiLoaderAsync(folderPath);

        ResetVars();
        GameDirectoryPath = folderPath;
        DirectoryLabelFontWeight = FontWeight.Normal;
        DirectoryLabelDecorations = null;
        TipsNoteDecorations = TextDecorations.Underline;
        IsLaunchOptionsPanelEnabled = true;
        IsDirectoryButtonDefault = false;

        (string? fusionFixIni, string? fusionFixCfg, string? patchIni, PatchIniKind patchKind, string? xlivelessIni) =
            HelperService.FindModConfigFiles(folderPath);

        var modConfig = await HelperService.HandleModConfigurationsAsync(
            folderPath,
            fusionFixIni,
            fusionFixCfg,
            patchIni,
            patchKind,
            xlivelessIni);

        _iniPath = modConfig.FusionFixIniPath;
        _ffix = modConfig.IsFusionFix;
        _patchIniPath = modConfig.PatchIniPath;
        _patchKind = modConfig.PatchKind;
        _xlivelessIniPath = modConfig.XlivelessIniPath;

        (bool isDxvkInstalled, bool isFusionFixLatest) = HelperService.DetectDxvkInstallation(folderPath);
        _ffixLatest = isFusionFixLatest;
        InstallationState = isDxvkInstalled ? DxvkState.Installed : DxvkState.NotInstalled;

        switch (_ffix)
        {
            case true when !_ffixLatest:
                {
                    Logger.Info(" FusionFix is present, but outdated.");
                    var result = await MessageBoxManager.GetMessageBoxStandard(
                        Resources.FusionFixOutdatedTitle,
                        Resources.FusionFixOutdatedDescription.Replace("\\n", "\n"),
                        ButtonEnum.YesNo,
                        Icon.Info
                    ).ShowAsync();

                    if (result != ButtonResult.Yes)
                    {
                        OpenHyperlink("https://github.com/ThirteenAG/GTAIV.EFLC.FusionFix/releases/latest");
                    }

                    break;
                }
            case true when _ffixLatest:
                Logger.Info(" Latest FusionFix is present.");
                break;
        }

        await ConfigureDxvkPanelAsync(folderPath, isDxvkInstalled);

        if (string.IsNullOrEmpty(fusionFixIni) && string.IsNullOrEmpty(fusionFixCfg) && string.IsNullOrEmpty(patchIni))
        {
            BorderlessWindowed = false;
            IsBorderlessWindowedEnabled = false;
        }
        else if (patchKind == PatchIniKind.OpenPreCE &&
                 string.IsNullOrEmpty(fusionFixIni) && string.IsNullOrEmpty(fusionFixCfg) &&
                 string.IsNullOrEmpty(xlivelessIni))
        {
            // OpenPreCE keeps BorderlessWindowed in its paired XLivelessAddon.ini; without it
            // there is nothing this checkbox could change.
            Logger.Info(" OpenPreCEPatch is present without XLivelessAddon.ini - disabling Borderless Windowed.");
            BorderlessWindowed = false;
            IsBorderlessWindowedEnabled = false;
        }

        if (!string.IsNullOrEmpty(patchIni))
        {
            _zpatch = true;
            _zpatchLatest = modConfig.PatchLatest;
        }

        DlssIv.InitializeForGame(folderPath, _ffix, fusionFixCfg);

        if (DlssIv.IsModEnabled)
            await EnsureDlssIvPrerequisitesAsync();
    }

    private async Task ConfigureDxvkPanelAsync(string folderPath, bool isDxvkInstalled)
    {
        if (InstallDxvk == 0)
        {
            IsDxvkPanelEnabled = false;
            Logger.Info(" DXVK is not supported - disabling the DXVK panel.");
            return;
        }

        IsDxvkPanelEnabled = true;
        Logger.Info(" Enabled the DXVK panel.");

        if (!isDxvkInstalled)
        {
            Logger.Info(" DXVK is not installed.");
            InstallationState = DxvkState.NotInstalled;
            IsDxvkButtonDefault = true;
            return;
        }

        Logger.Info(" DXVK is (likely) installed.");

        if (_ffixLatest)
        {
            IsVidMemCheckboxEnabled = false;
            AvailableVidMem = false;
            IsVidMemRadioEnabled = false;
            IsUninstallDxvkVisible = false;
        }
        else
        {
            IsUninstallDxvkVisible = true;
            DxvkButtonWidth = 120;
        }

        IsDxvkButtonDefault = false;
        DxvkButtonFontWeight = FontWeight.Normal;
        InstallationState = DxvkState.Installed;

        IsLaunchOptionsButtonDefault = true;
        LaunchOptionsButtonFontWeight = FontWeight.SemiBold;
        MonitorDetails = true;

        await HelperService.CleanCommandlineFileAsync(folderPath, "-managed");

        _isIvsdkInstalled = HelperService.IsIvsdkInstalled(folderPath);
        if (_isIvsdkInstalled) { Logger.Info(" Folder has IVSDKDotNet."); }

        if (await HelperService.CheckRtssConflictAsync(_isIvsdkInstalled, _rtssConfig))
        {
            _rtssConflict = true;
        }
    }

    /// <summary>
    /// DLSS-IV is a Vulkan/FusionFix mod. Whenever it is enabled, make sure the latest DXVK is
    /// present and switch FusionFix (and d3d9.cfg) to Vulkan. If Vulkan cannot work on this GPU,
    /// both switches are forced back to Direct3D instead.
    /// </summary>
    private async Task EnsureDlssIvPrerequisitesAsync()
    {
        if (!DlssIv.IsModEnabled || !DlssIv.HasFusionFix || string.IsNullOrWhiteSpace(GameDirectoryPath))
            return;

        // Without a probe result we cannot tell whether Vulkan works, so leave the user's
        // existing switches alone instead of forcing them off.
        if (!DlssIv.HasHardwareInfo)
        {
            Logger.Warn(" Vulkan hardware information is unavailable; leaving the API switches unchanged.");
            return;
        }

        bool vulkanAvailable = DlssIv.IsVulkanCapable && (IsLinux || InstallDxvk == 3);
        if (vulkanAvailable && !IsLinux)
            vulkanAvailable = await EnsureLatestDxvkAsync();

        // The user may have turned the mod off while DXVK was downloading.
        if (!DlssIv.IsModEnabled)
        {
            DlssIv.SetVulkanEnabled(false);
            return;
        }

        DlssIv.SetVulkanEnabled(vulkanAvailable);
    }

    private bool _isEnsuringDxvk;

    private async Task<bool> EnsureLatestDxvkAsync()
    {
        if (_isEnsuringDxvk)
            return DlssIv.IsVulkanCapable;

        string? installedVersion = DxvkVersionService.GetInstalledVersion(GameDirectoryPath);
        bool dxvkInstalled = HelperService.DetectDxvkInstallation(GameDirectoryPath).isDxvkInstalled;
        string? latestVersion = await DxvkVersionService.GetLatestDxvkVersionAsync();

        if (latestVersion is not null && installedVersion is not null &&
            DxvkVersionService.IsAtLeast(installedVersion, latestVersion))
        {
            return true;
        }

        if (latestVersion is null && dxvkInstalled)
        {
            Logger.Warn(" Could not verify the latest DXVK release; keeping the installed build.");
            return true;
        }

        DxvkInstallPlan? plan = DxvkInstallerService.FindPlan(3, false);
        if (plan is null)
            return false;

        _isEnsuringDxvk = true;
        try
        {
            Logger.Info(" DLSS-IV is enabled - installing the latest DXVK...");
            IsDxvkPanelEnabled = false;

            var progress = new Progress<int>(percent => DownloadProgress = percent);
            var state = new Progress<DxvkState>(value => InstallationState = value);

            await DxvkInstallerService.InstallAsync(
                plan,
                GameDirectoryPath,
                _ffixLatest,
                [],
                progress,
                state,
                writeDxvkConfig: false);

            _ffixLatest = HelperService.DetectDxvkInstallation(GameDirectoryPath).isFusionFixLatest;
            InstallationState = DxvkState.Installed;
            IsUninstallDxvkVisible = true;
            DxvkButtonWidth = 120;
            IsDxvkButtonDefault = false;
            DxvkButtonFontWeight = FontWeight.Normal;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, " Could not install the latest DXVK for DLSS-IV.");
            InstallationState = DxvkState.NotInstalled;
            return false;
        }
        finally
        {
            _isEnsuringDxvk = false;
            IsDxvkPanelEnabled = true;
        }
    }

    [RelayCommand]
    private async Task AsyncClick()
    {
        Logger.Info($" User toggled async to: {InstallAsync}");
        if (ShowTips)
        {
            await ShowTip(Resources.AsyncTipTitle, Resources.AsyncTipDescription);
        }
    }

    [RelayCommand]
    private async Task VSyncClick()
    {
        Logger.Info($" User toggled vsync to: {EnableVSync}");
        if (ShowTips)
        {
            await ShowTip(Resources.VSyncTipTitle, Resources.VSyncTipDescription);
        }
    }

    [RelayCommand]
    private async Task LatencyClick()
    {
        Logger.Info($" User toggled max frame latency to: {SetMaxFrameLatency}");
        if (ShowTips)
        {
            await ShowTip(Resources.LatencyTipTitle, Resources.LatencyTipDescription);
        }
    }

    [RelayCommand]
    private async Task NoRestrictionsClick()
    {
        Logger.Info($" User toggled -norestrictions to: {NoRestrictions}");
        if (ShowTips)
        {
            await ShowTip(Resources.NoRestrictionsTipTitle, Resources.NoRestrictionsTipDescription);
        }
    }

    [RelayCommand]
    private async Task NoMemRestrictClick()
    {
        Logger.Info($" User toggled -nomemrestrict to: {NoMemRestrict}");
        if (ShowTips)
        {
            await ShowTip(Resources.NoMemRestrictTipTitle, Resources.NoMemRestrictTipDescription);
        }
    }

    [RelayCommand]
    private async Task VidMemClick()
    {
        Logger.Info($" User toggled -availablevidmem to: {AvailableVidMem}");
        IsVidMemRadioEnabled = AvailableVidMem;

        if (ShowTips)
        {
            await ShowTip(Resources.AvailableVidMemTipTitle, Resources.AvailableVidMemTipDescription);
        }
    }

    [RelayCommand]
    private async Task VidMemLockClick()
    {
        Logger.Info($" User toggled the video memory lock to: {(IsVidMem3GB ? Resources.VidMemLimit3GB : Resources.VidMemLimit4GB)}");
        if (ShowTips)
        {
            await ShowTip(Resources.AvailableVidMemLockTipTitle, Resources.AvailableVidMemLockTipDescription);
        }
    }

    [RelayCommand]
    private async Task WindowedClick()
    {
        Logger.Info($" User toggled Borderless Windowed to: {BorderlessWindowed}");

        if (ShowTips)
        {
            await ShowTip(Resources.WindowedTipTitle, Resources.WindowedTipDescription);
        }
    }

    [RelayCommand]
    private async Task MonitorDetailsClick()
    {
        Logger.Info($" User toggled Monitor Details to: {MonitorDetails}");

        if (ShowTips)
        {
            await ShowTip(Resources.MonitorDetailsTipTitle, Resources.MonitorDetailsTipDescription);
        }
    }

    [RelayCommand]
    private async Task InstallDxvkAsync()
    {
        Logger.Info(" Prompted to install/reinstall DXVK.");

        if (InstallDxvk == 3 && !string.IsNullOrWhiteSpace(GameDirectoryPath))
        {
            string? installedVersion = DxvkVersionService.GetInstalledVersion(GameDirectoryPath);
            string? targetVersion = await DxvkVersionService.GetLatestDxvkVersionAsync();

            if (installedVersion != null && targetVersion != null &&
                DxvkVersionService.IsAtLeast(installedVersion, targetVersion))
            {
                Logger.Info($" DXVK {installedVersion} is already at least {targetVersion}; skipping the download.");
                await MessageBoxService.ShowAsync(
                    Resources.DxvkAlreadyLatestTitle,
                    string.Format(Resources.DxvkAlreadyLatestDescription, installedVersion),
                    Icon.Info);

                if (DlssIv.IsModEnabled && DlssIv.HasFusionFix)
                    DlssIv.SetVulkanEnabled(DlssIv.IsVulkanCapable);

                return;
            }
        }

        DxvkInstallPlan? plan = DxvkInstallerService.FindPlan(InstallDxvk, InstallAsync);
        if (plan is null)
        {
            Logger.Error($" No DXVK install plan for tier {InstallDxvk} (async: {InstallAsync}).");
            return;
        }

        IsDxvkPanelEnabled = false;

        Logger.Info(" Removing old files if present.");
        await DxvkInstallerService.UninstallAsync(_ffixLatest, GameDirectoryPath);

        InstallationState = DxvkState.Installing;
        IsDxvkButtonDefault = true;
        IsLaunchOptionsButtonDefault = false;
        IsDirectoryButtonEnabled = false;
        IsLaunchOptionsPanelEnabled = false;
        LaunchOptionsButtonFontWeight = FontWeight.Normal;
        DxvkButtonWidth = 200;
        DxvkButtonFontWeight = FontWeight.SemiBold;
        IsUninstallDxvkVisible = false;

        var progressReceiver = new Progress<int>(percent => { DownloadProgress = percent; });
        var stateReceiver = new Progress<DxvkState>(state => { InstallationState = state; });

        List<string> dxvkConfig = [];

        Logger.Info(" Setting up dxvk.conf in accordance with user's choices.");

        if (EnableVSync)
        {
            Logger.Debug(" Adding d3d9.presentInterval = 1 and d3d9.numBackBuffers = 3");
            dxvkConfig.Add("d3d9.presentInterval = 1");
            dxvkConfig.Add("d3d9.numBackBuffers = 3");
        }

        if (SetMaxFrameLatency)
        {
            Logger.Debug(" Adding d3d9.maxFrameLatency = 1");
            dxvkConfig.Add("d3d9.maxFrameLatency = 1");
        }

        try
        {
            string versionName = plan.LatestToken is null
                ? plan.Name
                : string.Format(Resources.LatestText, plan.LatestToken);

            Logger.Info($" Installing {versionName}...");

            if (plan.Async)
                dxvkConfig.Add("dxvk.enableAsync = true");

            if (plan.GplAsyncCache)
                dxvkConfig.Add("dxvk.gplAsyncCache = true");

            await DxvkInstallerService.InstallAsync(
                plan,
                GameDirectoryPath,
                _ffixLatest,
                dxvkConfig,
                progressReceiver,
                stateReceiver);

            await MessageBoxService.ShowAsync(
                Resources.DXVKInstalledTitle,
                string.Format(Resources.DXVKInstalledDescription, versionName),
                Icon.Success
                );
            Logger.Info($" {versionName} has been installed!");

            Logger.Debug(" DXVK installed, editing the launch options toggles and enabling the panels back...");
            InstallationState = DxvkState.Installed;
            DxvkButtonWidth = 120;
            IsUninstallDxvkVisible = true;
            IsDxvkButtonDefault = false;
            IsLaunchOptionsPanelEnabled = true;
            IsDirectoryButtonEnabled = true;
            IsLaunchOptionsButtonDefault = true;
            DxvkButtonFontWeight = FontWeight.Normal;
            MonitorDetails = true;
            IsDxvkPanelEnabled = true;

            if (DlssIv.IsModEnabled && DlssIv.HasFusionFix)
                DlssIv.SetVulkanEnabled(DlssIv.IsVulkanCapable);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, " DXVK installation failed.");
            InstallationState = DxvkState.NotInstalled;

            if (DlssIv.IsModEnabled)
                DlssIv.SetVulkanEnabled(false);
        }
    }

    [RelayCommand]
    private async Task UninstallDxvk()
    {
        IsDxvkPanelEnabled = false;
        IsLaunchOptionsPanelEnabled = false;
        IsDirectoryButtonEnabled = false;

        await DxvkInstallerService.UninstallAsync(_ffixLatest, GameDirectoryPath);

        if (DlssIv.IsModEnabled)
            DlssIv.SetVulkanEnabled(false);

        await MessageBoxService.ShowAsync(
            Resources.DXVKUninstalledTitle,
        Resources.DXVKUninstalledDescription,
            Icon.Success);

        IsUninstallDxvkVisible = false;
        IsDxvkButtonDefault = true;
        IsLaunchOptionsButtonDefault = false;
        DxvkButtonWidth = 200;
        InstallationState = DxvkState.NotInstalled;
        DxvkButtonFontWeight = FontWeight.SemiBold;
        MonitorDetails = false;

        IsDxvkPanelEnabled = true;
        IsLaunchOptionsPanelEnabled = true;
        IsDirectoryButtonEnabled = true;
    }

    [RelayCommand]
    private async Task SetupLaunchOptions()
    {
        Logger.Info(" User prompted to set up launch options.");
        var launchOptions = new List<string>();
        if (NoRestrictions) { launchOptions.Add("-norestrictions"); Logger.Debug(" Added -norestrictions."); }
        if (NoMemRestrict) { launchOptions.Add("-nomemrestrict"); Logger.Debug(" Added -nomemrestrict."); }

        if (IsBorderlessWindowedEnabled)
        {
            if (_ffix)
            {
                ApplyFusionFixBorderless();
            }
            else if (_patchKind == PatchIniKind.OpenPreCE && !string.IsNullOrEmpty(_xlivelessIniPath))
            {
                // OpenPreCE itself has no windowed options; its paired XLivelessAddon.ini does.
                ApplyXlivelessAddonBorderless();
            }
            else if (_patchKind == PatchIniKind.ZolikaPatch && !string.IsNullOrEmpty(_patchIniPath))
            {
                ApplyZolikaPatchBorderless();
            }

            void ApplyFusionFixBorderless()
            {
                var iniParser = new IniEditorService(_iniPath);
                bool windowed = iniParser.ReadValue("MAIN", "Windowed") == "1";
                bool borderless = iniParser.ReadValue("MAIN", "BorderlessWindowed") == "1";
                bool focusLossless = iniParser.ReadValue("MAIN", "BlockOnLostFocus") == "0";
                bool borderlessWindowedValue = windowed && borderless && focusLossless;

                switch (BorderlessWindowed)
                {
                    case true when !borderlessWindowedValue:
                        Logger.Debug(" Borderless Windowed is disabled in FusionFix's config, enabling it back...");
                        if (!windowed) { iniParser.EditValue("MAIN", "Windowed", "1"); }
                        if (!borderless) { iniParser.EditValue("MAIN", "BorderlessWindowed", "1"); }
                        if (!focusLossless) { iniParser.EditValue("MAIN", "BlockOnLostFocus", "0"); }
                        iniParser.SaveFile();
                        Logger.Info(" Enabled Borderless Windowed and disabled Pause Game on Focus Loss.");
                        break;
                    case false when windowed || borderless || focusLossless:
                        Logger.Info(" User chose to disable Borderless Windowed but it's enabled in FusionFix's config, disabling it...");
                        iniParser.EditValue("MAIN", "Windowed", "0");
                        iniParser.EditValue("MAIN", "BorderlessWindowed", "0");
                        iniParser.EditValue("MAIN", "BlockOnLostFocus", "1");
                        iniParser.SaveFile();
                        break;
                }
            }

            void ApplyZolikaPatchBorderless()
            {
                var iniParser = new IniEditorService(_patchIniPath);
                bool borderlessWindowedValue = iniParser.ReadValueAnySection("BorderlessWindowed") == "1";

                switch (BorderlessWindowed)
                {
                    case true when !borderlessWindowedValue:
                        Logger.Debug(" Borderless Windowed is disabled in ZolikaPatch.ini, enabling it back...");
                        iniParser.EditValueAnySection("BorderlessWindowed", "1", "Options");
                        launchOptions.Add("-windowed");
                        launchOptions.Add("-noBlockOnLostFocus");
                        iniParser.SaveFile();
                        break;
                    case false when borderlessWindowedValue:
                        Logger.Info(" User chose to disable Borderless Windowed but it's enabled in ZolikaPatch.ini, disabling it...");
                        iniParser.EditValueAnySection("BorderlessWindowed", "0", "Options");
                        iniParser.SaveFile();
                        break;
                }
            }

            void ApplyXlivelessAddonBorderless()
            {
                var iniParser = new IniEditorService(_xlivelessIniPath);
                bool borderless = iniParser.ReadValue("MAIN", "BorderlessWindowed") == "1";
                bool noPauseOnMinimize = iniParser.ReadValue("MAIN", "DoNotPauseOnMinimize") == "1";

                switch (BorderlessWindowed)
                {
                    case true when !borderless || !noPauseOnMinimize:
                        Logger.Debug(" Borderless Windowed is disabled in XLivelessAddon.ini, enabling it back...");
                        iniParser.EditValue("MAIN", "BorderlessWindowed", "1");
                        iniParser.EditValue("MAIN", "DoNotPauseOnMinimize", "1");
                        launchOptions.Add("-windowed");
                        launchOptions.Add("-noBlockOnLostFocus");
                        iniParser.SaveFile();
                        break;
                    case false when borderless || noPauseOnMinimize:
                        Logger.Info(" User chose to disable Borderless Windowed but it's enabled in XLivelessAddon.ini, disabling it...");
                        iniParser.EditValue("MAIN", "BorderlessWindowed", "0");
                        iniParser.EditValue("MAIN", "DoNotPauseOnMinimize", "0");
                        iniParser.SaveFile();
                        break;
                }
            }
        }
        if (!_ffixLatest)
        {
            if (AvailableVidMem)
            {
                Logger.Debug(" -availablevidmem checked, quering user's VRAM...");
                int vram = await HelperService.QueryVramAsync(IsLinux, _igpuOnly, _dgpuOnly, _dxvkOnIgpu, IsVidMem3GB);
                launchOptions.Add($"-availablevidmem {vram}");
                Logger.Debug($" Added -availablevidmem {vram}.");
            }
        }
        if (MonitorDetails)
        {
            Logger.Debug(" Monitor Details checked, quering user's monitor details...");
            DisplayInfoService.GetPrimaryDisplayInfo(out var width, out var height, out var refreshRate);
            launchOptions.Add($"-width {width}");
            launchOptions.Add($"-height {height}");
            launchOptions.Add($"-refreshrate {refreshRate}");
            Logger.Debug($" Added -width {width}, -height {height}, -refreshrate {refreshRate}.");
        }
        if (!File.Exists(Path.Combine(GameDirectoryPath, "d3d9.dll")) && !_ffixLatest)
        {
            launchOptions.Add("-managed");
        }
        if (_isRetail)
        {
            Logger.Debug(" Game .exe is retail - inputting values via commandline.txt...");
            if (File.Exists(Path.Combine(GameDirectoryPath, "commandline.txt")))
            {
                Logger.Debug(" Old commandline.txt detected, removing...");
                File.Delete(Path.Combine(GameDirectoryPath, "commandline.txt"));
            }
            Logger.Debug(" Writing new commandline.txt...");
            await using (var writer = new StreamWriter(Path.Combine(GameDirectoryPath, "commandline.txt")))
            {
                foreach (string line in launchOptions)
                {
                    await writer.WriteLineAsync(line);
                }
            }
            Logger.Info($" Following launch options have been set to commandline.txt: {string.Join(" ", launchOptions)}");
            await MessageBoxService.ShowAsync(
                Resources.LaunchOptionsSetUpTitle,
                string.Format(Resources.LaunchOptionsSetUpDescription, string.Join(" ", launchOptions)),
                Icon.Success);
        }
        else
        {
            Logger.Info($" Game .exe is 1.2 or later - asked user to input the values on their own and copied them to clipboard: {string.Join(" ", launchOptions)}");
            await MessageBoxService.ShowAsync(
                Resources.LaunchOptionsSteamTitle,
                string.Format(Resources.LaunchOptionsSteamDescription, string.Join(" ", launchOptions)),
                Icon.Setting);
            try
            {
                await ClipboardService.SetClipboardTextAsync(string.Join(" ", launchOptions));
            }
            catch (Exception ex)
            {
                await MessageBoxService.ShowAsync(
                    Resources.FailedToCopyTitle,
                string.Format(Resources.FailedToCopyDescription, string.Join(" ", launchOptions)),
                    Icon.Error);
                Logger.Debug(ex, " Weird issues with clipboard access.");
            }
        }
    }

    [RelayCommand]
    public static void OpenHyperlink(string url)
    {
        Logger.Debug($" Opening hyperlink: {url}");
        ShellService.OpenUrl(url);
    }

    [RelayCommand]
    private async Task About()
    {
        Logger.Debug(" User opened the About window.");

        string patchName = _patchKind == PatchIniKind.OpenPreCE ? "OpenPreCEPatch" : "ZolikaPatch";
        string message =
            $"{Resources.AboutIntroText}\n\n" +
            $"{Resources.AboutDXVKToInstall}: {ToSupportText(InstallDxvk)}\n" +
            $"{Resources.AboutdGPUDXVKSupport}: {ToSupportText(_vkDgpuDxvkSupport)}\n" +
            $"{Resources.AboutiGPUDXVKSupport}: {ToSupportText(_vkIgpuDxvkSupport)}\n" +
            $"{Resources.AboutDetectedGPUs}: {_gpus.Count}\n" +
            $"{Resources.AboutWindowsGameGpu}: {_defaultGpu?.Name ?? Resources.SupportUnknown}\n" +
            $"{Resources.AboutGplSupport}: {ToGplSupportText(_gplSupport)}\n" +
            $"{Resources.AboutIgpuOnly}: {ToYesNo(_igpuOnly)}\n" +
            $"{Resources.AboutDgpuOnly}: {ToYesNo(_dgpuOnly)}\n" +
            $"{Resources.AboutIntelIgpu}: {ToYesNo(_intelIgpu)}\n" +
            $"{Resources.AboutFusionFix}: {ToVersioningText(_ffix, _ffixLatest)}\n" +
            $"{patchName}: {ToVersioningText(_zpatch, _zpatchLatest)}\n" +
            $"{Resources.AboutIvsdkDotNet}: {ToYesNo(_isIvsdkInstalled)}\n" +
            $"{Resources.AboutRtss}: {ToConflictText(string.IsNullOrEmpty(_rtssConfig), _rtssConflict)}\n\n" +
            $"{Resources.AboutVersion}: {GetAssemblyVersion()}";

        await MessageBoxService.ShowAsync(Resources.AboutTitle, message, Icon.Info);
    }

    private static string ToGplSupportText(int level) => level switch
    {
        0 => Resources.SupportNone,
        1 => Resources.SupportPartly,
        2 => Resources.SupportFull,
        _ => Resources.SupportUnknown
    };

    private static string ToYesNo(bool value) => value ? Resources.TextYes : Resources.TextNo;

    private static string ToSupportText(int level) => level switch
    {
        -1 => Resources.SupportIntel,
        0 => Resources.SupportNone,
        1 => Resources.SupportLegacy,
        2 => Resources.SupportNewer,
        3 => Resources.SupportLatest,
        _ => Resources.SupportUnknown
    };

    private static string ToVersioningText(bool present, bool latest) => (present, latest) switch
    {
        (true, true) => Resources.SupportPresent,
        (true, false) => Resources.SupportOutdated,
        _ => Resources.TextNo
    };

    private static string ToConflictText(bool present, bool conflict) => (present, conflict) switch
    {
        (true, false) => Resources.TextYes,
        (true, true) => Resources.SupportConflict,
        _ => Resources.TextNo
    };

    private void ResetVars()
    {
        IsDxvkButtonDefault = true;
        IsLaunchOptionsButtonDefault = false;
        LaunchOptionsButtonFontWeight = FontWeight.Normal;
        DxvkButtonWidth = 200;
        DxvkButtonFontWeight = FontWeight.SemiBold;
        InstallationState = DxvkState.NotInstalled;
        IsUninstallDxvkVisible = false;
        IsVidMemCheckboxEnabled = true;
        AvailableVidMem = true;
        IsVidMemRadioEnabled = true;
    }

    private static async Task ShowTip(string title, string message)
    {
        await MessageBoxService.ShowAsync(title, message, Icon.Info);
    }
}
