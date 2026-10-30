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
    public partial bool IsDlssSelected { get; set; } = true;

    [ObservableProperty]
    public partial bool IsFsrSelected { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> ProfileOptions { get; set; } = [];

    [ObservableProperty]
    public partial int SelectedProfileIndex { get; set; }

    [ObservableProperty]
    public partial bool IsDlaaSelected { get; set; }

    [ObservableProperty]
    public partial bool IsQualitySelected { get; set; } = true;

    [ObservableProperty]
    public partial bool IsHdrEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsDlssAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsFsr4Available { get; set; }

    [ObservableProperty]
    public partial bool IsFsr4LiteAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool CanUpdateDlss { get; set; }

    [ObservableProperty]
    public partial bool CanUpdateFsr { get; set; }

    [ObservableProperty]
    public partial bool CanToggleMod { get; set; }

    [ObservableProperty]
    public partial bool IsDlssRadioEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsFsrRadioEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsProfileEnabled { get; set; }

    [ObservableProperty]
    public partial bool ShowFsr4CommunityButton { get; set; }

    private static readonly int[] DlssPresetValues = [0, 11, 13, 12, 10];
    private static readonly string[] DlssPresetLabels =
        [Resources.DlssIvPresetDefault, "K (Transformer)", "M (Transformer)", "L (Transformer)", "J (CNN)"];

    private int _dlssPresetIndex = 1;
    private int _fsrProfileIndex;
    private readonly List<int> _fsrProfileChoices = [];
    private readonly TimeProvider _timeProvider = TimeProvider.System;
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

    /// <summary>
    /// Whether the detected GPU can run any DLSS-IV upscaler at all. When false the mod can
    /// still be enabled/disabled for its performance features, but the upscaler settings are
    /// read-only and nothing is written to DLSS-IV.cfg.
    /// </summary>
    public bool HasUpscalingSupport =>
        IsDlssAvailable ||
        IsFsr4Available ||
        IsFsr4LiteAvailable ||
        DlssIvCapabilities.SupportsFsr31(_defaultGpu);

    public bool IsUpscalerControlsEnabled => IsAvailable && HasUpscalingSupport;

    /// <summary>FusionFix is mandatory for DLSS-IV's Vulkan renderer.</summary>
    public bool HasFusionFix => _hasFusionFix;

    /// <summary>Whether the Vulkan probe produced a usable result.</summary>
    public bool HasHardwareInfo => _hardwareKnown;

    /// <summary>Whether the detected DXVK tier can provide the Vulkan renderer DLSS-IV needs.</summary>
    public bool IsVulkanCapable => _hardwareKnown && _dxvkFeatureTier == 3;

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
        IsFsr4LiteAvailable = DlssIvCapabilities.SupportsFsr4Lite(defaultGpu);

        if (!IsDlssAvailable && IsDlssSelected)
            SetUpscaler(dlss: false);

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
        IsFsr4LiteAvailable = false;

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

        // DLSS-IV requires FusionFix's own AA to be off only when an upscaler is in use. Without
        // upscaling support the mod is just a performance add-on, so leave the rest of the
        // configuration completely alone.
        bool dxvkUnsupported = _hardwareKnown && _dxvkFeatureTier != 3;
        if (_hasFusionFix && HasUpscalingSupport && !dxvkUnsupported && _installation.IsPresent)
            DlssIvConfigService.SetFusionFixAntialiasing(_state, _fusionFixConfigPath, disable: _installation.IsEnabled);

        UpdateAvailability();
        LoadFromConfig();

        // Do not litter a game folder that has no DLSS-IV installation with our state file.
        if (_installation.IsPresent)
            DlssIvStateStore.Save(_installation, _state);

        UpdateButtonStates();
        _ = HandleUnsupportedDxvkAsync();
    }

    /// <summary>
    /// DLSS-IV bundles DXVK 3.1.1 and cannot run on a GPU without the latest DXVK feature set.
    /// If the user already has the mod enabled on such a machine, turn it off and explain why.
    /// </summary>
    private async Task HandleUnsupportedDxvkAsync()
    {
        if (_autoDisableHandled || !_hardwareKnown || _dxvkFeatureTier == 3 || _installation == null)
            return;

        if (!_hasFusionFix)
            return;

        // A GPU with no upscaler at all uses the mod for its performance features; there is
        // nothing to disable in that mode.
        if (!HasUpscalingSupport)
            return;

        if (!_installation.IsEnabled)
            return;

        _autoDisableHandled = true;

        try
        {
            Logger.Warn($" Disabling DLSS-IV: latest DXVK is not supported (tier {_dxvkFeatureTier}).");
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

    [RelayCommand]
    private async Task ToggleModAsync()
    {
        if (_installation == null)
            return;

        bool desired = IsModEnabled;

        if (desired && !_hasFusionFix)
        {
            IsModEnabled = false;
            StatusText = Resources.DlssIvFusionFixMissing;
            return;
        }

        try
        {
            SetBusy(true, string.Empty);
            DlssIvConfigService.SetModEnabled(
                _installation,
                desired,
                _state,
                HasUpscalingSupport ? _fusionFixConfigPath : null,
                updateFusionFixAntialiasing: HasUpscalingSupport);
            _installation = DlssIvInstallation.Find(_installation.GameDirectory);
            _state.ModEnabled = _installation.IsEnabled;

            if (desired && HasUpscalingSupport)
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
        // Without upscaling support the mod only provides its performance features: never
        // rewrite DLSS-IV.cfg or FusionFix's AA setting in that state.
        if (_installation == null || !HasUpscalingSupport)
            return;

        bool dlss = IsDlssSelected;
        if (dlss && !IsDlssAvailable)
            dlss = false;

        Logger.Debug($" Applying DLSS-IV: dlssSelected={IsDlssSelected}, fsrSelected={IsFsrSelected}, " +
                     $"dlssPreset={_dlssPresetIndex}, fsrProfile={_fsrProfileIndex}.");

        var settings = new DlssIvSettings(
            dlss ? DlssIvUpscaler.Dlss : DlssIvUpscaler.Fsr,
            DlssPresetValues[Math.Clamp(_dlssPresetIndex, 0, DlssPresetValues.Length - 1)],
            _fsrProfileIndex switch
            {
                1 => DlssIvFsrProfile.Fsr31,
                2 => DlssIvFsrProfile.Fsr4,
                3 => DlssIvFsrProfile.Fsr4Lite,
                _ => DlssIvFsrProfile.Auto
            },
            IsDlaaSelected ? DlssIvQuality.Dlaa : DlssIvQuality.Quality,
            IsHdrEnabled);

        if (_installation.IsEnabled != IsModEnabled)
        {
            DlssIvConfigService.SetModEnabled(_installation, IsModEnabled, _state, _fusionFixConfigPath);
            _installation = DlssIvInstallation.Find(_installation.GameDirectory);
            _state.ModEnabled = _installation.IsEnabled;
        }

        DlssIvConfigService.ApplySettings(_installation, settings);
        DlssIvConfigService.SetFusionFixAntialiasing(_state, _fusionFixConfigPath, disable: _installation.IsEnabled);
        _state.RequestedFsrProfile = ProfileKey(_fsrProfileIndex);

        DlssIvStateStore.Save(_installation, _state);
        UpdateStatus();
        UpdateButtonStates();
    }

    private void ScheduleAutoApply()
    {
        if (_suppressAutoApply || _installation == null || !IsUpscalerControlsEnabled)
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
            _state.RequestedFsrProfile = ProfileKey(_fsrProfileIndex);
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
            IsFsrSelected = true;
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

        bool wantsDlss = values.Upscaler switch
        {
            0 => true,
            1 => false,
            _ => IsDlssAvailable
        };

        if (wantsDlss && !IsDlssAvailable)
            wantsDlss = false;

        _dlssPresetIndex = MapDlssPreset(values.Preset);

        int profile = values.FsrVersion switch
        {
            3 => 1,
            4 => IsTrackedAsLite() ? 3 : 2,
            _ => 0
        };

        _fsrProfileIndex = profile;
        SetUpscaler(wantsDlss);

        // Default chain: DLSS -> FSR 4 -> FSR 4 Lite -> FSR 3.1.
        // Only override an explicit fsr_version; an explicit FSR choice is respected.
        int beforeFallback = _fsrProfileIndex;
        if (!wantsDlss && values.Upscaler != 1 && values.FsrVersion == 0)
            _fsrProfileIndex = HighestSupportedFsrProfile();

        RebuildProfileOptions();
        IsDlaaSelected = values.Quality == 0;
        IsQualitySelected = values.Quality != 0;
        IsHdrEnabled = values.Hdr != 0;
        IsModEnabled = _installation.IsEnabled;

        bool adjusted = NormalizeProfileSelection() || beforeFallback != _fsrProfileIndex;
        _state.RequestedFsrProfile ??= ProfileKey(_fsrProfileIndex);

        UpdateStatus();
        _suppressAutoApply = previousSuppress;

        if (adjusted && IsAvailable)
            ApplyInternal();
    }

    private int HighestSupportedFsrProfile() =>
        IsFsr4Available ? 2 : IsFsr4LiteAvailable ? 3 : 1;

    private bool NormalizeProfileSelection()
    {
        bool changed = false;

        if (IsDlssSelected && !IsDlssAvailable)
        {
            SetUpscaler(false);
            if (_fsrProfileIndex == 0)
                _fsrProfileIndex = HighestSupportedFsrProfile();
            changed = true;
        }

        // Keep the FSR fallback valid even while DLSS is selected, so switching to FSR
        // never shows an unsupported profile.
        bool supported = _fsrProfileIndex switch
        {
            2 => IsFsr4Available,
            3 => IsFsr4LiteAvailable,
            _ => true
        };

        if (!supported)
        {
            _fsrProfileIndex = HighestSupportedFsrProfile();
            changed = true;
        }

        if (changed)
            RebuildProfileOptions();

        return changed;
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

    private void RebuildProfileOptions()
    {
        _suppressProfileChange = true;

        if (IsDlssSelected)
        {
            ProfileOptions = DlssPresetLabels;
            SelectedProfileIndex = _dlssPresetIndex;
        }
        else
        {
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

            if (IsFsr4LiteAvailable)
            {
                _fsrProfileChoices.Add(3);
                labels.Add(Resources.DlssIvFsr4Lite);
            }

            if (!_fsrProfileChoices.Contains(_fsrProfileIndex))
                _fsrProfileIndex = _fsrProfileChoices[^1];

            ProfileOptions = labels;
            SelectedProfileIndex = _fsrProfileChoices.IndexOf(_fsrProfileIndex);
        }

        _suppressProfileChange = false;
        NotifyCommunityState();
        UpdateButtonStates();
    }

    private void NotifyCommunityState()
    {
        OnPropertyChanged(nameof(IsFsr4CommunityInstalled));
        OnPropertyChanged(nameof(Fsr4CommunityButtonText));
        UpdateButtonStates();
    }

    private bool IsTrackedAsLite()
    {
        if (!string.IsNullOrEmpty(_state.RequestedFsrProfile))
            return IsLiteKey(_state.RequestedFsrProfile);

        if (!string.IsNullOrEmpty(_state.FsrProfile))
            return IsLiteKey(_state.FsrProfile);

        if (HasLiteMarker())
            return true;

        // 28,761,864 bytes is the full-model FSR DLL shipped with DLSS-IV 1.0.9.
        // A different file is a custom build or AMD's 4.1.2 SDK (Lite-capable).
        string? dll = _installation == null
            ? null
            : Path.Combine(_installation.DataDirectory, "amd_fidelityfx_upscaler_dx12.dll");
        return dll != null && File.Exists(dll) && new FileInfo(dll).Length != 28_761_864;
    }

    private static bool IsLiteKey(string? value) =>
        string.Equals(value, "fsr4-lite", StringComparison.OrdinalIgnoreCase);

    private bool HasLiteMarker()
    {
        if (_installation == null)
            return false;

        return File.Exists(Path.Combine(_installation.DataDirectory, "fsr4-lite")) ||
               File.Exists(Path.Combine(_installation.DataDirectory, "fsr4-lite.txt"));
    }

    private void SetUpscaler(bool dlss)
    {
        IsDlssSelected = dlss;
        IsFsrSelected = !dlss;
    }

    private void UpdateAvailability()
    {
        bool present = _installation?.IsPresent == true;
        IsVisible = present;

        // A known-unsupported DXVK feature tier blocks the upscaler settings, but a GPU with no
        // upscaler at all must still be able to turn the mod's performance features on and off.
        bool dxvkReady = !_hardwareKnown || _dxvkFeatureTier == 3;
        IsAvailable = present && _hasFusionFix && (dxvkReady || !HasUpscalingSupport);
        IsSettingsEnabled = IsUpscalerControlsEnabled;

        if (!present)
        {
            StatusText = string.Empty;
        }
        else if (!_hasFusionFix)
        {
            StatusText = Resources.DlssIvFusionFixMissing;
        }
        else if (!HasUpscalingSupport)
        {
            StatusText = Resources.DlssIvNoUpscaling;
        }
        else if (!dxvkReady)
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

        if (!HasUpscalingSupport)
        {
            StatusText = Resources.DlssIvNoUpscaling;
            return;
        }

        StatusText = _dxvkFeatureTier != 3
            ? string.Format(Resources.DlssIvRequiresDxvk, ToSupportText(_dxvkFeatureTier))
            : string.Empty;
    }

    private void UpdateButtonStates()
    {
        bool controlsEnabled = IsAvailable && HasUpscalingSupport;

        CanUpdateDlss = controlsEnabled && !IsBusy;
        CanUpdateFsr = controlsEnabled && !IsBusy;
        CanToggleMod = !IsBusy && (_installation?.IsEnabled == true || IsAvailable);
        IsDlssRadioEnabled = controlsEnabled && IsDlssAvailable;
        IsFsrRadioEnabled = controlsEnabled;
        IsProfileEnabled = controlsEnabled;
        IsSettingsEnabled = controlsEnabled;
        ShowFsr4CommunityButton = controlsEnabled && IsFsrSelected && _fsrProfileIndex == 2 &&
                                  IsFsr4CommunityAvailable && !IsBusy;
    }

    private void SetBusy(bool busy, string progress)
    {
        IsBusy = busy;
        ProgressText = progress;
        UpdateButtonStates();
    }

    private static string ProfileKey(int index) => index switch
    {
        1 => "fsr31",
        2 => "fsr4",
        3 => "fsr4-lite",
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

    partial void OnIsDlssSelectedChanged(bool value)
    {
        Logger.Debug($" DLSS selected -> {value} (fsr={IsFsrSelected}).");
        if (value)
            IsFsrSelected = false;

        if (!_suppressProfileChange)
            RebuildProfileOptions();

        UpdateButtonStates();
        ScheduleAutoApply();
    }

    partial void OnIsFsrSelectedChanged(bool value)
    {
        Logger.Debug($" FSR selected -> {value} (dlss={IsDlssSelected}).");
        if (value)
            IsDlssSelected = false;

        if (!_suppressProfileChange)
            RebuildProfileOptions();

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

    partial void OnSelectedProfileIndexChanged(int value)
    {
        if (!_suppressProfileChange)
        {
            if (IsDlssSelected)
                _dlssPresetIndex = Math.Clamp(value, 0, DlssPresetValues.Length - 1);
            else if (value >= 0 && value < _fsrProfileChoices.Count)
                _fsrProfileIndex = _fsrProfileChoices[value];
        }

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
