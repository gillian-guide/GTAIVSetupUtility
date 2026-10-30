namespace GTAIVSetupUtility.Models;

/// <summary>
/// A physical GPU as seen by the headless Vulkan probe.
/// </summary>
public sealed record GpuInfo(
    int Index,
    string Name,
    uint VendorId,
    uint DeviceId,
    int DeviceType,
    int VulkanMajor,
    int VulkanMinor,
    bool IsWindowsDefault,
    int DxvkSupport)
{
    public bool IsDiscrete => DeviceType == 2;
    public bool IsIntegrated => DeviceType == 1;
}
