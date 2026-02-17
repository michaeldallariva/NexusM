using System.Runtime.Versioning;
using Microsoft.Win32;

namespace NexusM.Services;

/// <summary>
/// Manages the Windows "Run on Startup" registry entry (HKCU, no admin rights needed).
/// </summary>
[SupportedOSPlatform("windows")]
public static class StartupRegistryHelper
{
    private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NexusM";

    public static void SetRunOnStartup(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
        if (key == null) return;

        if (enable)
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
                key.SetValue(ValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    public static bool IsRunOnStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: false);
        return key?.GetValue(ValueName) != null;
    }
}
