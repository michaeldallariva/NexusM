using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NexusM.Data;
using NexusM.Models;

namespace NexusM.Services;

/// <summary>
/// Manages network share credentials (AES-256-GCM encryption),
/// mount/unmount operations, and connection testing.
/// Cross-platform: Windows (net use) and Linux (mount -t cifs).
/// </summary>
public class ShareCredentialService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ShareCredentialService> _logger;
    private readonly byte[] _encryptionKey;

    private static readonly string KeyFilePath = Path.Combine(
        AppContext.BaseDirectory, "data", ".share_key");

    // Characters not allowed in share paths/usernames to prevent command injection
    private static readonly Regex DangerousChars = new(@"[;&|`$]", RegexOptions.Compiled);

    public ShareCredentialService(
        IServiceProvider serviceProvider,
        ILogger<ShareCredentialService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _encryptionKey = InitializeEncryptionKey();
    }

    // ══════════════════════════════════════════════════════════════════
    //  ENCRYPTION KEY MANAGEMENT
    // ══════════════════════════════════════════════════════════════════

    private byte[] InitializeEncryptionKey()
    {
        try
        {
            var keyDir = Path.GetDirectoryName(KeyFilePath)!;
            if (!Directory.Exists(keyDir))
                Directory.CreateDirectory(keyDir);

            if (OperatingSystem.IsWindows())
            {
                // Use DPAPI to protect the key at rest
                if (File.Exists(KeyFilePath))
                {
                    var protectedKey = File.ReadAllBytes(KeyFilePath);
                    return ProtectedData.Unprotect(protectedKey, null,
                        DataProtectionScope.LocalMachine);
                }
                else
                {
                    var key = RandomNumberGenerator.GetBytes(32);
                    var protectedKey = ProtectedData.Protect(key, null,
                        DataProtectionScope.LocalMachine);
                    File.WriteAllBytes(KeyFilePath, protectedKey);
                    _logger.LogInformation("Created new encryption key (DPAPI-protected): {Path}", KeyFilePath);
                    return key;
                }
            }
            else
            {
                // Linux/Docker: plain key file with restricted permissions
                if (File.Exists(KeyFilePath))
                {
                    return File.ReadAllBytes(KeyFilePath);
                }
                else
                {
                    var key = RandomNumberGenerator.GetBytes(32);
                    File.WriteAllBytes(KeyFilePath, key);
                    try
                    {
                        Process.Start("chmod", $"600 \"{KeyFilePath}\"")?.WaitForExit(5000);
                    }
                    catch { /* best effort on chmod */ }
                    _logger.LogInformation("Created new encryption key: {Path}", KeyFilePath);
                    return key;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize encryption key, using ephemeral key");
            return RandomNumberGenerator.GetBytes(32);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  AES-256-GCM ENCRYPT / DECRYPT
    // ══════════════════════════════════════════════════════════════════

    public string EncryptPassword(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return "";

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_encryptionKey, 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Pack as: [nonce(12)][ciphertext(N)][tag(16)]
        var result = new byte[12 + ciphertext.Length + 16];
        nonce.CopyTo(result, 0);
        ciphertext.CopyTo(result, 12);
        tag.CopyTo(result, 12 + ciphertext.Length);

        return Convert.ToBase64String(result);
    }

    public string DecryptPassword(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return "";

        var data = Convert.FromBase64String(encrypted);
        if (data.Length < 29) return ""; // minimum: 12 nonce + 1 cipher + 16 tag

        var nonce = data[..12];
        var tag = data[^16..];
        var ciphertext = data[12..^16];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_encryptionKey, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    // ══════════════════════════════════════════════════════════════════
    //  CRUD OPERATIONS
    // ══════════════════════════════════════════════════════════════════

    public async Task<List<NetworkShare>> GetAllSharesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SharesDbContext>();
        return await db.Shares.OrderBy(s => s.SharePath).ToListAsync();
    }

    public async Task<NetworkShare?> GetShareAsync(int id)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SharesDbContext>();
        return await db.Shares.FindAsync(id);
    }

    public async Task<NetworkShare> AddShareAsync(string sharePath, string username,
        string password, string domain = "", string shareType = "smb",
        string mountPoint = "", string mountOptions = "", string folderType = "")
    {
        ValidateInput(sharePath, username, domain);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SharesDbContext>();

        var share = new NetworkShare
        {
            SharePath = sharePath.TrimEnd('\\', '/'),
            FolderType = folderType,
            Username = username,
            EncryptedPassword = EncryptPassword(password),
            Domain = domain,
            ShareType = shareType,
            MountPoint = mountPoint,
            MountOptions = mountOptions,
            Enabled = true,
            DateCreated = DateTime.UtcNow
        };

        db.Shares.Add(share);
        await db.SaveChangesAsync();
        _logger.LogInformation("Added network share: {Path}", share.SharePath);
        return share;
    }

    public async Task<bool> UpdateShareAsync(int id, string? sharePath, string? username,
        string? password, string? domain, string? shareType, string? mountPoint,
        string? mountOptions, bool? enabled, string? folderType = null)
    {
        if (sharePath != null || username != null || domain != null)
            ValidateInput(sharePath ?? "", username ?? "", domain ?? "");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SharesDbContext>();
        var share = await db.Shares.FindAsync(id);
        if (share == null) return false;

        if (sharePath != null) share.SharePath = sharePath.TrimEnd('\\', '/');
        if (folderType != null) share.FolderType = folderType;
        if (username != null) share.Username = username;
        if (password != null && password.Length > 0) share.EncryptedPassword = EncryptPassword(password);
        if (domain != null) share.Domain = domain;
        if (shareType != null) share.ShareType = shareType;
        if (mountPoint != null) share.MountPoint = mountPoint;
        if (mountOptions != null) share.MountOptions = mountOptions;
        if (enabled.HasValue) share.Enabled = enabled.Value;

        await db.SaveChangesAsync();
        _logger.LogInformation("Updated network share #{Id}: {Path}", id, share.SharePath);
        return true;
    }

    public async Task<bool> DeleteShareAsync(int id)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SharesDbContext>();
        var share = await db.Shares.FindAsync(id);
        if (share == null) return false;

        // Unmount first if mounted
        if (share.IsMounted)
            await UnmountShareAsync(share);

        db.Shares.Remove(share);
        await db.SaveChangesAsync();
        _logger.LogInformation("Deleted network share: {Path}", share.SharePath);
        return true;
    }

    // ══════════════════════════════════════════════════════════════════
    //  MOUNT / UNMOUNT
    // ══════════════════════════════════════════════════════════════════

    public async Task<(bool Success, string Message)> MountShareAsync(NetworkShare share)
    {
        try
        {
            var password = DecryptPassword(share.EncryptedPassword);
            string command, arguments;

            if (OperatingSystem.IsWindows())
            {
                var userArg = string.IsNullOrEmpty(share.Domain)
                    ? share.Username
                    : $"{share.Domain}\\{share.Username}";
                var sharePath = share.SharePath.Replace('/', '\\');

                if (!string.IsNullOrEmpty(share.MountPoint))
                {
                    command = "net";
                    arguments = $"use {share.MountPoint} {sharePath} /user:\"{userArg}\" \"{password}\"";
                }
                else
                {
                    command = "net";
                    arguments = $"use {sharePath} /user:\"{userArg}\" \"{password}\"";
                }
            }
            else
            {
                // Linux: mount -t cifs //server/share /mnt/point -o username=x,password=y
                var mountPoint = share.MountPoint;
                if (string.IsNullOrEmpty(mountPoint))
                {
                    var safeName = share.SharePath.Replace("//", "").Replace("/", "_").Replace("\\", "_");
                    mountPoint = $"/mnt/nexusm/{safeName}";
                }
                Directory.CreateDirectory(mountPoint);

                var sharePath = share.SharePath.Replace('\\', '/');
                var opts = $"username={share.Username},password={password}";
                if (!string.IsNullOrEmpty(share.Domain))
                    opts += $",domain={share.Domain}";
                if (!string.IsNullOrEmpty(share.MountOptions))
                    opts += $",{share.MountOptions}";

                command = "mount";
                arguments = $"-t cifs \"{sharePath}\" \"{mountPoint}\" -o {opts}";
            }

            var result = await RunProcessAsync(command, arguments, 15000);

            // Update share state in database
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SharesDbContext>();
            var dbShare = await db.Shares.FindAsync(share.Id);
            if (dbShare != null)
            {
                dbShare.IsMounted = result.ExitCode == 0;
                dbShare.LastError = result.ExitCode == 0 ? "" : SanitizeError(result.StdErr);
                if (result.ExitCode == 0)
                    dbShare.LastMounted = DateTime.UtcNow;
                if (!string.IsNullOrEmpty(share.MountPoint) && string.IsNullOrEmpty(dbShare.MountPoint))
                    dbShare.MountPoint = share.MountPoint;
                await db.SaveChangesAsync();
            }

            if (result.ExitCode == 0)
            {
                _logger.LogInformation("Mounted share: {Path}", share.SharePath);
                return (true, "Share mounted successfully");
            }
            else
            {
                var error = SanitizeError(result.StdErr.Length > 0 ? result.StdErr : result.StdOut);
                _logger.LogWarning("Failed to mount share {Path}: {Error}", share.SharePath, error);
                return (false, error.Length > 0 ? error : "Mount failed with no error message");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mounting share {Path}", share.SharePath);
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string Message)> UnmountShareAsync(NetworkShare share)
    {
        try
        {
            string command, arguments;

            if (OperatingSystem.IsWindows())
            {
                var target = !string.IsNullOrEmpty(share.MountPoint)
                    ? share.MountPoint
                    : share.SharePath.Replace('/', '\\');
                command = "net";
                arguments = $"use {target} /delete /yes";
            }
            else
            {
                command = "umount";
                arguments = $"\"{share.MountPoint}\"";
            }

            var result = await RunProcessAsync(command, arguments, 10000);

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SharesDbContext>();
            var dbShare = await db.Shares.FindAsync(share.Id);
            if (dbShare != null)
            {
                dbShare.IsMounted = false;
                dbShare.LastError = result.ExitCode == 0 ? "" : SanitizeError(result.StdErr);
                await db.SaveChangesAsync();
            }

            return result.ExitCode == 0
                ? (true, "Share unmounted")
                : (false, SanitizeError(result.StdErr));
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  TEST CONNECTION
    // ══════════════════════════════════════════════════════════════════

    public async Task<(bool Success, string Message)> TestConnectionAsync(
        string sharePath, string username, string password, string domain = "")
    {
        ValidateInput(sharePath, username, domain);

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var userArg = string.IsNullOrEmpty(domain)
                    ? username : $"{domain}\\{username}";
                var unc = sharePath.Replace('/', '\\');
                // net use does not accept quoted UNC paths; only quote user/password
                var result = await RunProcessAsync("net",
                    $"use {unc} /user:\"{userArg}\" \"{password}\"", 15000);
                if (result.ExitCode == 0)
                {
                    // Disconnect test connection
                    await RunProcessAsync("net", $"use {unc} /delete /yes", 5000);
                    return (true, "Connection successful");
                }
                var error = SanitizeError(result.StdErr.Length > 0 ? result.StdErr : result.StdOut);
                return (false, error.Length > 0 ? error : "Connection failed");
            }
            else
            {
                // Linux: use smbclient to test
                var smbPath = sharePath.Replace('\\', '/');
                var args = $"\"{smbPath}\" -U \"{username}%{password}\"";
                if (!string.IsNullOrEmpty(domain))
                    args += $" -W \"{domain}\"";
                args += " -c exit";
                var result = await RunProcessAsync("smbclient", args, 15000);
                return result.ExitCode == 0
                    ? (true, "Connection successful")
                    : (false, SanitizeError(result.StdErr.Length > 0 ? result.StdErr : result.StdOut));
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  AUTO-MOUNT ALL ENABLED
    // ══════════════════════════════════════════════════════════════════

    public async Task MountAllEnabledAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SharesDbContext>();
            var shares = await db.Shares.Where(s => s.Enabled).ToListAsync();

            if (shares.Count == 0) return;

            _logger.LogInformation("Auto-mounting {Count} network share(s)...", shares.Count);
            foreach (var share in shares)
            {
                var (success, msg) = await MountShareAsync(share);
                if (success)
                    _logger.LogInformation("  Mounted: {Path}", share.SharePath);
                else
                    _logger.LogWarning("  Failed to mount {Path}: {Error}", share.SharePath, msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during auto-mount of network shares");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════════════════════════

    private static void ValidateInput(string sharePath, string username, string domain)
    {
        if (DangerousChars.IsMatch(sharePath))
            throw new ArgumentException("Share path contains invalid characters");
        if (DangerousChars.IsMatch(username))
            throw new ArgumentException("Username contains invalid characters");
        if (DangerousChars.IsMatch(domain))
            throw new ArgumentException("Domain contains invalid characters");
    }

    /// <summary>
    /// Remove passwords from error output before storing/returning.
    /// </summary>
    private static string SanitizeError(string error)
    {
        if (string.IsNullOrEmpty(error)) return "";
        // Truncate long errors
        return error.Length > 500 ? error[..500] + "..." : error;
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string command, string arguments, int timeoutMs = 15000)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            return (-1, "", "Operation timed out");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return (process.ExitCode, stdout.Trim(), stderr.Trim());
    }
}
