using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NexusM.Data;
using NexusM.Models;

namespace NexusM.Services;

/// <summary>
/// Scans configured movies/TV folders and indexes video files into the videos SQLite database.
/// Uses FFmpeg/FFprobe for metadata extraction and thumbnail generation.
/// </summary>
public class VideoScannerService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConfigService _configService;
    private readonly FFmpegService _ffmpegService;
    private readonly MetadataService _metadataService;
    private readonly ILogger<VideoScannerService> _logger;

    private bool _isScanning;
    private VideoScanProgress _currentProgress = new();

    public bool IsScanning => _isScanning;
    public VideoScanProgress CurrentProgress => _currentProgress;

    public VideoScannerService(
        IServiceProvider serviceProvider,
        ConfigService configService,
        FFmpegService ffmpegService,
        MetadataService metadataService,
        ILogger<VideoScannerService> logger)
    {
        _serviceProvider = serviceProvider;
        _configService = configService;
        _ffmpegService = ffmpegService;
        _metadataService = metadataService;
        _logger = logger;
    }

    public Task StartScanAsync()
    {
        if (_isScanning)
        {
            _logger.LogWarning("Videos scan already in progress, ignoring request.");
            return Task.CompletedTask;
        }
        return Task.Run(async () => await ScanVideosAsync());
    }

    private async Task ScanVideosAsync()
    {
        _isScanning = true;
        _currentProgress = new VideoScanProgress { Status = "scanning", StartTime = DateTime.UtcNow };

        var folders = _configService.Config.Library.GetMoviesTVFolderList();
        var extensions = _configService.Config.Library.GetVideoExtensionList()
            .Select(e => e.ToLowerInvariant()).ToHashSet();

        if (folders.Count == 0)
        {
            _logger.LogWarning("No movies/TV folders configured. Edit NexusM.conf [Library] MoviesTVFolders.");
            _currentProgress.Status = "completed";
            _currentProgress.Message = "No movies/TV folders configured";
            _isScanning = false;
            return;
        }

        _logger.LogInformation("Starting movies/TV scan. Folders: {Count}, Extensions: {Ext}",
            folders.Count, string.Join(", ", extensions));

        try
        {
            var videoFiles = new List<string>();
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                {
                    _logger.LogWarning("Movies/TV folder not found: {Folder}", folder);
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

            _currentProgress.TotalFiles = videoFiles.Count;
            _logger.LogInformation("Found {Count} video files to process", videoFiles.Count);

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

            // ── Phase 2: Metadata enrichment ──
            var metaCfg = _configService.Config.Metadata;
            if (metaCfg.FetchOnScan && metaCfg.Provider.ToLowerInvariant() != "none")
            {
                _currentProgress.Message = "Fetching metadata from external providers...";
                _logger.LogInformation("Starting metadata enrichment phase...");
                await _metadataService.EnrichLibraryAsync(_currentProgress);
            }

            _currentProgress.Status = "completed";
            _currentProgress.Message = $"Scan complete. {_currentProgress.NewVideos} new, {_currentProgress.UpdatedVideos} updated, {_currentProgress.ErrorCount} errors.";
            _logger.LogInformation(_currentProgress.Message);
        }
        catch (Exception ex)
        {
            _currentProgress.Status = "failed";
            _currentProgress.Message = ex.Message;
            _logger.LogError(ex, "Videos scan failed");
        }
        finally
        {
            _isScanning = false;
        }
    }

    private async Task ProcessVideoFileAsync(string filePath)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VideosDbContext>();

            var fileInfo = new FileInfo(filePath);
            var existing = await db.Videos.FirstOrDefaultAsync(v => v.FilePath == filePath);

            if (existing != null && existing.LastModified == fileInfo.LastWriteTimeUtc)
            {
                // File unchanged — generate thumbnail if missing
                if (string.IsNullOrEmpty(existing.ThumbnailPath))
                {
                    var thumb = await GenerateThumbnailAsync(filePath, existing.Id, existing.Duration);
                    if (thumb != null)
                    {
                        existing.ThumbnailPath = thumb;
                        db.Videos.Update(existing);
                        await db.SaveChangesAsync();
                    }
                }
                return;
            }

            // Parse metadata from filename and folder structure
            var parsed = ParseFilename(fileInfo.Name, filePath);
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var format = ext.TrimStart('.').ToUpperInvariant();

            // Probe video metadata with ffprobe
            double duration = 0;
            string resolution = "", codec = "", audioCodec = "";
            int width = 0, height = 0, videoBitrate = 0, audioChannels = 2;
            bool mp4Compliant = true, needsOptimization = false;
            string audioLanguages = "", subtitleLanguages = "";

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
                    videoBitrate = probe.Bitrate;
                    audioCodec = probe.AudioCodec;
                    audioChannels = probe.AudioChannels;
                    audioLanguages = probe.AudioLanguages;
                    subtitleLanguages = probe.SubtitleLanguages;
                    mp4Compliant = probe.Mp4Compliant;
                    needsOptimization = probe.NeedsOptimization;
                }
            }

            if (existing != null)
            {
                existing.FileName = fileInfo.Name;
                existing.Title = parsed.Title;
                existing.Year = parsed.Year;
                existing.Duration = duration;
                existing.SizeBytes = fileInfo.Length;
                existing.Format = format;
                existing.Resolution = resolution;
                existing.Width = width;
                existing.Height = height;
                existing.Codec = codec;
                existing.VideoBitrate = videoBitrate;
                existing.AudioCodec = audioCodec;
                existing.AudioChannels = audioChannels;
                existing.AudioLanguages = audioLanguages;
                existing.SubtitleLanguages = subtitleLanguages;
                existing.MediaType = parsed.MediaType;
                existing.SeriesName = parsed.SeriesName;
                existing.Season = parsed.Season;
                existing.Episode = parsed.Episode;
                existing.Mp4Compliant = mp4Compliant;
                existing.NeedsOptimization = needsOptimization;
                existing.LastModified = fileInfo.LastWriteTimeUtc;

                if (string.IsNullOrEmpty(existing.ThumbnailPath))
                {
                    var thumb = await GenerateThumbnailAsync(filePath, existing.Id, duration);
                    if (thumb != null) existing.ThumbnailPath = thumb;
                }

                db.Videos.Update(existing);
                await db.SaveChangesAsync();
                Interlocked.Increment(ref _currentProgress._updatedVideos);
            }
            else
            {
                var video = new Video
                {
                    FilePath = filePath,
                    FileName = fileInfo.Name,
                    Title = parsed.Title,
                    Year = parsed.Year,
                    Duration = duration,
                    SizeBytes = fileInfo.Length,
                    Format = format,
                    Resolution = resolution,
                    Width = width,
                    Height = height,
                    Codec = codec,
                    VideoBitrate = videoBitrate,
                    AudioCodec = audioCodec,
                    AudioChannels = audioChannels,
                    AudioLanguages = audioLanguages,
                    SubtitleLanguages = subtitleLanguages,
                    MediaType = parsed.MediaType,
                    SeriesName = parsed.SeriesName,
                    Season = parsed.Season,
                    Episode = parsed.Episode,
                    Mp4Compliant = mp4Compliant,
                    NeedsOptimization = needsOptimization,
                    DateAdded = DateTime.UtcNow,
                    LastModified = fileInfo.LastWriteTimeUtc
                };

                db.Videos.Add(video);
                await db.SaveChangesAsync();

                var thumb = await GenerateThumbnailAsync(filePath, video.Id, duration);
                if (thumb != null)
                {
                    video.ThumbnailPath = thumb;
                    db.Videos.Update(video);
                    await db.SaveChangesAsync();
                }

                Interlocked.Increment(ref _currentProgress._newVideos);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing video: {File}", filePath);
            Interlocked.Increment(ref _currentProgress._errorCount);
        }
    }

    public async Task<string?> GenerateThumbnailAsync(string filePath, int videoId, double duration)
    {
        if (!_ffmpegService.IsAvailable) return null;

        try
        {
            var thumbDir = Path.Combine(AppContext.BaseDirectory, "assets", "videothumbs");
            if (!Directory.Exists(thumbDir))
                Directory.CreateDirectory(thumbDir);

            var thumbFilename = $"vthumb_{videoId}.jpg";
            var thumbPath = Path.Combine(thumbDir, thumbFilename);

            if (File.Exists(thumbPath))
                return thumbFilename;

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

    public async Task GenerateAllThumbnailsAsync()
    {
        if (!_ffmpegService.IsAvailable)
        {
            _logger.LogWarning("FFmpeg not available, cannot generate thumbnails.");
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VideosDbContext>();
        var videos = await db.Videos
            .Where(v => v.ThumbnailPath == null || v.ThumbnailPath == "")
            .Select(v => new { v.Id, v.FilePath, v.Duration })
            .ToListAsync();

        _logger.LogInformation("Generating thumbnails for {Count} videos", videos.Count);

        foreach (var video in videos)
        {
            if (!File.Exists(video.FilePath)) continue;
            var thumb = await GenerateThumbnailAsync(video.FilePath, video.Id, video.Duration);
            if (thumb != null)
            {
                var entity = await db.Videos.FindAsync(video.Id);
                if (entity != null)
                {
                    entity.ThumbnailPath = thumb;
                    await db.SaveChangesAsync();
                }
            }
        }

        _logger.LogInformation("Thumbnail generation complete");
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
                    result.Bitrate = b / 1000;
            }

            // Parse streams
            var audioLangs = new List<string>();
            var subtitleLangs = new List<string>();

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
                    else if (codecType == "audio")
                    {
                        if (result.AudioCodec == "")
                        {
                            if (stream.TryGetProperty("codec_name", out var acn)) result.AudioCodec = acn.GetString() ?? "";
                            if (stream.TryGetProperty("channels", out var ch)) result.AudioChannels = ch.GetInt32();
                        }
                        // Extract language tag
                        var lang = GetStreamLanguage(stream);
                        if (!string.IsNullOrEmpty(lang) && !audioLangs.Contains(lang))
                            audioLangs.Add(lang);
                    }
                    else if (codecType == "subtitle")
                    {
                        var lang = GetStreamLanguage(stream);
                        if (!string.IsNullOrEmpty(lang) && !subtitleLangs.Contains(lang))
                            subtitleLangs.Add(lang);
                    }
                }
            }

            result.AudioLanguages = string.Join(",", audioLangs);
            result.SubtitleLanguages = string.Join(",", subtitleLangs);

            // Determine MP4 compliance
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".mp4" || ext == ".m4v")
            {
                result.Mp4Compliant = result.AudioChannels <= 2;
                result.NeedsOptimization = !result.Mp4Compliant;
            }
            else
            {
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

    private static string GetStreamLanguage(JsonElement stream)
    {
        if (stream.TryGetProperty("tags", out var tags))
        {
            if (tags.TryGetProperty("language", out var lang))
            {
                var l = lang.GetString() ?? "";
                return l != "und" ? l : "";
            }
        }
        return "";
    }

    /// <summary>
    /// Parse filename and folder structure to extract title, year, and TV episode info.
    /// Supports patterns: S01E01, S1E1, Season 1 Episode 1, 1x01
    /// </summary>
    internal static ParsedVideoInfo ParseFilename(string fileName, string filePath)
    {
        var nameOnly = Path.GetFileNameWithoutExtension(fileName);
        var fullName = nameOnly; // Keep original before year truncation
        var result = new ParsedVideoInfo();

        // Extract year: (YYYY), [YYYY], or standalone YYYY
        // Exclude false positives like 2160p, 1080p etc. by requiring non-digit or end after year
        var yearMatch = Regex.Match(nameOnly, @"[\(\[]?((?:19|20)\d{2})[\)\]]?(?![pPiI\d])");
        if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out var y) && y <= DateTime.Now.Year + 1)
        {
            result.Year = y;
        }

        // Detect TV episode patterns on the FULL filename (before year truncation)
        // Pattern 1: S01E01 or S1E1
        var tvMatch = Regex.Match(fullName, @"[Ss](\d{1,2})[Ee](\d{1,3})", RegexOptions.IgnoreCase);
        if (!tvMatch.Success)
        {
            // Pattern 2: Season 1 Episode 1 (with optional colon/dash after episode number)
            tvMatch = Regex.Match(fullName, @"Season\s*(\d{1,2})\s*Episode\s*(\d{1,3})", RegexOptions.IgnoreCase);
        }
        if (!tvMatch.Success)
        {
            // Pattern 3: 1x01 or 2x03
            tvMatch = Regex.Match(fullName, @"(?<!\d)(\d{1,2})x(\d{2,3})(?!\d)");
        }

        if (tvMatch.Success)
        {
            result.MediaType = "tv";
            result.Season = int.Parse(tvMatch.Groups[1].Value);
            result.Episode = int.Parse(tvMatch.Groups[2].Value);

            // Series name: text before the episode pattern, stripping year if present in that range
            var beforeEp = fullName.Substring(0, tvMatch.Index).Trim(' ', '-', '_', '.');
            // Remove year from series name portion (e.g., "Ballard (2025)" -> "Ballard")
            if (result.Year.HasValue)
            {
                beforeEp = Regex.Replace(beforeEp, @"[\(\[]?" + result.Year.Value + @"[\)\]]?", "")
                    .Trim(' ', '-', '_', '.');
            }
            result.SeriesName = CleanTitle(beforeEp);

            // Episode title: text after the episode pattern, with technical tags stripped
            var afterEp = fullName.Substring(tvMatch.Index + tvMatch.Length).Trim(' ', '-', '_', '.', ':');
            var cleanedAfterEp = CleanTitle(afterEp);
            result.Title = string.IsNullOrEmpty(cleanedAfterEp)
                ? $"{result.SeriesName} S{result.Season:D2}E{result.Episode:D2}"
                : cleanedAfterEp;

            // If series name is empty, try the parent folder name
            if (string.IsNullOrEmpty(result.SeriesName))
            {
                var parentDir = Path.GetFileName(Path.GetDirectoryName(filePath) ?? "");
                // Skip if parent is a Season folder
                if (!Regex.IsMatch(parentDir, @"^Season\s*\d", RegexOptions.IgnoreCase))
                    result.SeriesName = CleanTitle(parentDir);
                else
                {
                    // Go up one more level
                    var grandParent = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(filePath) ?? "") ?? "");
                    result.SeriesName = CleanTitle(grandParent);
                }
            }
        }
        else
        {
            // It's a movie - truncate at year position for title
            if (result.Year.HasValue && yearMatch.Success)
                nameOnly = nameOnly.Substring(0, yearMatch.Index).TrimEnd(' ', '-', '_', '.');
            result.MediaType = "movie";
            result.Title = CleanTitle(nameOnly);
        }

        return result;
    }

    /// <summary>
    /// Regex matching common technical tags in media filenames.
    /// Truncates everything from the first match onwards.
    /// Covers: resolution, source, video/audio codecs, HDR, release tags.
    /// </summary>
    private static readonly Regex TechnicalTagsPattern = new(
        @"(?<!\w)(" +
        // Resolution
        @"480[pi]|576[pi]|720[pi]|1080[pi]|2160[pi]|4K|UHD" +
        @"|" +
        // Source / rip type
        @"WEBRip|WEB[\.\-\s]?DL|WEB[\.\-\s]?Rip|BluRay|Blu[\.\-\s]?Ray|BDRip|BRRip|HDRip|DVDRip|HDTV|PDTV|SDTV|CAM|TELESYNC|TELECINE|R5|DVDSCR|SCR|PPV" +
        @"|" +
        // Video codec
        @"[xXhH][\.\s]?264|[xXhH][\.\s]?265|HEVC|AVC|XviD|DivX|VP9|AV1|MPEG[24]?" +
        @"|" +
        // Audio codec / format
        @"AAC\d?[\.\d]*|AC3|EAC3|E[\.\-]?AC[\.\-]?3|DTS[\.\-]?HD|DTS|DDP\d?[\.\d]*|FLAC|TrueHD|Atmos|LPCM" +
        @"|" +
        // HDR
        @"HDR10\+?|HDR|DoVi|Dolby[\.\s]?Vision|DV" +
        @"|" +
        // Release/quality tags
        @"REMUX|REPACK|PROPER|iNTERNAL|EXTENDED|UNRATED|DC|Directors[\.\s]?Cut|MULTI|DUAL|NF|AMZN|DSNP|HMAX|ATVP|APTV" +
        @")(?!\w)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Strip technical tags from a filename fragment, returning only the meaningful title part.
    /// </summary>
    private static string StripTechnicalTags(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        // Replace dots and underscores with spaces first so tags are word-bounded
        var spaced = name.Replace('.', ' ').Replace('_', ' ');
        var match = TechnicalTagsPattern.Match(spaced);
        if (match.Success)
            spaced = spaced.Substring(0, match.Index);
        // Also strip trailing release group: - or [ at the end
        spaced = Regex.Replace(spaced, @"\s*[\-\[].{0,20}$", "");
        return Regex.Replace(spaced, @"\s{2,}", " ").Trim();
    }

    /// <summary>
    /// Cleans a title by replacing dots/underscores with spaces, stripping technical tags, and trimming.
    /// </summary>
    private static string CleanTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var cleaned = StripTechnicalTags(raw);
        // Remove parenthesized/bracketed year tags like (2024) or [2025]
        cleaned = Regex.Replace(cleaned, @"[\(\[]\s*(?:19|20)\d{2}\s*[\)\]]", " ");
        // Remove orphaned trailing brackets/parens left after stripping
        cleaned = Regex.Replace(cleaned, @"[\(\[\)\]]+\s*$", "");
        return Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
    }

    internal class ParsedVideoInfo
    {
        public string Title { get; set; } = "";
        public int? Year { get; set; }
        public string MediaType { get; set; } = "movie";
        public string SeriesName { get; set; } = "";
        public int? Season { get; set; }
        public int? Episode { get; set; }
    }

    private class VideoProbeResult
    {
        public double Duration { get; set; }
        public string Resolution { get; set; } = "";
        public string Codec { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public int Bitrate { get; set; }
        public string AudioCodec { get; set; } = "";
        public int AudioChannels { get; set; } = 2;
        public string AudioLanguages { get; set; } = "";
        public string SubtitleLanguages { get; set; } = "";
        public bool Mp4Compliant { get; set; } = true;
        public bool NeedsOptimization { get; set; }
    }
}
