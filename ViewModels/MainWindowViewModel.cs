using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTAIVSetupUtility.Localizations;
using GTAIVSetupUtility.Services;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using NLog;
using FontWeight = Avalonia.Media.FontWeight;
using TextDecorationCollection = Avalonia.Media.TextDecorationCollection;
using TextDecorations = Avalonia.Media.TextDecorations;
using WindowStartupLocation = Avalonia.Controls.WindowStartupLocation;

namespace GTAIVSetupUtility.ViewModels
{
    public enum DxvkState
    {
        NotInstalled,
        Downloading,
        Installing,
        Installed
    }
    
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DirectoryLabelText))]
        private string _gameDirectoryPath = string.Empty;

        public string DirectoryLabelText => string.IsNullOrWhiteSpace(GameDirectoryPath)
            ? Resources.DirectoryLabelUnselected
            : Resources.DirectoryLabelSelected;

        [ObservableProperty]
        private FontWeight _directoryLabelFontWeight = FontWeight.SemiBold;

        [ObservableProperty]
        private TextDecorationCollection? _directoryLabelDecorations = TextDecorations.Underline;

        [ObservableProperty]
        private TextDecorationCollection? _tipsNoteDecorations = null;

        [ObservableProperty]
        private bool _isDirectoryButtonDefault = true;
        
        [ObservableProperty]
        private bool _isDirectoryButtonEnabled = true;

        [ObservableProperty]
        private bool _showTips = true;

        [ObservableProperty]
        private bool _isDxvkPanelEnabled;

        [ObservableProperty]
        private bool _isLaunchOptionsPanelEnabled;

        [ObservableProperty]
        private bool _installAsync = false;

        [ObservableProperty]
        private bool _isAsyncEnabled;

        [ObservableProperty]
        private bool _isAsyncVisible;

        [ObservableProperty]
        private bool _enableVSync = true;

        [ObservableProperty]
        private bool _setMaxFrameLatency = true;

        [ObservableProperty]
        private bool _noRestrictions = true;

        [ObservableProperty]
        private bool _noMemRestrict = true;

        [ObservableProperty]
        private bool _availableVidMem = true;

        [ObservableProperty]
        private bool _isVidMemCheckboxEnabled = true;

        [ObservableProperty]
        private bool _isVidMem3GB = true;

        [ObservableProperty]
        private bool _isVidMemRadioEnabled = true;

        [ObservableProperty]
        private bool _borderlessWindowed = true;

        [ObservableProperty]
        private bool _isBorderlessWindowedEnabled = true;

        [ObservableProperty]
        private bool _monitorDetails;

        [ObservableProperty]
        private bool _isUninstallDxvkVisible;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DxvkButtonText))]
        private DxvkState _installationState = DxvkState.NotInstalled;
        
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DxvkButtonText))]
        private int _downloadProgress;
        
        public string DxvkButtonText => InstallationState switch
        {
            DxvkState.Installed => Resources.DXVKTextInstalled,
            DxvkState.NotInstalled => Resources.DXVKTextNotInstalled,
            DxvkState.Downloading => string.Format(Resources.DXVKTextDownloading, DownloadProgress),
            DxvkState.Installing => Resources.DXVKTextInstalling,
            _ => Resources.DXVKTextNotInstalled
        };
        [ObservableProperty]
        private double _dxvkButtonWidth = 200;

        [ObservableProperty]
        private FontWeight _dxvkButtonFontWeight = FontWeight.SemiBold;

        [ObservableProperty]
        private bool _isDxvkButtonDefault;

        [ObservableProperty]
        private FontWeight _launchOptionsButtonFontWeight = FontWeight.Normal;

        [ObservableProperty]
        private bool _isLaunchOptionsButtonDefault;

        public bool IsLinux = false;
        private int _vram1 = 0;
        private int _vram2 = 0;
        private bool _ffix = false;
        private bool _ffixLatest = false;
        private bool _zpatch = false;
        private bool _zpatchLatest = false;
        private bool _isRetail = false;
        private bool _isIvsdkInstalled = false;
        private bool _dxvkOnIgpu = false;
        private bool _firstGpu = true;
        private readonly string _rtssConfig = File.Exists(@"C:\Program Files (x86)\RivaTuner Statistics Server\Profiles\GTAIV.exe.cfg")
            ? @"C:\Program Files (x86)\RivaTuner Statistics Server\Profiles\GTAIV.exe.cfg"
            : File.Exists(@"C:\Program Files (x86)\RivaTuner Statistics Server\Profiles\Global")
                ? @"C:\Program Files (x86)\RivaTuner Statistics Server\Profiles\Global"
                : string.Empty;

        private bool _rtssConflict = false;
        private string? _iniPath = string.Empty;
        private string? _iniPathZp = string.Empty;

        public int InstallDxvk = 0;
        private int _vkDgpuDxvkSupport = 0;
        private int _vkIgpuDxvkSupport = 0;
        private int _gplSupport = 0;
        private bool _igpuOnly = true;
        private bool _dgpuOnly = true;
        private bool _intelIgpu = false;
        private bool _enableAsync = false;
        
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public MainWindowViewModel()
        {
            Logger.Info(" Application sucessfully initialized!");
        }

        private static string GetAssemblyVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                   ?? string.Empty;
        }
        
        public async Task ReceiveVulkanInfoAsync((int vkDgpuDxvkSupport, int vkIgpuDxvkSupport, int gplSupport, bool igpuOnly, bool dgpuOnly, bool intelIgpu, bool enableAsync) vulkanInfo)
        {
            _vkDgpuDxvkSupport = vulkanInfo.vkDgpuDxvkSupport;
            _vkIgpuDxvkSupport = vulkanInfo.vkIgpuDxvkSupport;
            _gplSupport = vulkanInfo.gplSupport;
            _igpuOnly = vulkanInfo.igpuOnly;
            _dgpuOnly = vulkanInfo.dgpuOnly;
            _intelIgpu = vulkanInfo.intelIgpu;
            _enableAsync = vulkanInfo.enableAsync;

            async Task<ButtonResult> ShowUserPrompt(string title, string message)
            {
                var box = MessageBoxManager.GetMessageBoxStandard(
                    new MessageBoxStandardParams
                    {
                        ContentTitle = title,
                        ContentMessage = message.Replace("\\n", "\n"),
                        ButtonDefinitions = ButtonEnum.YesNo,
                        Icon = Icon.Question,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        CanResize = false,
                        MaxWidth = 600,
                        ShowInCenter = true
                    });
                
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    return await box.ShowWindowDialogAsync(desktop.MainWindow);
                }
                
                return await box.ShowAsync();
            }

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
                            var result = await ShowUserPrompt(Resources.InstallDXVKPrompt, Resources.InstallDXVKPromptDescription);
                            
                            if (result == ButtonResult.Yes)
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
                            var resultVer = await ShowUserPrompt(Resources.WhichDXVKPrompt, Resources.WhichDXVKPromptDescription);
                            
                            if (resultVer == ButtonResult.Yes)
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
                var result = await ShowUserPrompt(Resources.InteliGPUDXVKPrompt, Resources.InteliGPUDXVKPromptDescription);

                if (result == ButtonResult.Yes)
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
            Logger.Info(" Vulkan check finished!");
        }

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

                string folderPath = folder.Path.LocalPath;
                string gameExePath = Path.Combine(folderPath, "GTAIV.exe");

                Logger.Info(" User selected a folder, checking validity...");

                if (!File.Exists(gameExePath))
                {
                    await ShowMessageBoxAsync(
                        Resources.InvalidFolderTitle,
                        Resources.InvalidFolderNoGTAIV,
                        Icon.Error);
                    continue;
                }

                string gameVersion = HelperService.GetFileVersion(gameExePath);
                
                if (!HelperService.IsVersionSupported(gameVersion))
                {
                    await ShowMessageBoxAsync(
                        Resources.InvalidFolderTitle,
                        Resources.InvalidFolderWrongVersion,
                        Icon.Error);
                    continue;
                }
                
                Logger.Info($" Selected valid game folder. Version: {gameVersion}");
                IsDirectoryButtonDefault = false;
                await ProcessGameDirectoryAsync(folderPath, gameVersion);
                break;
            }
        }

        private async Task ProcessGameDirectoryAsync(string folderPath, string gameVersion)
        {
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
                        await ShowMessageBoxAsync(
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
            
            (string? fusionFixIni, string? fusionFixCfg, string? zolikaPatchIni) = HelperService.FindModConfigFiles(folderPath);
            
            var modConfig = await HelperService.HandleModConfigurationsAsync(
                folderPath, 
                fusionFixIni, 
                fusionFixCfg, 
                zolikaPatchIni);

            _iniPath = modConfig.FusionFixIniPath;
            _iniPathZp = modConfig.ZolikaPatchIniPath;
            _ffix = modConfig.IsFusionFix;
            
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
            
            if (string.IsNullOrEmpty(fusionFixIni) && string.IsNullOrEmpty(zolikaPatchIni))
            {
                BorderlessWindowed = false;
                IsBorderlessWindowedEnabled = false;
            }

            if (!string.IsNullOrEmpty(zolikaPatchIni))
            {
                _zpatch = true;
                if (HelperService.zpatchLatest)
                {
                    _zpatchLatest = true;
                }
            }
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
            };
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
            IsDxvkPanelEnabled = false;

            Logger.Info(" Removing old files if present.");
            await DxvkInstallerService.UninstallDxvk(_ffixLatest, GameDirectoryPath);

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

            Logger.Debug(" Quering links to install DXVK...");

            try
            {
                string versionName = string.Empty;
                switch (InstallDxvk)
                {
                    case 1:
                        // we're using the "if" in each case because of the async checkbox
                        if (InstallAsync)
                        {
                            versionName = string.Format(Resources.LatestText, "DXVK-Sarek-async");
                            Logger.Info($" Installing {versionName}...");
                            dxvkConfig.Add("dxvk.enableAsync = true");
                            await DxvkInstallerService.DownloadDxvk(
                                progressReceiver,
                                stateReceiver,
                                GameDirectoryPath,
                                _ffixLatest,
                                "https://api.github.com/repos/pythonlover02/dxvk-Sarek/releases/latest",
                                dxvkConfig,
                                false, false);
                        }
                        else
                        {
                            versionName = string.Format(Resources.LatestText, "DXVK-Sarek");
                            Logger.Info($" Installing {versionName}...");
                            await DxvkInstallerService.DownloadDxvk(
                                progressReceiver,
                                stateReceiver,
                                GameDirectoryPath,
                                _ffixLatest,
                                "https://api.github.com/repos/pythonlover02/dxvk-Sarek/releases/latest",
                                dxvkConfig,
                                false, false, 1);
                        }

                        break;
                    case 2:
                        if (InstallAsync)
                        {
                            versionName = "DXVK-gplasync 2.6.2";
                            Logger.Info($" Installing {versionName}...");
                            dxvkConfig.Add("dxvk.enableAsync = true");
                            await DxvkInstallerService.DownloadDxvk(
                                progressReceiver,
                                stateReceiver,
                                GameDirectoryPath,
                                _ffixLatest,
                                "https://gitlab.com/api/v4/projects/43488626/releases/v2.6.2-1",
                                dxvkConfig,
                                true, true);
                        }
                        else
                        {
                            versionName = "DXVK 2.6.2";
                            Logger.Info($" Installing {versionName}...");
                            await DxvkInstallerService.DownloadDxvk(
                                progressReceiver,
                                stateReceiver,
                                GameDirectoryPath,
                                _ffixLatest,
                                "https://api.github.com/repos/doitsujin/dxvk/releases/assets/222230856",
                                dxvkConfig,
                                false, true);
                        }

                        break;
                    case 3:
                        if (InstallAsync)
                        {
                            versionName = string.Format(Resources.LatestText, "DXVK-gplasync");
                            Logger.Info($" Installing {versionName}...");
                            dxvkConfig.Add("dxvk.enableAsync = true");
                            dxvkConfig.Add("dxvk.gplAsyncCache = true");
                            await DxvkInstallerService.DownloadDxvk(
                                progressReceiver,
                                stateReceiver,
                                GameDirectoryPath,
                                _ffixLatest,
                                "https://gitlab.com/api/v4/projects/43488626/releases/",
                                dxvkConfig,
                                true, false);
                        }
                        else
                        {
                            versionName = string.Format(Resources.LatestText, "DXVK");
                            Logger.Info($" Installing {versionName}...");
                            await DxvkInstallerService.DownloadDxvk(
                                progressReceiver,
                                stateReceiver,
                                GameDirectoryPath,
                                _ffixLatest,
                                "https://api.github.com/repos/doitsujin/dxvk/releases/latest",
                                dxvkConfig,
                                false, false);
                        }

                        break;
                    case -1:
                        if (InstallAsync)
                        {
                            versionName = "DXVK-async 1.10.1";
                            Logger.Info($" Installing {versionName}...");
                            dxvkConfig.Add("dxvk.enableAsync = true");
                            await DxvkInstallerService.DownloadDxvk(
                                progressReceiver,
                                stateReceiver,
                                GameDirectoryPath,
                                _ffixLatest,
                                "https://api.github.com/repos/Sporif/dxvk-async/releases/assets/60677007",
                                dxvkConfig,
                                false, true);
                        }
                        else
                        {
                            versionName = "DXVK 1.10.1";
                            Logger.Info($" Installing {versionName}...");
                            await DxvkInstallerService.DownloadDxvk(
                                progressReceiver,
                                stateReceiver,
                                GameDirectoryPath,
                                _ffixLatest,
                                "https://api.github.com/repos/doitsujin/dxvk/releases/assets/60669426",
                                dxvkConfig,
                                false, true);
                        }
                        break;
                }
                
                await ShowMessageBoxAsync(
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
            }
            catch
            {
                InstallationState = DxvkState.NotInstalled;
            }
        }

        [RelayCommand]
        private async Task UninstallDxvk()
        {
            IsDxvkPanelEnabled = false;
            IsLaunchOptionsPanelEnabled = false;
            IsDirectoryButtonEnabled = false;
            
            await DxvkInstallerService.UninstallDxvk(_ffixLatest, GameDirectoryPath);
            
            await ShowMessageBoxAsync(
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

            bool ffWindowed = true;
            bool ffBorderless = true;
            bool ffFocusLossless = true;
            if (IsBorderlessWindowedEnabled)
            {
                var iniParser = new IniEditorService(_iniPath);
                bool borderlessWindowedValue;
                if (_ffix)
                {
                    ffWindowed = iniParser.ReadValue("MAIN", "Windowed") == "1";
                    ffBorderless = iniParser.ReadValue("MAIN", "BorderlessWindowed") == "1";
                    ffFocusLossless = iniParser.ReadValue("MAIN", "BlockOnLostFocus") == "0";
                    borderlessWindowedValue = ffWindowed && ffBorderless && ffFocusLossless;
                }
                else
                {
                    borderlessWindowedValue = iniParser.ReadValue("Options", "BorderlessWindowed") == "1";
                }
                switch (BorderlessWindowed)
                {
                    case true:
                    {
                        Logger.Info(" User chose to enable Borderless Windowed");
                        if (!borderlessWindowedValue)
                        {
                            Logger.Debug(" Borderless Windowed is disabled in the ini, enabling it back...");
                            if (_ffix)
                            {
                                if (!ffWindowed) { iniParser.EditValue("MAIN", "Windowed", "1"); }
                                if (!ffBorderless) { iniParser.EditValue("MAIN", "BorderlessWindowed", "1"); }
                                if (!ffFocusLossless) { iniParser.EditValue("MAIN", "BlockOnLostFocus", "0"); }
                                Logger.Info(" Enabled Borderless Windowed and disabled Pause Game on Focus Loss.");
                            }
                            else
                            {
                                launchOptions.Add("-windowed");
                                launchOptions.Add("-noBlockOnLostFocus");
                                iniParser.EditValue("Options", "BorderlessWindowed", "1");
                                Logger.Debug(" Added -windowed and -noBlockOnLostFocus.");
                            }
                            iniParser.SaveFile();
                        }

                        break;
                    }
                    case false when (borderlessWindowedValue || ffWindowed || ffBorderless || ffFocusLossless):
                    {
                        Logger.Info(" User chose to disable Borderless Windowed but it's enabled in the ini, disabling it...");
                        if (_ffix)
                        {
                            iniParser.EditValue("MAIN", "Windowed", "0");
                            iniParser.EditValue("MAIN", "BorderlessWindowed", "0");
                            iniParser.EditValue("MAIN", "BlockOnLostFocus", "1");
                        }
                        else
                        {
                            iniParser.EditValue("Options", "BorderlessWindowed", "0");
                        }
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
                    int vram = await HelperService.QueryVram(IsLinux, _igpuOnly, _dgpuOnly, _dxvkOnIgpu, IsVidMem3GB);
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
                await ShowMessageBoxAsync(
                    Resources.LaunchOptionsSetUpTitle,
                    string.Format(Resources.LaunchOptionsSetUpDescription, string.Join(" ", launchOptions)),
                    Icon.Success);
            }
            else
            {
                Logger.Info($" Game .exe is 1.2 or later - asked user to input the values on their own and copied them to clipboard: {string.Join(" ", launchOptions)}");
                await ShowMessageBoxAsync(
                    Resources.LaunchOptionsSteamTitle,
                    string.Format(Resources.LaunchOptionsSteamDescription, string.Join(" ", launchOptions)),
                    Icon.Setting);
                try
                {
                    await ClipboardService.SetClipboardTextAsync(string.Join(" ", launchOptions));
                }
                catch (Exception ex)
                {
                    await ShowMessageBoxAsync(
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
            Logger.Debug(" Opening a hyperlink...");
            var psi = new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = $"/c start {url}",
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            Process.Start(psi);
        }

        public static void RestartApplication()
        {
            var currentProcess = Process.GetCurrentProcess();
            Process.Start(currentProcess.MainModule?.FileName ?? string.Empty);
            Environment.Exit(0);
        }
    
        [RelayCommand]
        private async Task<ButtonResult> About()
        {
            Logger.Debug(" User opened the About window.");

            string message = 
                $"{Resources.AboutIntroText}\n\n" +
                $"{Resources.AboutDXVKToInstall}: {ToSupportText(InstallDxvk)}\n" +
                $"{Resources.AboutdGPUDXVKSupport}: {ToSupportText(_vkDgpuDxvkSupport)}\n" +
                $"{Resources.AboutiGPUDXVKSupport}: {ToSupportText(_vkIgpuDxvkSupport)}\n" +
                $"GPL support state: {ToGPLSupportText(_gplSupport)}\n" +
                $"iGPU Only: {ToYesNo(_igpuOnly)}\n" +
                $"dGPU Only: {ToYesNo(_dgpuOnly)}\n" +
                $"Intel iGPU: {ToYesNo(_intelIgpu)}\n" +
                $"FusionFix: {ToVersioningText(_ffix, _ffixLatest)}\n" +
                $"ZolikaPatch: {ToVersioningText(_zpatch, _zpatchLatest)}\n" +
                $"IVSDKDotNet: {ToYesNo(_isIvsdkInstalled)}\n" +
                $"RTSS: {ToConflictText(string.IsNullOrEmpty(_rtssConfig), _rtssConflict)}\n\n" +
                $"Version: {GetAssemblyVersion()}";
            
            var box = MessageBoxManager.GetMessageBoxStandard(
                new MessageBoxStandardParams
                {
                    ContentTitle = Resources.AboutTitle,
                    ContentMessage = message.Replace("\\n", "\n"),
                    ButtonDefinitions = ButtonEnum.Ok,
                    Icon = Icon.Info,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    MaxWidth = 600,
                    ShowInCenter = true
                });
            
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return await box.ShowWindowDialogAsync(desktop.MainWindow);
            }
                
            return await box.ShowAsync();

            string ToGPLSupportText(int level) => level switch
            {
                0 => Resources.SupportNone,
                1 => Resources.SupportPartly,
                2 => Resources.SupportFull,
                _ => Resources.SupportUnknown
            };

            string ToYesNo(bool value) => value ? Resources.TextYes : Resources.TextNo;

            string ToSupportText(int level) => level switch
            {
                -1 => Resources.SupportIntel,
                0 => Resources.SupportNone,
                1 => Resources.SupportLegacy,
                2 => Resources.SupportNewer,
                3 => Resources.SupportLatest,
                _ => Resources.SupportUnknown
            };
            
            string ToVersioningText(bool present, bool latest) => (present, latest) switch
            {
                (true, true)  => Resources.SupportPresent,
                (true, false) => Resources.SupportOutdated,
                _                          => Resources.TextNo
            };
            
            string ToConflictText(bool present, bool conflict) => (present, conflict) switch
            {
                (true, false) => Resources.TextYes,
                (true, true)  => Resources.SupportConflict,
                _                           => Resources.TextNo
            };
        }

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
        
        /// <summary>
        /// Shows a modal message box that blocks the main window interaction.
        /// </summary>
        public static async Task ShowMessageBoxAsync(string title, string message, Icon icon)
        {
            var msgBox = MessageBoxManager.GetMessageBoxStandard(
                new MessageBoxStandardParams
                {
                    ContentTitle = title,
                    ContentMessage = message.Replace("\\n", "\n"),
                    ButtonDefinitions = ButtonEnum.Ok,
                    Icon = icon,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    MaxWidth = 600,
                    ShowInCenter = true
                });
            
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
                {
                    MainWindow: { } mainWindow
                })
            {
                await msgBox.ShowWindowDialogAsync(mainWindow);
            }
            else
            {
                await msgBox.ShowAsync();
            }
        }

        private static async Task ShowTip(string title, string message)
        {
            await ShowMessageBoxAsync(title, message, Icon.Info);
        }
    }
}