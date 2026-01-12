using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.Json;
using System.Threading.Tasks;
using GTAIVSetupUtility.Localizations;
using GTAIVSetupUtility.ViewModels;
using MsBox.Avalonia.Enums;

// hi here, i'm an awful coder, so please clean up for me if it really bothers you (and like, this code is *really* stupid, sorry)
// this code accounts for ALL gpu's in the system and tries to work out the best conditions for installing DXVK
// so please don't strip the functionality
namespace GTAIVSetupUtility.Service
{
    public static class VulkanCheckerService
    {

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private static (int, int) ConvertApiVersion(uint apiVersion)
        {
            uint major = apiVersion >> 22;
            uint minor = apiVersion >> 12 & 0x3ff;
            return (Convert.ToInt32(major), Convert.ToInt32(minor));
        }
        public static async Task<(int vkDgpuDxvkSupport, int vkIgpuDxvkSupport, int gplSupport, bool igpuOnly, bool dgpuOnly, bool intelIgpu, bool enableAsync)> VulkanCheck()
        {
            int gpuCount;
            int gplSupport = 0;
            int vkDgpuDxvkSupport = 0;
            int vkIgpuDxvkSupport = 0;
            bool igpuOnly = true;
            bool dgpuOnly = true;
            bool intelIgpu = false;
            bool enableAsync = false;
            bool atLeastOneGpuSucceededVulkanInfo = false;
            bool atLeastOneGpuSucceededJson = false;
            bool atLeastOneGpuFailed = false;
            bool atLeastOneGpuFailedGpl = false;
            bool atLeastOneGpuFailedFl = false;
            bool nvidia50Series = false;
            var listOfFailedGpus = new List<int>();
            try
            {
                var query = new ObjectQuery("SELECT * FROM Win32_VideoController");
                var searcher = new ManagementObjectSearcher(query);
                var videoControllers = searcher.Get();
                gpuCount = videoControllers.Count;
            }
            catch (Exception)
            {
                Logger.Error($" Ran into error ");
                throw;
            }
            for (int i = 0; i < gpuCount; i++)
            {
                try
                {
                    Logger.Debug($" Running vulkaninfo on GPU{i}... If this infinitely loops, your GPU is weird!");
                    using var process = new Process();
                    process.StartInfo.FileName = "vulkaninfo";
                    process.StartInfo.Arguments = $"--json={i} --output data{i}.json";
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;

                    process.Start();

                    if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
                    {
                        atLeastOneGpuFailed = true;
                        listOfFailedGpus.Add(i);
                    }
                    else if (!File.Exists($"data{i}.json"))
                    {
                        Logger.Debug($" Failed to run vulkaninfo via the first method, trying again...");
                        process.StartInfo.Arguments = $"--json={i}";
                        process.Start();
                        string output = process.StandardOutput.ReadToEnd();
                        if (!process.WaitForExit(10) || string.IsNullOrEmpty(output))
                        {
                            atLeastOneGpuFailed = true;
                            listOfFailedGpus.Add(i);
                        }
                        else
                        {
                            await File.WriteAllTextAsync($"data{i}.json", output);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(" Ran into error: {Argument1}", ex);
                    atLeastOneGpuFailed = true;
                    listOfFailedGpus.Add(i);
                }

                if (!listOfFailedGpus.Contains(i))
                {
                    atLeastOneGpuSucceededVulkanInfo = true;
                }
                else
                {
                    Logger.Error($" Running vulkaninfo on GPU{i} failed! User likely has outdated drivers or an extremely old GPU.");
                }
            }
            if (!atLeastOneGpuSucceededVulkanInfo)
            {
                await MainWindowViewModel.ShowMessageBoxAsync(
                    Resources.VulkanInfoCheckFullFailTitle,
                    Resources.VulkanInfoCheckFullFailDescription,
                    Icon.Error);
                Logger.Error(" Running vulkaninfo failed entirely! User likely has outdated drivers or an extremely old GPU.");
                return (0, 0, 0, false, false, false, false);
            }

            Logger.Debug(" Analyzing the vulkaninfo for every .json generated...");
            for (int x = 0; x < gpuCount; x++)
            {
                if (listOfFailedGpus.Contains(x))
                {
                    Logger.Debug($" GPU{x} is in the failed list, skipping this iteration of the loop...");
                    continue;
                }

                Logger.Debug($" Checking data{x}.json...");
                if (File.Exists($"data{x}.json"))
                {
                    using (var file = File.OpenText($"data{x}.json"))
                    {
                        int dxvkSupport = 0;
                        JsonDocument doc;
                        try
                        {
                            doc = JsonDocument.Parse(file.ReadToEnd());
                        }
                        catch (JsonException)
                        {
                            Logger.Error($" Failed to read data{x}.json.");
                            atLeastOneGpuFailed = true;
                            listOfFailedGpus.Add(x);
                            Logger.Debug($" Removing data{x}.json...");
                            file.Close();
                            File.Delete($"data{x}.json");
                            continue;
                        }

                        var root = doc.RootElement;
                        JsonElement properties;
                        JsonElement physicalDeviceProperties;
                        JsonElement extensions;
                        JsonElement features;

                        if (root.TryGetProperty("capabilities", out var capabilities))
                        {
                            var deviceCapabilities = capabilities.GetProperty("device");
                            properties = deviceCapabilities.GetProperty("properties");
                            physicalDeviceProperties = properties.GetProperty("VkPhysicalDeviceProperties");

                            extensions = deviceCapabilities.GetProperty("extensions");
                            features = deviceCapabilities.GetProperty("features");
                        }
                        else if (root.TryGetProperty("VkPhysicalDeviceProperties", out physicalDeviceProperties)
                                 && root.TryGetProperty("ArrayOfVkExtensionProperties", out extensions))
                        {
                            properties = root;
                            features = root;
                        }
                        else
                        {
                            Logger.Error($" Failed to read data{x}.json.");
                            atLeastOneGpuFailed = true;
                            Logger.Debug($" Removing data{x}.json...");
                            File.Delete($"data{x}.json");
                            continue;
                        }

                        string? deviceName = physicalDeviceProperties.GetProperty("deviceName").GetString();
                        uint apiVersion = physicalDeviceProperties.GetProperty("apiVersion").GetUInt32();
                        (int vulkanVerMajor, int vulkanVerMinor) = ConvertApiVersion(apiVersion);

                        Logger.Info($" {deviceName}'s supported Vulkan version is: {vulkanVerMajor}.{vulkanVerMinor}");
                        Logger.Debug($" Checking if GPU{x} supports latest DXVK...");
                        
                        bool hasDepthClipEnable = CheckIfExtensionExists(extensions, "VK_EXT_depth_clip_enable");
                        Logger.Debug($" GPU{x}'s depth clip enable: {hasDepthClipEnable}");
                        bool hasMaintenance5 = CheckIfExtensionExists(extensions, "VK_KHR_maintenance5");
                        Logger.Debug($" GPU{x}'s maintenance5: {hasMaintenance5}");
                        bool hasMaintenance6 = CheckIfExtensionExists(extensions, "VK_KHR_maintenance6");
                        Logger.Debug($" GPU{x}'s maintenance6: {hasMaintenance6}");
                        bool hasRobustness2 = CheckIfExtensionExists(extensions, "VK_EXT_robustness2");
                        bool hasRobustBufferAccess2 = false;
                        bool hasNullDescriptor = false;
                        if (hasRobustness2)
                        {
                            bool foundRobustnessFeatures = false;
                            
                            if (features.TryGetProperty("VkPhysicalDeviceRobustness2FeaturesKHR", out var robustnessFeatures))
                            {
                                foundRobustnessFeatures = true;;
                            }
                            
                            // fallback for older drivers
                            else if (features.TryGetProperty("VkPhysicalDeviceRobustness2FeaturesEXT", out robustnessFeatures))
                            {
                                foundRobustnessFeatures = true;
                            }
    
                            if (foundRobustnessFeatures)
                            {
                                if (robustnessFeatures.TryGetProperty("robustBufferAccess2", out var robustBufferAccess))
                                {
                                    hasRobustBufferAccess2 = robustBufferAccess.GetBoolean();
                                }
                                if (robustnessFeatures.TryGetProperty("nullDescriptor", out var nullDescriptor))
                                {
                                    hasNullDescriptor = nullDescriptor.GetBoolean();
                                }
                            }
                        }
                        bool hasFullRobustness2 = hasRobustBufferAccess2 && hasNullDescriptor;
                        Logger.Debug($" GPU{x}'s robustness2: {hasRobustBufferAccess2}, {hasNullDescriptor}");
                        
                        bool hasTransformFeedback = CheckIfExtensionExists(extensions, "VK_EXT_transform_feedback");
                        Logger.Debug($" GPU{x}'s transform feedback: {hasTransformFeedback}");

                        bool hasSufficientPushConstants = false;
                        if (physicalDeviceProperties.TryGetProperty("limits", out var limits) &&
                            limits.TryGetProperty("maxPushConstantsSize", out var pushConstantsSize))
                        {
                            uint maxPushConstants = pushConstantsSize.GetUInt32();
                            hasSufficientPushConstants = maxPushConstants >= 256;
                            Logger.Debug($" GPU{x} maxPushConstantsSize: {maxPushConstants} bytes {(hasSufficientPushConstants ? "(sufficient)" : "(not supported)")}");
                        }
                        else
                        {
                            Logger.Debug($" Unable to determine GPU{x}'s maxPushConstantsSize");
                        }
                        
                        bool hasDescriptorIndexing = CheckIfExtensionExists(extensions, "VK_EXT_descriptor_indexing");
                        Logger.Debug($" GPU{x}'s descriptor indexing: {hasDescriptorIndexing}");
                        
                        bool hasGPL =  CheckIfExtensionExists(extensions, "VK_EXT_graphics_pipeline_library");

                        bool hasFL = false;
                        bool hasIID = false;
                        if (hasGPL && properties.TryGetProperty("VkPhysicalDeviceGraphicsPipelineLibraryPropertiesEXT", out var gplFeatures))
                        {
                            if (gplFeatures.TryGetProperty("graphicsPipelineLibraryIndependentInterpolationDecoration", out var IIDecoration))
                            {
                                hasIID = IIDecoration.GetBoolean();
                            }
                            if (gplFeatures.TryGetProperty("graphicsPipelineLibraryFastLinking", out var fastLinking))
                            {
                                hasFL = fastLinking.GetBoolean();
                            }
                        }
                        bool supportsFullGPL = hasGPL && hasIID;
                        Logger.Debug($" GPU{x}'s GPL support: {hasGPL}, {hasIID}, {hasFL}");
                        
                        bool supportsLatestDxvk = hasDepthClipEnable && 
                                             hasMaintenance5 && 
                                             hasMaintenance6 && 
                                             hasFullRobustness2 &&
                                             hasTransformFeedback && 
                                             hasSufficientPushConstants && 
                                             hasDescriptorIndexing;

                        if (supportsLatestDxvk)
                        {
                            atLeastOneGpuSucceededJson = true;
                            Logger.Info($" GPU{x} supports latest DXVK, yay!");
                            dxvkSupport = 3;
                        }
                        else
                        {
                            Logger.Info($" GPU{x} doesn't support latest DXVK, checking for 2.6.2 support...");

                            bool supportsDxvk2 = hasFullRobustness2 && hasTransformFeedback;
                            if (supportsDxvk2)
                            {
                                atLeastOneGpuSucceededJson = true;
                                Logger.Info($" GPU{x} supports DXVK 2.x!");
                                dxvkSupport = 2;
                            }
                            else
                            {
                                Logger.Debug($" GPU{x} doesn't support DXVK 2.x, checking legacy versions...");

                                switch (vulkanVerMajor)
                                {
                                    case 1 when vulkanVerMinor <= 1:
                                        atLeastOneGpuSucceededJson = true;
                                        Logger.Info($" GPU{x} doesn't support DXVK or has outdated drivers.");
                                        break;
                                    case 1 when vulkanVerMinor < 3:
                                        atLeastOneGpuSucceededJson = true;
                                        Logger.Info($" GPU{x} supports Legacy DXVK 1.x.");
                                        dxvkSupport = 1;
                                        break;
                                }
                            }
                        }

                        var deviceType = physicalDeviceProperties.GetProperty("deviceType");
                        bool deviceIsDiscreteGpu = deviceType.ValueKind switch
                        {
                            JsonValueKind.String => deviceType.GetString() == "VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU",
                            JsonValueKind.Number => deviceType.GetByte() == 2,
                            _ => throw new InvalidOperationException($"Unsupported value type {deviceType.ValueKind}"),
                        };

                        if (deviceIsDiscreteGpu && dxvkSupport > vkDgpuDxvkSupport)
                        {
                            Logger.Info($" GPU{x} is a discrete GPU.");
                            vkDgpuDxvkSupport = dxvkSupport;
                            igpuOnly = false;
                            if (deviceName!.Contains("RTX 50"))
                            {
                                Logger.Info($" GPU{x} is a 50-series NVIDIA GPU.");
                                nvidia50Series = true;
                            }
                        }
                        else if (dxvkSupport > vkIgpuDxvkSupport)
                        {
                            Logger.Info($" GPU{x} is an integrated GPU.");
                            vkIgpuDxvkSupport = dxvkSupport;
                            dgpuOnly = false;
                            if (deviceName!.Contains("Intel"))
                            {
                                Logger.Info($" GPU{x} is an integrated Intel iGPU.");
                                intelIgpu = true;
                            }
                        }

                        if (supportsFullGPL)
                        {
                            Logger.Info($" GPU{x} supports GPL.");

                            if (hasFL)
                            {
                                Logger.Info($" GPU{x} supports Fast Linking.");
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
                    }
                    Logger.Debug($" Removing data{x}.json...");
                    File.Delete($"data{x}.json");
                }
                else { break; }
            }

            string messagetext = "";
            if (!atLeastOneGpuSucceededJson)
            {
                Logger.Error($" Running vulkaninfo failed partially. User likely has outdated drivers or an old GPU.");
                messagetext += Resources.VulkanInfoFailPartial;
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
                    {
                        if (messagetext != "") { messagetext += "\n\n"; }
                        messagetext += Resources.VulkanInfoCheckFailDGPU;
                        break;
                    }
                    case true when !igpuOnly:
                    {
                        if (messagetext != "") { messagetext += "\n\n"; }
                        messagetext += Resources.VulkanInfoCheckOneGPUFail;
                        break;
                    }
                }
                if ((atLeastOneGpuFailedGpl || atLeastOneGpuFailedFl) && gplSupport == 2)
                {
                    if (messagetext != "") { messagetext += "\n\n"; }
                    messagetext += Resources.VulkanInfoOneGPLFail;
                    enableAsync = true;
                }
                if (nvidia50Series)
                {
                    if (messagetext != "") { messagetext += "\n\n"; }
                    messagetext += Resources.VulkanInfo50Series;
                }
            }
            
            if (messagetext != "")
            {
                await MainWindowViewModel.ShowMessageBoxAsync(
                    Resources.NotificationsTitle,
                    messagetext + Resources.NotificationsDescription,
                    Icon.Info);
            }
            
            return (vkDgpuDxvkSupport, vkIgpuDxvkSupport, gplSupport, igpuOnly, dgpuOnly, intelIgpu, enableAsync);
        }
        private static bool CheckIfExtensionExists(JsonElement extensionElement, string extensionName)
        {
            switch (extensionElement.ValueKind)
            {
                case JsonValueKind.Object:
                    return extensionElement.TryGetProperty(extensionName, out _);
                case JsonValueKind.Array:
                {
                    return extensionElement.EnumerateArray().Any(extension => extension.GetProperty("extensionName").GetString() == extensionName);
                }
                case JsonValueKind.Undefined:
                case JsonValueKind.String:
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Null:
                default:
                    throw new ArgumentOutOfRangeException(nameof(extensionElement), $"Unknown extension element kind {extensionElement.ValueKind}");
            }
        }
    }
}
