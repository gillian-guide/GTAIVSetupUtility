using System.Text.RegularExpressions;
using GTAIVSetupUtility.Models;

namespace GTAIVSetupUtility.Modules.DlssIv;

/// <summary>
/// GPU capability rules for the DLSS-IV choices. These are deliberately conservative:
/// they describe what the mod/FSR can actually use, not what a marketing page claims.
/// </summary>
public static partial class DlssIvCapabilities
{
    public const uint NvidiaVendorId = 0x10DE;
    public const uint AmdVendorId = 0x1002;
    public const uint IntelVendorId = 0x8086;

    [GeneratedRegex(@"\bRX\s*(6|7|9)\d{3}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RadeonRxRegex();

    [GeneratedRegex(@"\bRadeon\s+(6|7|8)\d{2}M\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RadeonMobileRegex();

    [GeneratedRegex(@"\bRyzen\s+AI\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RyzenAiRegex();

    [GeneratedRegex(@"\bRX\s*6\d{3}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RadeonRx6000Regex();

    [GeneratedRegex(@"\bIntel\s*(\(R\)\s*)?Arc\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IntelArcRegex();

    public static bool SupportsDlss(GpuInfo? gpu) =>
        gpu != null &&
        gpu.VendorId == NvidiaVendorId &&
        (gpu.Name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
         gpu.Name.Contains("TITAN RTX", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Native FSR 4.1.x: RDNA 2 (RX 6000, with FSR SDK 2.4) and newer Radeon, plus Ryzen AI.
    /// </summary>
    public static bool SupportsFsr4(GpuInfo? gpu)
    {
        if (gpu == null || gpu.VendorId != AmdVendorId)
            return false;

        return RadeonRxRegex().IsMatch(gpu.Name) ||
               RadeonMobileRegex().IsMatch(gpu.Name) ||
               RyzenAiRegex().IsMatch(gpu.Name);
    }

    /// <summary>
    /// XeSS runs on any GPU (Intel's general path on NVIDIA/AMD is slower), but only when the
    /// installed DLSS-IV build actually ships Intel's signed library.
    /// </summary>
    public static bool SupportsXess(GpuInfo? gpu) => gpu != null;

    /// <summary>
    /// The fsr4xyz INT8 build is for RDNA2 cards that cannot use the stock FSR 4 path.
    /// </summary>
    public static bool SupportsCommunityFsr4(GpuInfo? gpu) =>
        gpu != null && gpu.VendorId == AmdVendorId && RadeonRx6000Regex().IsMatch(gpu.Name);
}
