namespace GTAIVSetupUtility.Models;

/// <summary>
/// The result of the embedded Vulkan check. Mirrors the old vulkaninfo-based tuple,
/// with the GPU list kept so the DLSS-IV module can make capability decisions.
/// </summary>
public sealed record VulkanCheckResult(
    int VkDgpuDxvkSupport,
    int VkIgpuDxvkSupport,
    int GplSupport,
    bool IgpuOnly,
    bool DgpuOnly,
    bool IntelIgpu,
    bool EnableAsync,
    IReadOnlyList<GpuInfo> Gpus,
    int DefaultGpuIndex)
{
    public GpuInfo? DefaultGpu =>
        Gpus.Count == 0
            ? null
            : Gpus.FirstOrDefault(gpu => gpu.Index == DefaultGpuIndex) ?? Gpus[0];
}
