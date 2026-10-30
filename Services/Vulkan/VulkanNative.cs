using System.Runtime.InteropServices;

namespace GTAIVSetupUtility.Services.Vulkan;

/// <summary>
/// Minimal Vulkan interop for the headless capability probe. Only the entry points and
/// structures the probe actually needs are declared; this is not a general-purpose binding.
/// </summary>
internal static class VulkanNative
{
    public const string Library = "vulkan-1.dll";

    public const int VkSuccess = 0;
    public const int VkIncomplete = 5;
    public const int VkErrorIncompatibleDriver = -9;

    public const int VkStructureTypeApplicationInfo = 0;
    public const int VkStructureTypeInstanceCreateInfo = 1;
    public const int VkStructureTypePhysicalDeviceProperties2 = 1000059001;
    public const int VkStructureTypePhysicalDeviceFeatures2 = 1000059000;
    public const int VkStructureTypePhysicalDeviceIdProperties = 1000071004;
    public const int VkStructureTypePhysicalDeviceGraphicsPipelineLibraryPropertiesExt = 1000320001;
    public const int VkStructureTypePhysicalDeviceRobustness2FeaturesExt = 1000286000;

    public const int VkPhysicalDeviceTypeDiscreteGpu = 2;
    public const int VkPhysicalDeviceTypeIntegratedGpu = 1;

    public const int VkMaxPhysicalDeviceNameSize = 256;
    public const int VkUuidSize = 16;
    public const int VkLuidSize = 8;
    public const int VkExtensionPropertiesSize = 260; // char extensionName[256] + uint32_t specVersion

    // Offsets inside VkPhysicalDeviceProperties (stable per the Vulkan spec).
    // pipelineCacheUUID ends at 292; VkPhysicalDeviceLimits is 8-byte aligned, so it starts at 296.
    public const int PropertiesApiVersionOffset = 0;
    public const int PropertiesVendorIdOffset = 8;
    public const int PropertiesDeviceIdOffset = 12;
    public const int PropertiesDeviceTypeOffset = 16;
    public const int PropertiesDeviceNameOffset = 20;
    public const int PropertiesLimitsOffset = 296;
    public const int PropertiesMaxPushConstantsOffset = PropertiesLimitsOffset + 32;

    public const string ExtensionGetPhysicalDeviceProperties2 = "VK_KHR_get_physical_device_properties2";

    public static readonly uint VulkanApiVersion10 = MakeApiVersion(1, 0, 0);
    public static readonly uint VulkanApiVersion11 = MakeApiVersion(1, 1, 0);
    public static readonly uint VulkanApiVersion13 = MakeApiVersion(1, 3, 0);

    public static uint MakeApiVersion(uint major, uint minor, uint patch) =>
        (major << 22) | (minor << 12) | patch;

    public static (int Major, int Minor) DecodeApiVersion(uint apiVersion) =>
        ((int)(apiVersion >> 22), (int)((apiVersion >> 12) & 0x3ff));

    [StructLayout(LayoutKind.Sequential)]
    public struct VkApplicationInfo
    {
        public int SType;
        public IntPtr PNext;
        public IntPtr PApplicationName;
        public uint ApplicationVersion;
        public IntPtr PEngineName;
        public uint EngineVersion;
        public uint ApiVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkInstanceCreateInfo
    {
        public int SType;
        public IntPtr PNext;
        public uint Flags;
        public IntPtr PApplicationInfo;
        public uint EnabledLayerCount;
        public IntPtr PpEnabledLayerNames;
        public uint EnabledExtensionCount;
        public IntPtr PpEnabledExtensionNames;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkPhysicalDeviceIdProperties
    {
        public int SType;
        public IntPtr PNext;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = VkUuidSize)]
        public byte[] DeviceUUID;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = VkUuidSize)]
        public byte[] DriverUUID;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = VkLuidSize)]
        public byte[] DeviceLUID;

        public uint DeviceNodeMask;
        public uint DeviceLUIDValid;

        public static VkPhysicalDeviceIdProperties Create() => new()
        {
            SType = VkStructureTypePhysicalDeviceIdProperties,
            PNext = IntPtr.Zero,
            DeviceUUID = new byte[VkUuidSize],
            DriverUUID = new byte[VkUuidSize],
            DeviceLUID = new byte[VkLuidSize]
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkPhysicalDeviceGraphicsPipelineLibraryPropertiesExt
    {
        public int SType;
        public IntPtr PNext;
        public uint GraphicsPipelineLibraryFastLinking;
        public uint GraphicsPipelineLibraryIndependentInterpolationDecoration;

        public static VkPhysicalDeviceGraphicsPipelineLibraryPropertiesExt Create() => new()
        {
            SType = VkStructureTypePhysicalDeviceGraphicsPipelineLibraryPropertiesExt,
            PNext = IntPtr.Zero
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VkPhysicalDeviceRobustness2FeaturesExt
    {
        public int SType;
        public IntPtr PNext;
        public uint RobustBufferAccess2;
        public uint RobustImageAccess2;
        public uint NullDescriptor;

        public static VkPhysicalDeviceRobustness2FeaturesExt Create() => new()
        {
            SType = VkStructureTypePhysicalDeviceRobustness2FeaturesExt,
            PNext = IntPtr.Zero
        };
    }

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    public static extern int vkEnumerateInstanceVersion(out uint apiVersion);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    public static extern int vkCreateInstance(ref VkInstanceCreateInfo createInfo, IntPtr allocator, out IntPtr instance);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    public static extern void vkDestroyInstance(IntPtr instance, IntPtr allocator);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    public static extern int vkEnumerateInstanceExtensionProperties(IntPtr layerName, ref uint propertyCount, IntPtr properties);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    public static extern int vkEnumeratePhysicalDevices(IntPtr instance, ref uint physicalDeviceCount, IntPtr physicalDevices);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    public static extern int vkEnumerateDeviceExtensionProperties(IntPtr physicalDevice, IntPtr layerName, ref uint propertyCount, IntPtr properties);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    public static extern void vkGetPhysicalDeviceProperties(IntPtr physicalDevice, IntPtr properties);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    public static extern void vkGetPhysicalDeviceProperties2(IntPtr physicalDevice, IntPtr properties);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    public static extern void vkGetPhysicalDeviceFeatures(IntPtr physicalDevice, IntPtr features);

    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    public static extern void vkGetPhysicalDeviceFeatures2(IntPtr physicalDevice, IntPtr features);
}
