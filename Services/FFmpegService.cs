using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace NexusM.Services;

/// <summary>
/// Shared FFmpeg/FFprobe utility service.
/// Locates binaries on startup and provides methods to run ffmpeg/ffprobe commands.
/// Expected location: tools/ffmpeg/bin/ffmpeg.exe
/// </summary>
public class FFmpegService
{
    private readonly ILogger<FFmpegService> _logger;
    private readonly ConfigService _configService;
    private string? _ffmpegPath;
    private string? _ffprobePath;

    public bool IsAvailable => _ffmpegPath != null;
    public bool IsProbeAvailable => _ffprobePath != null;
    public string? FfmpegPath => _ffmpegPath;
    public string? FfprobePath => _ffprobePath;

    public FFmpegService(ConfigService configService, ILogger<FFmpegService> logger)
    {
        _configService = configService;
        _logger = logger;
        LocateBinaries();
    }

    public void LocateBinaries()
    {
        var exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var probeName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";

        var searchDirs = new List<string>();

        // 1. Configured path from [Playback] FFmpegPath
        var configPath = _configService.Config.Playback.FFmpegPath;
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            if (File.Exists(configPath))
                searchDirs.Add(Path.GetDirectoryName(configPath) ?? "");
            else if (Directory.Exists(configPath))
                searchDirs.Add(configPath);
        }

        // 2. tools/ffmpeg/bin relative to app base directory
        searchDirs.Add(Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "bin"));

        // 3. tools/ffmpeg/bin relative to working directory
        searchDirs.Add(Path.Combine(Directory.GetCurrentDirectory(), "tools", "ffmpeg", "bin"));

        // 4. Common system locations
        if (OperatingSystem.IsWindows())
        {
            searchDirs.Add(@"C:\ffmpeg\bin");
            searchDirs.Add(@"C:\Program Files\ffmpeg\bin");
            searchDirs.Add(@"C:\Program Files (x86)\ffmpeg\bin");
            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localApp))
                searchDirs.Add(Path.Combine(localApp, "ffmpeg", "bin"));
        }
        else
        {
            // Linux/macOS common locations
            searchDirs.Add("/usr/bin");
            searchDirs.Add("/usr/local/bin");
            searchDirs.Add("/opt/ffmpeg/bin");
            searchDirs.Add("/snap/bin");
        }

        foreach (var dir in searchDirs)
        {
            var ffmpegCandidate = Path.Combine(dir, exeName);
            if (File.Exists(ffmpegCandidate))
            {
                _ffmpegPath = ffmpegCandidate;
                var probeCandidate = Path.Combine(dir, probeName);
                if (File.Exists(probeCandidate))
                    _ffprobePath = probeCandidate;
                _logger.LogInformation("FFmpeg found at: {Path}", _ffmpegPath);
                if (_ffprobePath != null)
                    _logger.LogInformation("FFprobe found at: {Path}", _ffprobePath);
                return;
            }
        }

        // Try system PATH
        try
        {
            var (exitCode, stdout, _) = RunProcessSync(exeName, "-version", 5000);
            if (exitCode == 0 && !string.IsNullOrEmpty(stdout))
            {
                _ffmpegPath = exeName;
                _ffprobePath = probeName;
                _logger.LogInformation("FFmpeg found on system PATH");
                return;
            }
        }
        catch { /* not on PATH */ }

        _logger.LogWarning("FFmpeg not found. Download from Settings page.");
    }

    /// <summary>
    /// Download FFmpeg binaries if not already present.
    /// Called from Program.cs during startup.
    /// </summary>
    public async Task DownloadIfMissingAsync()
    {
        if (IsAvailable) return;

        var targetDir = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "bin");
        var exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        // Already there (maybe downloaded between constructor and this call)
        if (File.Exists(Path.Combine(targetDir, exeName)))
        {
            LocateBinaries();
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            await DownloadLinuxAsync(targetDir);
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("Auto-download is not supported on this platform. Install ffmpeg via your package manager.");
            return;
        }

        // Windows: Download from BtbN GitHub releases (GPL static build)
        const string downloadUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
        var tempZip = Path.Combine(Path.GetTempPath(), $"ffmpeg-nexusm-{Guid.NewGuid():N}.zip");

        try
        {
            _logger.LogInformation("Downloading FFmpeg from GitHub (BtbN builds)...");
            _logger.LogInformation("URL: {Url}", downloadUrl);
            _logger.LogInformation("This may take a few minutes depending on your connection...");

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(10);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NexusM/1.0");

            using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            _logger.LogInformation("Download size: {Size}", totalBytes.HasValue ? FormatBytes(totalBytes.Value) : "unknown");

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            var lastLog = DateTime.UtcNow;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;

                if ((DateTime.UtcNow - lastLog).TotalSeconds >= 5)
                {
                    var pct = totalBytes.HasValue ? (int)(totalRead * 100 / totalBytes.Value) : 0;
                    _logger.LogInformation("  Downloading... {Downloaded} / {Total} ({Pct}%)",
                        FormatBytes(totalRead), totalBytes.HasValue ? FormatBytes(totalBytes.Value) : "?", pct);
                    lastLog = DateTime.UtcNow;
                }
            }

            _logger.LogInformation("Download complete ({Size}). Extracting...", FormatBytes(totalRead));

            // Extract ffmpeg.exe and ffprobe.exe from zip
            // Archive structure: ffmpeg-master-latest-win64-gpl/bin/ffmpeg.exe
            fileStream.Close();

            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            using var archive = ZipFile.OpenRead(tempZip);
            int extracted = 0;
            foreach (var entry in archive.Entries)
            {
                var name = entry.Name.ToLowerInvariant();
                if (name is "ffmpeg.exe" or "ffprobe.exe")
                {
                    var destPath = Path.Combine(targetDir, entry.Name);
                    entry.ExtractToFile(destPath, overwrite: true);
                    _logger.LogInformation("  Extracted: {File} ({Size})", entry.Name, FormatBytes(entry.Length));
                    extracted++;
                }
                if (extracted >= 2) break;
            }

            if (extracted > 0)
            {
                _logger.LogInformation("FFmpeg installed successfully to: {Path}", targetDir);
                LocateBinaries();
            }
            else
            {
                _logger.LogError("Could not find ffmpeg.exe in downloaded archive");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download FFmpeg. Place ffmpeg.exe and ffprobe.exe in: {Path}", targetDir);
        }
        finally
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); }
            catch { /* ignore cleanup errors */ }
        }
    }

    /// <summary>
    /// Download FFmpeg static build for Linux (BtbN GPL tar.xz).
    /// Uses the system 'tar' command to extract — always available on Linux.
    /// </summary>
    private async Task DownloadLinuxAsync(string targetDir)
    {
        const string downloadUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linux64-gpl.tar.xz";
        var tempFile = Path.Combine(Path.GetTempPath(), $"ffmpeg-nexusm-{Guid.NewGuid():N}.tar.xz");
        var tempExtractDir = Path.Combine(Path.GetTempPath(), $"ffmpeg-nexusm-{Guid.NewGuid():N}");

        try
        {
            _logger.LogInformation("Downloading FFmpeg for Linux from GitHub (BtbN GPL static build)...");
            _logger.LogInformation("URL: {Url}", downloadUrl);

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(15);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NexusM/1.0");

            using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            _logger.LogInformation("Download size: {Size}", totalBytes.HasValue ? FormatBytes(totalBytes.Value) : "unknown");

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            var lastLog = DateTime.UtcNow;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;

                if ((DateTime.UtcNow - lastLog).TotalSeconds >= 5)
                {
                    var pct = totalBytes.HasValue ? (int)(totalRead * 100 / totalBytes.Value) : 0;
                    _logger.LogInformation("  Downloading... {Downloaded} / {Total} ({Pct}%)",
                        FormatBytes(totalRead), totalBytes.HasValue ? FormatBytes(totalBytes.Value) : "?", pct);
                    lastLog = DateTime.UtcNow;
                }
            }
            fileStream.Close();

            _logger.LogInformation("Download complete ({Size}). Extracting with tar...", FormatBytes(totalRead));

            // Extract using system tar (handles .tar.xz natively on all Linux)
            Directory.CreateDirectory(tempExtractDir);
            var (tarExit, _, tarErr) = RunProcessSync("tar", $"-xJf \"{tempFile}\" -C \"{tempExtractDir}\"", 120_000);
            if (tarExit != 0)
            {
                _logger.LogError("tar extraction failed (exit {Code}): {Err}", tarExit, tarErr);
                return;
            }

            // Find the bin/ directory inside the extracted folder
            // Archive structure: ffmpeg-master-latest-linux64-gpl/bin/ffmpeg
            var extractedBinDir = Directory
                .GetDirectories(tempExtractDir, "bin", SearchOption.AllDirectories)
                .FirstOrDefault(d => File.Exists(Path.Combine(d, "ffmpeg")));

            if (extractedBinDir == null)
            {
                _logger.LogError("Could not find ffmpeg binary in extracted archive");
                return;
            }

            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            int copied = 0;
            foreach (var binName in new[] { "ffmpeg", "ffprobe" })
            {
                var src = Path.Combine(extractedBinDir, binName);
                if (!File.Exists(src)) continue;

                var dest = Path.Combine(targetDir, binName);
                File.Copy(src, dest, overwrite: true);
                RunProcessSync("chmod", $"+x \"{dest}\"", 5000);
                _logger.LogInformation("  Installed: {File}", binName);
                copied++;
            }

            if (copied > 0)
            {
                _logger.LogInformation("FFmpeg installed successfully to: {Path}", targetDir);
                LocateBinaries();
            }
            else
            {
                _logger.LogError("No FFmpeg binaries found in extracted archive");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download FFmpeg for Linux. Install manually: apt install ffmpeg");
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, recursive: true); } catch { }
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        int i = 0;
        while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
        return $"{size:F1} {units[i]}";
    }

    /// <summary>
    /// Run ffprobe and return parsed JSON output for a video file.
    /// </summary>
    public async Task<JsonDocument?> ProbeAsync(string filePath)
    {
        if (_ffprobePath == null) return null;
        var args = $"-v quiet -print_format json -show_format -show_streams \"{filePath}\"";
        var (exitCode, stdout, _) = await RunProcessAsync(_ffprobePath, args, 30000);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout)) return null;
        try { return JsonDocument.Parse(stdout); }
        catch { return null; }
    }

    /// <summary>
    /// Generate a thumbnail at a specified seek position.
    /// </summary>
    public async Task<bool> GenerateThumbnailAsync(string inputPath, string outputPath, double seekSeconds)
    {
        if (_ffmpegPath == null) return false;
        var seek = TimeSpan.FromSeconds(seekSeconds);
        var seekStr = $"{(int)seek.TotalHours:D2}:{seek.Minutes:D2}:{seek.Seconds:D2}";
        var args = $"-ss {seekStr} -i \"{inputPath}\" -vframes 1 -vf \"scale=640:-1\" -q:v 3 \"{outputPath}\" -y";
        var (exitCode, _, _) = await RunProcessAsync(_ffmpegPath, args, 30000);
        return exitCode == 0 && File.Exists(outputPath);
    }

    /// <summary>
    /// Remux a video with faststart for browser streaming.
    /// </summary>
    public async Task<bool> RemuxFaststartAsync(string inputPath, string outputPath, int audioTrackIndex = 0)
    {
        if (_ffmpegPath == null) return false;
        var args = $"-hide_banner -loglevel error -i \"{inputPath}\" -map 0:v:0 -map 0:a:{audioTrackIndex} -c copy -movflags +faststart -f mp4 -y \"{outputPath}\"";
        var (exitCode, _, _) = await RunProcessAsync(_ffmpegPath, args, 600000); // 10 min timeout
        return exitCode == 0 && File.Exists(outputPath);
    }

    /// <summary>
    /// Remux with stereo downmix for surround audio.
    /// </summary>
    public async Task<bool> RemuxStereoDownmixAsync(string inputPath, string outputPath, int audioTrackIndex = 0)
    {
        if (_ffmpegPath == null) return false;
        var args = $"-hide_banner -loglevel error -i \"{inputPath}\" -map 0:v:0 -map 0:a:{audioTrackIndex} -c:v copy -c:a aac -ac 2 -b:a 128k -movflags +faststart -f mp4 -y \"{outputPath}\"";
        var (exitCode, _, _) = await RunProcessAsync(_ffmpegPath, args, 600000);
        return exitCode == 0 && File.Exists(outputPath);
    }

    private async Task<(int exitCode, string stdout, string stderr)> RunProcessAsync(
        string fileName, string arguments, int timeoutMs)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        // Read stdout and stderr concurrently to avoid deadlocks
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var completed = await Task.Run(() => process.WaitForExit(timeoutMs));
        if (!completed)
        {
            try { process.Kill(true); } catch { }
            return (-1, "", "Process timed out");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return (process.ExitCode, stdout, stderr);
    }

    private (int exitCode, string stdout, string stderr) RunProcessSync(
        string fileName, string arguments, int timeoutMs)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(timeoutMs);
        return (process.ExitCode, stdout, stderr);
    }
}
