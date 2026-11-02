using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTAIVSetupUtility.Localizations;
using GTAIVSetupUtility.Models;
using GTAIVSetupUtility.Services;
using MsBox.Avalonia.Enums;
using NLog;

namespace GTAIVSetupUtility.Modules.DlssIv;

/// <summary>
/// Self-contained DLSS-IV section. Everything this feature needs lives under Modules/DlssIv;
/// removing the folder plus the panel/property wiring in MainWindow is enough to drop it.
/// </summary>
public partial class DlssIvViewModel : ObservableObject, IDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private DlssIvInstallation? _installation;
    private DlssIvState _state = new();
    private GpuInfo? _defaultGpu;
    private string? _fusionFixConfigPath;
    private int _dxvkFeatureTier;
    private bool _hardwareKnown;
    private bool _hasFusionFix;
    private bool _autoDisableHandled;

    /// <summary>Raised after the mod is turned on, so the host can ensure DXVK/Vulkan prerequisites.</summary>
    public event EventHandler? ModEnabledChanged;

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial bool IsAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsSettingsEnabled { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsModEnabled { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> UpscalerOptions { get; set; } = [];

    [ObservableProperty]
    public partial int SelectedUpscalerIndex { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> ProfileOptions { get; set; } = [];

    [ObservableProperty]
    public partial int SelectedProfileIndex { get; set; }

    [ObservableProperty]
    public partial bool IsProfileVisible { get; set; }

    [ObservableProperty]
    public partial bool IsDlaaSelected { get; set; }

    [ObservableProperty]
    public partial bool IsQualitySelected { get; set; } = true;

    [ObservableProperty]
    public partial bool IsHdrEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsThreadedEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsUnpinEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsCpuPlaceEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsRenameSdrEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsFrameGenerationEnabled { get; set; }

    [ObservableProperty]
    public partial decimal? FrameGenerationMinFps { get; set; }

    [ObservableProperty]
    public partial bool IsDlssAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsFsr4Available { get; set; }

    [ObservableProperty]
    public partial bool IsXessAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsFrameGenerationAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool CanUpdateDlss { get; set; }

    [ObservableProperty]
    public partial bool CanUpdateFsr { get; set; }

    [ObservableProperty]
    public partial bool CanToggleMod { get; set; }

    [ObservableProperty]
    public partial bool IsUpscalerComboEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsProfileEnabled { get; set; }

    [ObservableProperty]
    public partial bool ShowFsr4CommunityButton { get; set; }

    private static readonly int[] DlssPresetValues = [0, 11, 13, 12, 10];
    private static readonly string[] DlssPresetLabels =
        [Resources.DlssIvPresetDefault, "K (Transformer)", "M (Transformer)", "L (Transformer)", "J (CNN)"];

    private readonly List<DlssIvUpscaler> _upscalerChoices = [];
    private int _dlssPresetIndex = 1;

    // 0 = auto, 1 = FSR 3.1, 2 = FSR 4
    private int _fsrProfileIndex;
    private readonly List<int> _fsrProfileChoices = [];

    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private bool _suppressUpscalerChange;
    private bool _suppressProfileChange;
    private bool _suppressAutoApply;
    private CancellationTokenSource? _applyCancellation;

    public DlssIvViewModel()
    {
        ProfileOptions = DlssPresetLabels;
        SelectedProfileIndex = _dlssPresetIndex;
    }

    public bool IsFsr4CommunityAvailable => DlssIvCapabilities.SupportsCommunityFsr4(_defaultGpu);

    public bool IsFsr4CommunityInstalled =>
        _installation != null && DlssIvBinaryService.IsFsr4CommunityCurrent(_installation, _state);

    public string Fsr4CommunityButtonText =>
        IsFsr4CommunityInstalled ? Resources.DlssIvUpdateFsr4 : Resources.DlssIvInstallFsr4;

    public bool HasStatusText => !string.IsNullOrEmpty(StatusText);

    /// <summary>FusionFix is mandatory for DLSS-IV's Vulkan renderer.</summary>
    public bool HasFusionFix => _hasFusionFix;

    /// <summary>Whether the Vulkan probe produced a usable result.</summary>
    public bool HasHardwareInfo => _hardwareKnown;

    /// <summary>Mirrors the main window's tips toggle for the HDR explanation.</summary>
    public bool ShowTips { get; set; } = true;

    /// <summary>Whether the detected DXVK tier can provide the Vulkan renderer DLSS-IV needs.</summary>
    public bool IsVulkanCapable => _hardwareKnown && _dxvkFeatureTier == 3;

    public DlssIvUpscaler CurrentUpscaler =>
        SelectedUpscalerIndex >= 0 && SelectedUpscalerIndex < _upscalerChoices.Count
            ? _upscalerChoices[SelectedUpscalerIndex]
            : DlssIvUpscaler.Fsr;

    /// <summary>
    /// Forces FusionFix's GraphicsAPI and d3d9.cfg's API switch on or off. Only meaningful while
    /// FusionFix is present; without it DLSS-IV is disabled anyway.
    /// </summary>
    public void SetVulkanEnabled(bool enabled)
    {
        if (_installation is null || !_hasFusionFix)
            return;

        DlssIvConfigService.SetVulkanEnabled(_installation, _fusionFixConfigPath, enabled);
    }

    public void ClearGame()
    {
        _installation = null;
        _state = new DlssIvState();
        IsVisible = false;
        IsAvailable = false;
        IsSettingsEnabled = false;
        StatusText = string.Empty;
        _hasFusionFix = false;
        _autoDisableHandled = false;
        IsXessAvailable = false;
        IsFrameGenerationAvailable = false;
        UpdateButtonStates();
    }

    public void SetHardware(GpuInfo? defaultGpu, int dxvkFeatureTier)
    {
        bool previousSuppress = _suppressAutoApply;
        _suppressAutoApply = true;

        _defaultGpu = defaultGpu;
        _dxvkFeatureTier = dxvkFeatureTier;
        _hardwareKnown = true;

        IsDlssAvailable = DlssIvCapabilities.SupportsDlss(defaultGpu);
        IsFsr4Available = DlssIvCapabilities.SupportsFsr4(defaultGpu);
        RefreshInstallationFeatures();

        if (_installation != null)
        {
            UpdateAvailability();
            LoadFromConfig();
        }

        UpdateButtonStates();
        _suppressAutoApply = previousSuppress;
        _ = HandleUnsupportedDxvkAsync();
    }

    /// <summary>
    /// The Vulkan probe produced no devices. Keep the mod manageable, but do not guess whether
    /// Vulkan works: the panel offers only the performance on/off switch and configs stay untouched.
    /// </summary>
    public void SetHardwareUnavailable()
    {
        bool previousSuppress = _suppressAutoApply;
        _suppressAutoApply = true;

        _defaultGpu = null;
        _dxvkFeatureTier = 0;
        _hardwareKnown = false;
        IsDlssAvailable = false;
        IsFsr4Available = false;
        IsXessAvailable = false;
        IsFrameGenerationAvailable = false;

        if (_installation != null)
        {
            UpdateAvailability();
            LoadFromConfig();
        }

        UpdateButtonStates();
        _suppressAutoApply = previousSuppress;
    }

    public void InitializeForGame(string gameDirectory, bool isFusionFix, string? fusionFixCfgPath = null)
    {
        // FusionFix stores its runtime options (including Antialiasing) in the .cfg, while the
        // .ini holds the documented defaults. Never use the .ini for AA.
        _fusionFixConfigPath = fusionFixCfgPath;
        _hasFusionFix = isFusionFix;
        _installation = DlssIvInstallation.Find(gameDirectory);
        _state = DlssIvStateStore.Load(_installation);
        _state.ModEnabled = _installation.IsEnabled;
        _autoDisableHandled = false;
        RefreshInstallationFeatures();

        // OptiScaler was tried as an FSR 4 Lite backend and is not compatible with DLSS-IV's
        // custom NGX feeder. If an older build left it behind, remove it once and restore files.
        if (_state.OptiScalerEnabled)
        {
            try
            {
                DlssIvBinaryService.RemoveOptiScaler(_installation, _state);
                _state.OptiScalerEnabled = false;
                Logger.Info(" Removed the incompatible OptiScaler installation from DLSSIV.");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, " Could not remove the previous OptiScaler installation.");
            }
        }

        // DLSS-IV only works through FusionFix's Vulkan renderer. If the ASI is enabled but
        // FusionFix is gone, turn the mod off instead of letting it load into a broken setup.
        if (_installation.IsEnabled && !_hasFusionFix)
        {
            Logger.Warn(" DLSS-IV is enabled but FusionFix is missing - disabling DLSS-IV.");
            try
            {
                DlssIvConfigService.SetModEnabled(
                    _installation,
                    enabled: false,
                    _state,
                    fusionFixConfigPath: null,
                    updateFusionFixAntialiasing: false);
                _installation = DlssIvInstallation.Find(gameDirectory);
                _state.ModEnabled = false;
                IsModEnabled = false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, " Could not disable DLSS-IV after FusionFix disappeared.");
            }
        }

        // DLSS-IV requires FusionFix's own AA to be off. If the mod is active, keep the config
        // and the UI in sync from the first load.
        bool dxvkUnsupported = _hardwareKnown && _dxvkFeatureTier != 3;
        if (_hasFusionFix && IsVulkanCapable && !dxvkUnsupported && _installation.IsPresent)
            DlssIvConfigService.SetFusionFixAntialiasing(_state, _fusionFixConfigPath, disable: _installation.IsEnabled);

        UpdateAvailability();
        LoadFromConfig();

        // Do not litter a game folder that has no DLSS-IV installation with our state file.
        if (_installation.IsPresent)
            DlssIvStateStore.Save(_installation, _state);

        UpdateButtonStates();
        _ = HandleUnsupportedDxvkAsync();
    }

    private void RefreshInstallationFeatures()
    {
        IsXessAvailable = DlssIvCapabilities.SupportsXess(_defaultGpu) && _installation?.HasXessBinary == true;
        IsFrameGenerationAvailable = _installation?.HasFrameGenerationBinary == true;
    }

    /// <summary>
    /// DLSS-IV bundles DXVK 3.1.1 and cannot run without its Vulkan feature set. If the mod is
    /// enabled on a machine that cannot provide it, turn it off and explain why.
    /// </summary>
    private async Task HandleUnsupportedDxvkAsync()
    {
        if (_autoDisableHandled || !_hardwareKnown || _dxvkFeatureTier == 3 || _installation == null)
            return;

        if (!_hasFusionFix || !_installation.IsEnabled)
            return;

        _autoDisableHandled = true;

        try
        {
            Logger.Warn($" Disabling DLSS-IV: latest DXVK/Vulkan is not supported (tier {_dxvkFeatureTier}).");
            DlssIvConfigService.SetVulkanEnabled(_installation, _fusionFixConfigPath, enabled: false);
            DlssIvConfigService.SetModEnabled(_installation, false, _state, _fusionFixConfigPath);
            _installation = DlssIvInstallation.Find(_installation.GameDirectory);
            _state.ModEnabled = false;
            IsModEnabled = false;
            DlssIvStateStore.Save(_installation, _state);
            UpdateAvailability();

            await MessageBoxService.ShowAsync(
                Resources.DlssIvAutoDisabledTitle,
                string.Format(Resources.DlssIvAutoDisabledDescription, ToSupportText(_dxvkFeatureTier)),
                Icon.Warning);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, " Could not automatically disable DLSS-IV.");
        }
    }

    /// <summary>
    /// Called by the host when the latest DXVK required for Vulkan could not be installed.
    /// </summary>
    public async Task DisableForUnavailableVulkanAsync()
    {
        if (_installation == null || !_installation.IsEnabled || _autoDisableHandled)
            return;

        _autoDisableHandled = true;

        try
        {
            Logger.Warn(" Disabling DLSS-IV: the latest DXVK/Vulkan runtime is not available.");
            DlssIvConfigService.SetVulkanEnabled(_installation, _fusionFixConfigPath, enabled: false);
            DlssIvConfigService.SetModEnabled(_installation, false, _state, _fusionFixConfigPath);
            _installation = DlssIvInstallation.Find(_installation.GameDirectory);
            _state.ModEnabled = false;
            IsModEnabled = false;
            DlssIvStateStore.Save(_installation, _state);
            UpdateAvailability();
            StatusText = Resources.DlssIvVulkanUnavailable;

            await MessageBoxService.ShowAsync(
                Resources.DlssIvAutoDisabledTitle,
                Resources.DlssIvVulkanUnavailableDescription,
                Icon.Warning);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, " Could not disable DLSS-IV after the DXVK/Vulkan update failed.");
        }
    }

    [RelayCommand]
    private async Task HdrClick()
    {
        Logger.Info($" User toggled HDR to: {IsHdrEnabled}");

        if (ShowTips)
            await MessageBoxService.ShowAsync(Resources.DlssIvHdr, Resources.DlssIvHdrTip, Icon.Info);
    }

    [RelayCommand]
    private async Task ToggleModAsync()
    {
        if (_installation == null)
            return;

        bool desired = IsModEnabled;

        if (desired && (!_hasFusionFix || !IsVulkanCapable))
        {
            IsModEnabled = false;
            StatusText = !_hasFusionFix
                ? Resources.DlssIvFusionFixMissing
                : Resources.DlssIvVulkanUnavailable;
            return;
        }

        try
        {
            SetBusy(true, string.Empty);
            DlssIvConfigService.SetModEnabled(
                _installation,
                desired,
                _state,
                _fusionFixConfigPath,
                updateFusionFixAntialiasing: true);
            _installation = DlssIvInstallation.Find(_installation.GameDirectory);
            _state.ModEnabled = _installation.IsEnabled;

            if (desired)
                ApplyInternal();

            DlssIvStateStore.Save(_installation, _state);
            UpdateAvailability();
            UpdateStatus();
            UpdateButtonStates();

            if (desired && _installation.IsEnabled)
                ModEnabledChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, " Could not toggle DLSS-IV.");
            IsModEnabled = _installation.IsEnabled;
            await MessageBoxService.ShowErrorAsync(Resources.DlssIvErrorTitle, ex.Message);
        }
        finally
        {
            SetBusy(false, string.Empty);
        }
    }

    private void ApplyInternal()
    {
        if (_installation == null || !IsAvailable)
            return;

        Logger.Debug($" Applying DLSS-IV: upscaler={CurrentUpscaler}, dlssPreset={_dlssPresetIndex}, " +
                     $"fsrProfile={_fsrProfileIndex}, fg={IsFrameGenerationEnabled}.");

        bool auto = CurrentUpscaler == DlssIvUpscaler.Auto;
        var settings = new DlssIvSettings(
            CurrentUpscaler,
            auto ? 0 : DlssPresetValues[Math.Clamp(_dlssPresetIndex, 0, DlssPresetValues.Length - 1)],
            auto
                ? DlssIvFsrProfile.Auto
                : _fsrProfileIndex switch
                {
                    1 => DlssIvFsrProfile.Fsr31,
                    2 => DlssIvFsrProfile.Fsr4,
                    _ => DlssIvFsrProfile.Auto
                },
            IsDlaaSelected ? DlssIvQuality.Dlaa : DlssIvQuality.Quality,
            IsHdrEnabled,
            IsFrameGenerationEnabled,
            Math.Max(0, (int)(FrameGenerationMinFps ?? 0)),
            new DlssIvPerformanceSettings(
                IsThreadedEnabled,
                IsUnpinEnabled,
                IsCpuPlaceEnabled,
                IsRenameSdrEnabled));

        if (_installation.IsEnabled != IsModEnabled)
        {
            DlssIvConfigService.SetModEnabled(_installation, IsModEnabled, _state, _fusionFixConfigPath);
            _installation = DlssIvInstallation.Find(_installation.GameDirectory);
            _state.ModEnabled = _installation.IsEnabled;
        }

        DlssIvConfigService.ApplySettings(_installation, settings);
        DlssIvConfigService.SetFusionFixAntialiasing(_state, _fusionFixConfigPath, disable: _installation.IsEnabled);
        _state.RequestedFsrProfile = FsrProfileKey(_fsrProfileIndex);

        DlssIvStateStore.Save(_installation, _state);
        UpdateStatus();
        UpdateButtonStates();
    }

    private void ScheduleAutoApply()
    {
        if (_suppressAutoApply || _installation == null || !IsAvailable)
            return;

        _applyCancellation?.Cancel();
        _applyCancellation?.Dispose();
        _applyCancellation = new CancellationTokenSource();
        _ = ApplySoonAsync(_applyCancellation.Token);
    }

    private async Task ApplySoonAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150), _timeProvider, cancellationToken);
            if (_installation == null)
                return;

            ApplyInternal();
        }
        catch (OperationCanceledException)
        {
            // A newer change superseded this one.
        }
        catch (Exception ex)
        {
            Logger.Error(ex, " Automatic DLSS-IV settings apply failed.");
        }
    }

    [RelayCommand]
    private async Task UpdateDlssAsync()
    {
        if (_installation == null)
            return;

        if (!await MessageBoxService.ConfirmAsync(Resources.DlssIvConfirmUpdateDlssTitle,
                string.Format(Resources.DlssIvConfirmUpdateDlssDescription, "59 MB")))
            return;

        try
        {
            SetBusy(true, string.Empty);
            var progress = new Progress<int>(percent =>
                ProgressText = string.Format(Resources.DlssIvBusyDownloading, percent));
            string tag = await DlssIvBinaryService.UpdateDlssAsync(_installation, _state, progress);
            DlssIvStateStore.Save(_installation, _state);
            await MessageBoxService.ShowAsync(
                Resources.DlssIvUpdateDoneTitle,
                string.Format(Resources.DlssIvUpdateDlssDone, tag),
                Icon.Success);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, " DLSS binary update failed.");
            await MessageBoxService.ShowErrorAsync(Resources.DlssIvErrorTitle, ex.Message);
        }
        finally
        {
            SetBusy(false, Resources.DlssIvReady);
        }
    }

    [RelayCommand]
    private async Task UpdateFsrAsync()
    {
        if (_installation == null)
            return;

        if (!await MessageBoxService.ConfirmAsync(Resources.DlssIvConfirmUpdateFsrTitle,
                string.Format(Resources.DlssIvConfirmUpdateFsrDescription, "58 MB")))
            return;

        try
        {
            SetBusy(true, string.Empty);
            var progress = new Progress<int>(percent =>
                ProgressText = string.Format(Resources.DlssIvBusyDownloading, percent));
            string tag = await DlssIvBinaryService.UpdateFsrAsync(_installation, _state, progress);
            _state.RequestedFsrProfile = FsrProfileKey(_fsrProfileIndex);
            DlssIvStateStore.Save(_installation, _state);
            await MessageBoxService.ShowAsync(
                Resources.DlssIvUpdateDoneTitle,
                string.Format(Resources.DlssIvUpdateFsrDone, tag),
                Icon.Success);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, " FSR binary update failed.");
            await MessageBoxService.ShowErrorAsync(Resources.DlssIvErrorTitle, ex.Message);
        }
        finally
        {
            SetBusy(false, Resources.DlssIvReady);
        }
    }

    [RelayCommand]
    private async Task InstallFsr4CommunityAsync()
    {
        if (_installation == null)
            return;

        if (!IsFsr4CommunityAvailable)
        {
            await MessageBoxService.ShowErrorAsync(Resources.DlssIvErrorTitle, Resources.DlssIvFsr4Unsupported);
            return;
        }

        if (!await MessageBoxService.ConfirmAsync(Resources.DlssIvConfirmFsr4Title,
                string.Format(Resources.DlssIvConfirmFsr4Description, "3.5 MB")))
            return;

        try
        {
            SetBusy(true, string.Empty);
            var progress = new Progress<int>(percent =>
                ProgressText = string.Format(Resources.DlssIvBusyDownloading, percent));

            string? latest = await DlssIvBinaryService.GetLatestFsr4TagAsync();
            if (latest != null && DlssIvBinaryService.IsFsr4CommunityCurrent(_installation, _state, latest))
            {
                await MessageBoxService.ShowAsync(
                    Resources.DlssIvUpdateDoneTitle,
                    string.Format(Resources.DlssIvFsr4UpToDate, latest),
                    Icon.Info);
                return;
            }

            string tag = await DlssIvBinaryService.InstallFsr4CommunityAsync(_installation, _state, progress);
            _fsrProfileIndex = 2;
            RebuildProfileOptions();
            DlssIvStateStore.Save(_installation, _state);
            ApplyInternal();
            await MessageBoxService.ShowAsync(
                Resources.DlssIvUpdateDoneTitle,
                string.Format(Resources.DlssIvFsr4Installed, tag),
                Icon.Success);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, " Community FSR 4 installation failed.");
            await MessageBoxService.ShowErrorAsync(Resources.DlssIvErrorTitle, ex.Message);
        }
        finally
        {
            SetBusy(false, Resources.DlssIvReady);
        }
    }

    private void LoadFromConfig()
    {
        if (_installation == null)
            return;

        bool previousSuppress = _suppressAutoApply;
        _suppressAutoApply = true;
        var values = DlssIvConfigService.LoadSettings(_installation);

        _dlssPresetIndex = MapDlssPreset(values.Preset);
        _fsrProfileIndex = values.FsrVersion switch
        {
            3 => 1,
            4 => 2,
            _ => 0
        };

        DlssIvUpscaler preferred = values.Upscaler switch
        {
            0 => DlssIvUpscaler.Dlss,
            1 => DlssIvUpscaler.Fsr,
            2 => DlssIvUpscaler.Xess,
            _ => DlssIvUpscaler.Auto
        };

        bool changed = RebuildUpscalerOptions(preferred);
        changed |= RebuildProfileOptions(normalize: true);

        IsDlaaSelected = values.Quality == 0;
        IsQualitySelected = values.Quality != 0;
        IsHdrEnabled = values.Hdr != 0;
        IsThreadedEnabled = values.Threaded;
        IsUnpinEnabled = values.Unpin;
        IsCpuPlaceEnabled = values.CpuPlace;
        IsRenameSdrEnabled = values.RenameSdr;
        IsFrameGenerationEnabled = values.FrameGeneration;
        FrameGenerationMinFps = Math.Max(0, values.FrameGenerationMinFps);
        IsModEnabled = _installation.IsEnabled;

        _state.RequestedFsrProfile ??= FsrProfileKey(_fsrProfileIndex);

        UpdateStatus();
        _suppressAutoApply = previousSuppress;

        // Only normalize an explicit choice that the current GPU/mod cannot run. Auto stays
        // auto, and a plain load never rewrites the user's config.
        if (changed && IsAvailable)
            ApplyInternal();
    }

    private static int MapDlssPreset(int preset) => preset switch
    {
        0 => 0,
        11 => 1,
        13 => 2,
        12 => 3,
        10 => 4,
        _ => 1
    };

    private bool RebuildUpscalerOptions(DlssIvUpscaler? preferred = null)
    {
        var choices = new List<DlssIvUpscaler> { DlssIvUpscaler.Auto };
        if (IsDlssAvailable)
            choices.Add(DlssIvUpscaler.Dlss);
        if (_defaultGpu != null)
            choices.Add(DlssIvUpscaler.Fsr);
        if (IsXessAvailable)
            choices.Add(DlssIvUpscaler.Xess);

        bool hadCurrent = SelectedUpscalerIndex >= 0 && SelectedUpscalerIndex < _upscalerChoices.Count;
        DlssIvUpscaler requested = preferred ?? (hadCurrent ? CurrentUpscaler : DlssIvUpscaler.Auto);
        DlssIvUpscaler actual = choices.Contains(requested) ? requested : DlssIvUpscaler.Auto;
        bool changed = requested != actual;

        _suppressUpscalerChange = true;
        _upscalerChoices.Clear();
        _upscalerChoices.AddRange(choices);
        UpscalerOptions = choices.Select(UpscalerLabel).ToArray();
        SetUpscalerSelection(choices.IndexOf(actual));
        _suppressUpscalerChange = false;

        Logger.Info($" Upscaler options: [{string.Join(", ", choices)}]; selected={actual}; " +
                    $"DLSS={IsDlssAvailable}; XeSS={IsXessAvailable}; GPU={_defaultGpu?.Name ?? "none"}.");

        return changed;
    }

    private void SetUpscalerSelection(int index)
    {
        SelectedUpscalerIndex = index;

        if (Application.Current == null || index < 0)
            return;

        // A new ItemsSource resets the ComboBox selection on the next binding pass. Re-apply
        // the selection after that pass (also covers a TwoWay binding writing -1 back).
        Dispatcher.UIThread.Post(() =>
        {
            _suppressUpscalerChange = true;
            SelectedUpscalerIndex = -1;
            SelectedUpscalerIndex = index;
            _suppressUpscalerChange = false;
            RebuildProfileOptions();
        }, DispatcherPriority.Background);
    }

    private bool RebuildProfileOptions(bool normalize = false)
    {
        _suppressProfileChange = true;
        bool changed = false;

        if (CurrentUpscaler == DlssIvUpscaler.Auto)
        {
            IsProfileVisible = true;
            ProfileOptions = [Resources.DlssIvAuto];
            SetProfileSelection(0);
            _suppressProfileChange = false;
            NotifyCommunityState();
            return false;
        }

        if (CurrentUpscaler == DlssIvUpscaler.Xess)
        {
            ProfileOptions = [];
            SelectedProfileIndex = -1;
            IsProfileVisible = false;
            _suppressProfileChange = false;
            NotifyCommunityState();
            return false;
        }

        if (CurrentUpscaler == DlssIvUpscaler.Dlss)
        {
            IsProfileVisible = true;
            ProfileOptions = DlssPresetLabels;
            SetProfileSelection(_dlssPresetIndex);
            _suppressProfileChange = false;
            NotifyCommunityState();
            return false;
        }

        IsProfileVisible = true;
        var labels = new List<string>
        {
            Resources.DlssIvFsrAuto,
            Resources.DlssIvFsr31
        };

        _fsrProfileChoices.Clear();
        _fsrProfileChoices.Add(0);
        _fsrProfileChoices.Add(1);

        if (IsFsr4Available)
        {
            _fsrProfileChoices.Add(2);
            labels.Add(Resources.DlssIvFsr4);
        }

        if (!_fsrProfileChoices.Contains(_fsrProfileIndex))
        {
            _fsrProfileIndex = _fsrProfileChoices[^1];
            changed = true;
        }

        ProfileOptions = labels;
        SetProfileSelection(_fsrProfileChoices.IndexOf(_fsrProfileIndex));
        _suppressProfileChange = false;
        NotifyCommunityState();
        return changed && normalize;
    }

    private void SetProfileSelection(int index)
    {
        SelectedProfileIndex = index;

        if (Application.Current == null || index < 0)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            _suppressProfileChange = true;
            SelectedProfileIndex = -1;
            SelectedProfileIndex = index;
            _suppressProfileChange = false;
        }, DispatcherPriority.Background);
    }

    private static string UpscalerLabel(DlssIvUpscaler upscaler) => upscaler switch
    {
        DlssIvUpscaler.Dlss => Resources.DlssIvDlss,
        DlssIvUpscaler.Xess => Resources.DlssIvXess,
        DlssIvUpscaler.Fsr => Resources.DlssIvFsr,
        _ => Resources.DlssIvAuto
    };

    private void NotifyCommunityState()
    {
        OnPropertyChanged(nameof(IsFsr4CommunityInstalled));
        OnPropertyChanged(nameof(Fsr4CommunityButtonText));
        UpdateButtonStates();
    }

    private void UpdateAvailability()
    {
        bool present = _installation?.IsPresent == true;
        IsVisible = present;

        bool vulkanReady = IsVulkanCapable;
        IsAvailable = present && _hasFusionFix && vulkanReady;
        IsSettingsEnabled = IsAvailable;
        IsUpscalerComboEnabled = IsAvailable;

        if (!present)
        {
            StatusText = string.Empty;
        }
        else if (!_hasFusionFix)
        {
            StatusText = Resources.DlssIvFusionFixMissing;
        }
        else if (!_hardwareKnown)
        {
            StatusText = Resources.DlssIvVulkanUnknown;
        }
        else if (!vulkanReady)
        {
            StatusText = string.Format(Resources.DlssIvRequiresDxvk, ToSupportText(_dxvkFeatureTier));
        }
        else
        {
            UpdateStatus();
        }

        UpdateButtonStates();
    }

    private void UpdateStatus()
    {
        if (_installation == null)
        {
            StatusText = Resources.DlssIvStatusMissing;
            return;
        }

        if (!_hasFusionFix)
        {
            StatusText = Resources.DlssIvFusionFixMissing;
            return;
        }

        if (!_hardwareKnown)
        {
            StatusText = Resources.DlssIvVulkanUnknown;
            return;
        }

        StatusText = _dxvkFeatureTier != 3
            ? string.Format(Resources.DlssIvRequiresDxvk, ToSupportText(_dxvkFeatureTier))
            : string.Empty;
    }

    private void UpdateButtonStates()
    {
        bool controlsEnabled = IsAvailable;

        CanUpdateDlss = controlsEnabled && !IsBusy;
        CanUpdateFsr = controlsEnabled && !IsBusy;
        CanToggleMod = !IsBusy && (_installation?.IsEnabled == true || IsAvailable);
        IsUpscalerComboEnabled = controlsEnabled;
        IsProfileEnabled = controlsEnabled && IsProfileVisible;
        IsSettingsEnabled = controlsEnabled;
        ShowFsr4CommunityButton = controlsEnabled &&
                                  CurrentUpscaler == DlssIvUpscaler.Fsr &&
                                  _fsrProfileIndex == 2 &&
                                  IsFsr4CommunityAvailable &&
                                  !IsBusy;
    }

    private void SetBusy(bool busy, string progress)
    {
        IsBusy = busy;
        ProgressText = progress;
        UpdateButtonStates();
    }

    private static string FsrProfileKey(int index) => index switch
    {
        1 => "fsr31",
        2 => "fsr4",
        _ => "auto"
    };

    private static string ToSupportText(int level) => level switch
    {
        -1 => Resources.SupportIntel,
        0 => Resources.SupportNone,
        1 => Resources.SupportLegacy,
        2 => Resources.SupportNewer,
        3 => Resources.SupportLatest,
        _ => Resources.SupportUnknown
    };

    partial void OnSelectedUpscalerIndexChanged(int value)
    {
        if (_suppressUpscalerChange || value < 0 || value >= _upscalerChoices.Count)
            return;

        RebuildProfileOptions(normalize: true);
        UpdateButtonStates();
        ScheduleAutoApply();
    }

    partial void OnIsDlaaSelectedChanged(bool value)
    {
        if (value)
            IsQualitySelected = false;
        ScheduleAutoApply();
    }

    partial void OnIsQualitySelectedChanged(bool value)
    {
        if (value)
            IsDlaaSelected = false;
        ScheduleAutoApply();
    }

    partial void OnIsHdrEnabledChanged(bool value) => ScheduleAutoApply();

    partial void OnIsThreadedEnabledChanged(bool value) => ScheduleAutoApply();

    partial void OnIsUnpinEnabledChanged(bool value) => ScheduleAutoApply();

    partial void OnIsCpuPlaceEnabledChanged(bool value) => ScheduleAutoApply();

    partial void OnIsRenameSdrEnabledChanged(bool value) => ScheduleAutoApply();

    partial void OnIsFrameGenerationEnabledChanged(bool value) => ScheduleAutoApply();

    partial void OnFrameGenerationMinFpsChanged(decimal? value) => ScheduleAutoApply();

    partial void OnSelectedProfileIndexChanged(int value)
    {
        if (_suppressProfileChange || value < 0)
            return;

        if (CurrentUpscaler == DlssIvUpscaler.Dlss)
            _dlssPresetIndex = Math.Clamp(value, 0, DlssPresetValues.Length - 1);
        else if (CurrentUpscaler == DlssIvUpscaler.Fsr &&
                 value < _fsrProfileChoices.Count)
            _fsrProfileIndex = _fsrProfileChoices[value];

        NotifyCommunityState();
        ScheduleAutoApply();
    }

    partial void OnStatusTextChanged(string value) =>
        OnPropertyChanged(nameof(HasStatusText));

    partial void OnIsModEnabledChanged(bool value)
    {
        UpdateButtonStates();
    }

    public void Dispose()
    {
        _applyCancellation?.Cancel();
        _applyCancellation?.Dispose();
        _applyCancellation = null;
        GC.SuppressFinalize(this);
    }
}
