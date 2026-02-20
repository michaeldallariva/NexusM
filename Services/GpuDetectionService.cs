using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;

namespace NexusM.Services;

/// <summary>
/// Detects GPU hardware and tests hardware encoder availability for video transcoding.
/// Supports NVIDIA NVENC, Intel QuickSync (QSV), and AMD AMF encoders.
/// Ported from NexusM PowerShell v11.70 (Get-SystemGPUInfo, Test-HardwareEncoder,
/// Get-AutoDetectedEncoder, Initialize-FFmpegTranscoding).
/// </summary>
public class GpuDetectionService
{
    private readonly ILogger<GpuDetectionService> _logger;
    private readonly FFmpegService _ffmpegService;
    private readonly ConfigService _configService;

    // ── Cached results ────────────────────────────────────────────────
    private GpuDetectionResult? _gpuInfo;
    private FFmpegCapabilities? _capabilities;

    public GpuDetectionResult? GpuInfo => _gpuInfo;
    public FFmpegCapabilities? Capabilities => _capabilities;
    public bool IsInitialised => _capabilities != null;

    public GpuDetectionService(
        FFmpegService ffmpegService,
        ConfigService configService,
        ILogger<GpuDetectionService> logger)
    {
        _ffmpegService = ffmpegService;
        _configService = configService;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Initialise GPU detection and encoder testing at startup.
    /// Mirrors Initialize-FFmpegTranscoding from the PowerShell version.
    /// </summary>
    public async Task InitialiseAsync()
    {
        var config = _configService.Config.Transcoding;

        if (!_ffmpegService.IsAvailable)
        {
            _logger.LogWarning("FFmpeg not available - GPU detection skipped. Transcoding will not work.");
            _capabilities = new FFmpegCapabilities
            {
                ActiveEncoder = "software",
                HwEncoders = HwEncoderFlags.None
            };
            return;
        }

        var ffmpegPath = _ffmpegService.FfmpegPath!;
        var preferred = config.PreferredEncoder.Trim().ToLowerInvariant();

        // ── Mode 1: Software only ─────────────────────────────────────
        if (preferred == "software")
        {
            _logger.LogInformation("Transcoding encoder set to software (CPU). GPU detection skipped.");
            _capabilities = new FFmpegCapabilities
            {
                ActiveEncoder = "software",
                HwEncoders = HwEncoderFlags.None
            };
            return;
        }

        // ── Detect GPUs via WMI ───────────────────────────────────────
        _gpuInfo = DetectGPUs();
        LogGpuDetectionResults(_gpuInfo);

        // ── Mode 2: Auto-detect best encoder ──────────────────────────
        if (preferred == "auto")
        {
            var (encoder, flags) = await AutoDetectBestEncoderAsync(ffmpegPath, _gpuInfo);
            _capabilities = new FFmpegCapabilities
            {
                ActiveEncoder = encoder,
                HwEncoders = flags
            };

            if (encoder == "software")
                _logger.LogInformation("Auto-detection result: no working hardware encoder found. Using software (CPU).");
            else
                _logger.LogInformation("Auto-detection result: using {Encoder} for hardware transcoding.", encoder.ToUpperInvariant());

            return;
        }

        // ── Mode 3: User-specified encoder ────────────────────────────
        if (preferred is "nvenc" or "qsv" or "amf" or "vaapi")
        {
            var works = await TestHardwareEncoderAsync(preferred, ffmpegPath);
            if (works)
            {
                _logger.LogInformation("Specified encoder {Encoder} tested OK.", preferred.ToUpperInvariant());
                _capabilities = new FFmpegCapabilities
                {
                    ActiveEncoder = preferred,
                    HwEncoders = EncoderToFlag(preferred)
                };
            }
            else
            {
                _logger.LogWarning("Specified encoder {Encoder} failed testing. Falling back to software.", preferred.ToUpperInvariant());
                _capabilities = new FFmpegCapabilities
                {
                    ActiveEncoder = "software",
                    HwEncoders = HwEncoderFlags.None
                };
            }
            return;
        }

        // Unknown value - treat as software
        _logger.LogWarning("Unknown PreferredEncoder value '{Value}'. Defaulting to software.", preferred);
        _capabilities = new FFmpegCapabilities
        {
            ActiveEncoder = "software",
            HwEncoders = HwEncoderFlags.None
        };
    }

    /// <summary>
    /// Returns the optimal FFmpeg encoder settings for transcoding.
    /// Mirrors Get-OptimalFFmpegEncoder from the PowerShell version.
    /// </summary>
    public EncoderSettings GetOptimalEncoder()
    {
        var active = _capabilities?.ActiveEncoder ?? "software";
        return active switch
        {
            "nvenc" => new EncoderSettings
            {
                Name = "NVIDIA NVENC",
                Type = "Hardware (NVIDIA GPU)",
                Encoder = "h264_nvenc",
                HwAccel = "cuda",
                Preset = "p4",
                UsesCRF = false
            },
            "qsv" => new EncoderSettings
            {
                Name = "Intel QuickSync",
                Type = "Hardware (Intel GPU)",
                Encoder = "h264_qsv",
                HwAccel = "qsv",
                Preset = "medium",
                UsesCRF = false
            },
            "amf" when OperatingSystem.IsWindows() => new EncoderSettings
            {
                Name = "AMD AMF",
                Type = "Hardware (AMD GPU)",
                Encoder = "h264_amf",
                HwAccel = "d3d11va",
                Preset = "balanced",
                UsesCRF = false
            },
            // Linux AMD uses VAAPI; also the explicit "vaapi" setting
            "vaapi" or "amf" => new EncoderSettings
            {
                Name = "VAAPI",
                Type = "Hardware (GPU via VAAPI)",
                Encoder = "h264_vaapi",
                HwAccel = "vaapi",
                Preset = "medium",
                UsesCRF = false
            },
            _ => new EncoderSettings
            {
                Name = "libx264",
                Type = "Software (CPU)",
                Encoder = "libx264",
                HwAccel = null,
                Preset = _configService.Config.Transcoding.VideoPreset,
                UsesCRF = true
            }
        };
    }

    // ══════════════════════════════════════════════════════════════════
    //  GPU DETECTION (WMI)
    //  Mirrors: Get-SystemGPUInfo
    // ══════════════════════════════════════════════════════════════════

    private GpuDetectionResult DetectGPUs()
    {
        var result = new GpuDetectionResult();

        if (!OperatingSystem.IsWindows())
        {
            _logger.LogInformation("Non-Windows platform - WMI GPU detection not available. Will test encoders directly.");
            return result;
        }

        try
        {
            _logger.LogInformation("Detecting GPUs via WMI...");

            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "Unknown GPU";
                var status = obj["Status"]?.ToString() ?? "Unknown";
                var driverVersion = obj["DriverVersion"]?.ToString() ?? "";
                var vramBytes = Convert.ToUInt64(obj["AdapterRAM"] ?? 0UL);
                var vramGb = Math.Round(vramBytes / (1024.0 * 1024.0 * 1024.0), 1);

                var vendor = ClassifyVendor(name);
                var encoderType = DetermineEncoderCapability(name, vendor);

                var gpu = new GpuEntry
                {
                    Name = name,
                    Status = status,
                    DriverVersion = driverVersion,
                    VramGB = vramGb,
                    Vendor = vendor,
                    EncoderType = encoderType
                };

                result.DetectedGPUs.Add(gpu);

                // Track per-vendor best GPU
                switch (vendor)
                {
                    case GpuVendor.NVIDIA:
                        result.Nvidia ??= new VendorInfo { Name = name };
                        if (encoderType == "nvenc") result.Nvidia.SupportsHwEncoder = true;
                        break;
                    case GpuVendor.Intel:
                        result.Intel ??= new VendorInfo { Name = name };
                        if (encoderType == "qsv") result.Intel.SupportsHwEncoder = true;
                        break;
                    case GpuVendor.AMD:
                        result.Amd ??= new VendorInfo { Name = name };
                        if (encoderType is "amf" or "vaapi") result.Amd.SupportsHwEncoder = true;
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI GPU detection failed. Will attempt encoder testing without GPU info.");
        }

        // Set recommended encoder priority: NVENC > QSV > AMF > software
        if (result.Nvidia is { SupportsHwEncoder: true })
            result.RecommendedEncoder = "nvenc";
        else if (result.Intel is { SupportsHwEncoder: true })
            result.RecommendedEncoder = "qsv";
        else if (result.Amd is { SupportsHwEncoder: true })
            result.RecommendedEncoder = OperatingSystem.IsWindows() ? "amf" : "vaapi";
        else
            result.RecommendedEncoder = "software";

        return result;
    }

    private static GpuVendor ClassifyVendor(string gpuName)
    {
        if (Regex.IsMatch(gpuName, @"NVIDIA|GeForce|Quadro|Tesla|RTX|GTX", RegexOptions.IgnoreCase))
            return GpuVendor.NVIDIA;
        if (Regex.IsMatch(gpuName, @"Intel|Arc\b|Iris|UHD|HD Graphics", RegexOptions.IgnoreCase))
            return GpuVendor.Intel;
        if (Regex.IsMatch(gpuName, @"AMD|Radeon", RegexOptions.IgnoreCase))
            return GpuVendor.AMD;
        return GpuVendor.Unknown;
    }

    private static string DetermineEncoderCapability(string gpuName, GpuVendor vendor)
    {
        return vendor switch
        {
            // NVIDIA: GTX 600+ (Kepler+), all RTX, Quadro, Tesla
            GpuVendor.NVIDIA when Regex.IsMatch(gpuName,
                @"RTX|GTX\s*[6-9]\d{2}|GTX\s*1\d{3}|Quadro|Tesla",
                RegexOptions.IgnoreCase) => "nvenc",

            // Intel: Arc GPUs, UHD/Iris/HD Graphics 5xx+ (Skylake 6th gen+)
            GpuVendor.Intel when Regex.IsMatch(gpuName,
                @"Arc\s*A?\d{3}|Arc\b|UHD|Iris|HD\s*Graphics\s*[5-9]\d{2}",
                RegexOptions.IgnoreCase) => "qsv",

            // AMD: RX 400+ (Polaris+), Vega, RDNA, Ryzen APU iGPUs
            // On Linux: AMD uses VAAPI; on Windows: AMF
            GpuVendor.AMD when Regex.IsMatch(gpuName,
                @"Radeon|RX|Vega|Graphics",
                RegexOptions.IgnoreCase) => OperatingSystem.IsWindows() ? "amf" : "vaapi",

            _ => "none"
        };
    }

    private void LogGpuDetectionResults(GpuDetectionResult info)
    {
        if (info.DetectedGPUs.Count == 0)
        {
            _logger.LogInformation("No GPUs detected via WMI.");
            return;
        }

        _logger.LogInformation("Detected {Count} GPU(s):", info.DetectedGPUs.Count);
        foreach (var gpu in info.DetectedGPUs)
        {
            _logger.LogInformation("  {Name} | {Vendor} | VRAM: {VRAM}GB | Driver: {Driver} | Encoder: {Encoder}",
                gpu.Name, gpu.Vendor, gpu.VramGB, gpu.DriverVersion, gpu.EncoderType.ToUpperInvariant());
        }
        _logger.LogInformation("Recommended encoder: {Encoder}", info.RecommendedEncoder.ToUpperInvariant());
    }

    // ══════════════════════════════════════════════════════════════════
    //  HARDWARE ENCODER TESTING
    //  Mirrors: Test-HardwareEncoder
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tests if a hardware encoder actually works by running a short test encode.
    /// Uses a 1-second synthetic black video at 256x256.
    /// </summary>
    private async Task<bool> TestHardwareEncoderAsync(string encoderType, string ffmpegPath)
    {
        var encoderName = encoderType switch
        {
            "nvenc" => "h264_nvenc",
            "qsv" => "h264_qsv",
            "amf" when OperatingSystem.IsWindows() => "h264_amf",
            "vaapi" or "amf" => "h264_vaapi",   // Linux AMD falls back to VAAPI
            _ => throw new ArgumentException($"Unknown encoder type: {encoderType}")
        };

        _logger.LogInformation("Testing {Encoder} encoder ({CodecName})...", encoderType.ToUpperInvariant(), encoderName);

        // Step 1: Check if encoder exists in this FFmpeg build
        if (!await IsEncoderInBuildAsync(ffmpegPath, encoderName))
        {
            _logger.LogWarning("  Encoder {Encoder} not found in FFmpeg build.", encoderName);
            return false;
        }

        // Step 2: Run actual test encode(s)
        var testArgs = GetTestArguments(encoderType, encoderName);

        foreach (var (label, args) in testArgs)
        {
            _logger.LogDebug("  Test [{Label}]: ffmpeg {Args}", label, args);

            var (exitCode, _, stderr) = await RunFFmpegAsync(ffmpegPath, args, timeoutMs: 10_000);

            if (exitCode == 0)
            {
                _logger.LogInformation("  {Encoder} test PASSED ({Label}).", encoderType.ToUpperInvariant(), label);
                return true;
            }

            // Classify the failure for diagnostics
            ClassifyAndLogFailure(encoderType, label, stderr);
        }

        _logger.LogWarning("  {Encoder} - all tests failed.", encoderType.ToUpperInvariant());
        return false;
    }

    /// <summary>
    /// Check if a specific encoder codec is available in the FFmpeg build.
    /// </summary>
    private async Task<bool> IsEncoderInBuildAsync(string ffmpegPath, string encoderName)
    {
        var (exitCode, stdout, _) = await RunFFmpegAsync(ffmpegPath, "-hide_banner -encoders", timeoutMs: 10_000);
        if (exitCode != 0) return false;
        return stdout.Contains(encoderName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the test encode arguments for each encoder type.
    /// Uses a synthetic 1-second 256x256 black video as input.
    /// </summary>
    private static List<(string Label, string Args)> GetTestArguments(string encoderType, string encoderName)
    {
        // NUL is Windows null device; /dev/null for Linux
        var nullDev = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

        return encoderType switch
        {
            "nvenc" =>
            [
                ("default",
                    $"-hide_banner -y -f lavfi -i color=black:s=256x256:d=1 -c:v {encoderName} -f null {nullDev}"),
                ("preset-p4",
                    $"-hide_banner -y -f lavfi -i color=black:s=256x256:d=1 -c:v {encoderName} -preset p4 -f null {nullDev}")
            ],
            "qsv" =>
            [
                ("default",
                    $"-hide_banner -y -f lavfi -i color=black:s=256x256:d=1 -c:v {encoderName} -f null {nullDev}")
            ],
            // AMD AMF requires D3D11VA device on Windows
            "amf" when OperatingSystem.IsWindows() =>
            [
                ("d3d11va",
                    $"-hide_banner -y -init_hw_device d3d11va=hw -filter_hw_device hw -f lavfi -i color=black:s=256x256:d=1 -c:v {encoderName} -f null {nullDev}")
            ],
            // Linux AMD/Intel VAAPI: try default device first, then explicit renderD128
            "vaapi" or "amf" =>
            [
                ("vaapi-default",
                    $"-hide_banner -y -f lavfi -i color=black:s=256x256:d=1 -vf format=nv12,hwupload -c:v {encoderName} -f null {nullDev}"),
                ("vaapi-renderD128",
                    $"-hide_banner -y -vaapi_device /dev/dri/renderD128 -f lavfi -i color=black:s=256x256:d=1 -vf format=nv12,hwupload -c:v {encoderName} -f null {nullDev}")
            ],
            _ => []
        };
    }

    /// <summary>
    /// Classify encoder test failure and log diagnostic information.
    /// </summary>
    private void ClassifyAndLogFailure(string encoderType, string testLabel, string stderr)
    {
        var msg = stderr ?? "";

        // NVIDIA-specific failures
        if (Regex.IsMatch(msg, @"Cannot load nvcuda|CUDA_ERROR|cuda driver|failed to load CUDA", RegexOptions.IgnoreCase))
        {
            _logger.LogWarning("  {Encoder} [{Label}] FAILED: CUDA driver issue. Update your NVIDIA drivers.", encoderType.ToUpperInvariant(), testLabel);
            return;
        }
        if (Regex.IsMatch(msg, @"No capable devices found|No NVENC capable|OpenEncodeSessionEx failed", RegexOptions.IgnoreCase))
        {
            _logger.LogWarning("  {Encoder} [{Label}] FAILED: No NVENC-capable GPU detected by driver.", encoderType.ToUpperInvariant(), testLabel);
            return;
        }
        if (Regex.IsMatch(msg, @"Driver does not support|out of memory|resources|nvenc API version", RegexOptions.IgnoreCase))
        {
            _logger.LogWarning("  {Encoder} [{Label}] FAILED: Driver/resource issue.", encoderType.ToUpperInvariant(), testLabel);
            return;
        }

        // Intel QSV failures
        if (Regex.IsMatch(msg, @"MFXInit|mfx|libmfx|oneVPL|vpl|QSV|Intel Media SDK|session not initialized", RegexOptions.IgnoreCase))
        {
            _logger.LogWarning("  {Encoder} [{Label}] FAILED: Intel QSV initialisation error. Check Intel GPU drivers.", encoderType.ToUpperInvariant(), testLabel);
            return;
        }

        // FFmpeg build issues
        if (Regex.IsMatch(msg, @"Unknown option|Unrecognized option|Invalid option|encoder not found", RegexOptions.IgnoreCase))
        {
            _logger.LogWarning("  {Encoder} [{Label}] FAILED: FFmpeg build does not support this encoder.", encoderType.ToUpperInvariant(), testLabel);
            return;
        }

        // Generic failure
        _logger.LogWarning("  {Encoder} [{Label}] FAILED with unclassified error.", encoderType.ToUpperInvariant(), testLabel);
        if (msg.Length > 0)
        {
            // Log first 500 chars of stderr for diagnostics
            var truncated = msg.Length > 500 ? msg[..500] + "..." : msg;
            _logger.LogDebug("  stderr: {Stderr}", truncated);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  AUTO-DETECTION
    //  Mirrors: Get-AutoDetectedEncoder
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Auto-detects the best working hardware encoder.
    /// Priority: NVENC > QSV > AMF > software.
    /// Only tests encoders that the GPU detection says should work AND
    /// that exist in the FFmpeg build.
    /// </summary>
    private async Task<(string Encoder, HwEncoderFlags Flags)> AutoDetectBestEncoderAsync(
        string ffmpegPath, GpuDetectionResult gpuInfo)
    {
        _logger.LogInformation("Auto-detecting best hardware encoder...");

        var flags = HwEncoderFlags.None;

        // Build candidate list based on detected GPU capabilities
        var candidates = new List<string>();

        if (gpuInfo.Nvidia is { SupportsHwEncoder: true })
            candidates.Add("nvenc");
        if (gpuInfo.Intel is { SupportsHwEncoder: true })
            candidates.Add("qsv");
        if (gpuInfo.Amd is { SupportsHwEncoder: true })
            candidates.Add(OperatingSystem.IsWindows() ? "amf" : "vaapi");

        // On non-Windows platforms where WMI isn't available, try all encoders
        if (!OperatingSystem.IsWindows() && candidates.Count == 0)
        {
            _logger.LogInformation("No WMI data available - testing all encoder types.");
            // Use vaapi instead of amf on Linux; amf (d3d11va) is Windows-only
            candidates.AddRange(OperatingSystem.IsLinux()
                ? new[] { "nvenc", "qsv", "vaapi" }
                : new[] { "nvenc", "qsv", "amf" });
        }

        if (candidates.Count == 0)
        {
            _logger.LogInformation("No GPU hardware encoders detected. Using software encoding.");
            return ("software", HwEncoderFlags.None);
        }

        _logger.LogInformation("Testing {Count} candidate encoder(s): {Candidates}",
            candidates.Count, string.Join(", ", candidates.Select(c => c.ToUpperInvariant())));

        // Test each candidate in priority order
        foreach (var candidate in candidates)
        {
            var works = await TestHardwareEncoderAsync(candidate, ffmpegPath);
            if (works)
            {
                flags |= EncoderToFlag(candidate);
                return (candidate, flags);
            }
        }

        return ("software", HwEncoderFlags.None);
    }

    // ══════════════════════════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════════════════════════

    private static HwEncoderFlags EncoderToFlag(string encoder) => encoder switch
    {
        "nvenc" => HwEncoderFlags.NVENC,
        "qsv" => HwEncoderFlags.QSV,
        "amf" => HwEncoderFlags.AMF,
        "vaapi" => HwEncoderFlags.VAAPI,
        _ => HwEncoderFlags.None
    };

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunFFmpegAsync(
        string ffmpegPath, string arguments, int timeoutMs)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            var completed = await Task.Run(() => process.WaitForExit(timeoutMs));
            if (!completed)
            {
                try { process.Kill(true); } catch { }
                _logger.LogWarning("FFmpeg process timed out after {Timeout}ms.", timeoutMs);
                return (-1, "", "Process timed out");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return (process.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to run FFmpeg process.");
            return (-1, "", ex.Message);
        }
    }
}

// ══════════════════════════════════════════════════════════════════════
//  DATA MODELS
// ══════════════════════════════════════════════════════════════════════

public enum GpuVendor
{
    Unknown,
    NVIDIA,
    Intel,
    AMD
}

[Flags]
public enum HwEncoderFlags
{
    None = 0,
    NVENC = 1,
    QSV = 2,
    AMF = 4,
    VAAPI = 8
}

public class GpuEntry
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string DriverVersion { get; set; } = "";
    public double VramGB { get; set; }
    public GpuVendor Vendor { get; set; } = GpuVendor.Unknown;
    public string EncoderType { get; set; } = "none";
}

public class VendorInfo
{
    public string Name { get; set; } = "";
    public bool SupportsHwEncoder { get; set; }
}

public class GpuDetectionResult
{
    public List<GpuEntry> DetectedGPUs { get; set; } = [];
    public string RecommendedEncoder { get; set; } = "software";
    public VendorInfo? Nvidia { get; set; }
    public VendorInfo? Intel { get; set; }
    public VendorInfo? Amd { get; set; }
}

public class FFmpegCapabilities
{
    public string ActiveEncoder { get; set; } = "software";
    public HwEncoderFlags HwEncoders { get; set; } = HwEncoderFlags.None;
}

public class EncoderSettings
{
    public string Name { get; set; } = "libx264";
    public string Type { get; set; } = "Software (CPU)";
    public string Encoder { get; set; } = "libx264";
    public string? HwAccel { get; set; }
    public string Preset { get; set; } = "veryfast";
    public bool UsesCRF { get; set; } = true;
}
