using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NexusM.Services;

/// <summary>
/// Full video transcoding engine for NexusM.
/// Handles smart streaming mode detection, HLS transcoding with hardware/software
/// encoding, CPU/GPU usage limiting, concurrent transcode semaphore, and cache management.
/// Ported from NexusM PowerShell v11.70 with optimisations.
/// </summary>
public class TranscodingService
{
    private readonly ILogger<TranscodingService> _logger;
    private readonly FFmpegService _ffmpeg;
    private readonly GpuDetectionService _gpu;
    private readonly ConfigService _configService;

    // Active transcode tracking
    private readonly ConcurrentDictionary<string, ActiveTranscode> _activeTranscodes = new();
    private SemaphoreSlim _transcodeSemaphore = null!;
    private Timer? _idleTimer;

    // Cache directories
    private string _hlsCachePath = null!;
    private string _hlsTempPath = null!;
    private string _remuxCachePath = null!;

    public TranscodingService(
        FFmpegService ffmpeg,
        GpuDetectionService gpu,
        ConfigService configService,
        ILogger<TranscodingService> logger)
    {
        _ffmpeg = ffmpeg;
        _gpu = gpu;
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// Initialise cache directories and semaphore. Called once at startup from Program.cs.
    /// </summary>
    public void Initialise()
    {
        var cfg = _configService.Config.Transcoding;
        var appDir = AppContext.BaseDirectory;

        _hlsCachePath = Path.Combine(appDir, "cache", "hls");
        _hlsTempPath = Path.Combine(appDir, "hls_temp");
        _remuxCachePath = Path.Combine(appDir, "cache", "remux");

        Directory.CreateDirectory(_hlsCachePath);
        Directory.CreateDirectory(_hlsTempPath);
        Directory.CreateDirectory(_remuxCachePath);

        _transcodeSemaphore = new SemaphoreSlim(
            Math.Max(1, cfg.MaxConcurrentTranscodes),
            Math.Max(1, cfg.MaxConcurrentTranscodes));

        // Background timer to kill idle transcodes (no segment requests for 60 seconds)
        _idleTimer = new Timer(_ => KillIdleTranscodes(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        _logger.LogInformation("TranscodingService initialised. MaxConcurrent={Max}, HLS cache: {Path}",
            cfg.MaxConcurrentTranscodes, _hlsCachePath);
    }

    private void KillIdleTranscodes()
    {
        var idleThreshold = DateTime.UtcNow.AddSeconds(-60);
        foreach (var kvp in _activeTranscodes)
        {
            var t = kvp.Value;
            if (!t.IsCompleted && t.LastActivityAt < idleThreshold)
            {
                _logger.LogInformation("Killing idle transcode {Id} (no activity for 60s)", kvp.Key);
                StopTranscode(kvp.Key);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  STREAMING MODE DETECTION
    //  Mirrors: Get-VideoStreamingMode
    // ══════════════════════════════════════════════════════════════════

    // Codecs browsers can natively decode
    private static readonly HashSet<string> BrowserVideoCodecs = new(StringComparer.OrdinalIgnoreCase)
        { "h264", "avc", "avc1", "h.264" };

    private static readonly HashSet<string> BrowserAudioCodecs = new(StringComparer.OrdinalIgnoreCase)
        { "aac", "mp3", "mp2", "mp1" };

    // Codecs that require full video transcode (HEVC not playable in Chrome/Firefox)
    private static readonly HashSet<string> TranscodeVideoCodecs = new(StringComparer.OrdinalIgnoreCase)
        { "hevc", "h265", "h.265", "av1", "vp9", "vp8", "mpeg2video", "mpeg1video", "vc1", "wmv3", "wmv2", "wmv1", "msmpeg4v3", "msmpeg4v2", "divx", "xvid" };

    // Audio codecs that need transcoding to AAC stereo
    private static readonly HashSet<string> IncompatibleAudioCodecs = new(StringComparer.OrdinalIgnoreCase)
        { "ac3", "eac3", "dts", "dts-hd", "truehd", "flac", "alac", "opus", "vorbis", "pcm_s16le", "pcm_s24le", "pcm_s32le" };

    /// <summary>
    /// Determines the best streaming mode for a video file.
    /// Returns: "direct", "remux", "remux-audio", or "transcode".
    /// </summary>
    public async Task<StreamingDecision> GetStreamingModeAsync(string filePath, string format,
        string? videoCodec, string? audioCodec, int audioChannels)
    {
        var decision = new StreamingDecision { FilePath = filePath };

        // Normalise
        var ext = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
        var vCodec = (videoCodec ?? "").ToLowerInvariant().Trim();
        var aCodec = (audioCodec ?? "").ToLowerInvariant().Trim();
        var fmt = (format ?? ext).ToUpperInvariant();

        // 1) Video codec requires full transcode?
        if (TranscodeVideoCodecs.Contains(vCodec) || !BrowserVideoCodecs.Contains(vCodec))
        {
            // Unknown or incompatible video codec -> full transcode
            if (!string.IsNullOrEmpty(vCodec) && !BrowserVideoCodecs.Contains(vCodec))
            {
                decision.Mode = "transcode";
                decision.Reason = $"Video codec '{vCodec}' not browser-compatible";
                return decision;
            }
        }

        // 2) Browser-compatible video (H.264)
        bool isH264 = BrowserVideoCodecs.Contains(vCodec);
        bool isCompatibleAudio = BrowserAudioCodecs.Contains(aCodec);
        bool isSurround = audioChannels > 2;
        bool isMp4Container = fmt is "MP4" or "M4V";

        if (isH264)
        {
            if (isSurround)
            {
                // H.264 video OK, but surround audio needs stereo downmix
                decision.Mode = "remux-audio";
                decision.Reason = $"Surround audio ({audioChannels}ch {aCodec}) needs stereo downmix";
                return decision;
            }

            if (!isCompatibleAudio && IncompatibleAudioCodecs.Contains(aCodec))
            {
                // H.264 video OK, but audio codec incompatible
                decision.Mode = "remux-audio";
                decision.Reason = $"Audio codec '{aCodec}' not browser-compatible";
                return decision;
            }

            if (isMp4Container && isCompatibleAudio && !isSurround)
            {
                // Check faststart (moov atom at beginning)
                bool hasFaststart = await CheckFaststartAsync(filePath);
                if (hasFaststart)
                {
                    decision.Mode = "direct";
                    decision.Reason = "MP4 H.264+AAC with faststart";
                    return decision;
                }
                else
                {
                    // MP4 but moov at end - needs faststart remux
                    decision.Mode = "remux";
                    decision.Reason = "MP4 missing faststart (moov atom at end)";
                    return decision;
                }
            }

            // H.264 in non-MP4 container (MKV, AVI, etc.) - remux to MP4
            decision.Mode = "remux";
            decision.Reason = $"H.264 in {fmt} container needs MP4 remux";
            return decision;
        }

        // 3) Fallback: unknown/unsupported codec
        decision.Mode = "transcode";
        decision.Reason = $"Codec '{vCodec}' / format '{fmt}' requires full transcode";
        return decision;
    }

    /// <summary>
    /// Check if an MP4 file has the moov atom before the mdat atom (faststart).
    /// Reads MP4 top-level boxes from the file header to find moov/mdat ordering.
    /// </summary>
    private async Task<bool> CheckFaststartAsync(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buf = new byte[8];
            long offset = 0;
            long fileLen = fs.Length;

            while (offset < fileLen)
            {
                fs.Position = offset;
                int read = await fs.ReadAsync(buf, 0, 8);
                if (read < 8) break;

                // MP4 box: 4 bytes size (big-endian) + 4 bytes type (ASCII)
                uint size = (uint)(buf[0] << 24 | buf[1] << 16 | buf[2] << 8 | buf[3]);
                var type = System.Text.Encoding.ASCII.GetString(buf, 4, 4);

                if (type == "moov") return true;   // moov before mdat = faststart
                if (type == "mdat") return false;  // mdat before moov = not faststart

                // Handle extended size (size == 1 means 64-bit size follows)
                if (size == 1)
                {
                    if (await fs.ReadAsync(buf, 0, 8) < 8) break;
                    long extSize = (long)buf[0] << 56 | (long)buf[1] << 48 | (long)buf[2] << 40 | (long)buf[3] << 32
                                 | (long)buf[4] << 24 | (long)buf[5] << 16 | (long)buf[6] << 8 | buf[7];
                    offset += extSize;
                }
                else if (size == 0)
                {
                    break; // box extends to end of file
                }
                else
                {
                    offset += size;
                }
            }

            return false; // couldn't determine, assume not faststart
        }
        catch
        {
            return false;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  SMART VIDEO STREAM (Main orchestration)
    //  Mirrors: Get-SmartVideoStream
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Determines the optimal streaming strategy and starts transcoding if needed.
    /// Returns streaming info for the client (URL, mode, transcode ID).
    /// </summary>
    public async Task<SmartStreamResult> GetSmartStreamAsync(
        int videoId, string filePath, string format,
        string? videoCodec, string? audioCodec, int audioChannels,
        double duration, int audioTrackIndex = 0)
    {
        if (!System.IO.File.Exists(filePath))
            return new SmartStreamResult { Error = "Video file not found on disk" };

        // 1) Determine streaming mode
        var decision = await GetStreamingModeAsync(filePath, format, videoCodec, audioCodec, audioChannels);
        _logger.LogInformation("Video {Id}: streaming mode = {Mode} ({Reason})",
            videoId, decision.Mode, decision.Reason);

        // 2) Direct play
        if (decision.Mode == "direct")
        {
            return new SmartStreamResult
            {
                Type = "direct",
                StreamUrl = $"/api/stream-video/{videoId}",
                Mode = "direct",
                Duration = duration
            };
        }

        // 3) Remux and transcode modes -> HLS
        // Remux modes use HLS with codec copy (no re-encoding) for instant segment availability.
        // This avoids blocking the HTTP response while FFmpeg processes the entire file.
        var transcodeId = audioTrackIndex > 0 ? $"video-{videoId}-a{audioTrackIndex}" : $"video-{videoId}";

        // Check for existing active transcode
        if (_activeTranscodes.TryGetValue(transcodeId, out var existing) && !existing.IsCompleted)
        {
            if (existing.Process != null && !existing.Process.HasExited)
            {
                _logger.LogInformation("Video {Id}: reusing active transcode {TranscodeId}", videoId, transcodeId);
                return new SmartStreamResult
                {
                    Type = "hls",
                    PlaylistUrl = $"/api/hls/{transcodeId}/playlist.m3u8",
                    TranscodeId = transcodeId,
                    Mode = "transcode",
                    Duration = duration,
                    Reason = decision.Reason
                };
            }
        }

        // Check for completed HLS cache (with source file validation)
        var cachePath = GetHLSCacheFolderForVideo(transcodeId);
        var playlistPath = Path.Combine(cachePath, "playlist.m3u8");
        if (_configService.Config.Transcoding.HLSCacheEnabled
            && System.IO.File.Exists(playlistPath)
            && IsHLSCacheComplete(playlistPath))
        {
            // Validate source file hasn't changed since cache was created
            if (ValidateCacheMeta(cachePath, filePath))
            {
                _logger.LogInformation("Video {Id}: serving from HLS cache", videoId);
                // Update last-accessed time for retention tracking
                TouchCacheMeta(cachePath);
                return new SmartStreamResult
                {
                    Type = "hls",
                    PlaylistUrl = $"/api/hls/{transcodeId}/playlist.m3u8",
                    TranscodeId = transcodeId,
                    Mode = "transcode-cached",
                    Duration = duration,
                    Reason = "Cached transcode"
                };
            }
            else
            {
                // Source file changed - invalidate stale cache
                _logger.LogInformation("Video {Id}: source file changed, invalidating HLS cache", videoId);
                InvalidateCacheFolder(cachePath);
            }
        }

        // Start new transcode
        var started = await StartTranscodeAsync(
            transcodeId, filePath, videoId, audioTrackIndex, decision.Mode, duration);

        if (!started)
        {
            return new SmartStreamResult
            {
                Error = "Server busy - maximum concurrent transcodes reached. Try again shortly."
            };
        }

        return new SmartStreamResult
        {
            Type = "hls",
            PlaylistUrl = $"/api/hls/{transcodeId}/playlist.m3u8",
            TranscodeId = transcodeId,
            Mode = "transcode",
            Duration = duration,
            Reason = decision.Reason
        };
    }

    // ══════════════════════════════════════════════════════════════════
    //  HLS TRANSCODING
    //  Mirrors: Start-VideoTranscode
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Starts an FFmpeg HLS transcode process for a video.
    /// Acquires the semaphore, builds FFmpeg arguments, and launches the process.
    /// </summary>
    private async Task<bool> StartTranscodeAsync(
        string transcodeId, string inputFile, int videoId,
        int audioTrackIndex, string mode, double duration)
    {
        var cfg = _configService.Config.Transcoding;

        // Acquire semaphore (non-blocking first, then 30s wait)
        if (!_transcodeSemaphore.Wait(0))
        {
            _logger.LogInformation("Transcode queue full, waiting up to 30s for slot...");
            if (!await _transcodeSemaphore.WaitAsync(TimeSpan.FromSeconds(30)))
            {
                _logger.LogWarning("Transcode semaphore timeout - server at max concurrent transcodes ({Max})",
                    cfg.MaxConcurrentTranscodes);
                return false;
            }
        }

        bool semaphoreReleased = false;
        try
        {
            // Clean up any previous transcode for this ID
            StopTranscode(transcodeId, cleanup: true);

            // Determine output folder
            var outputFolder = cfg.HLSCacheEnabled
                ? GetHLSCacheFolderForVideo(transcodeId)
                : Path.Combine(_hlsTempPath, transcodeId);

            Directory.CreateDirectory(outputFolder);

            // Clean previous output files
            foreach (var f in Directory.GetFiles(outputFolder))
                System.IO.File.Delete(f);

            var playlistPath = Path.Combine(outputFolder, "playlist.m3u8");

            // Build FFmpeg arguments
            var encoder = _gpu.GetOptimalEncoder();
            var args = BuildTranscodeArguments(inputFile, playlistPath, transcodeId,
                audioTrackIndex, mode, encoder, cfg);

            _logger.LogInformation("Starting transcode: {TranscodeId}, encoder={Encoder}, mode={Mode}",
                transcodeId, encoder.Name, mode);
            _logger.LogDebug("FFmpeg args: {Args}", args);

            // Launch FFmpeg process
            var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = _ffmpeg.FfmpegPath!,
                Arguments = args,
                WorkingDirectory = outputFolder,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();

            // Apply CPU limiting and process priority
            ApplyProcessLimits(process, encoder, cfg);

            // Track the active transcode
            var transcode = new ActiveTranscode
            {
                TranscodeId = transcodeId,
                VideoId = videoId,
                InputFile = inputFile,
                OutputFolder = outputFolder,
                PlaylistPath = playlistPath,
                Process = process,
                EncoderUsed = encoder.Name,
                Mode = mode,
                StartedAt = DateTime.UtcNow,
                Duration = duration
            };

            _activeTranscodes[transcodeId] = transcode;

            // Consume stderr asynchronously to prevent deadlock and capture errors
            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();

            // Monitor process completion in background
            _ = Task.Run(async () =>
            {
                try
                {
                    await process.WaitForExitAsync();
                    var stderr = await stderrTask;
                    await stdoutTask; // drain stdout to prevent deadlock

                    transcode.IsCompleted = true;
                    transcode.ExitCode = process.ExitCode;

                    if (process.ExitCode == 0)
                    {
                        _logger.LogInformation("Transcode {Id} completed successfully", transcodeId);
                        // Write cache meta for source file validation on future loads
                        if (cfg.HLSCacheEnabled)
                            WriteCacheMeta(outputFolder, inputFile);
                    }
                    else
                    {
                        _logger.LogError("Transcode {Id} FAILED (exit={Code})", transcodeId, process.ExitCode);
                        if (!string.IsNullOrWhiteSpace(stderr))
                        {
                            var truncated = stderr.Length > 1000 ? stderr[..1000] + "..." : stderr;
                            _logger.LogError("FFmpeg stderr for {Id}: {Stderr}", transcodeId, truncated);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error monitoring transcode {Id}", transcodeId);
                    transcode.IsCompleted = true;
                    transcode.ExitCode = -1;
                }
                finally
                {
                    if (!semaphoreReleased)
                    {
                        _transcodeSemaphore.Release();
                        semaphoreReleased = true;
                    }
                }
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start transcode {Id}", transcodeId);
            if (!semaphoreReleased)
            {
                _transcodeSemaphore.Release();
                semaphoreReleased = true;
            }
            return false;
        }
    }

    /// <summary>
    /// Builds FFmpeg command-line arguments for HLS transcode.
    /// </summary>
    private string BuildTranscodeArguments(
        string inputFile, string playlistPath, string transcodeId,
        int audioTrackIndex, string mode, EncoderSettings encoder,
        Models.TranscodingConfig cfg)
    {
        var args = new List<string>();

        // Global flags
        args.Add("-hide_banner -loglevel warning -y");

        // Hardware acceleration for decode (if using HW encoder)
        if (encoder.HwAccel != null)
        {
            switch (encoder.HwAccel)
            {
                case "cuda":
                    // NVIDIA: GPU decode, frames to system RAM for encode
                    args.Add("-hwaccel cuda");
                    break;
                case "qsv":
                    // Intel QuickSync: full GPU pipeline
                    args.Add("-init_hw_device qsv=hw -filter_hw_device hw");
                    break;
                case "d3d11va":
                    // AMD AMF: D3D11VA device init only (no -hwaccel flag, per PS v11.70 fix)
                    args.Add("-init_hw_device d3d11va=hw -filter_hw_device hw");
                    break;
            }
        }

        // Input with extended analysis for better seeking
        args.Add($"-analyzeduration 200000000 -probesize 1000000000");
        args.Add($"-fflags +genpts");
        args.Add($"-i \"{inputFile}\"");

        // Stream mapping: video + selected audio, no subtitles
        args.Add($"-map 0:v:0 -map 0:a:{audioTrackIndex} -map -0:s");

        // Mode-specific encoding
        switch (mode)
        {
            case "remux":
                args.Add("-c:v copy -c:a copy -bsf:a aac_adtstoasc");
                break;

            case "remux-audio":
                args.Add($"-c:v copy -c:a {cfg.AudioCodec} -b:a {cfg.AudioBitrate} -ac {cfg.AudioChannels}");
                break;

            case "transcode":
            default:
                // Video encoding
                args.Add($"-c:v {encoder.Encoder}");
                args.Add($"-preset {encoder.Preset}");

                if (encoder.UsesCRF)
                {
                    // Software encoding with CRF
                    args.Add($"-crf {cfg.VideoCRF}");
                    args.Add("-profile:v high -level 4.1");

                    // CPU thread limiting
                    int threadCount = CalculateThreadCount(cfg.FFmpegCPULimit);
                    if (threadCount > 0)
                    {
                        args.Add($"-threads {threadCount}");
                        args.Add($"-filter_threads {Math.Max(1, threadCount / 2)}");
                    }
                }
                else
                {
                    // Hardware encoder - rate control is encoder-specific
                    switch (encoder.Encoder)
                    {
                        case "h264_nvenc":
                            // NVENC: -rc:v vbr with max bitrate
                            args.Add($"-rc:v vbr -b:v {cfg.VideoMaxrate} -maxrate {cfg.VideoMaxrate} -bufsize {cfg.VideoBufsize}");
                            args.Add("-pix_fmt yuv420p");
                            break;

                        case "h264_amf":
                            // AMF: -rc vbr_peak (not -rc:v vbr which is NVENC-only)
                            args.Add($"-rc vbr_peak -b:v {cfg.VideoMaxrate} -maxrate {cfg.VideoMaxrate} -bufsize {cfg.VideoBufsize}");
                            args.Add("-pix_fmt nv12");
                            break;

                        case "h264_qsv":
                            // QSV: target bitrate mode, disable lookahead for Arc GPUs
                            args.Add($"-b:v {cfg.VideoMaxrate} -maxrate {cfg.VideoMaxrate} -bufsize {cfg.VideoBufsize}");
                            args.Add("-look_ahead 0");
                            args.Add("-pix_fmt nv12");
                            break;

                        default:
                            // Generic fallback
                            args.Add($"-b:v {cfg.VideoMaxrate} -bufsize {cfg.VideoBufsize}");
                            args.Add("-pix_fmt yuv420p");
                            break;
                    }
                }

                // Video filtering
                var filters = new List<string>();
                if (cfg.TranscodeMaxHeight > 0)
                    filters.Add($"scale=-2:'min(ih,{cfg.TranscodeMaxHeight})'");
                if (encoder.UsesCRF || encoder.Encoder == "h264_nvenc")
                    filters.Add("format=yuv420p");

                if (filters.Count > 0)
                    args.Add($"-vf \"{string.Join(",", filters)}\"");

                // Audio encoding
                args.Add($"-c:a {cfg.AudioCodec} -b:a {cfg.AudioBitrate} -ac {cfg.AudioChannels}");
                args.Add("-af \"aresample=async=1:first_pts=0\"");
                break;
        }

        // HLS output settings (fMP4/CMAF)
        args.Add("-f hls");
        args.Add($"-hls_time {cfg.HLSSegmentDuration}");
        args.Add("-hls_segment_type fmp4");
        args.Add($"-hls_fmp4_init_filename init.mp4");
        args.Add($"-hls_segment_filename \"{Path.Combine(Path.GetDirectoryName(playlistPath)!, $"{transcodeId}%d.m4s")}\"");
        args.Add("-hls_playlist_type event");
        args.Add("-hls_flags independent_segments+program_date_time");
        args.Add($"\"{playlistPath}\"");

        return string.Join(" ", args);
    }

    /// <summary>
    /// Apply CPU limiting (thread count, process priority, CPU affinity) to FFmpeg process.
    /// </summary>
    private void ApplyProcessLimits(Process process, EncoderSettings encoder, Models.TranscodingConfig cfg)
    {
        try
        {
            // For remux operations, use configured priority
            var remuxPriority = cfg.RemuxPriority.ToLowerInvariant() switch
            {
                "high" => ProcessPriorityClass.High,
                "abovenormal" => ProcessPriorityClass.AboveNormal,
                _ => ProcessPriorityClass.Normal
            };

            // Hardware encoders: GPU handles the work, set process to normal
            if (!encoder.UsesCRF)
            {
                process.PriorityClass = remuxPriority;
                return;
            }

            // Software encoding: apply CPU limits
            var cpuLimit = cfg.FFmpegCPULimit;
            if (cpuLimit <= 0 || cpuLimit >= 100)
            {
                // No limit, but still set a reasonable priority
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
                return;
            }

            // Set priority based on CPU limit
            process.PriorityClass = cpuLimit <= 50
                ? ProcessPriorityClass.BelowNormal
                : ProcessPriorityClass.Normal;

            // Set CPU affinity (restrict to N cores)
            if (OperatingSystem.IsWindows())
            {
                int cpuCount = Environment.ProcessorCount;
                int coresToUse = Math.Max(1, (int)Math.Floor(cpuCount * cpuLimit / 100.0));
                // Build affinity mask: enable first N cores
                nint affinityMask = 0;
                for (int i = 0; i < coresToUse && i < 64; i++)
                    affinityMask |= (nint)(1L << i);
                process.ProcessorAffinity = affinityMask;

                _logger.LogDebug("FFmpeg CPU limited: {Limit}% -> {Cores}/{Total} cores, priority={Priority}",
                    cpuLimit, coresToUse, cpuCount, process.PriorityClass);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not apply process limits (may need elevation)");
        }
    }

    /// <summary>
    /// Calculate FFmpeg thread count from CPU limit percentage.
    /// </summary>
    private static int CalculateThreadCount(int cpuLimitPercent)
    {
        if (cpuLimitPercent <= 0 || cpuLimitPercent >= 100) return 0; // 0 = FFmpeg auto
        int cpuCount = Environment.ProcessorCount;
        return Math.Max(1, (int)Math.Floor(cpuCount * cpuLimitPercent / 100.0));
    }

    // ══════════════════════════════════════════════════════════════════
    //  ENHANCED REMUX (with priority and threading)
    //  Replaces FFmpegService remux methods with tuned performance
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Remux a video to MP4 with faststart, with configurable process priority
    /// and threading for faster remux performance.
    /// </summary>
    public async Task<bool> RemuxFaststartAsync(string inputPath, string outputPath, int audioTrackIndex = 0)
    {
        if (!_ffmpeg.IsAvailable) return false;
        var cfg = _configService.Config.Transcoding;

        var threadArg = cfg.RemuxThreads > 0 ? $"-threads {cfg.RemuxThreads}" : "";
        var args = $"-hide_banner -loglevel error {threadArg} -i \"{inputPath}\" -map 0:v:0 -map 0:a:{audioTrackIndex} -c copy -movflags +faststart -f mp4 -y \"{outputPath}\"";

        return await RunRemuxProcessAsync(args, cfg.RemuxPriority);
    }

    /// <summary>
    /// Remux with stereo downmix, with configurable process priority.
    /// </summary>
    public async Task<bool> RemuxStereoDownmixAsync(string inputPath, string outputPath, int audioTrackIndex = 0)
    {
        if (!_ffmpeg.IsAvailable) return false;
        var cfg = _configService.Config.Transcoding;

        var threadArg = cfg.RemuxThreads > 0 ? $"-threads {cfg.RemuxThreads}" : "";
        var args = $"-hide_banner -loglevel error {threadArg} -i \"{inputPath}\" -map 0:v:0 -map 0:a:{audioTrackIndex} -c:v copy -c:a aac -ac 2 -b:a {cfg.AudioBitrate} -movflags +faststart -f mp4 -y \"{outputPath}\"";

        return await RunRemuxProcessAsync(args, cfg.RemuxPriority);
    }

    private async Task<bool> RunRemuxProcessAsync(string arguments, string priority)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = _ffmpeg.FfmpegPath!,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        // Apply priority
        try
        {
            process.PriorityClass = priority.ToLowerInvariant() switch
            {
                "high" => ProcessPriorityClass.High,
                "abovenormal" => ProcessPriorityClass.AboveNormal,
                _ => ProcessPriorityClass.Normal
            };
        }
        catch { /* may need elevation */ }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var completed = await Task.Run(() => process.WaitForExit(600_000)); // 10 min
        if (!completed)
        {
            try { process.Kill(true); } catch { }
            return false;
        }

        await stderrTask;
        return process.ExitCode == 0;
    }

    // ══════════════════════════════════════════════════════════════════
    //  HLS CACHE MANAGEMENT
    // ══════════════════════════════════════════════════════════════════

    private string GetHLSCacheFolderForVideo(string transcodeId)
    {
        var path = Path.Combine(_hlsCachePath, transcodeId);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Validate that a cached transcode still matches the source file.
    /// Checks file size and last-modified date stored in .meta JSON.
    /// </summary>
    private bool ValidateCacheMeta(string cacheFolderPath, string sourceFilePath)
    {
        var metaPath = Path.Combine(cacheFolderPath, ".meta");
        if (!System.IO.File.Exists(metaPath)) return false;

        try
        {
            var json = System.IO.File.ReadAllText(metaPath);
            var meta = JsonSerializer.Deserialize<CacheMeta>(json);
            if (meta == null) return false;

            var fi = new FileInfo(sourceFilePath);
            if (!fi.Exists) return false;

            return meta.SourceSize == fi.Length
                && meta.SourceModified == fi.LastWriteTimeUtc.Ticks;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Update the last-accessed timestamp on a cache .meta file for retention tracking.
    /// </summary>
    private void TouchCacheMeta(string cacheFolderPath)
    {
        var metaPath = Path.Combine(cacheFolderPath, ".meta");
        if (!System.IO.File.Exists(metaPath)) return;

        try
        {
            var json = System.IO.File.ReadAllText(metaPath);
            var meta = JsonSerializer.Deserialize<CacheMeta>(json);
            if (meta != null)
            {
                meta.LastAccessed = DateTime.UtcNow.Ticks;
                System.IO.File.WriteAllText(metaPath,
                    JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch { /* non-critical */ }
    }

    /// <summary>
    /// Write a .meta JSON file for a newly cached transcode, recording the source file's
    /// size and modification date so stale caches can be detected.
    /// </summary>
    private void WriteCacheMeta(string cacheFolderPath, string sourceFilePath)
    {
        try
        {
            var fi = new FileInfo(sourceFilePath);
            if (!fi.Exists) return;

            var meta = new CacheMeta
            {
                SourcePath = sourceFilePath,
                SourceSize = fi.Length,
                SourceModified = fi.LastWriteTimeUtc.Ticks,
                CreatedAt = DateTime.UtcNow.Ticks,
                LastAccessed = DateTime.UtcNow.Ticks
            };

            var metaPath = Path.Combine(cacheFolderPath, ".meta");
            System.IO.File.WriteAllText(metaPath,
                JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to write cache meta for {Path}", cacheFolderPath);
        }
    }

    /// <summary>
    /// Delete all files in a stale HLS cache folder.
    /// </summary>
    private void InvalidateCacheFolder(string cacheFolderPath)
    {
        try
        {
            if (Directory.Exists(cacheFolderPath))
            {
                Directory.Delete(cacheFolderPath, recursive: true);
                Directory.CreateDirectory(cacheFolderPath);
                _logger.LogInformation("Invalidated stale HLS cache: {Path}", cacheFolderPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate cache folder {Path}", cacheFolderPath);
        }
    }

    /// <summary>
    /// Background cache cleanup: removes entries older than retention days
    /// and trims cache to stay under max size.
    /// </summary>
    public void CleanupHLSCache()
    {
        var cfg = _configService.Config.Transcoding;
        if (!cfg.HLSCacheEnabled || !Directory.Exists(_hlsCachePath)) return;

        int removed = 0;
        long freedBytes = 0;

        // 1) Remove entries older than retention period
        var retentionCutoff = DateTime.UtcNow.AddDays(-cfg.HLSCacheRetentionDays);
        foreach (var dir in Directory.GetDirectories(_hlsCachePath))
        {
            var metaPath = Path.Combine(dir, ".meta");
            bool shouldRemove = false;

            if (System.IO.File.Exists(metaPath))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(metaPath);
                    var meta = JsonSerializer.Deserialize<CacheMeta>(json);
                    if (meta != null)
                    {
                        var lastAccessed = new DateTime(meta.LastAccessed, DateTimeKind.Utc);
                        shouldRemove = lastAccessed < retentionCutoff;
                    }
                }
                catch { shouldRemove = true; }
            }
            else
            {
                // No meta file - check folder modification time
                shouldRemove = Directory.GetLastWriteTimeUtc(dir) < retentionCutoff;
            }

            if (shouldRemove)
            {
                long dirSize = Directory.GetFiles(dir).Sum(f => new FileInfo(f).Length);
                try
                {
                    Directory.Delete(dir, recursive: true);
                    removed++;
                    freedBytes += dirSize;
                }
                catch { }
            }
        }

        // 2) If still over max size, remove oldest entries
        long maxBytes = (long)cfg.HLSCacheMaxSizeGB * 1024 * 1024 * 1024;
        if (maxBytes > 0)
        {
            var entries = Directory.GetDirectories(_hlsCachePath)
                .Select(dir =>
                {
                    long size = Directory.GetFiles(dir).Sum(f => new FileInfo(f).Length);
                    long lastAccessed = 0;
                    var metaPath = Path.Combine(dir, ".meta");
                    if (System.IO.File.Exists(metaPath))
                    {
                        try
                        {
                            var meta = JsonSerializer.Deserialize<CacheMeta>(System.IO.File.ReadAllText(metaPath));
                            lastAccessed = meta?.LastAccessed ?? 0;
                        }
                        catch { }
                    }
                    if (lastAccessed == 0)
                        lastAccessed = Directory.GetLastWriteTimeUtc(dir).Ticks;

                    return new { Path = dir, Size = size, LastAccessed = lastAccessed };
                })
                .OrderBy(e => e.LastAccessed) // oldest first
                .ToList();

            long totalSize = entries.Sum(e => e.Size);

            foreach (var entry in entries)
            {
                if (totalSize <= maxBytes) break;
                try
                {
                    Directory.Delete(entry.Path, recursive: true);
                    totalSize -= entry.Size;
                    freedBytes += entry.Size;
                    removed++;
                }
                catch { }
            }
        }

        if (removed > 0)
            _logger.LogInformation("HLS cache cleanup: removed {Count} entries, freed {Freed:F1} MB",
                removed, freedBytes / (1024.0 * 1024.0));
    }

    /// <summary>
    /// Check if a cached HLS transcode is complete (has ENDLIST marker).
    /// </summary>
    public bool IsHLSCacheComplete(string playlistPath)
    {
        if (!System.IO.File.Exists(playlistPath)) return false;
        try
        {
            var content = System.IO.File.ReadAllText(playlistPath);
            if (!content.Contains("#EXT-X-ENDLIST")) return false;

            // Verify init.mp4 and at least one segment exist
            var dir = Path.GetDirectoryName(playlistPath)!;
            if (!System.IO.File.Exists(Path.Combine(dir, "init.mp4"))) return false;

            var segments = Directory.GetFiles(dir, "*.m4s");
            if (segments.Length == 0) return false;

            // Count EXTINF entries and compare with segment files
            int extinfCount = Regex.Matches(content, "#EXTINF:").Count;
            // Allow ±1 tolerance
            return Math.Abs(extinfCount - segments.Length) <= 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get the HLS playlist content, converting EVENT -> VOD if transcode is complete.
    /// </summary>
    public string? GetPlaylistContent(string transcodeId)
    {
        // Check cache first, then temp
        var cachePath = Path.Combine(_hlsCachePath, transcodeId, "playlist.m3u8");
        var tempPath = Path.Combine(_hlsTempPath, transcodeId, "playlist.m3u8");
        var playlistPath = System.IO.File.Exists(cachePath) ? cachePath : tempPath;

        if (!System.IO.File.Exists(playlistPath)) return null;

        var content = System.IO.File.ReadAllText(playlistPath);

        // If transcode is complete, ensure ENDLIST marker and VOD type
        if (_activeTranscodes.TryGetValue(transcodeId, out var transcode) && transcode.IsCompleted)
        {
            if (!content.Contains("#EXT-X-ENDLIST"))
            {
                content = content.TrimEnd() + "\n#EXT-X-ENDLIST\n";
                // Also rewrite the file for caching
                try { System.IO.File.WriteAllText(playlistPath, content); }
                catch { /* non-critical */ }
            }
            // Convert EVENT -> VOD
            content = content.Replace("#EXT-X-PLAYLIST-TYPE:EVENT", "#EXT-X-PLAYLIST-TYPE:VOD");
        }

        return content;
    }

    /// <summary>
    /// Get the path to an HLS segment file.
    /// </summary>
    public string? GetSegmentPath(string transcodeId, string segmentName)
    {
        // Update last activity timestamp for idle timeout tracking
        if (_activeTranscodes.TryGetValue(transcodeId, out var transcode))
            transcode.LastActivityAt = DateTime.UtcNow;

        // Check cache first, then temp
        var cachePath = Path.Combine(_hlsCachePath, transcodeId, segmentName);
        var tempPath = Path.Combine(_hlsTempPath, transcodeId, segmentName);

        if (System.IO.File.Exists(cachePath)) return cachePath;
        if (System.IO.File.Exists(tempPath)) return tempPath;
        return null;
    }

    // ══════════════════════════════════════════════════════════════════
    //  TRANSCODE LIFECYCLE
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Check if a transcode has already failed (process exited with non-zero code).
    /// Used by the playlist endpoint to fail fast instead of waiting 30 seconds.
    /// </summary>
    public bool IsTranscodeFailed(string transcodeId)
    {
        if (_activeTranscodes.TryGetValue(transcodeId, out var transcode))
            return transcode.IsCompleted && transcode.ExitCode != 0;
        return false;
    }

    /// <summary>
    /// Stop a specific transcode by ID.
    /// </summary>
    public bool StopTranscode(string transcodeId, bool cleanup = false)
    {
        if (!_activeTranscodes.TryRemove(transcodeId, out var transcode))
            return false;

        if (transcode.Process is { HasExited: false })
        {
            try
            {
                // Use taskkill for reliable tree kill on Windows
                if (OperatingSystem.IsWindows())
                {
                    var kill = Process.Start(new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = $"/F /T /PID {transcode.Process.Id}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    });
                    kill?.WaitForExit(5000);
                }
                else
                {
                    transcode.Process.Kill(true);
                }

                _logger.LogInformation("Stopped transcode {Id} (PID={Pid})", transcodeId, transcode.Process.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping transcode {Id}", transcodeId);
            }
        }

        // Clean up temp folder (preserve cache folder)
        if (cleanup)
        {
            var tempFolder = Path.Combine(_hlsTempPath, transcodeId);
            if (Directory.Exists(tempFolder))
            {
                try { Directory.Delete(tempFolder, recursive: true); }
                catch { /* may be in use */ }
            }
        }

        return true;
    }

    /// <summary>
    /// Stop all active transcodes (called on shutdown).
    /// </summary>
    public void StopAllTranscodes()
    {
        _logger.LogInformation("Stopping all active transcodes ({Count})...", _activeTranscodes.Count);

        foreach (var kvp in _activeTranscodes)
        {
            StopTranscode(kvp.Key, cleanup: false);
        }

        // Clean up temp directory (preserve cache)
        if (Directory.Exists(_hlsTempPath))
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(_hlsTempPath))
                    Directory.Delete(dir, recursive: true);
            }
            catch { }
        }
    }

    /// <summary>
    /// Get status of all active transcodes.
    /// </summary>
    public IReadOnlyCollection<TranscodeStatus> GetActiveTranscodes()
    {
        return _activeTranscodes.Values.Select(t => new TranscodeStatus
        {
            TranscodeId = t.TranscodeId,
            VideoId = t.VideoId,
            EncoderUsed = t.EncoderUsed,
            Mode = t.Mode,
            StartedAt = t.StartedAt,
            IsCompleted = t.IsCompleted,
            ExitCode = t.ExitCode,
            Duration = t.Duration,
            IsProcessAlive = t.Process is { HasExited: false }
        }).ToList();
    }

    /// <summary>
    /// Get HLS cache statistics.
    /// </summary>
    public CacheStats GetCacheStats()
    {
        var stats = new CacheStats();

        if (Directory.Exists(_hlsCachePath))
        {
            foreach (var dir in Directory.GetDirectories(_hlsCachePath))
            {
                stats.HLSEntries++;
                foreach (var file in Directory.GetFiles(dir))
                    stats.HLSTotalSizeBytes += new FileInfo(file).Length;
            }
        }

        if (Directory.Exists(_remuxCachePath))
        {
            foreach (var file in Directory.GetFiles(_remuxCachePath))
            {
                stats.RemuxEntries++;
                stats.RemuxTotalSizeBytes += new FileInfo(file).Length;
            }
        }

        return stats;
    }

    /// <summary>
    /// Clear the HLS cache.
    /// </summary>
    public (int count, long freedBytes) ClearHLSCache()
    {
        int count = 0;
        long freed = 0;

        if (!Directory.Exists(_hlsCachePath))
            return (0, 0);

        foreach (var dir in Directory.GetDirectories(_hlsCachePath))
        {
            foreach (var file in Directory.GetFiles(dir))
                freed += new FileInfo(file).Length;
            Directory.Delete(dir, recursive: true);
            count++;
        }

        _logger.LogInformation("Cleared HLS cache: {Count} entries, {Freed} bytes freed", count, freed);
        return (count, freed);
    }
}

// ══════════════════════════════════════════════════════════════════════
//  DATA MODELS
// ══════════════════════════════════════════════════════════════════════

public class StreamingDecision
{
    public string FilePath { get; set; } = "";
    public string Mode { get; set; } = "direct";
    public string Reason { get; set; } = "";
}

public class SmartStreamResult
{
    public string? Error { get; set; }
    public string Type { get; set; } = "direct"; // "direct" or "hls"
    public string? StreamUrl { get; set; }
    public string? PlaylistUrl { get; set; }
    public string? TranscodeId { get; set; }
    public string Mode { get; set; } = "direct";
    public string? Reason { get; set; }
    public double Duration { get; set; }
}

public class ActiveTranscode
{
    public string TranscodeId { get; set; } = "";
    public int VideoId { get; set; }
    public string InputFile { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public string PlaylistPath { get; set; } = "";
    public Process? Process { get; set; }
    public string EncoderUsed { get; set; } = "";
    public string Mode { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public bool IsCompleted { get; set; }
    public int ExitCode { get; set; } = -1;
    public double Duration { get; set; }
}

public class TranscodeStatus
{
    public string TranscodeId { get; set; } = "";
    public int VideoId { get; set; }
    public string EncoderUsed { get; set; } = "";
    public string Mode { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public bool IsCompleted { get; set; }
    public int ExitCode { get; set; }
    public double Duration { get; set; }
    public bool IsProcessAlive { get; set; }
}

public class CacheStats
{
    public int HLSEntries { get; set; }
    public long HLSTotalSizeBytes { get; set; }
    public int RemuxEntries { get; set; }
    public long RemuxTotalSizeBytes { get; set; }
}

public class CacheMeta
{
    public string SourcePath { get; set; } = "";
    public long SourceSize { get; set; }
    public long SourceModified { get; set; }
    public long CreatedAt { get; set; }
    public long LastAccessed { get; set; }
}
