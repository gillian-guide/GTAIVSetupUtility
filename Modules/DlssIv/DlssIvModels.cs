namespace GTAIVSetupUtility.Modules.DlssIv;

public enum DlssIvUpscaler
{
    Auto = -1,
    Dlss,
    Fsr,
    Xess
}

public enum DlssIvFsrProfile
{
    Auto,
    Fsr31,
    Fsr4
}

public enum DlssIvQuality
{
    Dlaa,
    Quality
}

/// <summary>
/// The PERFORMANCE section of DLSS-IV.cfg. These are what make the mod act as a performance
/// add-on even when no upscaler is used.
/// </summary>
public sealed record DlssIvPerformanceSettings(
    bool Threaded,
    bool Unpin,
    bool CpuPlace,
    bool RenameSdr);

/// <summary>
/// The DLSS-IV.cfg values this tool owns. Everything else in the file is left alone.
/// </summary>
public sealed record DlssIvSettings(
    DlssIvUpscaler Upscaler,
    int DlssPreset,
    DlssIvFsrProfile FsrProfile,
    DlssIvQuality Quality,
    bool Hdr,
    bool FrameGeneration,
    int FrameGenerationMinFps,
    DlssIvPerformanceSettings Performance);

public sealed record DlssIvConfigValues(
    int Upscaler,
    int FsrVersion,
    int Quality,
    int Hdr,
    int Preset,
    bool Threaded,
    bool Unpin,
    bool CpuPlace,
    bool RenameSdr,
    bool FrameGeneration,
    int FrameGenerationMinFps)
{
    public static DlssIvConfigValues Default { get; } = new(
        Upscaler: -1,
        FsrVersion: 0,
        Quality: 1,
        Hdr: 0,
        Preset: 11,
        Threaded: true,
        Unpin: true,
        CpuPlace: false,
        RenameSdr: true,
        FrameGeneration: false,
        FrameGenerationMinFps: 0);
}
