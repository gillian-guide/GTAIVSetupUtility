using GTAIVSetupUtility.Localizations;
using GTAIVSetupUtility.Models;
using GTAIVSetupUtility.Services;
using GTAIVSetupUtility.Services.Vulkan;
using MsBox.Avalonia.Enums;

namespace GTAIVSetupUtility.Services;

/// <summary>
/// Aggregates the headless Vulkan probe into the DXVK support tiers the rest of the app uses.
/// The rules are intentionally identical to the old vulkaninfo-based checker (archived in
/// Services/Archived); only the data source changed.
/// </summary>
public static class VulkanCheckerService
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private const string ExtensionDepthClipEnable = "VK_EXT_depth_clip_enable";
    private const string ExtensionMaintenance5 = "VK_KHR_maintenance5";
    private const string ExtensionMaintenance6 = "VK_KHR_maintenance6";
    private const string ExtensionTransformFeedback = "VK_EXT_transform_feedback";
    private const string ExtensionDescriptorIndexing = "VK_EXT_descriptor_indexing";
    private const string ExtensionGraphicsPipelineLibrary = "VK_EXT_graphics_pipeline_library";

    public static async Task<VulkanCheckResult> VulkanCheckAsync(bool showNotifications = true)
    {
        Logger.Info(" Running the embedded, headless Vulkan check...");

        VulkanProbeResult probe;
        try
        {
            probe = await Task.Run(() => VulkanProbeService.Probe(null));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, " The embedded Vulkan check failed unexpectedly.");
            if (showNotifications)
            {
                await MessageBoxService.ShowAsync(
                    Resources.VulkanInfoCheckFullFailTitle,
                    Resources.VulkanInfoCheckFullFailDescription,
                    Icon.Error);
            }

            return EmptyResult();
        }

        if (!probe.IsAvailable || probe.Devices.Count == 0)
        {
            Logger.Error($" The embedded Vulkan check failed: {probe.FailureReason ?? "no physical devices"}");
            if (showNotifications)
            {
                await MessageBoxService.ShowAsync(
                    Resources.VulkanInfoCheckFullFailTitle,
                    Resources.VulkanInfoCheckFullFailDescription,
                    Icon.Error);
            }

            return EmptyResult();
        }

        int vkDgpuDxvkSupport = 0;
        int vkIgpuDxvkSupport = 0;
        int gplSupport = 0;
        bool igpuOnly = true;
        bool dgpuOnly = true;
        bool intelIgpu = false;
        bool enableAsync = false;
        bool atLeastOneGpuSucceeded = false;
        bool atLeastOneGpuFailed = false;
        bool atLeastOneGpuFailedGpl = false;
        bool atLeastOneGpuFailedFl = false;

        var gpus = new List<GpuInfo>(probe.Devices.Count);

        foreach (VulkanDevice device in probe.Devices)
        {
            if (!device.IsResponsive || device.VulkanMajor < 1)
            {
                Logger.Error($" GPU{device.Index} could not be read. The user may have outdated drivers or an old GPU.");
                atLeastOneGpuFailed = true;
                continue;
            }

            Logger.Info($" {device.Name}'s supported Vulkan version is: {device.VulkanMajor}.{device.VulkanMinor}");

            bool hasDepthClipEnable = device.HasExtension(ExtensionDepthClipEnable);
            bool hasMaintenance5 = device.HasExtension(ExtensionMaintenance5);
            bool hasMaintenance6 = device.HasExtension(ExtensionMaintenance6);
            bool hasFullRobustness2 = device.RobustBufferAccess2 && device.NullDescriptor;
            bool hasTransformFeedback = device.HasExtension(ExtensionTransformFeedback);
            bool hasSufficientPushConstants = device.MaxPushConstantsSize >= 256;
            bool hasDescriptorIndexing = device.HasExtension(ExtensionDescriptorIndexing);

            Logger.Debug($" GPU{device.Index}: depthClip={hasDepthClipEnable} maintenance5={hasMaintenance5} " +
                         $"maintenance6={hasMaintenance6} robustness2={hasFullRobustness2} " +
                         $"transformFeedback={hasTransformFeedback} pushConstants={device.MaxPushConstantsSize} " +
                         $"descriptorIndexing={hasDescriptorIndexing}");

            bool supportsLatestDxvk = hasDepthClipEnable &&
                                      hasMaintenance5 &&
                                      hasMaintenance6 &&
                                      hasFullRobustness2 &&
                                      hasTransformFeedback &&
                                      hasSufficientPushConstants &&
                                      hasDescriptorIndexing;

            int dxvkSupport = 0;
            if (supportsLatestDxvk)
            {
                atLeastOneGpuSucceeded = true;
                Logger.Info($" GPU{device.Index} supports latest DXVK, yay!");
                dxvkSupport = 3;
            }
            else
            {
                Logger.Info($" GPU{device.Index} doesn't support latest DXVK, checking for 2.6.2 support...");

                bool supportsDxvk2 = hasFullRobustness2 && hasTransformFeedback;
                if (supportsDxvk2)
                {
                    atLeastOneGpuSucceeded = true;
                    Logger.Info($" GPU{device.Index} supports DXVK 2.x!");
                    dxvkSupport = 2;
                }
                else
                {
                    Logger.Debug($" GPU{device.Index} doesn't support DXVK 2.x, checking legacy versions...");

                    switch (device.VulkanMajor)
                    {
                        case 1 when device.VulkanMinor <= 1:
                            atLeastOneGpuSucceeded = true;
                            Logger.Info($" GPU{device.Index} doesn't support DXVK or has outdated drivers.");
                            break;
                        case 1 when device.VulkanMinor < 3:
                            atLeastOneGpuSucceeded = true;
                            Logger.Info($" GPU{device.Index} supports Legacy DXVK 1.x.");
                            dxvkSupport = 1;
                            break;
                    }
                }
            }

            bool deviceIsDiscreteGpu = device.DeviceType == VulkanNative.VkPhysicalDeviceTypeDiscreteGpu;
            if (deviceIsDiscreteGpu && dxvkSupport > vkDgpuDxvkSupport)
            {
                Logger.Info($" GPU{device.Index} is a discrete GPU.");
                vkDgpuDxvkSupport = dxvkSupport;
                igpuOnly = false;
            }
            else if (dxvkSupport > vkIgpuDxvkSupport)
            {
                Logger.Info($" GPU{device.Index} is an integrated GPU.");
                vkIgpuDxvkSupport = dxvkSupport;
                dgpuOnly = false;
                if (device.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Info($" GPU{device.Index} is an integrated Intel iGPU.");
                    intelIgpu = true;
                }
            }

            bool hasGpl = device.HasExtension(ExtensionGraphicsPipelineLibrary);
            bool supportsFullGpl = hasGpl && device.HasIndependentInterpolationDecoration;
            if (supportsFullGpl)
            {
                Logger.Info($" GPU{device.Index} supports GPL.");

                if (device.HasFastLinking)
                {
                    Logger.Info($" GPU{device.Index} supports Fast Linking.");
                    if (gplSupport < 2)
                        gplSupport = 2;
                }
                else
                {
                    atLeastOneGpuFailedFl = true;
                    if (gplSupport < 1)
                        gplSupport = 1;
                }
            }
            else
            {
                atLeastOneGpuFailedGpl = true;
            }

            gpus.Add(new GpuInfo(
                device.Index,
                device.Name,
                device.VendorId,
                device.DeviceId,
                device.DeviceType,
                device.VulkanMajor,
                device.VulkanMinor,
                device.IsWindowsDefault,
                dxvkSupport));
        }

        string messageText = string.Empty;
        if (!atLeastOneGpuSucceeded)
        {
            Logger.Error(" The Vulkan check failed partially. The user likely has outdated drivers or an old GPU.");
            messageText += Resources.VulkanInfoFailPartial;
            igpuOnly = true;
            dgpuOnly = false;
            intelIgpu = false;
            enableAsync = true;
            vkIgpuDxvkSupport = 1;
        }
        else
        {
            switch (atLeastOneGpuFailed)
            {
                case true when igpuOnly:
                    if (messageText != string.Empty) messageText += "\n\n";
                    messageText += Resources.VulkanInfoCheckFailDGPU;
                    break;
                case true when !igpuOnly:
                    if (messageText != string.Empty) messageText += "\n\n";
                    messageText += Resources.VulkanInfoCheckOneGPUFail;
                    break;
            }

            if ((atLeastOneGpuFailedGpl || atLeastOneGpuFailedFl) && gplSupport == 2)
            {
                if (messageText != string.Empty) messageText += "\n\n";
                messageText += Resources.VulkanInfoOneGPLFail;
                enableAsync = true;
            }
        }

        if (messageText != string.Empty && showNotifications)
        {
            await MessageBoxService.ShowAsync(
                Resources.NotificationsTitle,
                messageText + Resources.NotificationsDescription,
                Icon.Info);
        }

        int defaultGpuIndex = probe.Devices.FirstOrDefault(device => device.IsWindowsDefault)?.Index ?? 0;

        Logger.Info(" Vulkan check finished!");
        return new VulkanCheckResult(
            vkDgpuDxvkSupport,
            vkIgpuDxvkSupport,
            gplSupport,
            igpuOnly,
            dgpuOnly,
            intelIgpu,
            enableAsync,
            gpus,
            defaultGpuIndex);
    }

    private static VulkanCheckResult EmptyResult() => new(
        0, 0, 0, false, false, false, false,
        Array.Empty<GpuInfo>(), 0);
}
