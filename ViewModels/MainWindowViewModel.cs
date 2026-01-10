using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTAIVSetupUtility.Localizations;
using GTAIVSetupUtility.Service;
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
        private bool _showTips = true;

        [ObservableProperty]
        private bool _isDxvkPanelEnabled;

        [ObservableProperty]
        private bool _isLaunchOptionsPanelEnabled;

        [ObservableProperty]
        private bool _installAsync = true;

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
        private bool _isDxvkButtonDefault = true;

        [ObservableProperty]
        private FontWeight _launchOptionsButtonFontWeight = FontWeight.Normal;

        [ObservableProperty]
        private bool _isLaunchOptionsButtonDefault;
        
        private int _vram1 = 0;
        private int _vram2 = 0;
        private bool _ffix = false;
        private bool _ffixLatest = false;
        private bool _isRetail = false;
        private bool _isIvsdkInstalled = false;
        private bool _dxvkOnIgpu = false;
        private bool _firstGpu = true;
        private string _rtssConfig = File.Exists(@"C:\Program Files (x86)\RivaTuner Statistics Server\Profiles\GTAIV.exe.cfg") ? @"C:\Program Files (x86)\RivaTuner Statistics Server\Profiles\GTAIV.exe.cfg" : File.Exists(@"C:\Program Files (x86)\RivaTuner Statistics Server\Profiles\Global") ? @"C:\Program Files (x86)\RivaTuner Statistics Server\Profiles\Global" : string.Empty;
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
                   ?? String.Empty;
        }
        
        private void DeleteFiles(string directory, List<string> filenames)
        {
            foreach (var file in filenames.Where(file => File.Exists($"{directory}/{file}")))
            {
                File.Delete($"{directory}/{file}");
            }
        }
        
        async Task<ButtonResult> ShowTip(string title, string message)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(
                new MessageBoxStandardParams
                {
                    ContentTitle = title,
                    ContentMessage = message.Replace("\\n", "\n"),
                    ButtonDefinitions = ButtonEnum.Ok,
                    Icon = Icon.Info,
                    WindowStartupLocation = (WindowStartupLocation)System.Windows.WindowStartupLocation.CenterOwner,
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
        
        public async Task ReceiveVulkanInfoAsync((int vkDgpuDxvkSupport, int vkIgpuDxvkSupport, int gplSupport, bool igpuOnly, bool dgpuOnly, bool intelIgpu, bool enableAsync) vulkanInfo)
        {
            _vkDgpuDxvkSupport = vulkanInfo.vkDgpuDxvkSupport;
            _vkIgpuDxvkSupport = vulkanInfo.vkIgpuDxvkSupport;
            _gplSupport = vulkanInfo.gplSupport;
            _igpuOnly = vulkanInfo.igpuOnly;
            _dgpuOnly = vulkanInfo.dgpuOnly;
            _intelIgpu = vulkanInfo.intelIgpu;
            _enableAsync = vulkanInfo.enableAsync;

            Logger.Debug(_vkDgpuDxvkSupport);
            Logger.Debug(_vkIgpuDxvkSupport);
            Logger.Debug(_gplSupport);
            Logger.Debug(_igpuOnly);
            Logger.Debug(_dgpuOnly);
            Logger.Debug(_intelIgpu);
            Logger.Debug(_enableAsync);

            async Task<ButtonResult> ShowUserPrompt(string title, string message)
            {
                var box = MessageBoxManager.GetMessageBoxStandard(
                    new MessageBoxStandardParams
                    {
                        ContentTitle = title,
                        ContentMessage = message.Replace("\\n", "\n"),
                        ButtonDefinitions = ButtonEnum.YesNo,
                        Icon = Icon.Question,
                        WindowStartupLocation = (WindowStartupLocation)System.Windows.WindowStartupLocation.CenterOwner,
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

        [RelayCommand]
        private async Task SelectDirectoryAsync()
        {
            // Implementation from original Button_Click
            // This would involve using Avalonia's StorageProvider for folder selection
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
            Logger.Info($" User toggled -norestrictions to: {NoMemRestrict}");
            if (ShowTips)
            {
                await ShowTip(Resources.NoMemRestrictTipTitle, Resources.NoMemRestrictTipDescription);
            }
        }

        [RelayCommand]
        private async Task VidMemClick()
        {
            Logger.Info($" User toggled -nomemrestrict to: {AvailableVidMem}");
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
            // Implementation from original InstallDxvkBtn_Click
        }

        [RelayCommand]
        private void UninstallDxvk()
        {
            // Implementation from original UninstallDxvkBtn_Click
        }

        [RelayCommand]
        private void SetupLaunchOptions()
        {
            // Implementation from original SetupLaunchOptions_Click
        }

        [RelayCommand]
        private void OpenHyperlink(string url)
        {
            Logger.Debug(" User clicked on a hyperlink from the main window.");
            var psi = new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = $"/c start {url}",
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            Process.Start(psi);
        }
    
        [RelayCommand]
        private async Task<ButtonResult> About()
        {
            Logger.Debug(" User opened the About window.");
            
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
            
            string ToGPLSupportText(int level) => level switch
            {
                0 => Resources.SupportNone,
                1 => Resources.SupportPartly,
                2 => Resources.SupportFull,
            };
            
            var message = 
                $"{Resources.AboutIntroText}\n\n" +
                $"{Resources.AboutDXVKToInstall}: {ToSupportText(InstallDxvk)}\n" +
                $"{Resources.AboutdGPUDXVKSupport}: {ToSupportText(_vkDgpuDxvkSupport)}\n" +
                $"{Resources.AboutiGPUDXVKSupport}: {ToSupportText(_vkIgpuDxvkSupport)}\n" +
                $"GPL support state: {ToGPLSupportText(_gplSupport)}\n" +
                $"iGPU Only: {ToYesNo(_igpuOnly)}\n" +
                $"dGPU Only: {ToYesNo(_dgpuOnly)}\n" +
                $"Intel iGPU: {ToYesNo(_intelIgpu)}\n\n" +
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
        }

        public void ResetVars()
        {
            IsDxvkButtonDefault = true;
            IsLaunchOptionsButtonDefault = false;
            LaunchOptionsButtonFontWeight = FontWeight.Normal;
            DxvkButtonWidth = 160;
            DxvkButtonFontWeight = FontWeight.SemiBold;
            InstallationState = DxvkState.NotInstalled;
            IsUninstallDxvkVisible = false;
            IsVidMemCheckboxEnabled = true;
            AvailableVidMem = true;
            IsVidMemRadioEnabled = true;
        }

        public void OnDirectorySelected()
        {
            DirectoryLabelFontWeight = FontWeight.Normal;
            DirectoryLabelDecorations = null;
            TipsNoteDecorations = TextDecorations.Underline;
            IsLaunchOptionsPanelEnabled = true;
            IsDirectoryButtonDefault = false;
        }
        
        private void client_DownloadFileCompleted(object? sender, AsyncCompletedEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Logger.Debug(" Successfully downloaded.");
                //_downloadFinished = true;
                //InstallDxvkBtn.Content = "Installing...";
            });
        }
    }
}