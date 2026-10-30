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
    /// FSR 4 Lite is only selected natively by AMD on Ryzen AI / Radeon 7xxM-8xxM APUs.
    /// The community INT8 fork is not offered for other GPUs here: it is RDNA2-specific and
    /// freezes on NVIDIA (tested on RTX 5060).
    /// </summary>
    public static bool SupportsFsr4Lite(GpuInfo? gpu) => SupportsNativeFsr4Lite(gpu);

    /// <summary>
    /// The fsr4xyz INT8 build is for RDNA2 cards that cannot use the stock FSR 4 path.
    /// </summary>
    public static bool SupportsCommunityFsr4(GpuInfo? gpu) =>
        gpu != null && gpu.VendorId == AmdVendorId && RadeonRx6000Regex().IsMatch(gpu.Name);

    /// <summary>
    /// AMD only selects the Lite model automatically on Ryzen AI / Radeon 7xxM-8xxM APUs.
    /// </summary>
    public static bool SupportsNativeFsr4Lite(GpuInfo? gpu) =>
        gpu != null &&
        gpu.VendorId == AmdVendorId &&
        (RyzenAiRegex().IsMatch(gpu.Name) || RadeonMobileRegex().IsMatch(gpu.Name));

    public static bool SupportsFsr31(GpuInfo? gpu) => gpu != null;
}
