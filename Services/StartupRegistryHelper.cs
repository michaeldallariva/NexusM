using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace NexusM.Services;

/// <summary>
/// Manages the "Run on Startup" setting cross-platform.
/// Windows: HKCU registry Run key (no admin required).
/// Linux:   systemd system service (/etc/systemd/system/) when root,
///          or user service (~/.config/systemd/user/) otherwise.
/// </summary>
public static class StartupRegistryHelper
{
    private const string ServiceName = "nexusm";

    // ── Public API (cross-platform) ───────────────────────────────────

    public static void SetRunOnStartup(bool enable)
    {
        if (OperatingSystem.IsWindows())
            SetRunOnStartupWindows(enable);
        else if (OperatingSystem.IsLinux())
            SetRunOnStartupLinux(enable);
    }

    public static bool IsRunOnStartup()
    {
        if (OperatingSystem.IsWindows())
            return IsRunOnStartupWindows();
        if (OperatingSystem.IsLinux())
            return IsRunOnStartupLinux();
        return false;
    }

    // ── Windows implementation ────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private static void SetRunOnStartupWindows(bool enable)
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "NexusM";
        const string legacyName = "MusicAPP01";

        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        if (key == null) return;

        key.DeleteValue(legacyName, throwOnMissingValue: false);

        if (enable)
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
                key.SetValue(valueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsRunOnStartupWindows()
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
        return key?.GetValue("NexusM") != null;
    }

    // ── Linux systemd implementation ──────────────────────────────────

    private static bool IsRootUser =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) == "/root" ||
        string.Equals(Environment.UserName, "root", StringComparison.OrdinalIgnoreCase);

    private static (string serviceDir, bool useUserScope) GetServicePaths()
    {
        if (IsRootUser)
            return ("/etc/systemd/system", false);

        var userCfgDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "systemd", "user");
        return (userCfgDir, true);
    }

    private static void SetRunOnStartupLinux(bool enable)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;
            var workDir = Path.GetDirectoryName(exePath) ?? "/";

            var (serviceDir, userScope) = GetServicePaths();
            var servicePath = Path.Combine(serviceDir, $"{ServiceName}.service");
            var systemctlArgs = userScope ? $"--user" : "";

            if (enable)
            {
                Directory.CreateDirectory(serviceDir);

                var wantedBy = userScope ? "default.target" : "multi-user.target";
                var serviceContent =
                    $"[Unit]\n" +
                    $"Description=NexusM Media Server\n" +
                    $"After=network.target\n\n" +
                    $"[Service]\n" +
                    $"Type=simple\n" +
                    $"ExecStart={exePath}\n" +
                    $"WorkingDirectory={workDir}\n" +
                    $"Restart=on-failure\n" +
                    $"RestartSec=5\n\n" +
                    $"[Install]\n" +
                    $"WantedBy={wantedBy}\n";

                File.WriteAllText(servicePath, serviceContent);

                RunSystemctl($"{systemctlArgs} daemon-reload".Trim());
                RunSystemctl($"{systemctlArgs} enable {ServiceName}".Trim());
            }
            else
            {
                RunSystemctl($"{systemctlArgs} disable {ServiceName}".Trim());
                try { if (File.Exists(servicePath)) File.Delete(servicePath); } catch { }
                RunSystemctl($"{systemctlArgs} daemon-reload".Trim());
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[NexusM] Failed to configure Linux startup: {ex.Message}");
        }
    }

    private static bool IsRunOnStartupLinux()
    {
        try
        {
            var (_, userScope) = GetServicePaths();
            var args = userScope
                ? $"--user is-enabled {ServiceName}"
                : $"is-enabled {ServiceName}";

            using var proc = Process.Start(new ProcessStartInfo("systemctl", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            proc?.WaitForExit(5000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void RunSystemctl(string args)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("systemctl", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            proc?.WaitForExit(5000);
        }
        catch { /* systemctl not available - ignore */ }
    }
}
