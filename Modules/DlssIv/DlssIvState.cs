using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;

namespace GTAIVSetupUtility.Modules.DlssIv;

/// <summary>
/// Utility-side bookkeeping that cannot be inferred from DLSS-IV.cfg: which FSR DLL profile is
/// installed, whether OptiScaler is active, and the FusionFix AA value we replaced.
/// Kept inside DLSSIV (or beside the game exe when DLSSIV is missing) so deleting the module
/// and its marker leaves no traces in the game folder.
/// </summary>
public sealed class DlssIvState
{
    public bool ModEnabled { get; set; }
    public string? RequestedFsrProfile { get; set; }
    public string? FsrProfile { get; set; }
    public string? FsrSource { get; set; }
    public string? FsrDllSha256 { get; set; }
    public bool OptiScalerEnabled { get; set; }
    public string? OptiScalerVersion { get; set; }
    public string? PreOptiScalerFsrProfile { get; set; }
    public string? PreOptiScalerFsrSource { get; set; }
    public string? PreOptiScalerFsrDllSha256 { get; set; }
    public string? FusionFixAntialiasing { get; set; }
    public string? FusionFixAntialiasingSection { get; set; }
    public string? FusionFixConfigPath { get; set; }
    public List<DlssIvBackedUpFile> OptiScalerFiles { get; set; } = [];
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DlssIvBackedUpFile
{
    public string RelativePath { get; set; } = string.Empty;
    public string? BackupRelativePath { get; set; }
    public bool Existed { get; set; }
}

public static class DlssIvStateStore
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static DlssIvState Load(DlssIvInstallation installation)
    {
        foreach (string path in CandidatePaths(installation))
        {
            if (!File.Exists(path))
                continue;

            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize(json, DlssIvJsonContext.Default.DlssIvState) ?? new DlssIvState();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $" Could not read DLSS-IV state from {path}.");
                return new DlssIvState();
            }
        }

        return new DlssIvState();
    }

    public static void Save(DlssIvInstallation installation, DlssIvState state)
    {
        state.UpdatedUtc = DateTimeOffset.UtcNow;
        string path = installation.StatePath;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(state, DlssIvJsonContext.Default.DlssIvState));
            Logger.Debug($" Saved DLSS-IV state to {path}.");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $" Could not write DLSS-IV state to {path}.");
        }
    }

    private static IEnumerable<string> CandidatePaths(DlssIvInstallation installation)
    {
        yield return Path.Combine(installation.DataDirectory, ".gtaiv-utility.json");
        yield return Path.Combine(installation.GameDirectory, ".gtaiv-utility-dlssiv.json");
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(DlssIvState))]
internal sealed partial class DlssIvJsonContext : JsonSerializerContext
{
}
