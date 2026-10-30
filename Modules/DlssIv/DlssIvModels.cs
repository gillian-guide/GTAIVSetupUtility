namespace GTAIVSetupUtility.Modules.DlssIv;

public enum DlssIvUpscaler
{
    Dlss,
    Fsr
}

public enum DlssIvFsrProfile
{
    Auto,
    Fsr31,
    Fsr4,
    Fsr4Lite
}

public enum DlssIvQuality
{
    Dlaa,
    Quality
}

/// <summary>
/// The DLSS-IV.cfg values this tool owns. Everything else in the file is left alone.
/// </summary>
public sealed record DlssIvSettings(
    DlssIvUpscaler Upscaler,
    int DlssPreset,
    DlssIvFsrProfile FsrProfile,
    DlssIvQuality Quality,
    bool Hdr);

public sealed record DlssIvConfigValues(int Upscaler, int FsrVersion, int Quality, int Hdr, int Preset)
{
    public static DlssIvConfigValues Default { get; } = new(-1, 0, 1, 0, 11);
}
