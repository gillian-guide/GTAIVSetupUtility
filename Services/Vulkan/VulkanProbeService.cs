using System.Runtime.InteropServices;
using System.Text;
using NLog;

namespace GTAIVSetupUtility.Services.Vulkan;

/// <summary>
/// One physical device, with the raw extension/feature data the DXVK check needs.
/// </summary>
public sealed class VulkanDevice
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public uint VendorId { get; init; }
    public uint DeviceId { get; init; }
    public int DeviceType { get; init; }
    public int VulkanMajor { get; init; }
    public int VulkanMinor { get; init; }
    public bool LuidValid { get; init; }
    public ulong Luid { get; init; }
    public bool IsResponsive { get; init; } = true;
    public bool RobustBufferAccess2 { get; init; }
    public bool NullDescriptor { get; init; }
    public uint MaxPushConstantsSize { get; init; }
    public bool HasFastLinking { get; init; }
    public bool HasIndependentInterpolationDecoration { get; init; }
    public IReadOnlySet<string> Extensions { get; init; } = new HashSet<string>();
    public bool IsWindowsDefault { get; set; }

    public bool HasExtension(string extension) => Extensions.Contains(extension);
}

public sealed record VulkanProbeResult(
    bool IsAvailable,
    string? FailureReason,
    IReadOnlyList<VulkanDevice> Devices,
    WindowsAdapterInfo? PreferredAdapter);

/// <summary>
/// Headless, in-process Vulkan capability probe. It creates a plain Vulkan instance, enumerates
/// every physical device, asks for the extensions/features the DXVK check uses, and picks the
/// adapter Windows would use for the game (DXGI LUID match).
/// </summary>
public static class VulkanProbeService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private const string DeviceExtensionDepthClipEnable = "VK_EXT_depth_clip_enable";
    private const string DeviceExtensionMaintenance5 = "VK_KHR_maintenance5";
    private const string DeviceExtensionMaintenance6 = "VK_KHR_maintenance6";
    private const string DeviceExtensionRobustness2 = "VK_EXT_robustness2";
    private const string DeviceExtensionTransformFeedback = "VK_EXT_transform_feedback";
    private const string DeviceExtensionDescriptorIndexing = "VK_EXT_descriptor_indexing";
    private const string DeviceExtensionGraphicsPipelineLibrary = "VK_EXT_graphics_pipeline_library";

    public static VulkanProbeResult Probe(string? gameExePath)
    {
        WindowsAdapterInfo? preferred = null;
        try
        {
            preferred = WindowsGpuPreference.GetGamePreferredAdapter(gameExePath);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, " Could not resolve the Windows game GPU; using Vulkan's first device.");
        }

        IntPtr instance = IntPtr.Zero;
        try
        {
            uint loaderVersion = GetLoaderApiVersion();
            Logger.Debug($" Vulkan loader API version: {VulkanNative.DecodeApiVersion(loaderVersion).Major}.{VulkanNative.DecodeApiVersion(loaderVersion).Minor}");

            uint requestedVersion = Math.Min(loaderVersion, VulkanNative.VulkanApiVersion13);
            if (requestedVersion < VulkanNative.VulkanApiVersion10)
                requestedVersion = VulkanNative.VulkanApiVersion10;

            int createResult = CreateInstance(requestedVersion, out instance);
            if (createResult != VulkanNative.VkSuccess && requestedVersion > VulkanNative.VulkanApiVersion10)
            {
                // Some very old loaders only accept API 1.0 at instance creation.
                Logger.Debug($" vkCreateInstance failed for 1.x (0x{createResult:X8}); retrying with Vulkan 1.0.");
                requestedVersion = VulkanNative.VulkanApiVersion10;
                createResult = CreateInstance(requestedVersion, out instance);
            }

            if (createResult != VulkanNative.VkSuccess || instance == IntPtr.Zero)
            {
                string reason = $"vkCreateInstance failed (0x{createResult:X8})";
                Logger.Error($" {reason}");
                return new VulkanProbeResult(false, reason, Array.Empty<VulkanDevice>(), preferred);
            }

            var devices = EnumerateDevices(instance);
            SelectWindowsDefault(devices, preferred);

            Logger.Info($" Vulkan probe found {devices.Count} physical device(s).");
            return new VulkanProbeResult(true, null, devices, preferred);
        }
        catch (DllNotFoundException)
        {
            const string reason = "vulkan-1.dll was not found. The Vulkan runtime is not installed.";
            Logger.Error($" {reason}");
            return new VulkanProbeResult(false, reason, Array.Empty<VulkanDevice>(), preferred);
        }
        catch (BadImageFormatException)
        {
            const string reason = "vulkan-1.dll has the wrong architecture.";
            Logger.Error($" {reason}");
            return new VulkanProbeResult(false, reason, Array.Empty<VulkanDevice>(), preferred);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, " The embedded Vulkan probe failed.");
            return new VulkanProbeResult(false, ex.Message, Array.Empty<VulkanDevice>(), preferred);
        }
        finally
        {
            if (instance != IntPtr.Zero)
            {
                try
                {
                    VulkanNative.vkDestroyInstance(instance, IntPtr.Zero);
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, " vkDestroyInstance failed.");
                }
            }
        }
    }

    private static uint GetLoaderApiVersion()
    {
        try
        {
            if (VulkanNative.vkEnumerateInstanceVersion(out uint version) == VulkanNative.VkSuccess &&
                version >= VulkanNative.VulkanApiVersion10)
            {
                return version;
            }
        }
        catch (EntryPointNotFoundException)
        {
            // Vulkan 1.0 loaders do not export vkEnumerateInstanceVersion.
        }

        return VulkanNative.VulkanApiVersion10;
    }

    private static int CreateInstance(uint apiVersion, out IntPtr instance)
    {
        instance = IntPtr.Zero;

        IntPtr appName = Marshal.StringToHGlobalAnsi("GTAIVSetupUtility");
        IntPtr engineName = Marshal.StringToHGlobalAnsi("GTAIVSetupUtility Vulkan probe");
        IntPtr extensionNames = IntPtr.Zero;
        uint extensionCount = 0;

        try
        {
            bool needProperties2Extension =
                apiVersion < VulkanNative.VulkanApiVersion11 &&
                HasInstanceExtension(VulkanNative.ExtensionGetPhysicalDeviceProperties2);

            if (needProperties2Extension)
            {
                string[] extensions = [VulkanNative.ExtensionGetPhysicalDeviceProperties2];
                extensionNames = AllocateStringArray(extensions);
                extensionCount = (uint)extensions.Length;
            }

            var appInfo = new VulkanNative.VkApplicationInfo
            {
                SType = VulkanNative.VkStructureTypeApplicationInfo,
                PNext = IntPtr.Zero,
                PApplicationName = appName,
                ApplicationVersion = VulkanNative.MakeApiVersion(2, 1, 0),
                PEngineName = engineName,
                EngineVersion = VulkanNative.MakeApiVersion(2, 1, 0),
                ApiVersion = apiVersion
            };

            var createInfo = new VulkanNative.VkInstanceCreateInfo
            {
                SType = VulkanNative.VkStructureTypeInstanceCreateInfo,
                PNext = IntPtr.Zero,
                Flags = 0,
                PApplicationInfo = AllocStructure(appInfo),
                EnabledLayerCount = 0,
                PpEnabledLayerNames = IntPtr.Zero,
                EnabledExtensionCount = extensionCount,
                PpEnabledExtensionNames = extensionNames
            };

            try
            {
                return VulkanNative.vkCreateInstance(ref createInfo, IntPtr.Zero, out instance);
            }
            finally
            {
                if (createInfo.PApplicationInfo != IntPtr.Zero)
                    Marshal.FreeHGlobal(createInfo.PApplicationInfo);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(appName);
            Marshal.FreeHGlobal(engineName);
            FreeStringArray(extensionNames, extensionCount);
        }
    }

    private static IReadOnlyList<VulkanDevice> EnumerateDevices(IntPtr instance)
    {
        uint count = 0;
        int result = VulkanNative.vkEnumeratePhysicalDevices(instance, ref count, IntPtr.Zero);
        if (result != VulkanNative.VkSuccess && result != VulkanNative.VkIncomplete)
        {
            Logger.Error($" vkEnumeratePhysicalDevices(count) failed (0x{result:X8}).");
            return Array.Empty<VulkanDevice>();
        }

        if (count == 0)
            return Array.Empty<VulkanDevice>();

        IntPtr handles = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size));
        try
        {
            result = VulkanNative.vkEnumeratePhysicalDevices(instance, ref count, handles);
            if (result != VulkanNative.VkSuccess && result != VulkanNative.VkIncomplete)
            {
                Logger.Error($" vkEnumeratePhysicalDevices(list) failed (0x{result:X8}).");
                return Array.Empty<VulkanDevice>();
            }

            var devices = new List<VulkanDevice>((int)count);
            for (int i = 0; i < count; i++)
            {
                IntPtr handle = Marshal.ReadIntPtr(handles, i * IntPtr.Size);
                try
                {
                    devices.Add(ReadDevice(handle, i));
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $" Failed to read Vulkan device {i}.");
                    devices.Add(new VulkanDevice
                    {
                        Index = i,
                        Name = $"Unknown GPU {i}",
                        IsResponsive = false,
                        Extensions = new HashSet<string>(StringComparer.Ordinal)
                    });
                }
            }

            return devices;
        }
        finally
        {
            Marshal.FreeHGlobal(handles);
        }
    }

    private static VulkanDevice ReadDevice(IntPtr physicalDevice, int index)
    {
        var extensions = ReadDeviceExtensions(physicalDevice);

        IntPtr propertiesBuffer = Marshal.AllocHGlobal(2048);
        IntPtr idPropertiesBuffer = IntPtr.Zero;
        IntPtr gplPropertiesBuffer = IntPtr.Zero;

        try
        {
            bool hasProperties2 = TryGetProperties2(
                physicalDevice,
                propertiesBuffer,
                out idPropertiesBuffer,
                out gplPropertiesBuffer);

            if (!hasProperties2)
            {
                Logger.Debug($" GPU{index}: vkGetPhysicalDeviceProperties2 is unavailable, using Vulkan 1.0 properties.");
                VulkanNative.vkGetPhysicalDeviceProperties(physicalDevice, propertiesBuffer);
            }

            string name = ReadUtf8String(IntPtr.Add(propertiesBuffer, VulkanNative.PropertiesDeviceNameOffset),
                VulkanNative.VkMaxPhysicalDeviceNameSize);
            uint apiVersion = (uint)Marshal.ReadInt32(propertiesBuffer, VulkanNative.PropertiesApiVersionOffset);
            uint vendorId = (uint)Marshal.ReadInt32(propertiesBuffer, VulkanNative.PropertiesVendorIdOffset);
            uint deviceId = (uint)Marshal.ReadInt32(propertiesBuffer, VulkanNative.PropertiesDeviceIdOffset);
            int deviceType = Marshal.ReadInt32(propertiesBuffer, VulkanNative.PropertiesDeviceTypeOffset);
            uint maxPushConstants = (uint)Marshal.ReadInt32(
                propertiesBuffer + VulkanNative.PropertiesMaxPushConstantsOffset);
            (int major, int minor) = VulkanNative.DecodeApiVersion(apiVersion);

            bool luidValid = false;
            ulong luid = 0;
            if (idPropertiesBuffer != IntPtr.Zero)
            {
                var idProperties = Marshal.PtrToStructure<VulkanNative.VkPhysicalDeviceIdProperties>(idPropertiesBuffer);
                luidValid = idProperties.DeviceLUIDValid != 0 &&
                            idProperties.DeviceLUID is { Length: >= VulkanNative.VkLuidSize };
                if (luidValid)
                    luid = BitConverter.ToUInt64(idProperties.DeviceLUID, 0);
            }

            bool fastLinking = false;
            bool independentInterpolation = false;
            if (gplPropertiesBuffer != IntPtr.Zero)
            {
                var gplProperties = Marshal.PtrToStructure<VulkanNative.VkPhysicalDeviceGraphicsPipelineLibraryPropertiesExt>(gplPropertiesBuffer);
                fastLinking = gplProperties.GraphicsPipelineLibraryFastLinking != 0;
                independentInterpolation = gplProperties.GraphicsPipelineLibraryIndependentInterpolationDecoration != 0;
            }

            (bool robustBufferAccess2, bool nullDescriptor) = ReadRobustnessFeatures(physicalDevice, index);

            bool hasRobustness2 = extensions.Contains(DeviceExtensionRobustness2);
            robustBufferAccess2 &= hasRobustness2;
            nullDescriptor &= hasRobustness2;

            Logger.Debug($" GPU{index}: {name} | Vulkan {major}.{minor} | vendor 0x{vendorId:X4} | device 0x{deviceId:X4} | type {deviceType}");
            Logger.Debug($" GPU{index}: LUID {(luidValid ? $"0x{luid:X16}" : "not exposed")} | maxPushConstants {maxPushConstants} | " +
                         $"depthClip={extensions.Contains(DeviceExtensionDepthClipEnable)} " +
                         $"maint5={extensions.Contains(DeviceExtensionMaintenance5)} " +
                         $"maint6={extensions.Contains(DeviceExtensionMaintenance6)} " +
                         $"robustness2={hasRobustness2} " +
                         $"transformFeedback={extensions.Contains(DeviceExtensionTransformFeedback)} " +
                         $"descriptorIndexing={extensions.Contains(DeviceExtensionDescriptorIndexing)} " +
                         $"gpl={extensions.Contains(DeviceExtensionGraphicsPipelineLibrary)}");

            return new VulkanDevice
            {
                Index = index,
                Name = name,
                VendorId = vendorId,
                DeviceId = deviceId,
                DeviceType = deviceType,
                VulkanMajor = major,
                VulkanMinor = minor,
                LuidValid = luidValid,
                Luid = luid,
                RobustBufferAccess2 = robustBufferAccess2,
                NullDescriptor = nullDescriptor,
                MaxPushConstantsSize = maxPushConstants,
                HasFastLinking = fastLinking,
                HasIndependentInterpolationDecoration = independentInterpolation,
                Extensions = extensions
            };
        }
        finally
        {
            Marshal.FreeHGlobal(propertiesBuffer);
            if (idPropertiesBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(idPropertiesBuffer);
            if (gplPropertiesBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(gplPropertiesBuffer);
        }
    }

    private static bool TryGetProperties2(
        IntPtr physicalDevice,
        IntPtr propertiesBuffer,
        out IntPtr idPropertiesBuffer,
        out IntPtr gplPropertiesBuffer)
    {
        idPropertiesBuffer = IntPtr.Zero;
        gplPropertiesBuffer = IntPtr.Zero;

        try
        {
            var idProperties = VulkanNative.VkPhysicalDeviceIdProperties.Create();
            idPropertiesBuffer = AllocStructure(idProperties);

            var gplProperties = VulkanNative.VkPhysicalDeviceGraphicsPipelineLibraryPropertiesExt.Create();
            gplPropertiesBuffer = AllocStructure(gplProperties);

            // Chain: properties2 -> ID properties -> GPL properties.
            Marshal.WriteIntPtr(idPropertiesBuffer, 8, gplPropertiesBuffer);

            IntPtr properties2 = Marshal.AllocHGlobal(16 + 2048);
            try
            {
                Marshal.WriteInt32(properties2, 0, VulkanNative.VkStructureTypePhysicalDeviceProperties2);
                Marshal.WriteIntPtr(properties2, 8, idPropertiesBuffer);

                VulkanNative.vkGetPhysicalDeviceProperties2(physicalDevice, properties2);

                // The native structure begins at offset 16; copy it into the caller's buffer.
                CopyMemory(IntPtr.Add(properties2, 16), propertiesBuffer, 2048);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(properties2);
            }
        }
        catch (EntryPointNotFoundException)
        {
            if (idPropertiesBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(idPropertiesBuffer);
                idPropertiesBuffer = IntPtr.Zero;
            }

            if (gplPropertiesBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(gplPropertiesBuffer);
                gplPropertiesBuffer = IntPtr.Zero;
            }

            return false;
        }
    }

    private static (bool RobustBufferAccess2, bool NullDescriptor) ReadRobustnessFeatures(IntPtr physicalDevice, int index)
    {
        IntPtr robustnessBuffer = IntPtr.Zero;
        IntPtr features2 = IntPtr.Zero;

        try
        {
            var robustness = VulkanNative.VkPhysicalDeviceRobustness2FeaturesExt.Create();
            robustnessBuffer = AllocStructure(robustness);

            features2 = Marshal.AllocHGlobal(16 + 512);
            Marshal.WriteInt32(features2, 0, VulkanNative.VkStructureTypePhysicalDeviceFeatures2);
            Marshal.WriteIntPtr(features2, 8, robustnessBuffer);

            VulkanNative.vkGetPhysicalDeviceFeatures2(physicalDevice, features2);

            var result = Marshal.PtrToStructure<VulkanNative.VkPhysicalDeviceRobustness2FeaturesExt>(robustnessBuffer);
            return (result.RobustBufferAccess2 != 0, result.NullDescriptor != 0);
        }
        catch (EntryPointNotFoundException)
        {
            Logger.Debug($" GPU{index}: vkGetPhysicalDeviceFeatures2 is unavailable.");
            return (false, false);
        }
        finally
        {
            if (robustnessBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(robustnessBuffer);
            if (features2 != IntPtr.Zero)
                Marshal.FreeHGlobal(features2);
        }
    }

    private static HashSet<string> ReadDeviceExtensions(IntPtr physicalDevice)
    {
        var extensions = new HashSet<string>(StringComparer.Ordinal);

        uint count = 0;
        int result = VulkanNative.vkEnumerateDeviceExtensionProperties(
            physicalDevice, IntPtr.Zero, ref count, IntPtr.Zero);
        if (result != VulkanNative.VkSuccess && result != VulkanNative.VkIncomplete)
            return extensions;

        if (count == 0)
            return extensions;

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)count * VulkanNative.VkExtensionPropertiesSize));
        try
        {
            result = VulkanNative.vkEnumerateDeviceExtensionProperties(
                physicalDevice, IntPtr.Zero, ref count, buffer);
            if (result != VulkanNative.VkSuccess && result != VulkanNative.VkIncomplete)
                return extensions;

            for (int i = 0; i < count; i++)
            {
                string name = ReadUtf8String(
                    IntPtr.Add(buffer, i * VulkanNative.VkExtensionPropertiesSize),
                    VulkanNative.VkMaxPhysicalDeviceNameSize);
                if (!string.IsNullOrEmpty(name))
                    extensions.Add(name);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return extensions;
    }

    private static bool HasInstanceExtension(string extensionName)
    {
        uint count = 0;
        int result = VulkanNative.vkEnumerateInstanceExtensionProperties(
            IntPtr.Zero, ref count, IntPtr.Zero);
        if (result != VulkanNative.VkSuccess && result != VulkanNative.VkIncomplete)
            return false;

        if (count == 0)
            return false;

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)count * VulkanNative.VkExtensionPropertiesSize));
        try
        {
            result = VulkanNative.vkEnumerateInstanceExtensionProperties(
                IntPtr.Zero, ref count, buffer);
            if (result != VulkanNative.VkSuccess && result != VulkanNative.VkIncomplete)
                return false;

            for (int i = 0; i < count; i++)
            {
                string name = ReadUtf8String(
                    IntPtr.Add(buffer, i * VulkanNative.VkExtensionPropertiesSize),
                    VulkanNative.VkMaxPhysicalDeviceNameSize);
                if (string.Equals(name, extensionName, StringComparison.Ordinal))
                    return true;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return false;
    }

    private static void SelectWindowsDefault(IReadOnlyList<VulkanDevice> devices, WindowsAdapterInfo? preferred)
    {
        if (devices.Count == 0)
            return;

        VulkanDevice? selected = null;

        if (preferred != null)
        {
            selected = devices.FirstOrDefault(device =>
                device.LuidValid && device.Luid == unchecked((ulong)preferred.Luid));

            if (selected == null && !string.IsNullOrWhiteSpace(preferred.Description))
            {
                selected = devices.FirstOrDefault(device =>
                    string.Equals(device.Name, preferred.Description, StringComparison.OrdinalIgnoreCase));
            }

            if (selected == null && !string.IsNullOrWhiteSpace(preferred.Description))
            {
                selected = devices.FirstOrDefault(device =>
                    device.Name.Contains(preferred.Description, StringComparison.OrdinalIgnoreCase) ||
                    preferred.Description.Contains(device.Name, StringComparison.OrdinalIgnoreCase));
            }
        }

        selected ??= devices.FirstOrDefault(device => device.IsResponsive && device.DeviceType == VulkanNative.VkPhysicalDeviceTypeDiscreteGpu)
                     ?? devices.FirstOrDefault(device => device.IsResponsive)
                     ?? devices[0];

        selected.IsWindowsDefault = true;
        Logger.Info($" Windows game GPU: {selected.Name} (device {selected.Index})");
    }

    private static string ReadUtf8String(IntPtr pointer, int maxLength)
    {
        if (pointer == IntPtr.Zero || maxLength <= 0)
            return string.Empty;

        byte[] bytes = new byte[maxLength];
        Marshal.Copy(pointer, bytes, 0, maxLength);

        int length = Array.IndexOf(bytes, (byte)0);
        if (length < 0)
            length = maxLength;

        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    private static IntPtr AllocStructure<T>(T value) where T : struct
    {
        IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        Marshal.StructureToPtr(value, pointer, false);
        return pointer;
    }

    private static IntPtr AllocateStringArray(string[] strings)
    {
        IntPtr array = Marshal.AllocHGlobal(strings.Length * IntPtr.Size);
        for (int i = 0; i < strings.Length; i++)
            Marshal.WriteIntPtr(array, i * IntPtr.Size, Marshal.StringToHGlobalAnsi(strings[i]));
        return array;
    }

    private static void FreeStringArray(IntPtr array, uint count)
    {
        if (array == IntPtr.Zero)
            return;

        for (int i = 0; i < (int)count; i++)
        {
            IntPtr value = Marshal.ReadIntPtr(array, i * IntPtr.Size);
            if (value != IntPtr.Zero)
                Marshal.FreeHGlobal(value);
        }

        Marshal.FreeHGlobal(array);
    }

    private static void CopyMemory(IntPtr source, IntPtr destination, int length)
    {
        byte[] buffer = new byte[length];
        Marshal.Copy(source, buffer, 0, length);
        Marshal.Copy(buffer, 0, destination, length);
    }
}
