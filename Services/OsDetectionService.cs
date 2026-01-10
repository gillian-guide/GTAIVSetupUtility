using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GTAIVSetupUtility.Services;

public abstract class OsDetectionService
{
    public static bool IsLinux()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return true;
        
        if (Environment.GetEnvironmentVariable("WINE_PREFIX") != null ||
            Environment.GetEnvironmentVariable("WINEPREFIX") != null)
            return true;
        
        if (Directory.Exists("/proc") || Directory.Exists("/sys"))
            return true;
        
        var path = Environment.GetEnvironmentVariable("PATH");
        return path != null && path.Contains(':') && !path.Contains(';');
    }
}