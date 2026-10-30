using System.Runtime.InteropServices;
using Microsoft.Win32;
using NLog;

namespace GTAIVSetupUtility.Services.Vulkan;

/// <summary>
/// A DXGI adapter description used to identify the GPU Windows would give GTA IV.
/// </summary>
public sealed record WindowsAdapterInfo(
    string Description,
    uint VendorId,
    uint DeviceId,
    long Luid,
    bool IsSoftware);

/// <summary>
/// Reads the Windows per-application GPU preference (Graphics settings) and asks DXGI
/// which adapter Windows considers the game adapter. Vulkan has no API for this, so the
/// probe matches the DXGI LUID against VkPhysicalDeviceIDProperties.deviceLUID.
/// </summary>
public static class WindowsGpuPreference
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private const int DxgiGpuPreferenceUnspecified = 0;
    private const int DxgiGpuPreferenceMinimumPower = 1;
    private const int DxgiGpuPreferenceHighPerformance = 2;
    private const uint DxgiAdapterFlagSoftware = 2;

    private static readonly Guid IidIdxgiFactory1 = new("770AAE78-F26F-4DBA-A829-253C83D1B387");
    private static readonly Guid IidIdxgiAdapter1 = new("29038F61-3839-4626-91FD-086879011A05");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;

        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    [DllImport("dxgi.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapterByGpuPreferenceFn(
        IntPtr self,
        uint adapter,
        int gpuPreference,
        ref Guid riid,
        out IntPtr adapterOut);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Fn(IntPtr self, uint adapter, out IntPtr adapterOut);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Fn(IntPtr self, out DxgiAdapterDesc1 description);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseFn(IntPtr self);

    /// <summary>
    /// Returns the adapter Windows would use for <paramref name="gameExePath"/>, honoring the
    /// per-app preference in HKCU\Software\Microsoft\DirectX\UserGpuPreferences when present.
    /// </summary>
    public static WindowsAdapterInfo? GetGamePreferredAdapter(string? gameExePath)
    {
        IntPtr factory = IntPtr.Zero;
        try
        {
            Guid factoryIid = IidIdxgiFactory1;
            int hr = CreateDXGIFactory1(ref factoryIid, out factory);
            if (hr < 0 || factory == IntPtr.Zero)
            {
                Logger.Debug($" DXGI factory creation failed: 0x{hr:X8}");
                return null;
            }

            int preference = ReadGameGpuPreference(gameExePath);
            Logger.Debug($" Windows GPU preference for {gameExePath ?? "the game"}: {DescribePreference(preference)}");

            var preferred = TryEnumAdapterByGpuPreference(factory, preference);
            if (preferred != null)
                return preferred;

            Logger.Debug(" Falling back to the first hardware adapter in DXGI order.");
            return EnumerateFirstHardwareAdapter(factory);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, " DXGI GPU preference lookup failed.");
            return null;
        }
        finally
        {
            Release(factory);
        }
    }

    private static WindowsAdapterInfo? TryEnumAdapterByGpuPreference(IntPtr factory, int preference)
    {
        try
        {
            var enumFn = GetDelegate<EnumAdapterByGpuPreferenceFn>(factory, 29);
            if (enumFn == null)
                return null;

            Guid adapterIid = IidIdxgiAdapter1;
            int hr = enumFn(factory, 0, preference, ref adapterIid, out var adapter);
            if (hr < 0 || adapter == IntPtr.Zero)
            {
                Logger.Debug($" EnumAdapterByGpuPreference failed: 0x{hr:X8}");
                return null;
            }

            try
            {
                return ReadAdapter(adapter);
            }
            finally
            {
                Release(adapter);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, " EnumAdapterByGpuPreference is unavailable.");
            return null;
        }
    }

    private static WindowsAdapterInfo? EnumerateFirstHardwareAdapter(IntPtr factory)
    {
        try
        {
            var enumFn = GetDelegate<EnumAdapters1Fn>(factory, 12);
            if (enumFn == null)
                return null;

            for (uint i = 0; i < 16; i++)
            {
                int hr = enumFn(factory, i, out var adapter);
                if (hr < 0 || adapter == IntPtr.Zero)
                    break;

                try
                {
                    var info = ReadAdapter(adapter);
                    if (info is { IsSoftware: false })
                        return info;
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, " DXGI adapter enumeration failed.");
        }

        return null;
    }

    private static WindowsAdapterInfo? ReadAdapter(IntPtr adapter)
    {
        var getDesc = GetDelegate<GetDesc1Fn>(adapter, 10);
        if (getDesc == null)
            return null;

        int hr = getDesc(adapter, out var desc);
        if (hr < 0)
            return null;

        return new WindowsAdapterInfo(
            desc.Description?.TrimEnd('\0') ?? string.Empty,
            desc.VendorId,
            desc.DeviceId,
            desc.AdapterLuid,
            (desc.Flags & DxgiAdapterFlagSoftware) != 0);
    }

    private static int ReadGameGpuPreference(string? gameExePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\DirectX\UserGpuPreferences");
            if (key == null)
                return DxgiGpuPreferenceHighPerformance;

            string? value = null;
            if (!string.IsNullOrWhiteSpace(gameExePath))
            {
                value = key.GetValue(gameExePath) as string;
                if (value == null)
                {
                    try
                    {
                        value = key.GetValue(Path.GetFullPath(gameExePath)) as string;
                    }
                    catch (Exception)
                    {
                        // A path the registry cannot normalize is simply not configured.
                    }
                }
            }

            // At startup the game folder is not selected yet. Windows still has a per-app entry
            // for GTAIV.exe; use the first one instead of falling back to the global default.
            value ??= FindGamePreferenceValue(key, "GTAIV.exe")
                      ?? FindGamePreferenceValue(key, "PlayGTAIV.exe")
                      ?? FindGamePreferenceValue(key, "LaunchGTAIV.exe");

            if (string.IsNullOrWhiteSpace(value))
                return DxgiGpuPreferenceHighPerformance;

            const string marker = "GpuPreference=";
            int index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return DxgiGpuPreferenceHighPerformance;

            string number = value[(index + marker.Length)..].Trim();
            int end = number.IndexOf(';');
            if (end >= 0)
                number = number[..end];

            return int.TryParse(number, out int preference) &&
                   preference is >= DxgiGpuPreferenceUnspecified and <= DxgiGpuPreferenceHighPerformance
                ? preference
                : DxgiGpuPreferenceHighPerformance;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, " Reading the Windows per-app GPU preference failed.");
            return DxgiGpuPreferenceHighPerformance;
        }
    }

    private static string? FindGamePreferenceValue(RegistryKey key, string executableName)
    {
        foreach (string name in key.GetValueNames())
        {
            if (name.EndsWith(executableName, StringComparison.OrdinalIgnoreCase))
                return key.GetValue(name) as string;
        }

        return null;
    }

    private static string DescribePreference(int preference) => preference switch
    {
        DxgiGpuPreferenceMinimumPower => "power saving",
        DxgiGpuPreferenceHighPerformance => "high performance",
        _ => "let Windows decide"
    };

    private static T? GetDelegate<T>(IntPtr comObject, int vtableIndex) where T : Delegate
    {
        if (comObject == IntPtr.Zero)
            return null;

        IntPtr vtable = Marshal.ReadIntPtr(comObject);
        if (vtable == IntPtr.Zero)
            return null;

        IntPtr function = Marshal.ReadIntPtr(vtable, vtableIndex * IntPtr.Size);
        return function == IntPtr.Zero
            ? null
            : Marshal.GetDelegateForFunctionPointer<T>(function);
    }

    private static void Release(IntPtr comObject)
    {
        if (comObject == IntPtr.Zero)
            return;

        try
        {
            var release = GetDelegate<ReleaseFn>(comObject, 2);
            release?.Invoke(comObject);
        }
        catch (Exception)
        {
            // Releasing a dead COM object is not actionable.
        }
    }
}
