using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NexusM.Data;
using NexusM.Models;

namespace NexusM.Services;

/// <summary>
/// Scans configured music video folders and indexes video files into the SQLite database.
/// Uses FFmpeg/FFprobe for metadata extraction, thumbnail generation, and MP4 analysis.
/// </summary>
public class MusicVideoScannerService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConfigService _configService;
    private readonly FFmpegService _ffmpegService;
    private readonly ILogger<MusicVideoScannerService> _logger;

    private int _scanningFlag; // 0 = idle, 1 = scanning (atomic)
    private MusicVideoScanProgress _currentProgress = new();

    public bool IsScanning => _scanningFlag == 1;
    public MusicVideoScanProgress CurrentProgress => _currentProgress;

    public MusicVideoScannerService(
        IServiceProvider serviceProvider,
        ConfigService configService,
        FFmpegService ffmpegService,
        ILogger<MusicVideoScannerService> logger)
    {
        _serviceProvider = serviceProvider;
        _configService = configService;
        _ffmpegService = ffmpegService;
        _logger = logger;
    }

    public Task StartScanAsync()
    {
        if (Interlocked.CompareExchange(ref _scanningFlag, 1, 0) != 0)
        {
            _logger.LogWarning("Music videos scan already in progress, ignoring request.");
            return Task.CompletedTask;
        }
        try
        {
            return Task.Run(async () => await ScanMusicVideosAsync());
        }
        catch
        {
            Interlocked.Exchange(ref _scanningFlag, 0);
            throw;
        }
    }

    private async Task ScanMusicVideosAsync()
    {
        _currentProgress = new MusicVideoScanProgress { Status = "scanning", StartTime = DateTime.UtcNow };

        var folders = _configService.Config.Library.GetMusicVideosFolderList();
        var extensions = _configService.Config.Library.GetMusicVideoExtensionList()
            .Select(e => e.ToLowerInvariant()).ToHashSet();

        if (folders.Count == 0)
        {
            _logger.LogWarning("No music videos folders configured. Edit NexusM.conf [Library] MusicVideosFolders.");
            _currentProgress.Status = "completed";
            _currentProgress.Message = "No music videos folders configured";
            Interlocked.Exchange(ref _scanningFlag, 0);
            return;
        }

        _logger.LogInformation("Starting music videos scan. Folders: {Count}, Extensions: {Ext}",
            folders.Count, string.Join(", ", extensions));

        try
        {
            var videoFiles = new List<string>();
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                {
                    _logger.LogWarning("Music videos folder not found: {Folder}", folder);
                    continue;
                }
                try
                {
                    var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                    videoFiles.AddRange(files);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error enumerating folder: {Folder}", folder);
                }
            }

            // Normalize paths and deduplicate to prevent double inserts
            var uniqueFiles = videoFiles
                .Select(f => Path.GetFullPath(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            videoFiles = uniqueFiles;

            _currentProgress.TotalFiles = videoFiles.Count;
            _logger.LogInformation("Found {Count} music video files to process", videoFiles.Count);

            var maxThreads = Math.Max(1, _configService.Config.Library.ScanThreads);
            var semaphore = new SemaphoreSlim(maxThreads);

            var tasks = videoFiles.Select(async filePath =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await ProcessVideoFileAsync(filePath);
                    Interlocked.Increment(ref _currentProgress._processedFiles);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            _currentProgress.Status = "completed";
            _currentProgress.Message = $"Scan complete. {_currentProgress.NewVideos} new, {_currentProgress.UpdatedVideos} updated, {_currentProgress.ErrorCount} errors.";
            _logger.LogInformation(_currentProgress.Message);
        }
        catch (Exception ex)
        {
            _currentProgress.Status = "failed";
            _currentProgress.Message = ex.Message;
            _logger.LogError(ex, "Music videos scan failed");
        }
        finally
        {
            Interlocked.Exchange(ref _scanningFlag, 0);
        }
    }

    // Track paths currently being processed to prevent parallel duplicate inserts
    private readonly ConcurrentDictionary<string, byte> _processingPaths = new(StringComparer.OrdinalIgnoreCase);

    private async Task ProcessVideoFileAsync(string filePath)
    {
        try
        {
            filePath = Path.GetFullPath(filePath);

            if (!_processingPaths.TryAdd(filePath, 0))
                return;

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MusicVideosDbContext>();

            var fileInfo = new FileInfo(filePath);
            var normalizedPath = filePath.ToLowerInvariant();
            var existing = await db.MusicVideos.FirstOrDefaultAsync(v => v.FilePath.ToLower() == normalizedPath);

            if (existing != null && existing.LastModified == fileInfo.LastWriteTimeUtc)
            {
                // File unchanged — generate thumbnail if missing or file lost from disk
                var thumbDir = Path.Combine(AppContext.BaseDirectory, "assets", "mvthumbs");
                var thumbMissing = string.IsNullOrEmpty(existing.ThumbnailPath) ||
                    !File.Exists(Path.Combine(thumbDir, existing.ThumbnailPath));
                if (thumbMissing)
                {
                    var thumb = await GenerateThumbnailAsync(filePath, existing.Id, existing.Duration);
                    if (thumb != null)
                    {
                        existing.ThumbnailPath = thumb;
                        db.MusicVideos.Update(existing);
                        await db.SaveChangesAsync();
                    }
                }
                return;
            }

            // Parse metadata from filename
            var (artist, title, year) = ParseFilename(fileInfo.Name);
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var format = ext.TrimStart('.').ToUpperInvariant();

            // Probe video metadata with ffprobe
            double duration = 0;
            string resolution = "", codec = "";
            int width = 0, height = 0, bitrate = 0, audioChannels = 2;
            string moovPosition = "";
            bool mp4Compliant = true, needsOptimization = false;

            if (_ffmpegService.IsProbeAvailable)
            {
                var probe = await ProbeVideoMetadataAsync(filePath);
                if (probe != null)
                {
                    duration = probe.Duration;
                    resolution = probe.Resolution;
                    codec = probe.Codec;
                    width = probe.Width;
                    height = probe.Height;
                    bitrate = probe.Bitrate;
                    audioChannels = probe.AudioChannels;
                    moovPosition = probe.MoovPosition;
                    mp4Compliant = probe.Mp4Compliant;
                    needsOptimization = probe.NeedsOptimization;
                }
            }

            if (existing != null)
            {
                existing.FileName = fileInfo.Name;
                existing.Title = title;
                existing.Artist = artist;
                existing.Year = year;
                existing.Duration = duration;
                existing.SizeBytes = fileInfo.Length;
                existing.Format = format;
                existing.Resolution = resolution;
                existing.Width = width;
                existing.Height = height;
                existing.Codec = codec;
                existing.Bitrate = bitrate;
                existing.AudioChannels = audioChannels;
                existing.MoovPosition = moovPosition;
                existing.Mp4Compliant = mp4Compliant;
                existing.NeedsOptimization = needsOptimization;
                existing.LastModified = fileInfo.LastWriteTimeUtc;

                if (string.IsNullOrEmpty(existing.ThumbnailPath))
                {
                    var thumb = await GenerateThumbnailAsync(filePath, existing.Id, duration);
                    if (thumb != null) existing.ThumbnailPath = thumb;
                }

                db.MusicVideos.Update(existing);
                await db.SaveChangesAsync();
                Interlocked.Increment(ref _currentProgress._updatedVideos);
            }
            else
            {
                var video = new MusicVideo
                {
                    FilePath = filePath,
                    FileName = fileInfo.Name,
                    Title = title,
                    Artist = artist,
                    Year = year,
                    Duration = duration,
                    SizeBytes = fileInfo.Length,
                    Format = format,
                    Resolution = resolution,
                    Width = width,
                    Height = height,
                    Codec = codec,
                    Bitrate = bitrate,
                    AudioChannels = audioChannels,
                    MoovPosition = moovPosition,
                    Mp4Compliant = mp4Compliant,
                    NeedsOptimization = needsOptimization,
                    DateAdded = DateTime.UtcNow,
                    LastModified = fileInfo.LastWriteTimeUtc
                };

                db.MusicVideos.Add(video);
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    _logger.LogDebug("Skipping duplicate music video: {File}", filePath);
                    return;
                }

                var thumb = await GenerateThumbnailAsync(filePath, video.Id, duration);
                if (thumb != null)
                {
                    video.ThumbnailPath = thumb;
                    db.MusicVideos.Update(video);
                    await db.SaveChangesAsync();
                }

                Interlocked.Increment(ref _currentProgress._newVideos);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing music video: {File}", filePath);
            Interlocked.Increment(ref _currentProgress._errorCount);
        }
        finally
        {
            _processingPaths.TryRemove(filePath, out _);
        }
    }

    /// <summary>
    /// Generate a thumbnail for a single video at 30% duration.
    /// </summary>
    public async Task<string?> GenerateThumbnailAsync(string filePath, int videoId, double duration)
    {
        if (!_ffmpegService.IsAvailable) return null;

        try
        {
            var thumbDir = Path.Combine(AppContext.BaseDirectory, "assets", "mvthumbs");
            if (!Directory.Exists(thumbDir))
                Directory.CreateDirectory(thumbDir);

            var thumbFilename = $"mvthumb_{videoId}.jpg";
            var thumbPath = Path.Combine(thumbDir, thumbFilename);

            if (File.Exists(thumbPath))
                return thumbFilename;

            // Seek to 30% of duration, or 5 seconds as fallback
            var seekTo = duration > 10 ? duration * 0.30 : 5;
            var success = await _ffmpegService.GenerateThumbnailAsync(filePath, thumbPath, seekTo);
            return success ? thumbFilename : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to generate thumbnail for video {Id}: {Error}", videoId, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Batch generate thumbnails for all videos missing them.
    /// </summary>
    public async Task GenerateAllThumbnailsAsync()
    {
        if (!_ffmpegService.IsAvailable)
        {
            _logger.LogWarning("FFmpeg not available, cannot generate thumbnails.");
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicVideosDbContext>();
        var thumbDir = Path.Combine(AppContext.BaseDirectory, "assets", "mvthumbs");

        // Get all videos — regenerate if thumbnail path is empty OR the file is missing from disk
        var videos = await db.MusicVideos
            .Select(v => new { v.Id, v.FilePath, v.Duration, v.ThumbnailPath })
            .ToListAsync();

        var toGenerate = videos.Where(v =>
            string.IsNullOrEmpty(v.ThumbnailPath) ||
            !File.Exists(Path.Combine(thumbDir, v.ThumbnailPath))).ToList();

        _logger.LogInformation("Generating thumbnails for {Count} music videos ({Total} total)", toGenerate.Count, videos.Count);

        foreach (var v in toGenerate)
        {
            if (!File.Exists(v.FilePath)) continue;
            var thumb = await GenerateThumbnailAsync(v.FilePath, v.Id, v.Duration);
            if (thumb != null)
            {
                var entity = await db.MusicVideos.FindAsync(v.Id);
                if (entity != null)
                {
                    entity.ThumbnailPath = thumb;
                    await db.SaveChangesAsync();
                }
            }
        }

        _logger.LogInformation("Thumbnail generation complete");
    }

    /// <summary>
    /// Analyze all MP4 files for compliance (moov atom position, audio channels).
    /// </summary>
    public async Task AnalyzeMp4ComplianceAsync()
    {
        if (!_ffmpegService.IsProbeAvailable)
        {
            _logger.LogWarning("FFprobe not available, cannot analyze MP4s.");
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicVideosDbContext>();
        var videos = await db.MusicVideos.ToListAsync();

        _logger.LogInformation("Analyzing {Count} music videos for MP4 compliance", videos.Count);

        foreach (var video in videos)
        {
            if (!File.Exists(video.FilePath)) continue;

            var probe = await ProbeVideoMetadataAsync(video.FilePath);
            if (probe != null)
            {
                video.Duration = probe.Duration;
                video.Resolution = probe.Resolution;
                video.Codec = probe.Codec;
                video.Width = probe.Width;
                video.Height = probe.Height;
                video.Bitrate = probe.Bitrate;
                video.AudioChannels = probe.AudioChannels;
                video.MoovPosition = probe.MoovPosition;
                video.Mp4Compliant = probe.Mp4Compliant;
                video.NeedsOptimization = probe.NeedsOptimization;
                await db.SaveChangesAsync();
            }
        }

        _logger.LogInformation("MP4 compliance analysis complete");
    }

    /// <summary>
    /// Fix a single MP4 by remuxing with faststart.
    /// </summary>
    public async Task<bool> FixMp4Async(int videoId)
    {
        if (!_ffmpegService.IsAvailable) return false;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicVideosDbContext>();
        var video = await db.MusicVideos.FindAsync(videoId);
        if (video == null || !File.Exists(video.FilePath)) return false;

        var tempPath = video.FilePath + ".tmp.mp4";
        try
        {
            bool success;
            if (video.AudioChannels > 2)
                success = await _ffmpegService.RemuxStereoDownmixAsync(video.FilePath, tempPath);
            else
                success = await _ffmpegService.RemuxFaststartAsync(video.FilePath, tempPath);

            if (success && File.Exists(tempPath))
            {
                var origSize = new FileInfo(video.FilePath).Length;
                var newSize = new FileInfo(tempPath).Length;

                // Sanity check: new file should be at least 50% of original
                if (newSize > origSize * 0.5)
                {
                    File.Delete(video.FilePath);
                    File.Move(tempPath, video.FilePath);

                    video.MoovPosition = "beginning";
                    video.NeedsOptimization = false;
                    video.Mp4Compliant = true;
                    video.SizeBytes = newSize;
                    video.LastModified = new FileInfo(video.FilePath).LastWriteTimeUtc;
                    if (video.AudioChannels > 2) video.AudioChannels = 2;
                    await db.SaveChangesAsync();
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fix MP4: {File}", video.FilePath);
        }
        finally
        {
            if (File.Exists(tempPath))
                try { File.Delete(tempPath); } catch { }
        }
        return false;
    }

    private async Task<VideoProbeResult?> ProbeVideoMetadataAsync(string filePath)
    {
        try
        {
            var doc = await _ffmpegService.ProbeAsync(filePath);
            if (doc == null) return null;

            var result = new VideoProbeResult();
            var root = doc.RootElement;

            // Parse format
            if (root.TryGetProperty("format", out var fmt))
            {
                if (fmt.TryGetProperty("duration", out var dur) && double.TryParse(dur.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                    result.Duration = d;
                if (fmt.TryGetProperty("bit_rate", out var br) && int.TryParse(br.GetString(), out var b))
                    result.Bitrate = b / 1000; // Convert to kbps
            }

            // Parse streams
            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    var codecType = stream.TryGetProperty("codec_type", out var ct) ? ct.GetString() : "";

                    if (codecType == "video" && result.Width == 0)
                    {
                        if (stream.TryGetProperty("width", out var w)) result.Width = w.GetInt32();
                        if (stream.TryGetProperty("height", out var h)) result.Height = h.GetInt32();
                        if (stream.TryGetProperty("codec_name", out var cn)) result.Codec = cn.GetString() ?? "";
                        if (result.Width > 0 && result.Height > 0)
                            result.Resolution = $"{result.Width}x{result.Height}";
                    }
                    else if (codecType == "audio" && result.AudioChannels == 2)
                    {
                        if (stream.TryGetProperty("channels", out var ch)) result.AudioChannels = ch.GetInt32();
                    }
                }
            }

            // Determine MP4 compliance
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".mp4" || ext == ".m4v")
            {
                // Check moov atom position by looking at format tags
                result.MoovPosition = "unknown";
                if (root.TryGetProperty("format", out var fmtCheck))
                {
                    if (fmtCheck.TryGetProperty("tags", out var tags))
                    {
                        // If major_brand exists, it's likely a proper MP4
                        if (tags.TryGetProperty("major_brand", out _))
                            result.MoovPosition = "beginning"; // Assume good unless proven otherwise
                    }
                }
                result.Mp4Compliant = result.MoovPosition != "end" && result.AudioChannels <= 2;
                result.NeedsOptimization = !result.Mp4Compliant;
            }
            else
            {
                // Non-MP4 formats need remux for browser playback
                result.Mp4Compliant = false;
                result.NeedsOptimization = true;
            }

            doc.Dispose();
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to probe video: {File}: {Error}", filePath, ex.Message);
            return null;
        }
    }

    private static (string artist, string title, int? year) ParseFilename(string fileName)
    {
        var nameOnly = Path.GetFileNameWithoutExtension(fileName);

        // Extract year from patterns: (YYYY), [YYYY], or standalone YYYY
        int? year = null;
        var yearMatch = Regex.Match(nameOnly, @"[\(\[]?((?:19|20)\d{2})[\)\]]?");
        if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out var y))
        {
            year = y;
            // Remove the year from the name for cleaner title
            nameOnly = nameOnly.Replace(yearMatch.Value, "").Trim(' ', '-', '_');
        }

        // Split on " - " for "Artist - Title" pattern
        var dashIndex = nameOnly.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIndex > 0)
        {
            var artist = nameOnly.Substring(0, dashIndex).Trim();
            var title = nameOnly.Substring(dashIndex + 3).Trim();
            return (artist, title, year);
        }

        return ("", nameOnly.Trim(), year);
    }

    private class VideoProbeResult
    {
        public double Duration { get; set; }
        public string Resolution { get; set; } = "";
        public string Codec { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public int Bitrate { get; set; }
        public int AudioChannels { get; set; } = 2;
        public string MoovPosition { get; set; } = "";
        public bool Mp4Compliant { get; set; } = true;
        public bool NeedsOptimization { get; set; }
    }
}
