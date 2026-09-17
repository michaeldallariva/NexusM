using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexusM.Data;
using NexusM.Models;
using NexusM.Services;
using SharpCompress.Archives;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;

namespace NexusM.Controllers;

/// <summary>
/// REST API for music library operations.
/// All endpoints prefixed with /api/
/// Requires authentication when SecurityByPin is enabled.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public class NexusMApiController : ControllerBase
{
    // The eleven DbContexts are resolved lazily from the request scope. A typical endpoint
    // touches one or two of them, and constructing all of them for every request (441
    // endpoints) was pure overhead. Same instances per request as before: the scope owns
    // and disposes them.
    private readonly IServiceProvider _services;
    private T Ctx<T>(ref T? slot) where T : class => slot ??= _services.GetRequiredService<T>();
    private MusicDbContext? _dbL;
    private MusicDbContext _db => Ctx(ref _dbL);
    private PicturesDbContext? _picDbL;
    private PicturesDbContext _picDb => Ctx(ref _picDbL);
    private EBooksDbContext? _ebookDbL;
    private EBooksDbContext _ebookDb => Ctx(ref _ebookDbL);
    private MusicVideosDbContext? _mvDbL;
    private MusicVideosDbContext _mvDb => Ctx(ref _mvDbL);
    private VideosDbContext? _videoDbL;
    private VideosDbContext _videoDb => Ctx(ref _videoDbL);
    private UsersDbContext? _usersDbL;
    private UsersDbContext _usersDb => Ctx(ref _usersDbL);
    private ActorsDbContext? _actorsDbL;
    private ActorsDbContext _actorsDb => Ctx(ref _actorsDbL);
    private TvChannelsDbContext? _tvDbL;
    private TvChannelsDbContext _tvDb => Ctx(ref _tvDbL);
    private PodcastsDbContext? _podcastDbL;
    private PodcastsDbContext _podcastDb => Ctx(ref _podcastDbL);
    private AudioBooksDbContext? _audioBooksDbL;
    private AudioBooksDbContext _audioBooksDb => Ctx(ref _audioBooksDbL);

    // Factory-backed HttpClients share pooled, rotated handlers (no socket exhaustion, fresh
    // DNS); disposing the wrapper is harmless. Timeout is per wrapper.
    private readonly IHttpClientFactory _httpFactory;
    private HttpClient Http(int timeoutSeconds)
    {
        var c = _httpFactory.CreateClient();
        c.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        return c;
    }
    private readonly LibraryScannerService _scanner;
    private readonly PictureScannerService _picScanner;
    private readonly EBookScannerService _ebookScanner;
    private readonly MusicVideoScannerService _mvScanner;
    private readonly VideoScannerService _videoScanner;
    private readonly AnimeScannerService _animeScanner;
    private readonly FFmpegService _ffmpeg;
    private readonly TranscodingService _transcoding;
    private readonly TranscodeHistoryService _transcodeHistory;
    private readonly GpuDetectionService _gpuDetection;
    private readonly RadioService _radio;
    private readonly TvChannelService _tvService;
    private readonly PodcastService _podcastSvc;
    private readonly AudioBookScannerService _audioBooksScanner;
    private readonly UserFavouritesService _userFavs;
    private readonly UserStatsService _userStats;
    private readonly MetadataService _metadata;
    private readonly NfoService _nfo;
    private readonly ArtistImageService _artistImageService;
    private readonly ShareCredentialService _shareService;
    private readonly SubtitleService _subtitles;
    private readonly ConfigService _config;
    private readonly SemanticSearchService _semantic;
    private readonly IWebHostEnvironment _env;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NexusMApiController> _logger;
    private readonly NewReleasesService _newReleases;
    private readonly WatchmodeService   _watchmode;
    private readonly TraktService       _trakt;
    private readonly ScrobblingService  _scrobbling;
    private readonly StreamingSessionService _streaming;
    private readonly VideoThumbnailService _thumbnails;
    private readonly VideoPreviewService  _vpreviews;
    private readonly DynamicCleanService _dynamicClean;
    private readonly PictureGeoService  _pictureGeo;
    private readonly OfflineGeoService  _offlineGeo;
    private readonly CloudflareTunnelService _cfTunnel;
    private readonly NexusMRelayService _relay;
    private readonly AutoUpdateService  _autoUpdate;
    private readonly EpgService         _epgService;
    private readonly CastService        _cast;
    private readonly IHostApplicationLifetime _appLifetime;

    // One remux per output file - concurrent range requests wait and reuse the result.
    // Reference-counted per cache key: the entry vanishes when the last waiter leaves, so
    // this no longer keeps one semaphore per ever-remuxed file for the life of the process.
    private static readonly KeyedAsyncLock _remuxLocks = new(StringComparer.OrdinalIgnoreCase);

    // One in-flight blocking remux per cache key, so a request that leaves can decide whether the
    // FFmpeg run it started is still wanted by someone else (see RunSharedRemuxAsync).
    private sealed class RemuxJob
    {
        public readonly CancellationTokenSource Cts = new();
        public volatile bool HolderGone;
    }
    private static readonly ConcurrentDictionary<string, RemuxJob> _activeRemuxes = new(StringComparer.OrdinalIgnoreCase);

    // Grace before an abandoned remux is killed. Android ExoPlayer routinely aborts its first
    // request and immediately re-issues it with a Range header; that retry must find the remux
    // still running instead of restarting it from scratch.
    private static readonly TimeSpan RemuxAbandonGrace = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Blocking remux of one source file into <paramref name="cachePath"/>, shared by every request
    /// for the same <paramref name="cacheKey"/>: one FFmpeg per output file, concurrent requests wait
    /// on the key lock and are served from the cache afterwards. Cancellation follows the CLIENT:
    /// a request that disconnects while waiting simply leaves; a request that disconnects while its
    /// remux runs kills FFmpeg after a short grace period, but only if nobody else is waiting for
    /// the same file. Returns true when the cache file is ready, false when the remux failed, and
    /// null when the calling request's client is gone (the caller should return an empty result).
    /// </summary>
    private async Task<bool?> RunSharedRemuxAsync(string cacheKey, string cachePath, string inputPath,
        int audioChannels, int audioTrack, string what)
    {
        var aborted = HttpContext.RequestAborted;
        IDisposable remuxLock;
        try { remuxLock = await _remuxLocks.LockAsync(cacheKey, aborted); }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Client left while waiting for the remux of {What}", what);
            ScheduleRemuxAbandonCheck(cacheKey);   // the holder may already be gone too
            return null;
        }

        using (remuxLock)
        {
            // Re-check after acquiring the lock - a concurrent request may have just finished.
            if (System.IO.File.Exists(cachePath))
            {
                _logger.LogDebug("Remux cache ready for {What} (waited for concurrent remux)", what);
                return true;
            }
            if (aborted.IsCancellationRequested) return null;

            _logger.LogInformation("Remuxing {What}: {File} (audio track {Track})", what, inputPath, audioTrack);
            var job = new RemuxJob();
            _activeRemuxes[cacheKey] = job;
            try
            {
                using var abortReg = aborted.Register(() =>
                {
                    job.HolderGone = true;
                    ScheduleRemuxAbandonCheck(cacheKey);
                });
                var success = audioChannels > 2
                    ? await _transcoding!.RemuxStereoDownmixAsync(inputPath, cachePath, audioTrack, job.Cts.Token)
                    : await _transcoding!.RemuxFaststartAsync(inputPath, cachePath, audioTrack, job.Cts.Token);

                if (job.Cts.IsCancellationRequested) return null;
                if (success && System.IO.File.Exists(cachePath))
                {
                    _logger.LogInformation("Remux successful for {What}, cached at {Path}", what, cachePath);
                    return true;
                }
                _logger.LogWarning("Remux failed for {What}", what);
                return false;
            }
            finally
            {
                _activeRemuxes.TryRemove(new KeyValuePair<string, RemuxJob>(cacheKey, job));
                job.Cts.Dispose();
            }
        }
    }

    // Runs after the grace period: kill the in-flight remux for this key only when the request that
    // started it has gone AND nobody else holds or waits for the key.
    private void ScheduleRemuxAbandonCheck(string cacheKey)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(RemuxAbandonGrace);
                if (!_activeRemuxes.TryGetValue(cacheKey, out var job) || !job.HolderGone) return;
                if (_remuxLocks.Interest(cacheKey) > 1) return;   // someone else still wants this file
                _logger.LogInformation("Client left and nobody else is waiting - cancelling remux {Key}", cacheKey);
                job.Cts.Cancel();
            }
            catch (ObjectDisposedException) { /* remux finished in the meantime */ }
            catch (Exception ex) { _logger.LogDebug(ex, "Remux abandon check failed for {Key}", cacheKey); }
        });
    }


    public NexusMApiController(
        IServiceProvider services,
        IHttpClientFactory httpFactory,
        PodcastService podcastSvc,
        AudioBookScannerService audioBooksScanner,
        LibraryScannerService scanner,
        PictureScannerService picScanner,
        EBookScannerService ebookScanner,
        MusicVideoScannerService mvScanner,
        VideoScannerService videoScanner,
        AnimeScannerService animeScanner,
        FFmpegService ffmpeg,
        TranscodingService transcoding,
        TranscodeHistoryService transcodeHistory,
        GpuDetectionService gpuDetection,
        RadioService radio,
        TvChannelService tvService,
        UserFavouritesService userFavs,
        UserStatsService userStats,
        MetadataService metadata,
        NfoService nfo,
        ArtistImageService artistImageService,
        ShareCredentialService shareService,
        SubtitleService subtitles,
        ConfigService config,
        SemanticSearchService semantic,
        IWebHostEnvironment env,
        IServiceScopeFactory scopeFactory,
        NewReleasesService newReleases,
        WatchmodeService   watchmode,
        TraktService trakt,
        ScrobblingService scrobbling,
        StreamingSessionService streaming,
        VideoThumbnailService thumbnails,
        VideoPreviewService vpreviews,
        DynamicCleanService dynamicClean,
        PictureGeoService pictureGeo,
        OfflineGeoService offlineGeo,
        CloudflareTunnelService cfTunnel,
        NexusMRelayService relay,
        AutoUpdateService autoUpdate,
        EpgService epgService,
        CastService cast,
        IHostApplicationLifetime appLifetime,
        ILogger<NexusMApiController> logger)
    {
        _services = services;
        _httpFactory = httpFactory;
        _podcastSvc = podcastSvc;
        _audioBooksScanner = audioBooksScanner;
        _scanner = scanner;
        _picScanner = picScanner;
        _ebookScanner = ebookScanner;
        _mvScanner = mvScanner;
        _videoScanner = videoScanner;
        _animeScanner = animeScanner;
        _ffmpeg = ffmpeg;
        _transcoding = transcoding;
        _transcodeHistory = transcodeHistory;
        _gpuDetection = gpuDetection;
        _radio = radio;
        _tvService = tvService;
        _userFavs = userFavs;
        _userStats = userStats;
        _metadata = metadata;
        _nfo = nfo;
        _artistImageService = artistImageService;
        _shareService = shareService;
        _subtitles = subtitles;
        _config = config;
        _semantic = semantic;
        _env = env;
        _scopeFactory = scopeFactory;
        _newReleases = newReleases;
        _watchmode   = watchmode;
        _trakt       = trakt;
        _scrobbling  = scrobbling;
        _streaming   = streaming;
        _thumbnails     = thumbnails;
        _vpreviews      = vpreviews;
        _dynamicClean   = dynamicClean;
        _pictureGeo     = pictureGeo;
        _offlineGeo     = offlineGeo;
        _cfTunnel       = cfTunnel;
        _relay          = relay;
        _autoUpdate     = autoUpdate;
        _epgService     = epgService;
        _cast           = cast;
        _appLifetime    = appLifetime;
        _logger = logger;
    }

    private string CurrentUsername => User.Identity?.Name ?? "unknown";

    // Resolve the artist-grouping mode for a single request. The Music page offers a
    // per-view grouping dropdown (Filtre et Vue) so a user can fold compilations without
    // touching the global Settings value; when it sends ?grouping=<mode> that overrides the
    // configured default for this request only. An unrecognised/absent value falls back to
    // the global Library.ArtistGrouping. ByAlbumArtist covers both the browse-time
    // ("albumartist") and indexed ("albumartist-indexed") modes - they group identically at
    // query time; only the scanner differs. ByPrimary merges collab credits (see
    // PrimaryArtistKey). See LibraryConfig.ArtistGrouping.
    private (bool ByAlbumArtist, bool ByPrimary) ResolveArtistGrouping(string? overrideMode)
    {
        var mode = overrideMode?.Trim().ToLowerInvariant();
        if (mode is not ("artist" or "primary" or "albumartist" or "albumartist-indexed"))
            mode = _config.Config.Library.ArtistGrouping;
        return (mode is "albumartist" or "albumartist-indexed", mode == "primary");
    }

    // Collaboration separators. FEAT-type unambiguously mark a secondary/featured artist;
    // AMP-type are ambiguous (credited collab vs. a genuine band name).
    private static readonly string[] _featSeps = { " feat. ", " feat ", " ft. ", " ft ", " featuring ", " with " };
    private static readonly string[] _ampSeps = { " & ", " x ", " vs. ", " vs ", ", " };

    // The leading part before the first of the given separators, or null if none is present.
    private static string? LeadingBefore(string artist, string[] seps)
    {
        var lower = artist.ToLowerInvariant();
        int best = -1;
        foreach (var s in seps)
        {
            int idx = lower.IndexOf(s, StringComparison.Ordinal);
            if (idx > 0 && (best < 0 || idx < best)) best = idx;
        }
        return best > 0 ? artist.Substring(0, best).Trim() : null;
    }

    private static bool ArtistHasCollab(string artist)
        => LeadingBefore(artist, _featSeps) != null || LeadingBefore(artist, _ampSeps) != null;

    // Map a raw ARTIST credit to its primary artist for the "primary" grouping mode.
    private static string PrimaryArtistKey(string artist, HashSet<string> standalone)
    {
        var trimmed = (artist ?? "").Trim();
        var feat = LeadingBefore(trimmed, _featSeps);
        if (!string.IsNullOrEmpty(feat)) return feat;                       // "X feat. Y" → X (always)
        var amp = LeadingBefore(trimmed, _ampSeps);
        if (!string.IsNullOrEmpty(amp) && standalone.Contains(amp)) return amp;  // "A & B" → A only if A is standalone
        return trimmed;
    }

    // Artist strings that appear WITHOUT any collaboration separator - used to decide whether
    // an ambiguous "A & B" credit should collapse to "A".
    private async Task<HashSet<string>> GetStandaloneArtistSetAsync()
    {
        var names = await _db.Tracks.Select(t => t.Artist).Distinct().ToListAsync();
        return new HashSet<string>(
            names.Select(a => (a ?? "").Trim()).Where(a => a.Length > 0 && !ArtistHasCollab(a)),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Content ratings considered safe for child users.</summary>
    private static readonly HashSet<string> ChildSafeRatings = new(StringComparer.OrdinalIgnoreCase)
        { "G", "PG", "PG-12", "PG-13", "TV-G", "TV-PG", "TV-Y", "TV-Y7", "TV-14", "U", "K", "ALL" };

    // ─── Dashboard / Stats ──────────────────────────────────────────

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var stats = new
        {
            totalTracks = await _db.Tracks.CountAsync(),
            totalAlbums = await _db.Albums.CountAsync(),
            totalArtists = await _db.Artists.CountAsync(),
            totalPlaylists = _userFavs.GetPlaylistCount(CurrentUsername),
            totalDuration = await _db.Tracks.SumAsync(t => t.Duration),
            totalSize = await _db.Tracks.SumAsync(t => t.FileSize),
            favouriteTracks = _userFavs.GetFavouriteIds(CurrentUsername, "track").Count,
            recentlyAdded = await _db.Tracks.CountAsync(t => t.DateAdded > DateTime.UtcNow.AddDays(-7)),
            genres = (await _db.Tracks.Where(t => t.Genre != "").Select(t => t.Genre).ToListAsync())
                .SelectMany(g => g.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count()
        };
        return Ok(stats);
    }

    [HttpGet("analysis")]
    public async Task<IActionResult> GetAnalysis()
    {
        // ── Music analysis ──
        var musicFormats = await _db.Tracks
            .Where(t => t.Codec != "")
            .GroupBy(t => t.Codec)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count).Take(10).ToListAsync();

        var rawGenreStrings = await _db.Tracks.Where(t => t.Genre != "").Select(t => t.Genre).ToListAsync();
        var genreTally = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in rawGenreStrings)
            foreach (var g in entry.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                genreTally[g] = genreTally.GetValueOrDefault(g, 0) + 1;
        var musicGenres = genreTally
            .Select(kv => new { name = kv.Key, count = kv.Value })
            .OrderByDescending(g => g.count).Take(10).ToList();

        var topArtists = await _db.Tracks
            .Where(t => t.Artist != "")
            .GroupBy(t => t.Artist)
            .Select(g => new { name = g.Key, count = g.Count(), size = g.Sum(t => t.FileSize) })
            .OrderByDescending(g => g.count).Take(10).ToListAsync();

        var musicBitrates = await _db.Tracks
            .Where(t => t.Bitrate > 0)
            .GroupBy(t => t.Bitrate <= 128 ? "≤128 kbps" :
                          t.Bitrate <= 192 ? "129-192 kbps" :
                          t.Bitrate <= 256 ? "193-256 kbps" :
                          t.Bitrate <= 320 ? "257-320 kbps" : ">320 kbps (Lossless)")
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count).ToListAsync();

        var musicSampleRates = await _db.Tracks
            .Where(t => t.SampleRate > 0)
            .GroupBy(t => t.SampleRate)
            .Select(g => new { name = g.Key + " Hz", count = g.Count() })
            .OrderByDescending(g => g.count).Take(5).ToListAsync();

        // ── Video (Movies/TV) analysis ──
        var videoResolutions = await _videoDb.Videos
            .GroupBy(v => v.Height >= 2160 ? "4K UHD" :
                          v.Height >= 1080 ? "1080p FHD" :
                          v.Height >= 720 ? "720p HD" :
                          v.Height > 0 ? "< 720p" : "Unknown")
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count).ToListAsync();

        var videoFormats = await _videoDb.Videos
            .Where(v => v.Format != "")
            .GroupBy(v => v.Format)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count).Take(10).ToListAsync();

        var videoCodecs = await _videoDb.Videos
            .Where(v => v.Codec != "")
            .GroupBy(v => v.Codec)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count).Take(10).ToListAsync();

        var videoGenres = await _videoDb.Videos
            .Where(v => v.Genre != "")
            .GroupBy(v => v.Genre)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count).Take(10).ToListAsync();

        var videoHdrFormats = await _videoDb.Videos
            .Where(v => v.HdrFormat != "")
            .GroupBy(v => v.HdrFormat)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count).ToListAsync();

        // ── Music Videos analysis ──
        var mvResolutions = await _mvDb.MusicVideos
            .GroupBy(v => v.Height >= 2160 ? "4K UHD" :
                          v.Height >= 1080 ? "1080p FHD" :
                          v.Height >= 720 ? "720p HD" :
                          v.Height > 0 ? "< 720p" : "Unknown")
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count).ToListAsync();

        var mvFormats = await _mvDb.MusicVideos
            .Where(v => v.Format != "")
            .GroupBy(v => v.Format)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count).Take(10).ToListAsync();

        var mvTopArtists = await _mvDb.MusicVideos
            .Where(v => v.Artist != "")
            .GroupBy(v => v.Artist)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count).Take(10).ToListAsync();

        // ── Pictures analysis ──
        var picFormats = await _picDb.Pictures
            .Where(p => p.Format != "")
            .GroupBy(p => p.Format)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count).Take(10).ToListAsync();

        var picCategories = await _picDb.Pictures
            .Where(p => p.Category != "")
            .GroupBy(p => p.Category)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count).Take(10).ToListAsync();

        var picResolutions = await _picDb.Pictures
            .GroupBy(p => p.Width >= 3840 ? "4K+" :
                          p.Width >= 1920 ? "Full HD" :
                          p.Width >= 1280 ? "HD" :
                          p.Width > 0 ? "< HD" : "Unknown")
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count).ToListAsync();

        // ── eBooks analysis ──
        var ebookFormats = await _ebookDb.EBooks
            .GroupBy(e => e.Format)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count).ToListAsync();

        var ebookTopAuthors = await _ebookDb.EBooks
            .Where(e => e.Author != "")
            .GroupBy(e => e.Author)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count).Take(10).ToListAsync();

        var ebookCategories = await _ebookDb.EBooks
            .Where(e => e.Category != "")
            .GroupBy(e => e.Category)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count).Take(10).ToListAsync();

        // ── Totals ──
        var totalTracks = await _db.Tracks.CountAsync();
        var totalMusicSize = await _db.Tracks.SumAsync(t => t.FileSize);
        var totalMusicDuration = await _db.Tracks.SumAsync(t => t.Duration);
        var totalAlbums = await _db.Albums.CountAsync();
        var totalArtists = await _db.Artists.CountAsync();
        var totalVideos = await _videoDb.Videos.CountAsync();
        var totalVideoSize = await _videoDb.Videos.SumAsync(v => v.SizeBytes);
        var totalVideoDuration = await _videoDb.Videos.SumAsync(v => v.Duration);
        var totalMovies = await _videoDb.Videos.CountAsync(v => v.MediaType == "movie");
        var totalTvEpisodes = await _videoDb.Videos.CountAsync(v => v.MediaType == "tv");
        var totalMv = await _mvDb.MusicVideos.CountAsync();
        var totalMvSize = await _mvDb.MusicVideos.SumAsync(v => v.SizeBytes);
        var totalMvDuration = await _mvDb.MusicVideos.SumAsync(v => v.Duration);
        var totalPictures = await _picDb.Pictures.CountAsync();
        var totalPicSize = await _picDb.Pictures.SumAsync(p => p.SizeBytes);
        var totalEbooks = await _ebookDb.EBooks.CountAsync();
        var totalEbookSize = await _ebookDb.EBooks.SumAsync(e => e.FileSize);
        var videoNeedsOpt = await _videoDb.Videos.CountAsync(v => v.NeedsOptimization);
        var mvNeedsOpt = await _mvDb.MusicVideos.CountAsync(v => v.NeedsOptimization);
        var totalHdVideos = await _videoDb.Videos.CountAsync(v => v.Height >= 720);
        var totalHdMv = await _mvDb.MusicVideos.CountAsync(v => v.Height >= 720);
        var totalHdrVideos = await _videoDb.Videos.CountAsync(v => v.HdrFormat != "");
        var totalDolbyVisionVideos = await _videoDb.Videos.CountAsync(v => v.HdrFormat == "Dolby Vision");

        return Ok(new
        {
            totals = new {
                totalItems = totalTracks + totalVideos + totalMv + totalPictures + totalEbooks,
                totalSize = totalMusicSize + totalVideoSize + totalMvSize + totalPicSize + totalEbookSize,
                totalTracks, totalMusicSize, totalMusicDuration, totalAlbums, totalArtists,
                totalVideos, totalVideoSize, totalVideoDuration, totalMovies, totalTvEpisodes,
                totalMv, totalMvSize, totalMvDuration,
                totalPictures, totalPicSize,
                totalEbooks, totalEbookSize,
                videoNeedsOpt, mvNeedsOpt,
                hdRatioVideos = totalVideos > 0 ? Math.Round(totalHdVideos * 100.0 / totalVideos) : 0,
                hdRatioMv = totalMv > 0 ? Math.Round(totalHdMv * 100.0 / totalMv) : 0,
                totalHdrVideos, totalDolbyVisionVideos
            },
            music = new { formats = musicFormats, genres = musicGenres, topArtists, bitrates = musicBitrates, sampleRates = musicSampleRates },
            videos = new { resolutions = videoResolutions, formats = videoFormats, codecs = videoCodecs, genres = videoGenres, hdrFormats = videoHdrFormats },
            musicVideos = new { resolutions = mvResolutions, formats = mvFormats, topArtists = mvTopArtists },
            pictures = new { formats = picFormats, categories = picCategories, resolutions = picResolutions },
            ebooks = new { formats = ebookFormats, topAuthors = ebookTopAuthors, categories = ebookCategories }
        });
    }

    // ─── Deep Media Analysis ──────────────────────────────────────────────────

    [HttpGet("analysis/deep/status")]
    public async Task<IActionResult> GetDeepAnalysisStatus()
    {
        var total    = await _videoDb.Videos.CountAsync();
        var analyzed = await _videoDb.Videos.CountAsync(v => v.DeepAnalysisDone);
        return Ok(new
        {
            total,
            analyzed,
            pending   = total - analyzed,
            isRunning = MediaAnalysisService.IsRunning,
            lastScanCompleted = MediaAnalysisService.LastScanCompleted,
            enabled   = _config.Config.Analysis.DeepScanEnabled
        });
    }

    [HttpPost("analysis/deep/trigger")]
    public IActionResult TriggerDeepAnalysis()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
            return Forbid();
        MediaAnalysisService.TriggerScan();
        return Ok(new { success = true, message = "Deep analysis scan triggered." });
    }

    [HttpPost("analysis/deep/reset")]
    public async Task<IActionResult> ResetDeepAnalysis()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
            return Forbid();
        await _videoDb.Database.ExecuteSqlRawAsync(
            "UPDATE Videos SET DeepAnalysisDone=0, AudioTracks=NULL, SubtitleTracks=NULL, VideoProfile=NULL, ColorSpace=NULL, TotalBitrateKbps=NULL");
        MediaAnalysisService.TriggerScan();
        return Ok(new { success = true, message = "Deep analysis reset. Re-scan triggered." });
    }

    [HttpGet("analysis/duplicates")]
    public async Task<IActionResult> GetDuplicates()
    {
        static string FmtDur(double secs) {
            var ts = TimeSpan.FromSeconds(secs);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m"
                : $"{ts.Minutes}:{ts.Seconds:D2}";
        }
        // ── Music tracks ──────────────────────────────────────────────────────
        var tracks = await _db.Tracks
            .Select(t => new { t.Id, t.Title, t.Artist, t.Album, t.FilePath, t.FileSize, t.Codec, t.Bitrate, t.Duration })
            .ToListAsync();
        var musicDups = tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Title))
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Artist)
                ? t.Title.Trim().ToLowerInvariant()
                : $"{t.Title.Trim().ToLowerInvariant()}|{t.Artist.Trim().ToLowerInvariant()}")
            .Where(g => g.Count() > 1)
            .Select(g => new {
                label = string.IsNullOrWhiteSpace(g.First().Artist) ? g.First().Title : $"{g.First().Title} - {g.First().Artist}",
                count = g.Count(),
                items = g.OrderByDescending(t => t.FileSize).Select(t => new {
                    t.Id, t.FilePath, fileSize = t.FileSize,
                    detail = $"{t.Codec} {t.Bitrate}kbps, {FmtDur(t.Duration)}{(string.IsNullOrWhiteSpace(t.Album) ? "" : $" • {t.Album}")}"
                }).ToList()
            })
            .OrderByDescending(g => g.count).ThenBy(g => g.label).ToList();

        // ── Videos (standalone movies / docs / anime films) ───────────────────
        var videos = await _videoDb.Videos
            .Select(v => new { v.Id, v.Title, v.SeriesName, v.Season, v.Episode, v.MediaType, v.FilePath, v.SizeBytes, v.Format, v.Resolution, v.Year })
            .ToListAsync();
        var standaloneDups = videos
            .Where(v => !string.IsNullOrWhiteSpace(v.Title) && (v.Episode == null || v.Episode == 0))
            .GroupBy(v => v.Title.Trim().ToLowerInvariant())
            .Where(g => g.Count() > 1)
            .Select(g => new {
                label = g.First().Year > 0 ? $"{g.First().Title} ({g.First().Year})" : g.First().Title,
                mediaType = g.First().MediaType,
                count = g.Count(),
                items = g.OrderByDescending(v => v.SizeBytes).Select(v => new {
                    v.Id, v.FilePath, fileSize = v.SizeBytes,
                    detail = $"{v.Format} {v.Resolution}".Trim()
                }).ToList()
            })
            .OrderByDescending(g => g.count).ThenBy(g => g.label).ToList();
        var tvEpisodeDups = videos
            .Where(v => !string.IsNullOrWhiteSpace(v.SeriesName) && v.Season > 0 && v.Episode > 0)
            .GroupBy(v => $"{v.SeriesName.Trim().ToLowerInvariant()}|s{v.Season:D2}e{v.Episode:D2}")
            .Where(g => g.Count() > 1)
            .Select(g => new {
                label = $"{g.First().SeriesName} S{g.First().Season:D2}E{g.First().Episode:D2}",
                count = g.Count(),
                items = g.OrderByDescending(v => v.SizeBytes).Select(v => new {
                    v.Id, v.FilePath, fileSize = v.SizeBytes,
                    detail = $"{v.Format} {v.Resolution}".Trim()
                }).ToList()
            })
            .OrderByDescending(g => g.count).ThenBy(g => g.label).ToList();

        // ── Music Videos ──────────────────────────────────────────────────────
        var mvs = await _mvDb.MusicVideos
            .Select(v => new { v.Id, v.Title, v.Artist, v.FilePath, v.SizeBytes, v.Format, v.Resolution })
            .ToListAsync();
        var mvDups = mvs
            .Where(v => !string.IsNullOrWhiteSpace(v.Title))
            .GroupBy(v => string.IsNullOrWhiteSpace(v.Artist)
                ? v.Title.Trim().ToLowerInvariant()
                : $"{v.Title.Trim().ToLowerInvariant()}|{v.Artist.Trim().ToLowerInvariant()}")
            .Where(g => g.Count() > 1)
            .Select(g => new {
                label = string.IsNullOrWhiteSpace(g.First().Artist) ? g.First().Title : $"{g.First().Title} - {g.First().Artist}",
                count = g.Count(),
                items = g.OrderByDescending(v => v.SizeBytes).Select(v => new {
                    v.Id, v.FilePath, fileSize = v.SizeBytes,
                    detail = $"{v.Format} {v.Resolution}".Trim()
                }).ToList()
            })
            .OrderByDescending(g => g.count).ThenBy(g => g.label).ToList();

        // ── eBooks ────────────────────────────────────────────────────────────
        var ebooks = await _ebookDb.EBooks
            .Select(e => new { e.Id, e.Title, e.Author, e.FilePath, e.FileSize, e.Format, e.PageCount })
            .ToListAsync();
        var ebookDups = ebooks
            .Where(e => !string.IsNullOrWhiteSpace(e.Title))
            .GroupBy(e => string.IsNullOrWhiteSpace(e.Author)
                ? e.Title.Trim().ToLowerInvariant()
                : $"{e.Title.Trim().ToLowerInvariant()}|{e.Author.Trim().ToLowerInvariant()}")
            .Where(g => g.Count() > 1)
            .Select(g => new {
                label = string.IsNullOrWhiteSpace(g.First().Author) ? g.First().Title : $"{g.First().Title} - {g.First().Author}",
                count = g.Count(),
                items = g.OrderByDescending(e => e.FileSize).Select(e => new {
                    e.Id, e.FilePath, fileSize = e.FileSize,
                    detail = e.PageCount > 0 ? $"{e.Format}, {e.PageCount} pages" : e.Format
                }).ToList()
            })
            .OrderByDescending(g => g.count).ThenBy(g => g.label).ToList();

        // ── Audio Books ───────────────────────────────────────────────────────
        var audiobooks = await _audioBooksDb.AudioBooks
            .Select(a => new { a.Id, a.Title, a.Author, a.FilePath, a.FileSize, a.Format, a.Duration })
            .ToListAsync();
        var audiobookDups = audiobooks
            .Where(a => !string.IsNullOrWhiteSpace(a.Title))
            .GroupBy(a => string.IsNullOrWhiteSpace(a.Author)
                ? a.Title.Trim().ToLowerInvariant()
                : $"{a.Title.Trim().ToLowerInvariant()}|{a.Author.Trim().ToLowerInvariant()}")
            .Where(g => g.Count() > 1)
            .Select(g => new {
                label = string.IsNullOrWhiteSpace(g.First().Author) ? g.First().Title : $"{g.First().Title} - {g.First().Author}",
                count = g.Count(),
                items = g.OrderByDescending(a => a.FileSize).Select(a => new {
                    a.Id, a.FilePath, fileSize = a.FileSize,
                    detail = $"{a.Format} {FmtDur(a.Duration)}".Trim()
                }).ToList()
            })
            .OrderByDescending(g => g.count).ThenBy(g => g.label).ToList();

        // ── Summary ───────────────────────────────────────────────────────────
        static long WastedBytes(IEnumerable<long> sizes) { var s = sizes.OrderByDescending(x => x).ToList(); return s.Count > 1 ? s.Skip(1).Sum() : 0; }
        var totalGroups = musicDups.Count + standaloneDups.Count + tvEpisodeDups.Count + mvDups.Count + ebookDups.Count + audiobookDups.Count;
        var totalDuplicateFiles = musicDups.Sum(g => g.count) + standaloneDups.Sum(g => g.count) + tvEpisodeDups.Sum(g => g.count) + mvDups.Sum(g => g.count) + ebookDups.Sum(g => g.count) + audiobookDups.Sum(g => g.count);
        var totalWastedBytes =
            musicDups.Sum(g => WastedBytes(g.items.Select(i => i.fileSize))) +
            standaloneDups.Sum(g => WastedBytes(g.items.Select(i => i.fileSize))) +
            tvEpisodeDups.Sum(g => WastedBytes(g.items.Select(i => i.fileSize))) +
            mvDups.Sum(g => WastedBytes(g.items.Select(i => i.fileSize))) +
            ebookDups.Sum(g => WastedBytes(g.items.Select(i => i.fileSize))) +
            audiobookDups.Sum(g => WastedBytes(g.items.Select(i => i.fileSize)));

        return Ok(new {
            totalGroups, totalDuplicateFiles, totalWastedBytes,
            music = musicDups,
            videos = standaloneDups,
            tvEpisodes = tvEpisodeDups,
            musicVideos = mvDups,
            ebooks = ebookDups,
            audiobooks = audiobookDups
        });
    }

    /// <summary>
    /// Analysis → Transcoding: last 10 transcode/remux operations with performance samples
    /// (CPU / memory / GPU), duration, hardware names, and output cache details.
    /// Entries are enriched with media titles from the videos / music-videos databases,
    /// and a live check of whether the cached output still exists on disk.
    /// </summary>
    [HttpGet("analysis/transcoding")]
    public async Task<IActionResult> GetTranscodingHistory()
    {
        var entries = _transcodeHistory.GetHistory();

        var vidIds = entries.Where(e => e.Kind == "video" && e.MediaId > 0).Select(e => e.MediaId).Distinct().ToList();
        var mvIds  = entries.Where(e => e.Kind == "musicvideo" && e.MediaId > 0).Select(e => e.MediaId).Distinct().ToList();

        var vids = vidIds.Count > 0
            ? await _videoDb.Videos.Where(v => vidIds.Contains(v.Id))
                .Select(v => new { v.Id, v.Title, v.MediaType, v.SeriesName, v.Season, v.Episode })
                .ToDictionaryAsync(v => v.Id)
            : null;
        var mvs = mvIds.Count > 0
            ? await _mvDb.MusicVideos.Where(v => mvIds.Contains(v.Id))
                .Select(v => new { v.Id, v.Title, v.Artist })
                .ToDictionaryAsync(v => v.Id)
            : null;

        var result = entries.Select(e =>
        {
            string title = e.FileName;
            string mediaType = e.Kind == "musicvideo" ? "musicvideo" : "video";
            if (e.Kind == "video" && vids != null && vids.TryGetValue(e.MediaId, out var v))
            {
                mediaType = v.MediaType;
                title = !string.IsNullOrEmpty(v.SeriesName) && v.Season.HasValue && v.Episode.HasValue
                    ? $"{v.SeriesName} S{v.Season:D2}E{v.Episode:D2} - {v.Title}"
                    : v.Title;
            }
            else if (e.Kind == "musicvideo" && mvs != null && mvs.TryGetValue(e.MediaId, out var mv))
            {
                title = string.IsNullOrEmpty(mv.Artist) ? mv.Title : $"{mv.Artist} - {mv.Title}";
            }

            bool outputPresent = false;
            try
            {
                outputPresent = e.OutputKind == "file"
                    ? System.IO.File.Exists(e.OutputPath)
                    : e.OutputPath != null && Directory.Exists(e.OutputPath);
            }
            catch { }

            return new
            {
                e.Id, e.Kind, e.MediaId, title, mediaType,
                e.FileName, e.FilePath, e.Mode, e.Reason,
                e.Encoder, e.EncoderType, e.HardwareAccelerated,
                e.CpuName, e.GpuName,
                e.StartedAt, e.EndedAt, e.DurationSec, e.Status, e.ExitCode, e.FfmpegStats,
                e.OutputKind, e.OutputPath, e.OutputFileCount, e.OutputSizeBytes, outputPresent,
                e.AvgCpu, e.PeakCpu, e.AvgMem, e.PeakMemMb, e.AvgGpu, e.PeakGpu,
                samples = e.Samples
            };
        }).ToList();

        return Ok(new { entries = result });
    }

    // ─── User Statistics ──────────────────────────────────────────────
    // Built from the per-user media access history (logs/LocalAccess.log, LAN plays)
    // combined with each user's own library engagement (watched / favourites / etc.).
    // No separate telemetry is gathered - see UserStatsService.

    /// <summary>The current user's own statistics (any role - self-scoped).</summary>
    [HttpGet("me/stats")]
    public async Task<IActionResult> GetMyStats()
    {
        var username = CurrentUsername;
        var user = await _usersDb.Users.FirstOrDefaultAsync(u => u.Username == username);
        var evs = _userStats.GetEvents()
            .Where(e => string.Equals(e.User, username, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Ok(await BuildUserStatAsync(user, username, evs));
    }

    /// <summary>Global statistics across every user (admin only). With <c>?username=</c>
    /// returns that single user's detailed statistics (admin drill-down).</summary>
    [HttpGet("analysis/user-stats")]
    public async Task<IActionResult> GetUserStats([FromQuery] string? username = null)
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
            return Forbid();

        var allEvents = _userStats.GetEvents();

        // Drill-down to a single user.
        if (!string.IsNullOrEmpty(username))
        {
            var u = await _usersDb.Users.FirstOrDefaultAsync(x => x.Username == username);
            var evs = allEvents.Where(e => string.Equals(e.User, username, StringComparison.OrdinalIgnoreCase)).ToList();
            return Ok(await BuildUserStatAsync(u, username, evs));
        }

        // Global overview.
        var users = await _usersDb.Users
            .Select(u => new { u.Username, u.DisplayName, u.Role, u.LastLogin })
            .ToListAsync();

        var eventsByUser = allEvents
            .GroupBy(e => e.User, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var userRows = new List<object>();
        foreach (var u in users.OrderBy(u => u.Username))
        {
            eventsByUser.TryGetValue(u.Username, out var uevs);
            int plays = uevs?.Count ?? 0;
            DateTime? last = (uevs != null && uevs.Count > 0) ? uevs.Max(e => e.Ts) : (DateTime?)null;
            int watched = 0; try { watched = _userFavs.GetWatchedIds(u.Username).Count; } catch { }
            userRows.Add(new
            {
                u.Username, displayName = u.DisplayName ?? u.Username, u.Role,
                totalPlays = plays, watchedCount = watched, lastActive = last, lastLogin = u.LastLogin
            });
        }
        int activeUsers = users.Count(u => eventsByUser.ContainsKey(u.Username));

        var topMedia = await BuildTopMediaAsync(allEvents);
        var playsByType = allEvents.GroupBy(e => e.Type)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count).ToList();
        var playsPerDay = BuildPlaysPerDay(allEvents);

        return Ok(new
        {
            totalUsers = users.Count,
            activeUsers,
            totalPlays = allEvents.Count,
            users = userRows,
            topMedia,
            playsByType,
            playsPerDay
        });
    }

    private async Task<object> BuildUserStatAsync(AppUser? user, string username, List<UserStatsService.AccessEvent> evs)
    {
        int watchedCount = 0, favCount = 0, watchlistCount = 0, inProgressCount = 0;
        try { watchedCount = _userFavs.GetWatchedIds(username).Count; } catch { }
        try { favCount = _userFavs.GetFavouriteIds(username, "track").Count + _userFavs.GetFavouriteIds(username, "video").Count; } catch { }
        try { watchlistCount = _userFavs.GetWatchlistIds(username).Count; } catch { }
        try { inProgressCount = _userFavs.GetContinueWatching(username, 500).Count; } catch { }

        DateTime? firstActive = evs.Count > 0 ? evs.Min(e => e.Ts) : (DateTime?)null;
        DateTime? lastActive = evs.Count > 0 ? evs.Max(e => e.Ts) : (DateTime?)null;

        var playsByType = evs.GroupBy(e => e.Type)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count).ToList();

        // Peak viewing times (local time): 24 hourly buckets + 7 day-of-week buckets,
        // both from the access-log timestamps already in memory - no new telemetry.
        var byHour = new int[24];
        var byDow = new int[7];
        foreach (var e in evs)
        {
            var lt = e.Ts.ToLocalTime();
            byHour[lt.Hour]++;
            byDow[(int)lt.DayOfWeek]++;
        }
        var playsByHour = Enumerable.Range(0, 24)
            .Select(h => (object)new { hour = h, count = byHour[h] }).ToList();
        var playsByDow = Enumerable.Range(0, 7)
            .Select(d => (object)new { dow = d, count = byDow[d] }).ToList();

        var (watchTime, genreBreakdown, completion) = await BuildWatchTimeAndGenresAsync(username);

        return new
        {
            username,
            displayName = user?.DisplayName ?? username,
            role = user?.Role ?? "guest",
            dateCreated = user?.DateCreated,
            lastLogin = user?.LastLogin,
            firstActive,
            lastActive,
            totalPlays = evs.Count,
            distinctDevices = evs.Select(e => e.Ip).Distinct().Count(),
            watchedCount,
            favouritesCount = favCount,
            watchlistCount,
            inProgressCount,
            playsByType,
            playsPerDay = BuildPlaysPerDay(evs),
            playsByHour,
            playsByDow,
            topMedia = await BuildTopMediaAsync(evs),
            watchTime,
            genreBreakdown,
            completion
        };
    }

    // Derives real watch-TIME (in seconds) and per-genre affinity for a user WITHOUT any
    // extra telemetry - from the position/duration already stored per watched video
    // (VideoProgress in the user's own DB) plus per-user music play counts × track length.
    //  • A video counted as watched (>=80%) contributes its full runtime; an in-progress
    //    one contributes its furthest position (clamped to runtime). Re-watches are not
    //    tracked (progress overwrites), so this is a floor, not an exact figure.
    //  • Genre affinity is weighted by seconds watched, so a fully-watched film counts far
    //    more than a 5-minute sample. Video genres only (music genres are noisy) - top 12.
    private async Task<(object watchTime, List<object> genres, object completion)> BuildWatchTimeAndGenresAsync(string username)
    {
        double videoSec = 0, musicSec = 0;
        int started = 0, finished = 0;
        var genreSeconds = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var genreCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // ── Videos: watch-time + genre affinity from VideoProgress ──
        Dictionary<int, (double Position, double Duration, double Percent)> vprog;
        try { vprog = _userFavs.GetAllVideoProgress(username); } catch { vprog = new(); }
        if (vprog.Count > 0)
        {
            var ids = vprog.Keys.ToList();
            var meta = await _videoDb.Videos.Where(v => ids.Contains(v.Id))
                .Select(v => new { v.Id, v.Genre, v.Duration }).ToListAsync();
            var metaById = meta.ToDictionary(m => m.Id);

            foreach (var kv in vprog)
            {
                metaById.TryGetValue(kv.Key, out var m);
                double metaDur = m?.Duration ?? 0;
                double dur = kv.Value.Duration > 0 ? kv.Value.Duration : metaDur;
                double sec = kv.Value.Percent >= 80
                    ? (dur > 0 ? dur : kv.Value.Position)
                    : Math.Min(kv.Value.Position, dur > 0 ? dur : kv.Value.Position);
                if (sec < 0) sec = 0;
                videoSec += sec;

                // Completion rate: any progress row = "started"; ≥80% = "finished".
                started++;
                if (kv.Value.Percent >= 80) finished++;

                if (m != null && !string.IsNullOrWhiteSpace(m.Genre))
                    foreach (var raw in m.Genre.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        genreSeconds[raw] = genreSeconds.TryGetValue(raw, out var s) ? s + sec : sec;
                        genreCounts[raw] = genreCounts.TryGetValue(raw, out var c) ? c + 1 : 1;
                    }
            }
        }

        // ── Music: play count × track length ──
        try
        {
            var plays = _userFavs.GetMostPlayed(username, "music", 100000);
            if (plays.Count > 0)
            {
                var mids = plays.Select(p => p.MediaId).ToList();
                var durById = await _db.Tracks.Where(t => mids.Contains(t.Id))
                    .Select(t => new { t.Id, t.Duration }).ToDictionaryAsync(t => t.Id, t => t.Duration);
                foreach (var p in plays)
                    if (durById.TryGetValue(p.MediaId, out var d) && d > 0)
                        musicSec += d * p.Count;
            }
        }
        catch { }

        var genres = genreSeconds
            .OrderByDescending(g => g.Value)
            .Take(12)
            .Select(g => (object)new
            {
                name = g.Key,
                seconds = (long)Math.Round(g.Value),
                count = genreCounts.TryGetValue(g.Key, out var c) ? c : 0
            })
            .ToList();

        var watchTime = new
        {
            videoSeconds = (long)Math.Round(videoSec),
            musicSeconds = (long)Math.Round(musicSec),
            totalSeconds = (long)Math.Round(videoSec + musicSec)
        };
        var completion = new
        {
            started,
            finished,
            rate = started > 0 ? (int)Math.Round(finished * 100.0 / started) : 0
        };
        return (watchTime, genres, completion);
    }

    // Last 30 local days of activity (missing days filled with 0) for a bar chart.
    private static List<object> BuildPlaysPerDay(IEnumerable<UserStatsService.AccessEvent> evs)
    {
        var today = DateTime.Now.Date;
        var dayCounts = evs.GroupBy(e => e.Ts.ToLocalTime().Date).ToDictionary(g => g.Key, g => g.Count());
        var list = new List<object>();
        for (int i = 29; i >= 0; i--)
        {
            var d = today.AddDays(-i);
            list.Add(new { name = d.ToString("yyyy-MM-dd"), count = dayCounts.TryGetValue(d, out var c) ? c : 0 });
        }
        return list;
    }

    private async Task<List<object>> BuildTopMediaAsync(IEnumerable<UserStatsService.AccessEvent> evs)
    {
        var topKeys = evs.Where(e => e.MediaId > 0)
            .GroupBy(e => new { e.Type, e.MediaId })
            .Select(g => new { g.Key.Type, g.Key.MediaId, count = g.Count() })
            .OrderByDescending(x => x.count).Take(10).ToList();
        var titles = await ResolveMediaTitlesAsync(topKeys.Select(k => (k.Type, k.MediaId)).ToList());
        return topKeys.Select(k => (object)new
        {
            name = titles.TryGetValue((k.Type, k.MediaId), out var tt) ? tt : (k.Type + " #" + k.MediaId),
            type = k.Type,
            count = k.count
        }).ToList();
    }

    // Batch-resolve display titles for (type, id) media keys across the media DBs.
    private async Task<Dictionary<(string, int), string>> ResolveMediaTitlesAsync(List<(string Type, int Id)> keys)
    {
        var map = new Dictionary<(string, int), string>();
        if (keys.Count == 0) return map;
        var byType = keys.GroupBy(k => k.Type).ToDictionary(g => g.Key, g => g.Select(k => k.Id).Distinct().ToList());

        if (byType.TryGetValue("Video", out var vIds))
            foreach (var v in await _videoDb.Videos.Where(v => vIds.Contains(v.Id))
                        .Select(v => new { v.Id, v.Title, v.SeriesName, v.Season, v.Episode }).ToListAsync())
                map[("Video", v.Id)] = !string.IsNullOrEmpty(v.SeriesName) && v.Season.HasValue && v.Episode.HasValue
                    ? $"{v.SeriesName} S{v.Season:D2}E{v.Episode:D2}" : v.Title;

        if (byType.TryGetValue("Music", out var mIds))
            foreach (var tr in await _db.Tracks.Where(t => mIds.Contains(t.Id))
                        .Select(t => new { t.Id, t.Title, t.Artist }).ToListAsync())
                map[("Music", tr.Id)] = string.IsNullOrEmpty(tr.Artist) ? tr.Title : $"{tr.Artist} - {tr.Title}";

        if (byType.TryGetValue("MusicVideo", out var mvIds))
            foreach (var mv in await _mvDb.MusicVideos.Where(v => mvIds.Contains(v.Id))
                        .Select(v => new { v.Id, v.Title, v.Artist }).ToListAsync())
                map[("MusicVideo", mv.Id)] = string.IsNullOrEmpty(mv.Artist) ? mv.Title : $"{mv.Artist} - {mv.Title}";

        if (byType.TryGetValue("AudioBook", out var abIds))
            foreach (var ab in await _audioBooksDb.AudioBooks.Where(a => abIds.Contains(a.Id))
                        .Select(a => new { a.Id, a.Title }).ToListAsync())
                map[("AudioBook", ab.Id)] = ab.Title;

        if (byType.TryGetValue("eBook", out var ebIds))
            foreach (var eb in await _ebookDb.EBooks.Where(b => ebIds.Contains(b.Id))
                        .Select(b => new { b.Id, b.Title }).ToListAsync())
                map[("eBook", eb.Id)] = eb.Title;

        return map;
    }

    // ─── Recommendations (content-based, per user) ────────────────────────────
    // A personalised ranking built from THIS user's own habits - watched (+dates),
    // favourites, watchlist and per-user play counts, all living in the user's own
    // users/{username}.db - matched against the content metadata already scraped
    // into videos.db (genres / cast / directors / studios / decade / franchise).
    // No new telemetry, no cross-user data. The taste profile is cached briefly per
    // user (TTL) so repeat calls are cheap. See RECOMMENDATIONS_PLAN.md.

    /// <summary>A user's accumulated, recency-weighted taste, normalised 0..1 per bucket.</summary>
    private sealed class TasteProfile
    {
        public Dictionary<string, double> Genre = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> Person = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> Studio = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<int, double> Decade = new();
        public Dictionary<string, double> ContentRating = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<int> Collections = new();
        public int LikedCount;
        public DateTime BuiltAt;
    }

    /// <summary>Lightweight candidate/liked row projection (scoring + card building).</summary>
    private sealed class RecRow
    {
        public int Id; public string Title = ""; public int? Year; public double Duration;
        public int Height; public string? HdrFormat; public double Rating;
        public string? Genre; public string? ContentRating; public int? CollectionId; public string? CollectionName;
        public string MediaType = ""; public string? SeriesName; public int? Season; public int? Episode;
        public string? PosterPath; public string? ThumbnailPath; public string? BackdropPath;
        public string? Overview; public string? DirectorJson; public string? CastJson; public string? StudiosJson;
        public DateTime DateAdded;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, TasteProfile> _tasteCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan TasteProfileTtl = TimeSpan.FromMinutes(10);

    /// <summary>The current user's personalised recommendations for one media type.
    /// Self-scoped (no username param) so it is inherently per-user; any role may call it.</summary>
    [HttpGet("recommendations")]
    public async Task<IActionResult> GetRecommendations([FromQuery] string type = "movie", [FromQuery] int limit = 30)
    {
        var mt = (type ?? "movie").ToLowerInvariant();
        if (mt != "movie" && mt != "tv" && mt != "documentary" && mt != "anime") mt = "movie";
        limit = Math.Clamp(limit, 1, 100);

        var username = CurrentUsername;
        var profile = await GetOrBuildTasteProfileAsync(username);
        var watched = _userFavs.GetWatchedIds(username);
        HashSet<int> favIds; try { favIds = _userFavs.GetFavouriteIds(username, "video"); } catch { favIds = new(); }

        // Candidate rows of the requested type. Same order-of-work as the grouped
        // library endpoint (which also materialises every episode), so no new cost class.
        var rows = await _videoDb.Videos
            .Where(v => v.MediaType == mt)
            .Select(v => new RecRow
            {
                Id = v.Id, Title = v.Title, Year = v.Year, Duration = v.Duration,
                Height = v.Height, HdrFormat = v.HdrFormat, Rating = v.Rating,
                Genre = v.Genre, ContentRating = v.ContentRating, CollectionId = v.CollectionId,
                MediaType = v.MediaType, SeriesName = v.SeriesName, Season = v.Season, Episode = v.Episode,
                PosterPath = v.PosterPath, ThumbnailPath = v.ThumbnailPath, BackdropPath = v.BackdropPath,
                Overview = v.Overview, DirectorJson = v.DirectorJson, CastJson = v.CastJson, StudiosJson = v.StudiosJson,
                CollectionName = v.CollectionName, DateAdded = v.DateAdded
            })
            .ToListAsync();

        // Cold start: too little history to personalise → rank by quality/recency.
        // In popularity mode we attach no reason (there is no personal driver).
        bool popularity = profile.LikedCount < 3;
        List<object> top;

        if (mt == "tv" || mt == "anime")
        {
            // TV and anime share the SeriesName/Season/Episode shape → group episodes into
            // series (case-insensitive) and score at series level. Anime carries its own
            // mediaType so the card opens the anime series detail, not the TV one.
            var groups = rows
                .Where(r => !string.IsNullOrWhiteSpace(r.SeriesName))
                .GroupBy(r => r.SeriesName!.Trim().ToLowerInvariant())
                .Where(g => g.Key.Length > 0);
            var scored = new List<(double Score, IGrouping<string, RecRow> G)>();
            foreach (var g in groups)
            {
                if (g.All(r => watched.Contains(r.Id))) continue; // caught up → not a rec
                double score = popularity ? g.Max(PopularityScore) : g.Max(r => ScoreRow(r, profile));
                scored.Add((score, g));
            }
            // Build cards only for the top N - so the reason is computed for those only.
            top = scored.OrderByDescending(i => i.Score).Take(limit)
                .Select(t => BuildSeriesCard(t.G, mt, popularity ? null : BestReason(t.G, profile)))
                .ToList();
        }
        else
        {
            // Movies and standalone documentaries: individual cards.
            var scored = new List<(double Score, RecRow Row)>();
            foreach (var r in rows)
            {
                if (watched.Contains(r.Id)) continue;
                double score = popularity ? PopularityScore(r) : ScoreRow(r, profile);
                scored.Add((score, r));
            }
            top = scored.OrderByDescending(i => i.Score).Take(limit)
                .Select(t => BuildMovieCard(t.Row, favIds.Contains(t.Row.Id), popularity ? null : TopReason(t.Row, profile)))
                .ToList();
        }

        return Ok(new { items = top, personalised = !popularity, likedCount = profile.LikedCount });
    }

    // The single strongest personal driver behind a candidate - mirrors ScoreRow's
    // weights so the "reason" shown to the user is the feature that actually ranked it.
    // Returns a (type, display-value) pair or null when nothing matched the profile.
    private (string Type, string Value)? TopReason(RecRow r, TasteProfile p)
    {
        string? bType = null, bVal = null; double best = 0;
        if (!string.IsNullOrWhiteSpace(r.Genre))
            foreach (var raw in r.Genre.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var norm = MetadataService.NormalizeGenreToken(raw);
                if (p.Genre.TryGetValue(norm, out var gv)) { double c = 2.0 * gv; if (c > best) { best = c; bType = "genre"; bVal = raw; } }
            }
        foreach (var d in ExtractNames(r.DirectorJson, 3))
            if (p.Person.TryGetValue(d, out var pv)) { double c = 1.5 * pv; if (c > best) { best = c; bType = "person"; bVal = d; } }
        foreach (var cn in ExtractNames(r.CastJson, 5))
            if (p.Person.TryGetValue(cn, out var pv)) { double c = 1.2 * pv; if (c > best) { best = c; bType = "person"; bVal = cn; } }
        foreach (var s in ExtractNames(r.StudiosJson, 3))
            if (p.Studio.TryGetValue(s, out var sv)) { double c = 1.0 * sv; if (c > best) { best = c; bType = "studio"; bVal = s; } }
        if (r.CollectionId.HasValue && p.Collections.Contains(r.CollectionId.Value) && !string.IsNullOrWhiteSpace(r.CollectionName))
        { double c = 2.0; if (c > best) { best = c; bType = "franchise"; bVal = r.CollectionName; } }
        if (bType == null || string.IsNullOrWhiteSpace(bVal)) return null;
        return (bType, bVal!);
    }

    // Series reason = the reason of its best-scoring episode.
    private (string Type, string Value)? BestReason(IGrouping<string, RecRow> g, TasteProfile p)
        => TopReason(g.OrderByDescending(r => ScoreRow(r, p)).First(), p);

    private async Task<TasteProfile> GetOrBuildTasteProfileAsync(string username)
    {
        if (_tasteCache.TryGetValue(username, out var cached) && (DateTime.UtcNow - cached.BuiltAt) < TasteProfileTtl)
            return cached;
        var built = await BuildTasteProfileAsync(username);
        _tasteCache[username] = built;
        return built;
    }

    private async Task<TasteProfile> BuildTasteProfileAsync(string username)
    {
        // 1) Collect the user's "liked" video ids with a combined weight.
        var weights = new Dictionary<int, double>();
        void Add(int id, double w) { weights.TryGetValue(id, out var cur); weights[id] = cur + w; }

        try
        {
            foreach (var (id, dateStr) in _userFavs.GetWatchedWithDates(username))
                Add(id, 3.0 * RecencyWeight(dateStr));
        }
        catch { }
        try { foreach (var id in _userFavs.GetFavouriteIds(username, "video")) Add(id, 4.0); } catch { }
        try { foreach (var id in _userFavs.GetWatchlistIds(username)) Add(id, 1.0); } catch { }
        try { foreach (var (id, count) in _userFavs.GetMostPlayed(username, "video", 500)) Add(id, 0.5 * Math.Min(count, 10)); } catch { }

        var profile = new TasteProfile { LikedCount = weights.Count, BuiltAt = DateTime.UtcNow };
        if (weights.Count == 0) return profile;

        // 2) Load metadata for the liked items and distribute each weight into buckets.
        var likedIds = weights.Keys.ToList();
        var meta = await _videoDb.Videos
            .Where(v => likedIds.Contains(v.Id))
            .Select(v => new RecRow
            {
                Id = v.Id, Year = v.Year, Genre = v.Genre, ContentRating = v.ContentRating,
                CollectionId = v.CollectionId, DirectorJson = v.DirectorJson, CastJson = v.CastJson, StudiosJson = v.StudiosJson
            })
            .ToListAsync();

        foreach (var r in meta)
        {
            double w = weights.TryGetValue(r.Id, out var ww) ? ww : 1.0;
            foreach (var gname in ParseGenres(r.Genre)) Bump(profile.Genre, gname, w);
            foreach (var p in ExtractNames(r.DirectorJson, 3)) Bump(profile.Person, p, w);
            foreach (var p in ExtractNames(r.CastJson, 5)) Bump(profile.Person, p, w * 0.7);
            foreach (var s in ExtractNames(r.StudiosJson, 3)) Bump(profile.Studio, s, w);
            if (r.Year.HasValue && r.Year > 1900) { int dec = (r.Year.Value / 10) * 10; profile.Decade.TryGetValue(dec, out var d); profile.Decade[dec] = d + w; }
            if (!string.IsNullOrWhiteSpace(r.ContentRating)) Bump(profile.ContentRating, r.ContentRating!, w);
            if (r.CollectionId.HasValue) profile.Collections.Add(r.CollectionId.Value);
        }

        // 3) Normalise each bucket to 0..1 so no single dimension dominates.
        Normalise(profile.Genre); Normalise(profile.Person); Normalise(profile.Studio); Normalise(profile.ContentRating);
        if (profile.Decade.Count > 0) { double mx = profile.Decade.Values.Max(); if (mx > 0) foreach (var k in profile.Decade.Keys.ToList()) profile.Decade[k] /= mx; }
        return profile;
    }

    private static void Bump(Dictionary<string, double> d, string key, double w)
    {
        key = key.Trim(); if (key.Length == 0) return;
        d.TryGetValue(key, out var cur); d[key] = cur + w;
    }
    private static void Normalise(Dictionary<string, double> d)
    {
        if (d.Count == 0) return; double mx = d.Values.Max(); if (mx <= 0) return;
        foreach (var k in d.Keys.ToList()) d[k] /= mx;
    }

    // Newer engagement counts for more: exponential decay, ~83-day half-life.
    private static double RecencyWeight(string dateStr)
    {
        if (DateTime.TryParse(dateStr, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
        {
            double ageDays = Math.Max(0, (DateTime.UtcNow - dt).TotalDays);
            return Math.Exp(-ageDays / 120.0);
        }
        return 0.5; // undated engagement (fav/watchlist) still counts, just not boosted
    }

    private double ScoreRow(RecRow r, TasteProfile p)
    {
        double s = 0;
        foreach (var g in ParseGenres(r.Genre)) if (p.Genre.TryGetValue(g, out var gv)) s += 2.0 * gv;
        foreach (var person in ExtractNames(r.DirectorJson, 3)) if (p.Person.TryGetValue(person, out var pv)) s += 1.5 * pv;
        foreach (var person in ExtractNames(r.CastJson, 5)) if (p.Person.TryGetValue(person, out var pv)) s += 1.2 * pv;
        foreach (var st in ExtractNames(r.StudiosJson, 3)) if (p.Studio.TryGetValue(st, out var sv)) s += 1.0 * sv;
        if (r.Year.HasValue && r.Year > 1900) { int dec = (r.Year.Value / 10) * 10; if (p.Decade.TryGetValue(dec, out var dv)) s += 0.8 * dv; }
        if (!string.IsNullOrWhiteSpace(r.ContentRating) && p.ContentRating.TryGetValue(r.ContentRating!, out var cv)) s += 0.6 * cv;
        if (r.CollectionId.HasValue && p.Collections.Contains(r.CollectionId.Value)) s += 2.0; // franchise
        s += 0.1 * (r.Rating / 10.0); // quality tiebreak
        return s;
    }

    // Cold-start / no-profile ordering: TMDB quality plus a small freshness nudge.
    private static double PopularityScore(RecRow r)
    {
        double recency = Math.Exp(-Math.Max(0, (DateTime.UtcNow - r.DateAdded).TotalDays) / 180.0);
        return r.Rating + 0.5 * recency;
    }

    private static IEnumerable<string> ParseGenres(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) yield break;
        foreach (var g in csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var norm = MetadataService.NormalizeGenreToken(g);
            if (!string.IsNullOrWhiteSpace(norm)) yield return norm;
        }
    }

    // DirectorJson/CastJson/StudiosJson are arrays of objects with a "name" field.
    private static List<string> ExtractNames(string? json, int limit)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return list;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (list.Count >= limit) break;
                if (el.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    el.TryGetProperty("name", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var s = n.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                }
            }
        }
        catch { }
        return list;
    }

    private object BuildMovieCard(RecRow r, bool isFav, (string Type, string Value)? reason = null) => new
    {
        type = "video",
        id = r.Id, title = r.Title, year = r.Year, duration = r.Duration,
        height = r.Height, hdrFormat = r.HdrFormat ?? "", rating = r.Rating,
        genre = r.Genre ?? "", overview = r.Overview ?? "", contentRating = r.ContentRating ?? "",
        mediaType = r.MediaType, seriesName = r.SeriesName ?? "", season = r.Season, episode = r.Episode,
        posterPath = r.PosterPath ?? "", thumbnailPath = r.ThumbnailPath ?? "", backdropPath = r.BackdropPath ?? "",
        isWatched = false, isFavourite = isFav,
        reasonType = reason?.Type, reasonValue = reason?.Value
    };

    private object BuildSeriesCard(IGrouping<string, RecRow> g, string mediaType = "tv", (string Type, string Value)? reason = null)
    {
        var bestName = g.Select(r => r.SeriesName ?? "").Where(n => n.Length > 0)
            .OrderBy(n => n.Any(char.IsLower) ? 0 : 1).First();
        string? FirstNonEmpty(Func<RecRow, string?> sel) => g
            .OrderBy(r => r.Season).ThenBy(r => r.Episode)
            .Select(sel).FirstOrDefault(v => !string.IsNullOrEmpty(v));
        return new
        {
            type = "series",
            id = g.OrderBy(r => r.Season).ThenBy(r => r.Episode).Select(r => r.Id).First(),
            seriesName = bestName,
            episodeCount = g.Count(),
            seasonCount = g.Select(r => r.Season).Distinct().Count(),
            duration = g.Sum(r => r.Duration),
            thumbnailPath = FirstNonEmpty(r => r.ThumbnailPath) ?? "",
            posterPath = FirstNonEmpty(r => r.PosterPath) ?? "",
            backdropPath = FirstNonEmpty(r => r.BackdropPath) ?? "",
            rating = g.Max(r => r.Rating),
            genre = FirstNonEmpty(r => r.Genre) ?? "",
            overview = FirstNonEmpty(r => r.Overview) ?? "",
            contentRating = FirstNonEmpty(r => r.ContentRating) ?? "",
            height = g.Max(r => r.Height),
            hdrFormat = FirstNonEmpty(r => r.HdrFormat) ?? "",
            year = g.Max(r => r.Year),
            mediaType = mediaType,
            allWatched = false, // fully-watched series were excluded above
            reasonType = reason?.Type, reasonValue = reason?.Value
        };
    }

    [HttpGet("analysis/deep")]
    public async Task<IActionResult> GetDeepAnalysis()
    {
        // Pull only analyzed rows and aggregate in memory (cross-DB JSON parsing)
        var rows = await _videoDb.Videos
            .Where(v => v.DeepAnalysisDone)
            .Select(v => new
            {
                v.Resolution, v.Width, v.Height, v.Codec, v.HdrFormat,
                v.VideoProfile, v.ColorSpace, v.TotalBitrateKbps,
                v.AudioTracks, v.SubtitleTracks, v.MediaType
            })
            .ToListAsync();

        if (rows.Count == 0)
            return Ok(new { analyzed = 0, audioCodecs = Array.Empty<object>(), subtitleCodecs = Array.Empty<object>(),
                audioLanguages = Array.Empty<object>(), subtitleLanguages = Array.Empty<object>(),
                videoProfiles = Array.Empty<object>(), colorSpaces = Array.Empty<object>(),
                bitrateDistribution = Array.Empty<object>(), exactResolutions = Array.Empty<object>() });

        // Accumulators
        var audioCodecCounts    = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var subtitleCodecCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var audioLangCounts     = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var subLangCounts       = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var profileCounts       = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var colorSpaceCounts    = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var resolutionCounts    = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var bitrateBuckets      = new Dictionary<string, int>();
        var hdrCounts           = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var jsonOpts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        foreach (var row in rows)
        {
            // Exact resolutions (top 10 later)
            if (row.Width > 0 && row.Height > 0)
            {
                var res = $"{row.Width}x{row.Height}";
                resolutionCounts[res] = resolutionCounts.GetValueOrDefault(res) + 1;
            }

            // HDR
            var hdr = string.IsNullOrEmpty(row.HdrFormat) ? "SDR" : row.HdrFormat;
            hdrCounts[hdr] = hdrCounts.GetValueOrDefault(hdr) + 1;

            // Video profile
            if (!string.IsNullOrEmpty(row.VideoProfile))
                profileCounts[row.VideoProfile] = profileCounts.GetValueOrDefault(row.VideoProfile) + 1;

            // Colour space
            if (!string.IsNullOrEmpty(row.ColorSpace))
                colorSpaceCounts[row.ColorSpace] = colorSpaceCounts.GetValueOrDefault(row.ColorSpace) + 1;

            // Bitrate bucket
            if (row.TotalBitrateKbps.HasValue)
            {
                var bk = row.TotalBitrateKbps.Value switch
                {
                    < 1000   => "< 1 Mbps",
                    < 3000   => "1–3 Mbps",
                    < 6000   => "3–6 Mbps",
                    < 10000  => "6–10 Mbps",
                    < 20000  => "10–20 Mbps",
                    < 40000  => "20–40 Mbps",
                    _        => "> 40 Mbps"
                };
                bitrateBuckets[bk] = bitrateBuckets.GetValueOrDefault(bk) + 1;
            }

            // Audio tracks JSON
            if (!string.IsNullOrEmpty(row.AudioTracks))
            {
                try
                {
                    var tracks = System.Text.Json.JsonSerializer.Deserialize<List<AudioTrackDto>>(row.AudioTracks, jsonOpts);
                    if (tracks != null)
                    {
                        foreach (var tr in tracks)
                        {
                            if (!string.IsNullOrEmpty(tr.Codec))
                                audioCodecCounts[tr.Codec] = audioCodecCounts.GetValueOrDefault(tr.Codec) + 1;
                            var lang = string.IsNullOrEmpty(tr.Language) ? "und" : tr.Language.ToLowerInvariant();
                            audioLangCounts[lang] = audioLangCounts.GetValueOrDefault(lang) + 1;
                        }
                    }
                }
                catch { /* skip malformed */ }
            }

            // Subtitle tracks JSON
            if (!string.IsNullOrEmpty(row.SubtitleTracks))
            {
                try
                {
                    var tracks = System.Text.Json.JsonSerializer.Deserialize<List<SubtitleTrackDto>>(row.SubtitleTracks, jsonOpts);
                    if (tracks != null)
                    {
                        foreach (var tr in tracks)
                        {
                            if (!string.IsNullOrEmpty(tr.Codec))
                                subtitleCodecCounts[tr.Codec] = subtitleCodecCounts.GetValueOrDefault(tr.Codec) + 1;
                            var lang = string.IsNullOrEmpty(tr.Language) ? "und" : tr.Language.ToLowerInvariant();
                            subLangCounts[lang] = subLangCounts.GetValueOrDefault(lang) + 1;
                        }
                    }
                }
                catch { /* skip malformed */ }
            }
        }

        static List<object> ToList(Dictionary<string, int> d, int take = 10) =>
            d.OrderByDescending(kv => kv.Value).Take(take)
             .Select(kv => (object)new { name = kv.Key, count = kv.Value }).ToList();

        var bitrateOrder = new[] { "< 1 Mbps","1–3 Mbps","3–6 Mbps","6–10 Mbps","10–20 Mbps","20–40 Mbps","> 40 Mbps" };
        var bitrateList  = bitrateOrder
            .Where(b => bitrateBuckets.ContainsKey(b))
            .Select(b => (object)new { name = b, count = bitrateBuckets[b] })
            .ToList();

        return Ok(new
        {
            analyzed          = rows.Count,
            audioCodecs       = ToList(audioCodecCounts),
            subtitleCodecs    = ToList(subtitleCodecCounts),
            audioLanguages    = ToList(audioLangCounts),
            subtitleLanguages = ToList(subLangCounts),
            videoProfiles     = ToList(profileCounts),
            colorSpaces       = ToList(colorSpaceCounts),
            bitrateDistribution = bitrateList,
            exactResolutions  = ToList(resolutionCounts),
            hdrCoverage       = ToList(hdrCounts)
        });
    }

    private sealed record AudioTrackDto(string? Codec, string? Language, int Channels, int BitrateKbps);
    private sealed record SubtitleTrackDto(string? Codec, string? Language, bool Forced);

    [HttpGet("export/csv")]
    public async Task<IActionResult> ExportCsv()
    {
        var lines = new List<string>();
        lines.Add("media_type,sub_type,title,artist_or_author,album_or_series,year,genre,format,codec,resolution,file_size_bytes,duration_seconds,file_path,date_added");

        // Music tracks
        var tracks = await _db.Tracks.OrderBy(t => t.Artist).ThenBy(t => t.Title).ToListAsync();
        foreach (var t in tracks)
            lines.Add($"Music,Audio Track,{Csv(t.Title)},{Csv(t.Artist)},{Csv(t.Album)},{t.Year},{Csv(t.Genre)},{Csv(t.Codec)},{Csv(t.Codec)},,,{t.FileSize},{t.Duration},{Csv(t.FilePath)},{t.DateAdded:yyyy-MM-dd}");

        // Movies & TV
        var videos = await _videoDb.Videos.OrderBy(v => v.Title).ToListAsync();
        foreach (var v in videos)
            lines.Add($"Video,{(v.MediaType == "tv" ? "TV Show" : "Movie")},{Csv(v.Title)},{Csv(v.Director)},{Csv(v.SeriesName)},{v.Year},{Csv(v.Genre)},{Csv(v.Format)},{Csv(v.Codec)},{v.Resolution},{v.SizeBytes},{v.Duration},{Csv(v.FilePath)},{v.DateAdded:yyyy-MM-dd}");

        // Music Videos
        var mvs = await _mvDb.MusicVideos.OrderBy(v => v.Artist).ThenBy(v => v.Title).ToListAsync();
        foreach (var v in mvs)
            lines.Add($"Music Video,Music Video,{Csv(v.Title)},{Csv(v.Artist)},{Csv(v.Album)},{v.Year},{Csv(v.Genre)},{Csv(v.Format)},{Csv(v.Codec)},{v.Resolution},{v.SizeBytes},{v.Duration},{Csv(v.FilePath)},{v.DateAdded:yyyy-MM-dd}");

        // Pictures
        var pics = await _picDb.Pictures.OrderBy(p => p.FileName).ToListAsync();
        foreach (var p in pics)
            lines.Add($"Picture,Image,{Csv(p.FileName)},,,,,{Csv(p.Format)},,,{p.SizeBytes},,{Csv(p.FilePath)},{p.DateAdded:yyyy-MM-dd}");

        // eBooks
        var ebooks = await _ebookDb.EBooks.OrderBy(e => e.Title).ToListAsync();
        foreach (var e in ebooks)
            lines.Add($"Document,{e.Format},{Csv(e.Title)},{Csv(e.Author)},{Csv(e.Category)},,,{e.Format},,,{e.FileSize},,{Csv(e.FilePath)},{e.DateAdded:yyyy-MM-dd}");

        var csv = string.Join("\n", lines);
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"NexusM_Library_{DateTime.Now:yyyyMMdd}.csv");
    }

    [HttpGet("export/html")]
    public async Task<IActionResult> ExportHtml()
    {
        var rows = new List<(string type, string sub, string title, string artistAuthor, string album, string year, string genre, string format, string resolution, long size, string path)>();

        var tracks = await _db.Tracks.OrderBy(t => t.Artist).ThenBy(t => t.Title).ToListAsync();
        foreach (var t in tracks)
            rows.Add(("Music", "Audio", t.Title, t.Artist, t.Album, t.Year?.ToString() ?? "", t.Genre, t.Codec, "", t.FileSize, t.FilePath));

        var videos = await _videoDb.Videos.OrderBy(v => v.Title).ToListAsync();
        foreach (var v in videos)
            rows.Add(("Video", v.MediaType == "tv" ? "TV" : "Movie", v.Title, v.Director, v.SeriesName, v.Year?.ToString() ?? "", v.Genre, v.Format, v.Resolution, v.SizeBytes, v.FilePath));

        var mvs = await _mvDb.MusicVideos.OrderBy(v => v.Artist).ThenBy(v => v.Title).ToListAsync();
        foreach (var v in mvs)
            rows.Add(("Music Video", "MV", v.Title, v.Artist, v.Album, v.Year?.ToString() ?? "", v.Genre, v.Format, v.Resolution, v.SizeBytes, v.FilePath));

        var pics = await _picDb.Pictures.OrderBy(p => p.FileName).ToListAsync();
        foreach (var p in pics)
            rows.Add(("Picture", "Image", p.FileName, "", p.Category, "", "", p.Format, $"{p.Width}x{p.Height}", p.SizeBytes, p.FilePath));

        var ebooks = await _ebookDb.EBooks.OrderBy(e => e.Title).ToListAsync();
        foreach (var e in ebooks)
            rows.Add(("Document", e.Format, e.Title, e.Author, e.Category, "", "", e.Format, "", e.FileSize, e.FilePath));

        var totalSize = rows.Sum(r => r.size);
        var tableRows = string.Join("\n", rows.Select(r =>
        {
            var badgeClass = r.type switch { "Music" => "music", "Video" => "video", "Music Video" => "mv", "Picture" => "pic", _ => "doc" };
            return $"<tr><td><span class=\"badge {badgeClass}\">{Esc(r.type)}</span></td><td>{Esc(r.sub)}</td><td>{Esc(r.title)}</td><td>{Esc(r.artistAuthor)}</td><td>{Esc(r.album)}</td><td>{Esc(r.year)}</td><td>{Esc(r.genre)}</td><td>{Esc(r.format)}</td><td>{Esc(r.resolution)}</td><td class=\"size\">{FormatSize(r.size)}</td></tr>";
        }));

        var html = $@"<!DOCTYPE html><html><head><meta charset=""UTF-8""><title>NexusM Library Report</title>
<style>
*{{margin:0;padding:0;box-sizing:border-box}}body{{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;background:#121212;color:#e0e0e0;padding:24px}}
h1{{font-size:22px;margin-bottom:4px}}
.meta{{color:#999;font-size:13px;margin-bottom:20px}}
.summary{{display:flex;gap:16px;flex-wrap:wrap;margin-bottom:24px}}
.summary div{{background:#1e1e1e;padding:12px 20px;border-radius:8px;text-align:center}}
.summary .val{{font-size:20px;font-weight:700;color:#4d8bf5}}.summary .lbl{{font-size:11px;color:#999;margin-top:2px}}
table{{width:100%;border-collapse:collapse;font-size:13px}}
th{{text-align:left;padding:8px 10px;background:#1e1e1e;color:#999;font-weight:600;border-bottom:2px solid #333;position:sticky;top:0}}
td{{padding:6px 10px;border-bottom:1px solid #252525}}
tr:hover td{{background:#1a1a2e}}
.badge{{display:inline-block;padding:2px 8px;border-radius:3px;font-size:10px;font-weight:700;text-transform:uppercase;color:#fff}}
.music{{background:#4d8bf5}}.video{{background:#e74c3c}}.mv{{background:#9b59b6}}.pic{{background:#27ae60}}.doc{{background:#f39c12}}
.size{{text-align:right;white-space:nowrap}}
</style></head><body>
<h1>NexusM Library Report</h1>
<p class=""meta"">Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} &middot; {rows.Count} items &middot; {FormatSize(totalSize)}</p>
<div class=""summary"">
<div><div class=""val"">{tracks.Count}</div><div class=""lbl"">Music</div></div>
<div><div class=""val"">{videos.Count}</div><div class=""lbl"">Videos</div></div>
<div><div class=""val"">{mvs.Count}</div><div class=""lbl"">Music Videos</div></div>
<div><div class=""val"">{pics.Count}</div><div class=""lbl"">Pictures</div></div>
<div><div class=""val"">{ebooks.Count}</div><div class=""lbl"">eBooks</div></div>
<div><div class=""val"">{FormatSize(totalSize)}</div><div class=""lbl"">Total Size</div></div>
</div>
<table><thead><tr><th>Type</th><th>Sub</th><th>Title</th><th>Artist/Author</th><th>Album/Series</th><th>Year</th><th>Genre</th><th>Format</th><th>Resolution</th><th class=""size"">Size</th></tr></thead>
<tbody>{tableRows}</tbody></table>
</body></html>";

        return File(System.Text.Encoding.UTF8.GetBytes(html), "text/html", $"NexusM_Library_{DateTime.Now:yyyyMMdd}.html");
    }

    private static string Csv(string? s) => s == null ? "" :
        s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;

    private static string Esc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");

    private static string FormatSize(long bytes) =>
        bytes >= 1_073_741_824 ? $"{bytes / 1_073_741_824.0:F1} GB" :
        bytes >= 1_048_576 ? $"{bytes / 1_048_576.0:F1} MB" :
        bytes >= 1024 ? $"{bytes / 1024.0:F1} KB" : $"{bytes} B";

    // ─── Albums ─────────────────────────────────────────────────────

    [HttpGet("albums")]
    public async Task<IActionResult> GetAlbums(
        [FromQuery] string? search = null,
        [FromQuery] string? sort = "recent",  // recent, name, artist, year
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50)
    {
        var query = _db.Albums.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            using var conn = OpenMusicDbRaw();
            var ids = FtsIds(conn, "albums_fts", BuildFtsQuery(search), 2000);
            if (ids.Count > 0)
                query = query.Where(a => ids.Contains(a.Id));
            else
            {
                var s = MusicKey.Of(search);
                query = query.Where(a => a.NameKey.Contains(s) || a.ArtistKey.Contains(s));
            }
        }

        var rows = await query.Select(a => new
        {
            a.Id, a.Name, a.Artist, a.Year, a.Genre,
            a.CoverArtPath, a.TrackCount, a.TotalDuration,
            a.IsFavourite, a.Rating, a.DateAdded
        }).ToListAsync();

        // Merge albums that share the same complete album NAME (from metadata) into one
        // object. A compilation carries a different ARTIST per track and often no ALBUMARTIST
        // tag, so the scanner created one Album row per artist - hundreds of duplicate cards
        // for a single album. Grouping by the full name collapses them. The artist shown is
        // taken ONLY from metadata: the album artist when every row agrees on it, otherwise
        // left blank - no synthesised label is ever invented.
        var groups = rows
            .GroupBy(a => (a.Name ?? "").Trim().ToLowerInvariant())
            .Select(g =>
            {
                var rep = g.FirstOrDefault(a => !string.IsNullOrEmpty(a.CoverArtPath))
                          ?? g.OrderByDescending(a => a.TrackCount).ThenBy(a => a.Id).First();
                var artists = g.Select(a => a.Artist)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                return new
                {
                    id = rep.Id,          // representative row → /api/cover/{id} + openAlbum key
                    name = rep.Name,
                    artist = artists.Count == 1 ? artists[0] : "",
                    rep.Year,
                    rep.Genre,
                    rep.CoverArtPath,
                    trackCount = g.Sum(a => a.TrackCount),
                    totalDuration = g.Sum(a => a.TotalDuration),
                    rep.IsFavourite,
                    rep.Rating,
                    dateAdded = g.Max(a => a.DateAdded)
                };
            });

        // Drop orphaned empty albums. Editing an album/track's metadata (e.g. renaming
        // "Ideal Standard" -> "Idéal standard") re-keys its tracks to a new album name and
        // leaves the old Album row behind with no tracks. The scanner is additive and never
        // prunes it, so it lingered as an empty, unopenable card that could not be removed.
        // Keep only album names that still have at least one real track, matched on the
        // accent-safe AlbumKey (mirrors the GetAlbum lookup; SQLite lower()/NOCASE is
        // ASCII-only - see the music accent fix). Query-time only, no data is deleted.
        var liveAlbumKeys = (await _db.Tracks
                .Where(t => t.AlbumKey != null && t.AlbumKey != "")
                .Select(t => t.AlbumKey).Distinct().ToListAsync())
            .ToHashSet();
        groups = groups.Where(a => liveAlbumKeys.Contains(MusicKey.Of(a.name)));

        var sorted = sort switch
        {
            "name"   => groups.OrderBy(a => a.name, StringComparer.OrdinalIgnoreCase),
            "artist" => groups.OrderBy(a => a.artist, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.name, StringComparer.OrdinalIgnoreCase),
            "year"   => groups.OrderByDescending(a => a.Year).ThenBy(a => a.name, StringComparer.OrdinalIgnoreCase),
            _        => groups.OrderByDescending(a => a.dateAdded)
        };

        var allAlbums = sorted.ToList();
        var total = allAlbums.Count;
        var albums = allAlbums.Skip((page - 1) * limit).Take(limit).ToList();

        return Ok(new { total, page, limit, albums });
    }

    [HttpGet("albums/{id}")]
    public async Task<IActionResult> GetAlbum(int id)
    {
        var album = await _db.Albums.FirstOrDefaultAsync(a => a.Id == id);
        if (album == null) return NotFound();

        // Albums are grouped by NAME (see GetAlbums), so list EVERY track that shares this
        // album's name - otherwise a compilation opened from its single card would show only
        // the one row's track. Case-insensitive match mirrors the grouping key.
        var nameKey = MusicKey.Of(album.Name);
        var trackFavIds = _userFavs.GetFavouriteIds(CurrentUsername, "track");
        var tracks = await _db.Tracks.Where(t => t.AlbumKey == nameKey)
            .OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber).ThenBy(t => t.Title)
            .Select(t => new
            {
                t.Id, t.Title, t.Artist, t.AlbumArtist, t.Album, t.Genre, t.Year,
                t.TrackNumber, t.DiscNumber, t.Duration, t.Bitrate,
                t.Codec, t.FileSize, t.HasAlbumArt, t.AlbumArtCached,
                IsFavourite = trackFavIds.Contains(t.Id), t.PlayCount, t.Rating
            })
            .ToListAsync();

        // Header artist is taken ONLY from metadata - the album artist when every track
        // agrees on one, else the track artist when they all agree, else left blank. No
        // synthesised label ("Various Artists" etc.) is ever invented.
        var albumArtists = tracks.Select(t => t.AlbumArtist)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var trackArtists = tracks.Select(t => t.Artist)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var headerArtist = albumArtists.Count == 1 ? albumArtists[0]
            : (trackArtists.Count == 1 ? trackArtists[0] : "");

        return Ok(new
        {
            album.Id, album.Name, artist = headerArtist, album.Year, album.Genre,
            trackCount = tracks.Count, totalDuration = tracks.Sum(t => t.Duration),
            tracks
        });
    }

    [HttpPut("albums/{id:int}")]
    public async Task<IActionResult> UpdateAlbum(int id, [FromBody] System.Text.Json.JsonElement body)
    {
        try
        {
        var album = await _db.Albums.FindAsync(id);
        if (album == null) return NotFound(new { success = false, error = "Album not found" });

        if (body.TryGetProperty("name", out var n)) { album.Name = n.GetString() ?? ""; album.NameKey = MusicKey.Of(album.Name); }
        if (body.TryGetProperty("artist", out var a)) { album.Artist = a.GetString() ?? ""; album.ArtistKey = MusicKey.Of(album.Artist); }

        if (body.TryGetProperty("genre", out var g))
        {
            album.Genre = g.GetString() ?? "";
            // Cascade genre to every track by this artist (across all their albums)
            if (!string.IsNullOrEmpty(album.Genre) && !string.IsNullOrEmpty(album.Artist))
            {
                var artistTracks = await _db.Tracks
                    .Where(t => t.Artist == album.Artist)
                    .ToListAsync();
                foreach (var at in artistTracks) { at.Genre = album.Genre; at.GenreKey = MusicKey.Of(album.Genre); }

                // Also update genre on all other albums by this artist
                var artistAlbums = await _db.Albums
                    .Where(al => al.Artist == album.Artist && al.Id != album.Id)
                    .ToListAsync();
                foreach (var al in artistAlbums) al.Genre = album.Genre;
            }
        }

        if (body.TryGetProperty("year", out var y) && y.ValueKind == System.Text.Json.JsonValueKind.Number)
            album.Year = y.GetInt32();
        else if (body.TryGetProperty("year", out var yn) && yn.ValueKind == System.Text.Json.JsonValueKind.Null)
            album.Year = null;

        // Handle cover image upload (base64 data URI)
        if (body.TryGetProperty("coverImage", out var ci))
        {
            var dataUri = ci.GetString();
            if (!string.IsNullOrEmpty(dataUri) && dataUri.Contains(","))
            {
                var base64 = dataUri[(dataUri.IndexOf(',') + 1)..];
                var bytes = Convert.FromBase64String(base64);
                var artDir = Path.Combine(AppContext.BaseDirectory, "assets", "albumart");
                if (!Directory.Exists(artDir)) Directory.CreateDirectory(artDir);
                var filename = $"manual_album_{album.Id}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.jpg";
                var filePath = Path.Combine(artDir, filename);
                await System.IO.File.WriteAllBytesAsync(filePath, bytes);

                // Remove old manual cover if exists
                if (!string.IsNullOrEmpty(album.CoverArtPath) && album.CoverArtPath.StartsWith("manual_"))
                {
                    var oldPath = Path.Combine(artDir, album.CoverArtPath);
                    if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                }
                album.CoverArtPath = filename;
            }
        }

        // Handle picking an existing art file by filename
        if (body.TryGetProperty("coverArtPath", out var cap))
        {
            var pickedFile = cap.GetString();
            if (pickedFile != null) album.CoverArtPath = pickedFile;
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    // ─── Artists ────────────────────────────────────────────────────

    [HttpGet("artists")]
    public async Task<IActionResult> GetArtists(
        [FromQuery] string? search = null,
        [FromQuery] string? sort = "name",
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50,
        [FromQuery] string? grouping = null)
    {
        // Compute counts dynamically from tracks. The grouping key follows the
        // ArtistGrouping setting - the ALBUMARTIST tag (falling back to ARTIST when a
        // track has none) or the per-track ARTIST tag. Branch in C# so each query has a
        // single concrete GROUP BY key (rather than a parameterised CASE). The optional
        // ?grouping= param lets the Music page override the global setting per view.
        var (byAlbumArtist, byPrimary) = ResolveArtistGrouping(grouping);

        // SourceNames = the raw ARTIST strings folded into each group; used to build a collage
        // of collaborator portraits (a merged "Bakermat" tile still shows Bakermat + Savanna).
        List<(string Name, int TrackCount, int AlbumCount, List<string> SourceNames)> artistStats;
        if (byPrimary)
        {
            var raw = await _db.Tracks.Select(t => new { t.Artist, t.Album }).ToListAsync();
            var standalone = new HashSet<string>(
                raw.Select(t => (t.Artist ?? "").Trim()).Where(a => a.Length > 0 && !ArtistHasCollab(a)),
                StringComparer.OrdinalIgnoreCase);
            artistStats = raw
                .GroupBy(t => PrimaryArtistKey(t.Artist ?? "", standalone), StringComparer.OrdinalIgnoreCase)
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .Select(g => (
                    g.Key,
                    g.Count(),
                    g.Select(t => t.Album).Distinct().Count(),
                    g.Select(t => (t.Artist ?? "").Trim()).Where(s => s.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                )).ToList();
        }
        else if (byAlbumArtist)
        {
            // Group case-insensitively in memory (SQLite GROUP BY is case-sensitive, so
            // "Laurent Voulzy" and "Laurent voulzy" would otherwise split into two artists).
            // The displayed name is the most common original casing within the fold.
            var raw = await _db.Tracks.Select(t => new { t.Artist, t.AlbumArtist, t.Album }).ToListAsync();
            artistStats = raw
                .Select(t => new { Name = (!string.IsNullOrEmpty(t.AlbumArtist) ? t.AlbumArtist : t.Artist) ?? "", t.Album })
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .GroupBy(t => t.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => {
                    var name = g.GroupBy(x => x.Name.Trim()).OrderByDescending(x => x.Count()).First().Key;
                    return (name, g.Count(), g.Select(x => x.Album).Distinct().Count(), new List<string> { name });
                }).ToList();
        }
        else
        {
            // Case-insensitive fold of the per-track ARTIST tag (see the albumartist branch).
            var raw = await _db.Tracks.Select(t => new { t.Artist, t.Album }).ToListAsync();
            artistStats = raw
                .Where(t => !string.IsNullOrWhiteSpace(t.Artist))
                .GroupBy(t => t.Artist!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => {
                    var name = g.GroupBy(x => x.Artist!.Trim()).OrderByDescending(x => x.Count()).First().Key;
                    return (name, g.Count(), g.Select(x => x.Album).Distinct().Count(), new List<string> { name });
                }).ToList();
        }

        var statsQuery = artistStats.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            statsQuery = statsQuery.Where(a => a.Name.ToLower().Contains(search.ToLower()));

        statsQuery = sort switch
        {
            "albums" => statsQuery.OrderByDescending(a => a.AlbumCount),
            "tracks" => statsQuery.OrderByDescending(a => a.TrackCount),
            _ => statsQuery.OrderBy(a => a.Name)
        };

        var total = statsQuery.Count();
        var page_artists = statsQuery.Skip((page - 1) * limit).Take(limit).ToList();

        // Build image lookup including individual parts from EVERY source name in the group
        // ("A & B" → also lookup "A" and "B"; in primary mode this spans all folded variants).
        var lookupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in page_artists)
        {
            lookupNames.Add(a.Name);
            foreach (var src in a.SourceNames)
                foreach (var part in ArtistImageService.SplitArtistName(src))
                    lookupNames.Add(part);
        }
        var imageMap = await _db.Artists
            .Where(a => lookupNames.Contains(a.Name))
            .ToDictionaryAsync(a => a.Name, a => a.ImagePath, StringComparer.OrdinalIgnoreCase);

        var artists = page_artists.Select(a => {
            // Distinct portrait parts across all folded source names, primary name first, so a
            // merged tile shows a collage (Bakermat + Savanna, Alain Souchon + Jane Birkin, …).
            var parts = a.SourceNames
                .SelectMany(src => ArtistImageService.SplitArtistName(src))
                .Select(p => p.Trim()).Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(p => string.Equals(p, a.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            // Up to 4 distinct images - DB imageMap first, then the name-hash file cache
            // (populated by FetchAllMissingAsync pass 2 for parts without standalone DB entries).
            var imagePaths = parts
                .Select(p => {
                    if (imageMap.TryGetValue(p, out var img) && !string.IsNullOrEmpty(img)) return img;
                    return _artistImageService.GetCachedImageForName(p);
                })
                .Where(img => img != null).Distinct().Take(4).ToList();
            // Fallback: direct lookup on the group name itself
            if (imagePaths.Count == 0 && imageMap.TryGetValue(a.Name, out var direct) && !string.IsNullOrEmpty(direct))
                imagePaths.Add(direct);
            return new { name = a.Name, albumCount = a.AlbumCount, trackCount = a.TrackCount, imagePaths };
        }).ToList();

        return Ok(new { total, page, limit, artists });
    }

    [HttpPost("artists/fetch-images")]
    public IActionResult FetchArtistImages()
    {
        if (_artistImageService.IsFetchInProgress)
            return Conflict(new { message = "Fetch already in progress" });
        _ = Task.Run(() => _artistImageService.FetchAllMissingAsync());
        return Ok(new { message = "Artist portrait fetch started" });
    }

    [HttpGet("artists/fetch-images/status")]
    public IActionResult GetArtistFetchStatus()
    {
        return Ok(new { inProgress = _artistImageService.IsFetchInProgress });
    }

    [HttpGet("artists/by-name")]
    public async Task<IActionResult> GetArtistByName([FromQuery] string name, [FromQuery] string? grouping = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return BadRequest();

        var (byAlbumArtist, byPrimary) = ResolveArtistGrouping(grouping);

        List<Album> albums;
        int trackCount;
        if (byPrimary)
        {
            // The page's albums + count must fold the same collab variants the Artists grid did.
            var standalone = await GetStandaloneArtistSetAsync();
            albums = (await _db.Albums.ToListAsync())
                .Where(a => string.Equals(PrimaryArtistKey(a.Artist ?? "", standalone), name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.Year).ToList();
            trackCount = (await _db.Tracks.Select(t => t.Artist).ToListAsync())
                .Count(x => string.Equals(PrimaryArtistKey(x ?? "", standalone), name, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            // Case-insensitive match so a name like "Laurent Voulzy" folds every casing variant.
            // Compare on the Unicode-folded key (SQL lower() is ASCII-only - see MusicKey).
            var nameKey = MusicKey.Of(name);
            albums = await _db.Albums
                .Where(a => a.ArtistKey == nameKey)
                .OrderByDescending(a => a.Year)
                .ToListAsync();
            trackCount = byAlbumArtist
                ? await _db.Tracks.CountAsync(t => (t.AlbumArtistKey != "" ? t.AlbumArtistKey : t.ArtistKey) == nameKey)
                : await _db.Tracks.CountAsync(t => t.ArtistKey == nameKey);
        }

        var infoNameKey = MusicKey.Of(name);
        var artistInfo = await _db.Artists
            .Where(a => a.NameKey == infoNameKey)
            .FirstOrDefaultAsync();

        return Ok(new {
            artist = new {
                name,
                imagePath = artistInfo?.ImagePath,
                bio = artistInfo?.Bio,
                albumCount = albums.Count,
                trackCount
            },
            albums
        });
    }

    public class UpdateArtistRequest { public string? Bio { get; set; } }

    // Edit an artist's "fiche" (currently the bio). Upserts the Artist row so an artist
    // that only ever existed as a track tag can still be edited. Admin-only - the music
    // catalogue is shared across every user.
    [HttpPut("artists/by-name")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdateArtistByName([FromQuery] string name, [FromBody] UpdateArtistRequest req)
    {
        if (string.IsNullOrWhiteSpace(name)) return BadRequest();
        var artist = await _db.Artists.FirstOrDefaultAsync(a => a.Name == name);
        if (artist == null)
        {
            artist = new Artist { Name = name, NameKey = MusicKey.Of(name) };
            _db.Artists.Add(artist);
        }
        if (req?.Bio != null)
        {
            var bio = req.Bio.Trim();
            artist.Bio = bio.Length == 0 ? null : bio;
        }
        await _db.SaveChangesAsync();
        return Ok(new { success = true, bio = artist.Bio });
    }

    // Upload a custom artist portrait (the "miniature"). Stored under assets/singers as
    // singer_custom_{hash}.jpg (name-keyed, so it never collides with the id-keyed Deezer
    // cache) and recorded on the Artist row. Admin-only.
    [HttpPost("artists/image")]
    [Authorize(Roles = "admin")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> UploadArtistImage([FromQuery] string name, IFormFile? file)
    {
        if (string.IsNullOrWhiteSpace(name)) return BadRequest();
        if (file == null || file.Length == 0) return BadRequest(new { error = "No file provided" });

        var artist = await _db.Artists.FirstOrDefaultAsync(a => a.Name == name);
        if (artist == null) { artist = new Artist { Name = name, NameKey = MusicKey.Of(name) }; _db.Artists.Add(artist); }

        var dir = Path.Combine(AppContext.BaseDirectory, "assets", "singers");
        Directory.CreateDirectory(dir);
        var hash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(name.ToLowerInvariant())))[..12];
        var fileName = $"singer_custom_{hash}.jpg";
        var dest = Path.Combine(dir, fileName);

        try
        {
            using var img = await Image.LoadAsync(file.OpenReadStream());
            img.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(500, 500), Mode = ResizeMode.Crop }));
            await img.SaveAsJpegAsync(dest);
        }
        catch
        {
            return BadRequest(new { error = "Invalid image file" });
        }

        artist.ImagePath = fileName;
        await _db.SaveChangesAsync();
        // Cache-buster so the just-changed portrait refreshes immediately in the UI.
        return Ok(new { success = true, imagePath = fileName, v = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
    }

    // Clear a custom portrait (revert to the fetched/placeholder image). Admin-only.
    [HttpDelete("artists/image")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ClearArtistImage([FromQuery] string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return BadRequest();
        var artist = await _db.Artists.FirstOrDefaultAsync(a => a.Name == name);
        if (artist == null) return NotFound();
        artist.ImagePath = null;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpGet("artists/{id}")]
    public async Task<IActionResult> GetArtist(int id)
    {
        var artist = await _db.Artists.FindAsync(id);
        if (artist == null) return NotFound();

        var albums = await _db.Albums.Where(a => a.Artist == artist.Name)
            .OrderByDescending(a => a.Year).ToListAsync();

        return Ok(new { artist, albums });
    }

    // ─── Tracks / Songs ────────────────────────────────────────────

    [HttpGet("tracks/formats")]
    public async Task<IActionResult> GetTrackFormats()
    {
        var tracks = await _db.Tracks.Select(t => new { t.MimeType, t.Codec }).Distinct().ToListAsync();
        var formats = new HashSet<string>();
        foreach (var t in tracks.Where(t => !string.IsNullOrEmpty(t.MimeType)))
        {
            var m = t.MimeType.ToLower();
            var c = (t.Codec ?? "").ToLower();
            if      (c.Contains("alac"))  formats.Add("ALAC");
            else if (m.Contains("flac"))  formats.Add("FLAC");
            else if (m.Contains("mpeg"))  formats.Add("MP3");
            else if (m.Contains("mp4") || m.Contains("aac")) formats.Add("AAC");
            else if (m.Contains("wav"))   formats.Add("WAV");
            else if (m.Contains("ogg"))   formats.Add("OGG");
            else if (m.Contains("wma"))   formats.Add("WMA");
            else if (m.Contains("opus"))  formats.Add("OPUS");
            else if (m.Contains("aiff"))  formats.Add("AIFF");
            else if (m.Contains("ape"))   formats.Add("APE");
        }
        return Ok(formats.OrderBy(f => f));
    }

    [HttpGet("tracks")]
    public async Task<IActionResult> GetTracks(
        [FromQuery] string? search = null,
        [FromQuery] string? genre = null,
        [FromQuery] string? genres = null,
        [FromQuery] string? genreSearch = null,
        [FromQuery] bool hires = false,
        [FromQuery] string? artist = null,
        [FromQuery] string? sort = "title",
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50,
        [FromQuery] int? yearFrom = null,
        [FromQuery] int? yearTo = null,
        [FromQuery] bool favouritesOnly = false,
        [FromQuery] string? format = null,
        [FromQuery] string? customCategory = null,
        [FromQuery] string? customGenreId = null,
        [FromQuery] string? grouping = null)
    {
        var query = _db.Tracks.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            using var conn = OpenMusicDbRaw();
            var ids = FtsIds(conn, "tracks_fts", BuildFtsQuery(search), 5000);
            if (ids.Count > 0)
                query = query.Where(t => ids.Contains(t.Id));
            else
            {
                var s = MusicKey.Of(search);
                query = query.Where(t => t.TitleKey.Contains(s)
                    || t.ArtistKey.Contains(s)
                    || t.AlbumKey.Contains(s));
            }
        }
        if (!string.IsNullOrWhiteSpace(genre))
        {
            var gl = MusicKey.Of(genre);
            // Match single genre OR any position in a semicolon-delimited multi-genre string.
            // GenreKey preserves the ';' separators (only case is folded), so the boundary
            // matching below is unchanged - just Unicode-correct.
            query = query.Where(t =>
                t.GenreKey == gl ||
                t.GenreKey.StartsWith(gl + ";") ||
                t.GenreKey.EndsWith(";" + gl) ||
                t.GenreKey.Contains(";" + gl + ";"));
        }
        if (!string.IsNullOrWhiteSpace(genreSearch))
        {
            var gs = MusicKey.Of(genreSearch);
            query = query.Where(t => t.GenreKey.Contains(gs));
        }
        // Multi-genre (comma-separated) filter - used by the Explore "Moods & Activities"
        // buckets, where one mood maps to several genre terms. A track matches when any of
        // its genre tokens contains any term. Matched in memory (folded keys) so accents and
        // multi-genre strings compare correctly.
        if (!string.IsNullOrWhiteSpace(genres))
        {
            var terms = genres.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(g => MusicKey.Of(g)).Where(s => s.Length > 0).Distinct().ToList();
            if (terms.Count > 0)
            {
                var matchIds = (await _db.Tracks.Select(t => new { t.Id, t.GenreKey }).ToListAsync())
                    .Where(t => !string.IsNullOrEmpty(t.GenreKey) && terms.Any(term => t.GenreKey.Contains(term)))
                    .Select(t => t.Id).ToHashSet();
                query = matchIds.Count > 0 ? query.Where(t => matchIds.Contains(t.Id)) : query.Where(t => false);
            }
        }
        // Hi-Res audio = sample rate above the CD/standard 48 kHz ceiling (88.2/96/176/192 kHz).
        if (hires)
            query = query.Where(t => t.SampleRate > 48000);
        if (!string.IsNullOrWhiteSpace(artist))
        {
            var al = MusicKey.Of(artist);
            // Match the same field the Artists browse groups by, so the artist page's
            // track list matches its header count under every grouping mode. Honour the
            // same per-view ?grouping= override the Artists grid used.
            var (trByAlbumArtist, trByPrimary) = ResolveArtistGrouping(grouping);
            if (trByPrimary)
            {
                var standalone = await GetStandaloneArtistSetAsync();
                var members = (await _db.Tracks.Select(t => t.Artist).Distinct().ToListAsync())
                    .Where(a => string.Equals(PrimaryArtistKey(a ?? "", standalone), artist, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                query = query.Where(t => members.Contains(t.Artist));
            }
            else
            {
                query = trByAlbumArtist
                    ? query.Where(t => (t.AlbumArtistKey != "" ? t.AlbumArtistKey : t.ArtistKey) == al)
                    : query.Where(t => t.ArtistKey == al);
            }
        }
        if (yearFrom.HasValue)
            query = query.Where(t => t.Year >= yearFrom.Value);
        if (yearTo.HasValue)
            query = query.Where(t => t.Year <= yearTo.Value);
        if (favouritesOnly)
        {
            var favSet = _userFavs.GetFavouriteIds(CurrentUsername, "track");
            query = query.Where(t => favSet.Contains(t.Id));
        }
        if (!string.IsNullOrWhiteSpace(format))
        {
            switch (format.ToLower())
            {
                case "mp3":  query = query.Where(t => t.MimeType.Contains("mpeg")); break;
                case "flac": query = query.Where(t => t.MimeType.Contains("flac")); break;
                case "aac":  query = query.Where(t => (t.MimeType.Contains("mp4") || t.MimeType.Contains("aac")) && !t.Codec.ToLower().Contains("alac")); break;
                case "alac": query = query.Where(t => t.Codec.ToLower().Contains("alac")); break;
                case "wav":  query = query.Where(t => t.MimeType.Contains("wav")); break;
                case "ogg":  query = query.Where(t => t.MimeType.Contains("ogg")); break;
                case "wma":  query = query.Where(t => t.MimeType.Contains("wma")); break;
                case "opus": query = query.Where(t => t.MimeType.Contains("opus")); break;
                case "aiff": query = query.Where(t => t.MimeType.Contains("aiff")); break;
                case "ape":  query = query.Where(t => t.MimeType.Contains("ape")); break;
            }
        }
        if (!string.IsNullOrWhiteSpace(customCategory))
        {
            query = query.Where(t => t.CustomCategoryKey == MusicKey.Of(customCategory));
        }
        else if (!string.IsNullOrWhiteSpace(customGenreId))
        {
            string cgRulesJson = "[]";
            var directTrackIds = new HashSet<int>();
            using (var conn = OpenMusicDbRaw())
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Rules FROM CustomGenres WHERE Id = @id LIMIT 1";
                cmd.Parameters.AddWithValue("@id", customGenreId);
                cgRulesJson = cmd.ExecuteScalar()?.ToString() ?? "[]";

                using var cmdD = conn.CreateCommand();
                cmdD.CommandText = "SELECT TrackId FROM CustomGenreItems WHERE GenreId = @id";
                cmdD.Parameters.AddWithValue("@id", customGenreId);
                using var rD = cmdD.ExecuteReader();
                while (rD.Read()) directTrackIds.Add(rD.GetInt32(0));
            }
            var cgRules = TryDeserializeRules(cgRulesJson);
            var genreVals = cgRules.Where(r => r.Type == "genre").Select(r => r.Value).ToList();
            var folderVals = cgRules.Where(r => r.Type == "folder").Select(r => r.Value).ToList();
            var ruleIds = (genreVals.Count > 0 || folderVals.Count > 0)
                ? (await _db.Tracks
                    .Select(t => new { t.Id, t.Genre, t.CustomCategory })
                    .ToListAsync())
                    .Where(t =>
                        genreVals.Any(gv => t.Genre.Contains(gv, StringComparison.OrdinalIgnoreCase)) ||
                        folderVals.Any(fv => t.CustomCategory.Equals(fv, StringComparison.OrdinalIgnoreCase)))
                    .Select(t => t.Id)
                    .ToHashSet()
                : new HashSet<int>();
            var matchingIds = ruleIds.Union(directTrackIds).ToHashSet();
            if (matchingIds.Count > 0)
                query = query.Where(t => matchingIds.Contains(t.Id));
            else
                query = query.Where(t => false);
        }
        else
        {
            // No explicit filter - apply the user's exclusion settings
            var (musicExcluded, musicHidden) = _userFavs.GetMusicCategoryExclusions(CurrentUsername);
            var allExcluded = musicExcluded.Concat(musicHidden)
                .Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (allExcluded.Count > 0)
                query = query.Where(t => !allExcluded.Contains(t.CustomCategory));
        }
        // "Most played" is PER-USER. Track.PlayCount is a shared/global column that is not
        // maintained for music playback, so filtering/ordering by it returns nothing (the
        // "library is empty" bug). The real signal lives in users/{name}.db PlayCounts via
        // GetMostPlayed. Pull the user's top-100 played track ids and order the already
        // filtered query by that per-user count.
        if (sort == "mostplayed")
        {
            var userPlays = _userFavs.GetMostPlayed(CurrentUsername, "track", 100);
            if (userPlays.Count == 0)
                return Ok(new { total = 0, page, limit, tracks = new List<Track>() });

            var playMap = userPlays.ToDictionary(p => p.MediaId, p => p.Count);
            var playedIds = userPlays.Select(p => p.MediaId).ToList();
            query = query.Where(t => playedIds.Contains(t.Id));

            var favIdsMp = _userFavs.GetFavouriteIds(CurrentUsername, "track");
            var played = (await query.AsNoTracking().ToListAsync())
                .OrderByDescending(t => playMap.GetValueOrDefault(t.Id, 0))
                .ThenBy(t => t.Title)
                .ToList();
            foreach (var t in played) t.IsFavourite = favIdsMp.Contains(t.Id);
            var pageTracks = played.Skip((page - 1) * limit).Take(limit).ToList();
            return Ok(new { total = played.Count, page, limit, tracks = pageTracks });
        }

        query = sort switch
        {
            "artist" => query.OrderBy(t => t.Artist).ThenBy(t => t.Title),
            "album" => query.OrderBy(t => t.Album).ThenBy(t => t.TrackNumber),
            "genre" => query.OrderBy(t => t.Genre).ThenBy(t => t.Artist).ThenBy(t => t.Title),
            "recent" => query.OrderByDescending(t => t.DateAdded),
            "duration" => query.OrderByDescending(t => t.Duration),
            "random" => query.OrderBy(t => EF.Functions.Random()),
            _ => query.OrderBy(t => t.Title)
        };

        var total = await query.CountAsync();
        var favIds = _userFavs.GetFavouriteIds(CurrentUsername, "track");
        var tracks = await query.AsNoTracking().Skip((page - 1) * limit).Take(limit).ToListAsync();
        foreach (var t in tracks) t.IsFavourite = favIds.Contains(t.Id);

        return Ok(new { total, page, limit, tracks });
    }

    [HttpGet("tracks/recent")]
    public async Task<IActionResult> GetRecentTracks([FromQuery] int limit = 20)
    {
        var tracks = await _db.Tracks.OrderByDescending(t => t.DateAdded)
            .Take(limit)
            .Select(t => new { t.Id, t.Title, t.Artist, t.Album, t.Duration, t.HasAlbumArt, t.DateAdded })
            .ToListAsync();
        return Ok(tracks);
    }

    [HttpGet("tracks/mostplayed")]
    public async Task<IActionResult> GetMostPlayed([FromQuery] int limit = 20)
    {
        var userPlays = _userFavs.GetMostPlayed(CurrentUsername, "track", limit);
        if (userPlays.Count == 0) return Ok(Array.Empty<object>());

        var ids = userPlays.Select(p => p.MediaId).ToList();
        var tracks = await _db.Tracks.Where(t => ids.Contains(t.Id))
            .Select(t => new { t.Id, t.Title, t.Artist, t.Album, t.Duration, t.HasAlbumArt, t.AlbumArtCached })
            .ToListAsync();

        // Join with user play counts and preserve sort order
        var playMap = userPlays.ToDictionary(p => p.MediaId, p => p.Count);
        var result = tracks
            .Select(t => new { t.Id, t.Title, t.Artist, t.Album, t.Duration, PlayCount = playMap.GetValueOrDefault(t.Id), t.HasAlbumArt, t.AlbumArtCached })
            .OrderByDescending(t => t.PlayCount)
            .ToList();
        return Ok(result);
    }

    [HttpGet("tracks/random")]
    public async Task<IActionResult> GetRandomTrack()
    {
        var (musicExcluded, musicHidden) = _userFavs.GetMusicCategoryExclusions(CurrentUsername);
        var allExcluded = musicExcluded.Concat(musicHidden)
            .Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var query = _db.Tracks.AsQueryable();
        if (allExcluded.Count > 0)
            query = query.Where(t => !allExcluded.Contains(t.CustomCategory));
        var count = await query.CountAsync();
        if (count == 0) return NotFound();
        var skip = new Random().Next(count);
        var trackFavIds = _userFavs.GetFavouriteIds(CurrentUsername, "track");
        var track = await query.Skip(skip)
            .Select(t => new { t.Id, t.Title, t.Artist, t.Album, t.HasAlbumArt, IsFavourite = trackFavIds.Contains(t.Id) })
            .FirstAsync();
        return Ok(track);
    }

    [HttpGet("tracks/favourites")]
    public async Task<IActionResult> GetFavourites([FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        var favIds = _userFavs.GetFavouriteIds(CurrentUsername, "track");
        if (favIds.Count == 0) return Ok(new { total = 0, page, limit, tracks = Array.Empty<object>() });

        var query = _db.Tracks.Where(t => favIds.Contains(t.Id)).OrderBy(t => t.Title);
        var total = await query.CountAsync();
        var tracks = await query.Skip((page - 1) * limit).Take(limit).ToListAsync();
        // Mark all as favourite for the response
        foreach (var t in tracks) t.IsFavourite = true;
        return Ok(new { total, page, limit, tracks });
    }

    [HttpGet("tracks/{id:int}")]
    public async Task<IActionResult> GetTrack(int id)
    {
        var track = await _db.Tracks.FindAsync(id);
        if (track == null) return NotFound();
        var favIds = _userFavs.GetFavouriteIds(CurrentUsername, "track");
        return Ok(new
        {
            track.Id, track.Title, track.Artist, track.Album, track.AlbumArtist,
            track.Genre, track.Year, track.TrackNumber, track.Composer,
            track.Duration, track.Bitrate, track.SampleRate, track.Channels,
            track.Codec, track.MimeType, track.HasAlbumArt,
            track.AlbumArtCached, track.Rating, IsFavourite = favIds.Contains(track.Id),
            track.AlbumId
        });
    }

    [HttpPut("tracks/{id:int}")]
    public async Task<IActionResult> UpdateTrack(int id, [FromBody] System.Text.Json.JsonElement body)
    {
        var track = await _db.Tracks.FindAsync(id);
        if (track == null) return NotFound();

        if (body.TryGetProperty("title", out var t)) { track.Title = t.GetString() ?? ""; track.TitleKey = MusicKey.Of(track.Title); }
        if (body.TryGetProperty("artist", out var a)) { track.Artist = a.GetString() ?? ""; track.ArtistKey = MusicKey.Of(track.Artist); }
        if (body.TryGetProperty("album", out var al)) { track.Album = al.GetString() ?? ""; track.AlbumKey = MusicKey.Of(track.Album); }
        if (body.TryGetProperty("albumArtist", out var aa)) { track.AlbumArtist = aa.GetString() ?? ""; track.AlbumArtistKey = MusicKey.Of(track.AlbumArtist); }
        if (body.TryGetProperty("genre", out var g)) { track.Genre = g.GetString() ?? ""; track.GenreKey = MusicKey.Of(track.Genre); }
        if (body.TryGetProperty("composer", out var comp)) track.Composer = comp.GetString() ?? "";
        if (body.TryGetProperty("year", out var y) && y.ValueKind == System.Text.Json.JsonValueKind.Number)
            track.Year = y.GetInt32();
        else if (body.TryGetProperty("year", out var yn) && yn.ValueKind == System.Text.Json.JsonValueKind.Null)
            track.Year = null;
        if (body.TryGetProperty("trackNumber", out var tn) && tn.ValueKind == System.Text.Json.JsonValueKind.Number)
            track.TrackNumber = tn.GetInt32();
        else if (body.TryGetProperty("trackNumber", out var tnn) && tnn.ValueKind == System.Text.Json.JsonValueKind.Null)
            track.TrackNumber = null;

        // Handle cover image upload (base64 data URI)
        if (body.TryGetProperty("coverImage", out var ci))
        {
            var dataUri = ci.GetString();
            if (!string.IsNullOrEmpty(dataUri) && dataUri.Contains(","))
            {
                var base64 = dataUri[(dataUri.IndexOf(',') + 1)..];
                var bytes = Convert.FromBase64String(base64);
                var artDir = Path.Combine(AppContext.BaseDirectory, "assets", "albumart");
                if (!Directory.Exists(artDir)) Directory.CreateDirectory(artDir);
                var filename = $"manual_track_{track.Id}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.jpg";
                var filePath = Path.Combine(artDir, filename);
                await System.IO.File.WriteAllBytesAsync(filePath, bytes);

                // Remove old manual cover if exists
                if (!string.IsNullOrEmpty(track.AlbumArtCached) && track.AlbumArtCached.StartsWith("manual_"))
                {
                    var oldPath = Path.Combine(artDir, track.AlbumArtCached);
                    if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                }
                track.AlbumArtCached = filename;
                track.HasAlbumArt = true;
            }
        }

        // Handle picking an existing art file by filename
        if (body.TryGetProperty("coverArtPath", out var cap))
        {
            var pickedFile = cap.GetString();
            if (pickedFile != null) { track.AlbumArtCached = pickedFile; track.HasAlbumArt = true; }
        }

        // Handle applying a fanart.tv artist image as the cover
        if (body.TryGetProperty("coverFanartUrl", out var cfu))
        {
            var furl = cfu.GetString();
            static bool IsFanartUrl(string? u) =>
                !string.IsNullOrEmpty(u) &&
                Uri.TryCreate(u, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps &&
                (uri.Host.Equals("fanart.tv", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.EndsWith(".fanart.tv", StringComparison.OrdinalIgnoreCase));

            if (IsFanartUrl(furl))
            {
                var file = await new FanartService().DownloadToAlbumArtAsync(
                    furl!, $"fanart_track_{track.Id}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.jpg");
                if (file != null)
                {
                    var artDir = Path.Combine(AppContext.BaseDirectory, "assets", "albumart");
                    if (!string.IsNullOrEmpty(track.AlbumArtCached) &&
                        (track.AlbumArtCached.StartsWith("manual_") || track.AlbumArtCached.StartsWith("fanart_track_")))
                    {
                        var oldPath = Path.Combine(artDir, track.AlbumArtCached);
                        if (System.IO.File.Exists(oldPath)) try { System.IO.File.Delete(oldPath); } catch { }
                    }
                    track.AlbumArtCached = file;
                    track.HasAlbumArt = true;
                }
            }
        }

        // Apply genre to all tracks + albums by the same artist when requested
        if (body.TryGetProperty("applyGenreToArtist", out var ata) &&
            ata.ValueKind == System.Text.Json.JsonValueKind.True &&
            !string.IsNullOrEmpty(track.Artist) &&
            !string.IsNullOrEmpty(track.Genre))
        {
            var artistTracks = await _db.Tracks
                .Where(x => x.Artist == track.Artist && x.Id != track.Id)
                .ToListAsync();
            foreach (var at in artistTracks) { at.Genre = track.Genre; at.GenreKey = MusicKey.Of(track.Genre); }

            var artistAlbums = await _db.Albums
                .Where(ab => ab.Artist == track.Artist)
                .ToListAsync();
            foreach (var ab in artistAlbums) ab.Genre = track.Genre;
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    /// <summary>Lists fanart.tv artist images for this track's artist. Resolves a MusicBrainz MBID
    /// from the artist name (cached), then queries fanart.tv's music endpoint.</summary>
    [HttpGet("tracks/{id}/fanart")]
    public async Task<IActionResult> GetTrackFanart(int id)
    {
        var track = await _db.Tracks.FindAsync(id);
        if (track == null) return NotFound();

        var fanartKey = _config.Config.Metadata.FanartApiKey;
        if (string.IsNullOrWhiteSpace(fanartKey))
            return Ok(new { configured = false, images = Array.Empty<object>() });

        var artist = !string.IsNullOrWhiteSpace(track.Artist) ? track.Artist : track.AlbumArtist;
        if (string.IsNullOrWhiteSpace(artist))
            return Ok(new { configured = true, images = Array.Empty<object>() });

        var fanart = new FanartService();
        var mbid = await fanart.ResolveArtistMbidAsync(artist);
        if (string.IsNullOrWhiteSpace(mbid))
            return Ok(new { configured = true, images = Array.Empty<object>() });

        var images = await fanart.GetMusicArtistImagesAsync(fanartKey, mbid);
        if (images == null)
            return Ok(new { configured = true, images = Array.Empty<object>() });

        return Ok(new
        {
            configured = true,
            images = images.Select(i => new { type = i.Type, url = i.Url, lang = i.Lang, likes = i.Likes })
        });
    }

    // ─── Genres ─────────────────────────────────────────────────────

    [HttpGet("genres")]
    public async Task<IActionResult> GetGenres()
    {
        var raw = await _db.Tracks
            .Where(t => t.Genre != "")
            .Select(t => t.Genre)
            .ToListAsync();

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in raw)
            foreach (var g in entry.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                counts[g] = counts.GetValueOrDefault(g, 0) + 1;

        // Gather up to 4 album IDs with cover art per genre for thumbnail collages.
        // Use track genres (same source as counts above) so every genre gets matching art.
        var tracksWithArt = await _db.Tracks
            .Where(t => t.HasAlbumArt && t.AlbumId.HasValue)
            .Select(t => new { AlbumId = t.AlbumId!.Value, t.Genre })
            .ToListAsync();

        var genreAlbums = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in tracksWithArt)
        {
            if (string.IsNullOrEmpty(track.Genre)) continue;
            foreach (var g in track.Genre.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!genreAlbums.TryGetValue(g, out var list))
                    genreAlbums[g] = list = new List<int>();
                if (list.Count < 4 && !list.Contains(track.AlbumId))
                    list.Add(track.AlbumId);
            }
        }

        var genres = counts
            .Select(kv => new {
                name = kv.Key,
                count = kv.Value,
                albumIds = genreAlbums.TryGetValue(kv.Key, out var ids) ? (IEnumerable<int>)ids : Array.Empty<int>()
            })
            .OrderBy(g => g.name)
            .ToList();

        return Ok(genres);
    }

    [HttpGet("tracks/custom-categories")]
    public async Task<IActionResult> GetTrackCustomCategories()
    {
        var (musicExcluded, musicHidden) = _userFavs.GetMusicCategoryExclusions(CurrentUsername);
        var hiddenSet = new HashSet<string>(musicHidden, StringComparer.OrdinalIgnoreCase);
        var excludedSet = new HashSet<string>(musicExcluded, StringComparer.OrdinalIgnoreCase);

        var cats = await _db.Tracks
            .Where(t => t.CustomCategory != null && t.CustomCategory != "")
            .GroupBy(t => t.CustomCategory)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderBy(g => g.name)
            .ToListAsync();

        // Hidden categories are completely invisible; excluded ones are shown with a flag
        var result = cats
            .Where(c => !hiddenSet.Contains(c.name))
            .Select(c => new { c.name, c.count, excludedFromLibrary = excludedSet.Contains(c.name) })
            .ToList();
        return Ok(result);
    }

    [HttpGet("decades")]
    public async Task<IActionResult> GetDecades()
    {
        var currentYear = DateTime.UtcNow.Year;
        var decades = await _db.Tracks
            .Where(t => t.Year >= 1900 && t.Year <= currentYear + 1)
            .GroupBy(t => t.Year / 10 * 10)
            .Select(g => new { decade = g.Key, count = g.Count() })
            .OrderBy(d => d.decade)
            .ToListAsync();
        return Ok(decades);
    }

    // ─── Music Explore (TIDAL-style browse landing) ─────────────────────
    // Moods & Activities have no editorial source in a self-hosted library, so each mood is
    // mapped to a set of genre terms present in the user's own tags. A track belongs to a mood
    // when any of its genre tokens contains any of the mood's terms (case-insensitive). The
    // frontend sends the same term list back to GET tracks?genres=... when a mood is opened,
    // so this table is the single source of truth for the mapping.
    private static readonly (string Key, string Name, string[] Genres)[] _moodDefs = new[]
    {
        ("party",    "Party",     new[] { "dance", "electronic", "edm", "house", "techno", "disco", "pop" }),
        ("chill",    "Chill",     new[] { "jazz", "ambient", "chill", "lo-fi", "lofi", "lounge", "downtempo" }),
        ("workout",  "Workout",   new[] { "rock", "hip", "rap", "electronic", "metal", "punk", "drum" }),
        ("focus",    "Focus",     new[] { "classical", "instrumental", "ambient", "piano", "soundtrack", "score" }),
        ("romance",  "Romance",   new[] { "r&b", "rnb", "soul", "ballad", "love" }),
        ("sleep",    "Sleep",     new[] { "ambient", "classical", "new age", "piano", "meditation", "sleep" }),
        ("feelgood", "Feel Good", new[] { "pop", "funk", "soul", "reggae", "disco", "motown" }),
        ("roadtrip", "Road Trip", new[] { "rock", "country", "indie", "folk", "americana" }),
    };

    [HttpGet("music/explore")]
    public async Task<IActionResult> GetMusicExplore()
    {
        var currentYear = DateTime.UtcNow.Year;

        // One pass over the library - genre / year / sample-rate / art in a single projection.
        var rows = await _db.Tracks
            .Select(t => new { t.Genre, t.Year, t.SampleRate, t.HasAlbumArt, t.AlbumId })
            .ToListAsync();

        var genreCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var genreArt    = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var moodCounts  = new Dictionary<string, int>();
        var moodArt     = new Dictionary<string, List<int>>();
        foreach (var m in _moodDefs) { moodCounts[m.Key] = 0; moodArt[m.Key] = new List<int>(); }
        var decadeCounts = new Dictionary<int, int>();
        var decadeArt    = new Dictionary<int, List<int>>();
        int hiresCount = 0;
        var hiresArt   = new List<int>();

        static void AddArt(List<int> list, int? albumId, bool hasArt, int cap)
        {
            if (!hasArt || !albumId.HasValue) return;
            if (list.Count < cap && !list.Contains(albumId.Value)) list.Add(albumId.Value);
        }

        foreach (var r in rows)
        {
            var tokens = string.IsNullOrEmpty(r.Genre)
                ? Array.Empty<string>()
                : r.Genre.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var g in tokens)
            {
                genreCounts[g] = genreCounts.GetValueOrDefault(g, 0) + 1;
                if (!genreArt.TryGetValue(g, out var gl)) genreArt[g] = gl = new List<int>();
                AddArt(gl, r.AlbumId, r.HasAlbumArt, 4);
            }

            if (tokens.Length > 0)
            {
                foreach (var m in _moodDefs)
                {
                    bool match = tokens.Any(tok => m.Genres.Any(term =>
                        tok.Contains(term, StringComparison.OrdinalIgnoreCase)));
                    if (match)
                    {
                        moodCounts[m.Key]++;
                        AddArt(moodArt[m.Key], r.AlbumId, r.HasAlbumArt, 4);
                    }
                }
            }

            if (r.Year.HasValue && r.Year.Value >= 1900 && r.Year.Value <= currentYear + 1)
            {
                int dec = r.Year.Value / 10 * 10;
                decadeCounts[dec] = decadeCounts.GetValueOrDefault(dec, 0) + 1;
                if (!decadeArt.TryGetValue(dec, out var dl)) decadeArt[dec] = dl = new List<int>();
                AddArt(dl, r.AlbumId, r.HasAlbumArt, 4);
            }

            if (r.SampleRate > 48000)
            {
                hiresCount++;
                AddArt(hiresArt, r.AlbumId, r.HasAlbumArt, 6);
            }
        }

        var genres = genreCounts
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
            .Select(kv => new { name = kv.Key, count = kv.Value, albumIds = genreArt[kv.Key] })
            .ToList();

        var moods = _moodDefs
            .Where(m => moodCounts[m.Key] > 0)
            .Select(m => new { key = m.Key, name = m.Name, count = moodCounts[m.Key], albumIds = moodArt[m.Key], genres = m.Genres })
            .ToList();

        var decades = decadeCounts
            .OrderByDescending(kv => kv.Key)
            .Select(kv => new { decade = kv.Key, count = kv.Value, albumIds = decadeArt[kv.Key] })
            .ToList();

        // Playlists (up to 30). Use the stored cover when present; otherwise build a small
        // collage from the album art of the playlist's first tracks.
        var playlistsRaw = _userFavs.GetPlaylists(CurrentUsername).Take(30).ToList();
        var needArt = new List<(int PlId, List<int> TrackIds)>();
        foreach (var p in playlistsRaw)
        {
            var cover = p.TryGetValue("coverImagePath", out var c) ? c?.ToString() ?? "" : "";
            if (!string.IsNullOrEmpty(cover)) continue;
            int plId = Convert.ToInt32(p["id"]);
            var (_, tr) = _userFavs.GetPlaylist(CurrentUsername, plId);
            var tids = tr.Take(8).Select(e => Convert.ToInt32(e["trackId"])).ToList();
            if (tids.Count > 0) needArt.Add((plId, tids));
        }
        var allNeeded = needArt.SelectMany(n => n.TrackIds).Distinct().ToList();
        var artMap = allNeeded.Count == 0
            ? new Dictionary<int, int>()
            : await _db.Tracks.Where(t => allNeeded.Contains(t.Id) && t.HasAlbumArt && t.AlbumId.HasValue)
                .Select(t => new { t.Id, AlbumId = t.AlbumId!.Value })
                .ToDictionaryAsync(x => x.Id, x => x.AlbumId);
        var plArt = new Dictionary<int, List<int>>();
        foreach (var n in needArt)
        {
            var ids = new List<int>();
            foreach (var tid in n.TrackIds)
                if (artMap.TryGetValue(tid, out var aid) && !ids.Contains(aid)) { ids.Add(aid); if (ids.Count >= 4) break; }
            plArt[n.PlId] = ids;
        }
        var playlists = playlistsRaw.Select(p =>
        {
            int plId = Convert.ToInt32(p["id"]);
            return new
            {
                id = plId,
                name = p["name"]?.ToString() ?? "",
                trackCount = p.TryGetValue("trackCount", out var tc) ? Convert.ToInt32(tc) : 0,
                coverImagePath = p.TryGetValue("coverImagePath", out var cv) ? cv?.ToString() ?? "" : "",
                albumIds = plArt.TryGetValue(plId, out var a) ? a : new List<int>()
            };
        }).ToList();

        return Ok(new
        {
            genres,
            moods,
            decades,
            playlists,
            hires = new { count = hiresCount, albumIds = hiresArt }
        });
    }

    // Albums grouped by genre - powers the "Albums" music view (one horizontal album-cover
    // row per genre). Reuses the same NAME grouping as GetAlbums so compilations collapse into
    // a single card, then buckets each album under its PRIMARY genre (first token of the album
    // Genre tag) so an album appears in exactly one row. Each genre returns up to `perGenre`
    // albums (newest first) plus the true total so the UI can offer "View all".
    [HttpGet("music/albums-by-genre")]
    public async Task<IActionResult> GetAlbumsByGenre([FromQuery] int perGenre = 24)
    {
        if (perGenre < 1) perGenre = 24;
        if (perGenre > 100) perGenre = 100;

        var rows = await _db.Albums.Select(a => new
        {
            a.Id, a.Name, a.Artist, a.Genre, a.CoverArtPath, a.TrackCount, a.DateAdded
        }).ToListAsync();

        // Merge albums that share the same complete NAME (mirrors GetAlbums) - collapses a
        // compilation's per-artist duplicate rows into one object. Artist stays 100% metadata
        // derived; genre is the first non-empty Genre tag among the merged rows.
        var merged = rows
            .GroupBy(a => (a.Name ?? "").Trim().ToLowerInvariant())
            .Select(g =>
            {
                var rep = g.FirstOrDefault(a => !string.IsNullOrEmpty(a.CoverArtPath))
                          ?? g.OrderByDescending(a => a.TrackCount).ThenBy(a => a.Id).First();
                var artists = g.Select(a => a.Artist)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var genre = g.Select(a => a.Genre).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";
                return new
                {
                    id = rep.Id,
                    name = rep.Name,
                    artist = artists.Count == 1 ? artists[0] : "",
                    genre,
                    dateAdded = g.Max(a => a.DateAdded)
                };
            })
            .ToList();

        // Drop orphaned empty albums (mirrors GetAlbums) - keep only names that still have a
        // real track, matched on the accent-safe AlbumKey.
        var liveAlbumKeys = (await _db.Tracks
                .Where(t => t.AlbumKey != null && t.AlbumKey != "")
                .Select(t => t.AlbumKey).Distinct().ToListAsync())
            .ToHashSet();
        merged = merged.Where(a => liveAlbumKeys.Contains(MusicKey.Of(a.name))).ToList();

        // Bucket each album under its primary genre (first ';'-separated token).
        var buckets = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
        var counts  = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in merged.OrderByDescending(a => a.dateAdded))
        {
            var primary = string.IsNullOrWhiteSpace(a.genre)
                ? ""
                : a.genre.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .FirstOrDefault() ?? "";
            if (string.IsNullOrWhiteSpace(primary)) continue;   // skip untagged albums

            counts[primary] = counts.GetValueOrDefault(primary, 0) + 1;
            if (!buckets.TryGetValue(primary, out var list)) buckets[primary] = list = new List<object>();
            if (list.Count < perGenre)
                list.Add(new { a.id, a.name, a.artist });
        }

        var genres = counts
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new { name = kv.Key, count = kv.Value, albums = buckets[kv.Key] })
            .ToList();

        return Ok(new { genres });
    }

    // ─── Auto-Generated Playlist Config (per-user) ──────────────────

    private static readonly System.Text.Json.JsonSerializerOptions _camelCase = new()
        { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };

    [HttpGet("agp-config")]
    public IActionResult GetAgpConfig()
    {
        var json = _userFavs.GetUserPref(CurrentUsername, "agp_config");
        if (string.IsNullOrEmpty(json))
            return Ok(new { activeTypes = Array.Empty<string>(), excludedDecades = Array.Empty<int>() });
        try
        {
            var dto = System.Text.Json.JsonSerializer.Deserialize<AgpConfigDto>(json, _camelCase);
            return Ok(dto ?? new AgpConfigDto());
        }
        catch { return Ok(new AgpConfigDto()); }
    }

    [HttpPost("agp-config")]
    public IActionResult SaveAgpConfig([FromBody] AgpConfigDto dto)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(dto, _camelCase);
        _userFavs.SetUserPref(CurrentUsername, "agp_config", json);
        return Ok(new { success = true });
    }

    // ─── Category Settings ───────────────────────────────────────────

    [HttpGet("category-settings")]
    public IActionResult GetCategorySettings()
    {
        var (excl, hidden) = _userFavs.GetMusicCategoryExclusions(CurrentUsername);
        var videoHidden = _userFavs.GetVideoCategoryHidden(CurrentUsername);
        var hiddenCustomGenres = _userFavs.GetHiddenCustomGenres(CurrentUsername);
        return Ok(new {
            music = new { excludedFromLibrary = excl, hidden },
            video = new { hidden = videoHidden },
            hiddenCustomGenres
        });
    }

    [HttpPost("category-settings")]
    public IActionResult SaveCategorySettings([FromBody] CategorySettingsDto dto)
    {
        var musicJson = System.Text.Json.JsonSerializer.Serialize(dto.Music, _camelCase);
        _userFavs.SetUserPref(CurrentUsername, "music_category_settings", musicJson);
        var videoJson = System.Text.Json.JsonSerializer.Serialize(dto.Video, _camelCase);
        _userFavs.SetUserPref(CurrentUsername, "video_category_settings", videoJson);
        if (dto.HiddenCustomGenres != null)
            _userFavs.SetHiddenCustomGenres(CurrentUsername, dto.HiddenCustomGenres);
        return Ok(new { success = true });
    }

    /// <summary>Admin: get category settings for another user.</summary>
    [HttpGet("category-settings/user/{username}")]
    public IActionResult GetUserCategorySettings(string username)
    {
        var currentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (currentRole != "admin") return Forbid();
        var (excl, hidden) = _userFavs.GetMusicCategoryExclusions(username);
        var videoHidden = _userFavs.GetVideoCategoryHidden(username);
        var hiddenCustomGenres = _userFavs.GetHiddenCustomGenres(username);
        return Ok(new {
            music = new { excludedFromLibrary = excl, hidden },
            video = new { hidden = videoHidden },
            hiddenCustomGenres
        });
    }

    /// <summary>Admin: set category settings for another user.</summary>
    [HttpPost("category-settings/user/{username}")]
    public IActionResult SaveUserCategorySettings(string username, [FromBody] CategorySettingsDto dto)
    {
        var currentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (currentRole != "admin") return Forbid();
        var musicJson = System.Text.Json.JsonSerializer.Serialize(dto.Music, _camelCase);
        _userFavs.SetUserPref(username, "music_category_settings", musicJson);
        var videoJson = System.Text.Json.JsonSerializer.Serialize(dto.Video, _camelCase);
        _userFavs.SetUserPref(username, "video_category_settings", videoJson);
        if (dto.HiddenCustomGenres != null)
            _userFavs.SetHiddenCustomGenres(username, dto.HiddenCustomGenres);
        return Ok(new { success = true });
    }

    // ─── Custom Genres ────────────────────────────────────────────

    private static List<CustomGenreRule> TryDeserializeRules(string rulesJson)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<List<CustomGenreRule>>(rulesJson, _camelCase) ?? new(); }
        catch { return new(); }
    }

    private SqliteConnection OpenMusicDbRaw()
    {
        var cs = _db.Database.GetConnectionString();
        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }

    // Builds an FTS5 MATCH expression: each whitespace-delimited token becomes "token"*
    // (quoted to neutralise FTS5 operators, * for prefix matching).
    private static string BuildFtsQuery(string input) =>
        string.Join(" ",
            input.Trim()
                 .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(t => $"\"{t.Replace("\"", "")}\"*"));

    // Returns rowids from an FTS5 virtual table ordered by BM25 rank.
    private static List<int> FtsIds(SqliteConnection conn, string table, string ftsQuery, int limit)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT rowid FROM {table} WHERE {table} MATCH @q ORDER BY rank LIMIT @lim";
            cmd.Parameters.AddWithValue("@q", ftsQuery);
            cmd.Parameters.AddWithValue("@lim", limit);
            var ids = new List<int>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) ids.Add(r.GetInt32(0));
            return ids;
        }
        catch { return []; }
    }

    private SqliteConnection OpenVideoDbRaw()
    {
        var cs = _videoDb.Database.GetConnectionString();
        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }

    [HttpGet("custom-genres")]
    public async Task<IActionResult> GetCustomGenres([FromQuery] string domain = "music")
    {
        var hidden = _userFavs.GetHiddenCustomGenres(CurrentUsername);
        var hiddenSet = new HashSet<string>(hidden, StringComparer.OrdinalIgnoreCase);

        if (domain == "music")
        {
            var genres = new List<(string Id, string Name, string Rules)>();
            using (var conn = OpenMusicDbRaw())
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, Name, Rules FROM CustomGenres ORDER BY Name";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    genres.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }

            var allTracks = await _db.Tracks
                .Select(t => new { t.Id, t.Genre, t.CustomCategory })
                .ToListAsync();

            var result = genres
                .Where(g => !hiddenSet.Contains(g.Id))
                .Select(g =>
                {
                    var rules = TryDeserializeRules(g.Rules);
                    var genreVals = rules.Where(r => r.Type == "genre").Select(r => r.Value).ToList();
                    var folderVals = rules.Where(r => r.Type == "folder").Select(r => r.Value).ToList();
                    var ruleIds = (genreVals.Count > 0 || folderVals.Count > 0)
                        ? allTracks
                            .Where(t =>
                                genreVals.Any(gv => t.Genre.Contains(gv, StringComparison.OrdinalIgnoreCase)) ||
                                folderVals.Any(fv => t.CustomCategory.Equals(fv, StringComparison.OrdinalIgnoreCase)))
                            .Select(t => t.Id)
                            .ToHashSet()
                        : new HashSet<int>();
                    var directIds = new HashSet<int>();
                    using (var conn2 = OpenMusicDbRaw())
                    {
                        using var cmd2 = conn2.CreateCommand();
                        cmd2.CommandText = "SELECT TrackId FROM CustomGenreItems WHERE GenreId = @gid";
                        cmd2.Parameters.AddWithValue("@gid", g.Id);
                        using var r2 = cmd2.ExecuteReader();
                        while (r2.Read()) directIds.Add(r2.GetInt32(0));
                    }
                    var count = ruleIds.Union(directIds).Count();
                    return new { g.Id, g.Name, rules = g.Rules, count, domain = "music" };
                })
                .ToList();
            return Ok(result);
        }
        else
        {
            var genres = new List<(string Id, string Name, string Domain, string Rules, string Poster)>();
            using (var conn = OpenVideoDbRaw())
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, Name, Domain, Rules, Poster FROM CustomGenres WHERE Domain = @d ORDER BY Name";
                cmd.Parameters.AddWithValue("@d", domain);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    genres.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? "" : reader.GetString(4)));
            }

            var allVideos = await _videoDb.Videos
                .Where(v => v.MediaType == domain)
                .Select(v => new { v.Id, Genre = v.Genre ?? "", CustomCategory = v.CustomCategory ?? "" })
                .ToListAsync();

            var result = genres
                .Where(g => !hiddenSet.Contains(g.Id))
                .Select(g =>
                {
                    var rules = TryDeserializeRules(g.Rules);
                    var genreVals = rules.Where(r => r.Type == "genre").Select(r => r.Value).ToList();
                    var folderVals = rules.Where(r => r.Type == "folder").Select(r => r.Value).ToList();
                    var ruleIds = (genreVals.Count > 0 || folderVals.Count > 0)
                        ? allVideos
                            .Where(v =>
                                genreVals.Any(gv => v.Genre.Contains(gv, StringComparison.OrdinalIgnoreCase)) ||
                                folderVals.Any(fv => v.CustomCategory.Equals(fv, StringComparison.OrdinalIgnoreCase)))
                            .Select(v => v.Id)
                            .ToHashSet()
                        : new HashSet<int>();
                    // Also count directly assigned videos
                    var directIds = new HashSet<int>();
                    using (var conn2 = OpenVideoDbRaw())
                    {
                        using var cmd2 = conn2.CreateCommand();
                        cmd2.CommandText = "SELECT VideoId FROM CustomGenreItems WHERE GenreId = @gid";
                        cmd2.Parameters.AddWithValue("@gid", g.Id);
                        using var r2 = cmd2.ExecuteReader();
                        while (r2.Read()) directIds.Add(r2.GetInt32(0));
                    }
                    var count = ruleIds.Union(directIds).Count();
                    return new { g.Id, g.Name, rules = g.Rules, count, g.Domain, poster = g.Poster };
                })
                .ToList();
            return Ok(result);
        }
    }

    [HttpPost("custom-genres")]
    public IActionResult CreateCustomGenre([FromBody] CreateCustomGenreDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name required");
        var id = Guid.NewGuid().ToString();
        var rulesJson = System.Text.Json.JsonSerializer.Serialize(dto.Rules ?? new(), _camelCase);

        if (dto.Domain == "music")
        {
            using var conn = OpenMusicDbRaw();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO CustomGenres (Id, Name, Rules, CreatedBy) VALUES (@id, @name, @rules, @by)";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@name", dto.Name.Trim());
            cmd.Parameters.AddWithValue("@rules", rulesJson);
            cmd.Parameters.AddWithValue("@by", CurrentUsername);
            cmd.ExecuteNonQuery();
        }
        else
        {
            using var conn = OpenVideoDbRaw();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO CustomGenres (Id, Name, Domain, Rules, CreatedBy) VALUES (@id, @name, @domain, @rules, @by)";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@name", dto.Name.Trim());
            cmd.Parameters.AddWithValue("@domain", dto.Domain ?? "movie");
            cmd.Parameters.AddWithValue("@rules", rulesJson);
            cmd.Parameters.AddWithValue("@by", CurrentUsername);
            cmd.ExecuteNonQuery();
        }
        return Ok(new { id, success = true });
    }

    [HttpPut("custom-genres/{id}")]
    public IActionResult UpdateCustomGenre(string id, [FromBody] CreateCustomGenreDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name required");
        var rulesJson = System.Text.Json.JsonSerializer.Serialize(dto.Rules ?? new(), _camelCase);

        if (dto.Domain == "music")
        {
            using var conn = OpenMusicDbRaw();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE CustomGenres SET Name = @name, Rules = @rules WHERE Id = @id";
            cmd.Parameters.AddWithValue("@name", dto.Name.Trim());
            cmd.Parameters.AddWithValue("@rules", rulesJson);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
        else
        {
            using var conn = OpenVideoDbRaw();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE CustomGenres SET Name = @name, Rules = @rules WHERE Id = @id";
            cmd.Parameters.AddWithValue("@name", dto.Name.Trim());
            cmd.Parameters.AddWithValue("@rules", rulesJson);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
        return Ok(new { success = true });
    }

    /// <summary>Persists the poster tile chosen for a video custom genre so the
    /// choice follows the account across devices (was previously client-only).
    /// Video-domain custom genres only - the music genre bar has no poster picker.</summary>
    [HttpPut("custom-genres/{id}/poster")]
    public IActionResult SetCustomGenrePoster(string id, [FromBody] SetCustomGenrePosterDto dto)
    {
        using var conn = OpenVideoDbRaw();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE CustomGenres SET Poster = @poster WHERE Id = @id";
        cmd.Parameters.AddWithValue("@poster", (object?)(dto?.Poster) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        var rows = cmd.ExecuteNonQuery();
        return Ok(new { success = rows > 0, poster = dto?.Poster ?? "" });
    }

    public class SetCustomGenrePosterDto { public string? Poster { get; set; } }

    /// <summary>Poster overrides for STANDARD genres (Action, Comedy…), which have no
    /// row of their own. Returns a { genreName: poster } map for the given domain.</summary>
    [HttpGet("genre-posters")]
    public IActionResult GetGenrePosters([FromQuery] string domain = "movie")
    {
        var map = new Dictionary<string, string>();
        using var conn = OpenVideoDbRaw();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT GenreName, Poster FROM GenrePosterOverrides WHERE Domain = @d";
        cmd.Parameters.AddWithValue("@d", domain);
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[r.GetString(0)] = r.IsDBNull(1) ? "" : r.GetString(1);
        return Ok(map);
    }

    /// <summary>Upserts a standard-genre poster override so the chosen tile persists
    /// server-side (previously client-only in localStorage).</summary>
    [HttpPut("genre-posters")]
    public IActionResult SetGenrePoster([FromBody] SetGenrePosterDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.Genre)) return BadRequest("genre required");
        using var conn = OpenVideoDbRaw();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO GenrePosterOverrides (Domain, GenreName, Poster) VALUES (@d, @g, @p)
            ON CONFLICT(Domain, GenreName) DO UPDATE SET Poster = @p";
        cmd.Parameters.AddWithValue("@d", string.IsNullOrWhiteSpace(dto.Domain) ? "movie" : dto.Domain);
        cmd.Parameters.AddWithValue("@g", dto.Genre.Trim());
        cmd.Parameters.AddWithValue("@p", dto.Poster ?? "");
        cmd.ExecuteNonQuery();
        return Ok(new { success = true });
    }

    public class SetGenrePosterDto { public string? Domain { get; set; } public string? Genre { get; set; } public string? Poster { get; set; } }

    [HttpDelete("custom-genres/{id}")]
    public IActionResult DeleteCustomGenre(string id, [FromQuery] string domain = "music")
    {
        if (domain == "music")
        {
            using var conn = OpenMusicDbRaw();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM CustomGenres WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
        else
        {
            using var conn = OpenVideoDbRaw();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM CustomGenres WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
        return Ok(new { success = true });
    }

    /// <summary>Returns all custom genres for a video's domain, with assigned flag per genre.</summary>
    [HttpGet("videos/{id}/custom-genres")]
    public IActionResult GetVideoCustomGenres(int id)
    {
        var mediaType = _videoDb.Videos.AsNoTracking()
            .Where(v => v.Id == id)
            .Select(v => v.MediaType)
            .FirstOrDefault();
        if (mediaType == null) return NotFound();

        var domain = mediaType switch {
            "anime" => "anime",
            "tv" or "documentary" => "tv",
            _ => "movie"
        };

        var genres = new List<(string Id, string Name)>();
        var assignedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var conn = OpenVideoDbRaw())
        {
            using var cmdG = conn.CreateCommand();
            cmdG.CommandText = "SELECT Id, Name FROM CustomGenres WHERE Domain = @d ORDER BY Name";
            cmdG.Parameters.AddWithValue("@d", domain);
            using var rG = cmdG.ExecuteReader();
            while (rG.Read()) genres.Add((rG.GetString(0), rG.GetString(1)));

            using var cmdA = conn.CreateCommand();
            cmdA.CommandText = "SELECT GenreId FROM CustomGenreItems WHERE VideoId = @vid";
            cmdA.Parameters.AddWithValue("@vid", id);
            using var rA = cmdA.ExecuteReader();
            while (rA.Read()) assignedIds.Add(rA.GetString(0));
        }

        var result = genres.Select(g => new { g.Id, g.Name, domain, assigned = assignedIds.Contains(g.Id) }).ToList();
        return Ok(result);
    }

    /// <summary>Set direct custom genre assignments for a video.</summary>
    [HttpPost("videos/{id}/custom-genres")]
    public IActionResult SetVideoCustomGenres(int id, [FromBody] SetVideoCustomGenresDto dto)
    {
        using var conn = OpenVideoDbRaw();
        using var del = conn.CreateCommand();
        del.CommandText = "DELETE FROM CustomGenreItems WHERE VideoId = @vid";
        del.Parameters.AddWithValue("@vid", id);
        del.ExecuteNonQuery();

        foreach (var genreId in dto.GenreIds ?? new())
        {
            using var ins = conn.CreateCommand();
            ins.CommandText = "INSERT OR IGNORE INTO CustomGenreItems (GenreId, VideoId) VALUES (@gid, @vid)";
            ins.Parameters.AddWithValue("@gid", genreId);
            ins.Parameters.AddWithValue("@vid", id);
            ins.ExecuteNonQuery();
        }
        return Ok(new { success = true });
    }

    /// <summary>Get all music custom genres with direct-assignment flag for a track.</summary>
    [HttpGet("tracks/{id}/custom-genres")]
    public IActionResult GetTrackCustomGenres(int id)
    {
        var exists = _db.Tracks.AsNoTracking().Any(t => t.Id == id);
        if (!exists) return NotFound();

        var genres = new List<(string Id, string Name)>();
        var assignedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var conn = OpenMusicDbRaw())
        {
            using var cmdG = conn.CreateCommand();
            cmdG.CommandText = "SELECT Id, Name FROM CustomGenres ORDER BY Name";
            using var rG = cmdG.ExecuteReader();
            while (rG.Read()) genres.Add((rG.GetString(0), rG.GetString(1)));

            using var cmdA = conn.CreateCommand();
            cmdA.CommandText = "SELECT GenreId FROM CustomGenreItems WHERE TrackId = @tid";
            cmdA.Parameters.AddWithValue("@tid", id);
            using var rA = cmdA.ExecuteReader();
            while (rA.Read()) assignedIds.Add(rA.GetString(0));
        }

        var result = genres.Select(g => new { g.Id, g.Name, domain = "music", assigned = assignedIds.Contains(g.Id) }).ToList();
        return Ok(result);
    }

    /// <summary>Set direct custom genre assignments for a track.</summary>
    [HttpPost("tracks/{id}/custom-genres")]
    public IActionResult SetTrackCustomGenres(int id, [FromBody] SetVideoCustomGenresDto dto)
    {
        using var conn = OpenMusicDbRaw();
        using var del = conn.CreateCommand();
        del.CommandText = "DELETE FROM CustomGenreItems WHERE TrackId = @tid";
        del.Parameters.AddWithValue("@tid", id);
        del.ExecuteNonQuery();

        foreach (var genreId in dto.GenreIds ?? new())
        {
            using var ins = conn.CreateCommand();
            ins.CommandText = "INSERT OR IGNORE INTO CustomGenreItems (GenreId, TrackId) VALUES (@gid, @tid)";
            ins.Parameters.AddWithValue("@gid", genreId);
            ins.Parameters.AddWithValue("@tid", id);
            ins.ExecuteNonQuery();
        }
        return Ok(new { success = true });
    }

    [HttpGet("welcome-dismissed")]
    public IActionResult GetWelcomeDismissed()
    {
        return Ok(new { dismissed = !_config.Config.UI.ShowWelcome });
    }

    [HttpPost("welcome-dismissed")]
    public IActionResult SetWelcomeDismissed()
    {
        _config.Config.UI.ShowWelcome = false;
        _config.SaveConfig();
        return Ok(new { success = true });
    }

    [HttpDelete("welcome-dismissed")]
    public IActionResult ResetWelcomeDismissed()
    {
        _config.Config.UI.ShowWelcome = true;
        _config.SaveConfig();
        return Ok(new { success = true });
    }

    // ─── Playlists (stored per-user in users/{username}.db) ─────────

    [HttpGet("playlists")]
    public IActionResult GetPlaylists()
    {
        var playlists = _userFavs.GetPlaylists(CurrentUsername);
        return Ok(playlists);
    }

    [HttpGet("playlists/{id}")]
    public async Task<IActionResult> GetPlaylist(int id)
    {
        var (playlistInfo, playlistTrackEntries) = _userFavs.GetPlaylist(CurrentUsername, id);
        if (playlistInfo == null) return NotFound();

        var favIds = _userFavs.GetFavouriteIds(CurrentUsername, "track");

        // Resolve track details from music DB
        var trackIds = playlistTrackEntries.Select(e => (int)e["trackId"]).ToList();
        var tracks = await _db.Tracks.Where(t => trackIds.Contains(t.Id)).ToListAsync();
        var trackMap = tracks.ToDictionary(t => t.Id);

        var playlistTracks = playlistTrackEntries.Select(e =>
        {
            var trId = (int)e["trackId"];
            trackMap.TryGetValue(trId, out var track);
            return new
            {
                id = (int)e["id"],
                position = (int)e["position"],
                dateAdded = (string)e["dateAdded"],
                track = track == null ? null : new
                {
                    track.Id, track.Title, track.Artist, track.Album,
                    track.Duration, track.Genre, track.Year, track.TrackNumber,
                    track.HasAlbumArt, track.AlbumArtCached,
                    IsFavourite = favIds.Contains(track.Id),
                    track.FileSize, track.Bitrate, track.Codec
                }
            };
        }).ToList();

        return Ok(new
        {
            id = playlistInfo["id"],
            name = playlistInfo["name"],
            description = playlistInfo["description"],
            coverImagePath = playlistInfo["coverImagePath"],
            dateCreated = playlistInfo["dateCreated"],
            dateModified = playlistInfo["dateModified"],
            playlistTracks
        });
    }

    [HttpPost("playlists")]
    public IActionResult CreatePlaylist([FromBody] PlaylistCreateDto dto)
    {
        var result = _userFavs.CreatePlaylist(CurrentUsername, dto.Name, dto.Description);
        if (result == null) return StatusCode(500, new { error = "Failed to create playlist" });
        return Ok(result);
    }

    [HttpPost("playlists/{id}/tracks")]
    public IActionResult AddTrackToPlaylist(int id, [FromBody] PlaylistAddTrackDto dto)
    {
        var result = _userFavs.AddTrackToPlaylist(CurrentUsername, id, dto.TrackId);
        if (result == null) return NotFound("Playlist not found");
        return Ok(result);
    }

    [HttpPut("playlists/{id}")]
    public IActionResult UpdatePlaylist(int id, [FromBody] PlaylistCreateDto dto)
    {
        var result = _userFavs.UpdatePlaylist(CurrentUsername, id, dto.Name, dto.Description, dto.CoverImagePath);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("albumart/list")]
    public IActionResult ListAlbumArt()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "assets", "albumart");
        if (!Directory.Exists(dir)) return Ok(Array.Empty<object>());

        var files = Directory.GetFiles(dir)
            .Select(Path.GetFileName)
            .Where(f => f != null && (f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Build filename → (artist, album) map from the music DB
        var meta = _db.Tracks
            .Where(t => t.AlbumArtCached != null && t.AlbumArtCached != "")
            .Select(t => new { t.AlbumArtCached, t.Artist, t.Album })
            .AsEnumerable()
            .GroupBy(t => t.AlbumArtCached!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var result = files
            .OrderBy(f => f)
            .Select(f => new {
                filename = f,
                artist = meta.TryGetValue(f!, out var m) ? m.Artist ?? "" : "",
                album  = meta.TryGetValue(f!, out var m2) ? m2.Album  ?? "" : ""
            })
            .ToArray();

        return Ok(result);
    }

    [HttpDelete("playlists/{id}")]
    public IActionResult DeletePlaylist(int id)
    {
        var deleted = _userFavs.DeletePlaylist(CurrentUsername, id);
        if (!deleted) return NotFound();
        return Ok(new { message = "Playlist deleted" });
    }

    [HttpDelete("playlists/{id}/tracks/{entryId}")]
    public IActionResult RemovePlaylistTrack(int id, int entryId)
    {
        var removed = _userFavs.RemovePlaylistTrack(CurrentUsername, id, entryId);
        if (!removed) return NotFound();
        return Ok(new { message = "Track removed" });
    }

    [HttpPost("playlists/{id}/add-tracks")]
    public IActionResult AddTracksToPlaylist(int id, [FromBody] PlaylistAddTracksDto dto)
    {
        var (added, found) = _userFavs.AddTracksToPlaylist(CurrentUsername, id, dto.TrackIds);
        if (!found) return NotFound("Playlist not found");
        return Ok(new { message = $"{added} tracks added", count = added });
    }

    /// <summary>
    [HttpGet("playlists/{id}/export.m3u")]
    public async Task<IActionResult> ExportPlaylist(int id)
    {
        var (playlistInfo, entries) = _userFavs.GetPlaylist(CurrentUsername, id);
        if (playlistInfo == null) return NotFound();

        var name = playlistInfo.TryGetValue("name", out var n) ? n?.ToString() ?? "playlist" : "playlist";
        var trackIds = entries.Select(e => (int)e["trackId"]).ToList();
        var trackMap = await _db.Tracks
            .Where(t => trackIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Title, t.Artist, t.Duration, t.FilePath })
            .ToDictionaryAsync(t => t.Id);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine($"#PLAYLIST:{name}");

        foreach (var entry in entries.OrderBy(e => (int)e["position"]))
        {
            if (!trackMap.TryGetValue((int)entry["trackId"], out var t)) continue;
            var secs = (int)Math.Round(t.Duration);
            var display = string.IsNullOrEmpty(t.Artist) ? t.Title : $"{t.Artist} - {t.Title}";
            sb.AppendLine($"#EXTINF:{secs},{display}");
            sb.AppendLine(t.FilePath);
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        var safeName = string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim();
        if (string.IsNullOrEmpty(safeName)) safeName = "playlist";
        return File(bytes, "application/octet-stream", $"{safeName}.m3u");
    }

    [HttpGet("agp-export.m3u")]
    public async Task<IActionResult> ExportAgpPlaylist(
        [FromQuery] string type,
        [FromQuery] int? param = null)
    {
        IQueryable<Track>? query = null;
        string playlistName;

        switch (type)
        {
            case "topplayed":
                playlistName = "Top 100 Most Played";
                var userPlays = _userFavs.GetMostPlayed(CurrentUsername, "track", 100);
                if (userPlays.Count == 0) return Ok(Array.Empty<object>());
                var ids = userPlays.Select(p => p.MediaId).ToList();
                var playMap = userPlays.ToDictionary(p => p.MediaId, p => p.Count);
                var topTracks = await _db.Tracks
                    .Where(t => ids.Contains(t.Id))
                    .Select(t => new { t.Id, t.Title, t.Artist, t.Duration, t.FilePath })
                    .ToListAsync();
                return BuildM3u(playlistName,
                    topTracks.OrderByDescending(t => playMap.GetValueOrDefault(t.Id))
                             .Select(t => (t.Title, t.Artist, t.Duration, t.FilePath)));

            case "recent":
                playlistName = "Recently Added";
                query = _db.Tracks.OrderByDescending(t => t.DateAdded).Take(100);
                break;

            case "favourites":
                playlistName = "All Favourites";
                var favIds = _userFavs.GetFavouriteIds(CurrentUsername, "track");
                query = _db.Tracks.Where(t => favIds.Contains(t.Id)).OrderBy(t => t.Artist).ThenBy(t => t.Title);
                break;

            case "decade":
                if (!param.HasValue) return BadRequest("param (decade start year) required");
                playlistName = $"{param}s";
                query = _db.Tracks
                    .Where(t => t.Year >= param.Value && t.Year < param.Value + 10)
                    .OrderBy(t => t.Year).ThenBy(t => t.Artist).ThenBy(t => t.Title);
                break;

            case "genre_rock":
                playlistName = "Rock";
                query = _db.Tracks.Where(t => t.GenreKey.Contains("rock")).OrderBy(t => t.Artist).ThenBy(t => t.Title);
                break;

            case "genre_rap":
                playlistName = "Rap / Hip-Hop";
                query = _db.Tracks.Where(t => t.GenreKey.Contains("rap") || t.GenreKey.Contains("hip-hop")).OrderBy(t => t.Artist).ThenBy(t => t.Title);
                break;

            case "genre_country":
                playlistName = "Country";
                query = _db.Tracks.Where(t => t.GenreKey.Contains("country")).OrderBy(t => t.Artist).ThenBy(t => t.Title);
                break;

            case "genre_rnb":
                playlistName = "R&B / Soul";
                query = _db.Tracks.Where(t => t.GenreKey.Contains("r&b") || t.GenreKey.Contains("soul")).OrderBy(t => t.Artist).ThenBy(t => t.Title);
                break;

            default:
                return BadRequest("Unknown AGP type");
        }

        var tracks = await query!
            .Select(t => new { t.Title, t.Artist, t.Duration, t.FilePath })
            .ToListAsync();

        return BuildM3u(playlistName, tracks.Select(t => (t.Title, t.Artist, t.Duration, t.FilePath)));
    }

    private FileContentResult BuildM3u(string name, IEnumerable<(string Title, string Artist, double Duration, string FilePath)> tracks)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine($"#PLAYLIST:{name}");
        foreach (var (title, artist, duration, filePath) in tracks)
        {
            var secs = (int)Math.Round(duration);
            var display = string.IsNullOrEmpty(artist) ? title : $"{artist} - {title}";
            sb.AppendLine($"#EXTINF:{secs},{display}");
            sb.AppendLine(filePath);
        }
        var safeName = string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim();
        if (string.IsNullOrEmpty(safeName)) safeName = "playlist";
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "application/octet-stream", $"{safeName}.m3u");
    }

    /// Import a playlist from parsed M3U / M3U8 / PLS entries.
    /// Matches entries against the library by: (1) normalised full path,
    /// (2) filename only, (3) title + artist from inline metadata.
    /// Duplicate tracks (same file matched by multiple strategies) are de-duped.
    /// </summary>
    [HttpPost("playlists/import-entries")]
    public IActionResult ImportPlaylistEntries([FromBody] PlaylistImportDto dto)
    {
        if (dto.Entries == null || dto.Entries.Length == 0)
            return BadRequest(new { error = "No entries provided" });

        var name = string.IsNullOrWhiteSpace(dto.Name) ? "Imported Playlist" : dto.Name.Trim();

        static string NormP(string p) => p.Replace('\\', '/').ToLowerInvariant();

        // Project only what we need - avoids loading full track objects into RAM
        var tracks = _db.Tracks.AsNoTracking()
            .Select(t => new { t.Id, t.FilePath, t.Title, t.Artist })
            .ToList();

        var byPath       = tracks.GroupBy(t => NormP(t.FilePath))
                                 .ToDictionary(g => g.Key, g => g.First().Id);
        var byFileName   = tracks.GroupBy(t => Path.GetFileName(t.FilePath).ToLowerInvariant())
                                 .ToDictionary(g => g.Key, g => g.First().Id);
        var byTitleArtist = tracks
            .Where(t => !string.IsNullOrEmpty(t.Title) && !string.IsNullOrEmpty(t.Artist))
            .GroupBy(t => $"{t.Title!.ToLowerInvariant()}|{t.Artist!.ToLowerInvariant()}")
            .ToDictionary(g => g.Key, g => g.First().Id);

        var matchedIds = new List<int>();
        var seen = new HashSet<int>();

        foreach (var entry in dto.Entries)
        {
            int id = 0;

            // Strategy 1: normalised full path
            if (!string.IsNullOrEmpty(entry.Path) && byPath.TryGetValue(NormP(entry.Path), out var a))
                id = a;

            // Strategy 2: filename only
            if (id == 0 && !string.IsNullOrEmpty(entry.Path))
            {
                var fn = Path.GetFileName(entry.Path).ToLowerInvariant();
                if (!string.IsNullOrEmpty(fn) && byFileName.TryGetValue(fn, out var b))
                    id = b;
            }

            // Strategy 3: title + artist from inline metadata (EXTINF / PLS Title)
            if (id == 0 && !string.IsNullOrEmpty(entry.Title) && !string.IsNullOrEmpty(entry.Artist))
            {
                var key = $"{entry.Title.ToLowerInvariant()}|{entry.Artist.ToLowerInvariant()}";
                if (byTitleArtist.TryGetValue(key, out var c))
                    id = c;
            }

            if (id > 0 && seen.Add(id))
                matchedIds.Add(id);
        }

        if (matchedIds.Count == 0)
            return Ok(new { matched = 0, total = dto.Entries.Length, playlistId = (int?)null, name });

        var playlist = _userFavs.CreatePlaylist(CurrentUsername, name, "");
        if (playlist == null) return StatusCode(500, new { error = "Failed to create playlist" });

        var newId = Convert.ToInt32(playlist["id"]);
        var (added, _) = _userFavs.AddTracksToPlaylist(CurrentUsername, newId, [.. matchedIds]);

        return Ok(new { playlistId = newId, name, matched = added, total = dto.Entries.Length });
    }

    // ─── Smart Playlists ──────────────────────────────────────────

    [HttpGet("smart-playlists")]
    public IActionResult GetSmartPlaylists()
        => Ok(_userFavs.GetSmartPlaylists(CurrentUsername));

    [HttpGet("smart-playlists/{id}")]
    public async Task<IActionResult> GetSmartPlaylist(int id)
    {
        var raw = _userFavs.GetSmartPlaylist(CurrentUsername, id);
        if (raw == null) return NotFound();
        var def = SmartPlaylistEngine.ParseDef(raw);
        var count = await SmartPlaylistEngine.Apply(_db.Tracks.AsQueryable(), def).CountAsync();
        raw["trackCount"] = count;
        return Ok(raw);
    }

    [HttpPost("smart-playlists")]
    public IActionResult CreateSmartPlaylist([FromBody] SmartPlaylistSaveDto dto)
    {
        var rulesJson = System.Text.Json.JsonSerializer.Serialize(dto.Rules ?? Array.Empty<object>());
        var newId = _userFavs.CreateSmartPlaylist(CurrentUsername,
            dto.Name, dto.Description, dto.MatchMode ?? "all",
            rulesJson, dto.SortField ?? "title", dto.SortDirection ?? "asc", dto.Limit);
        if (newId < 0) return StatusCode(500, new { error = "Failed to create smart playlist" });
        return Ok(new { id = newId, message = "Smart playlist created" });
    }

    [HttpPut("smart-playlists/{id}")]
    public IActionResult UpdateSmartPlaylist(int id, [FromBody] SmartPlaylistSaveDto dto)
    {
        var rulesJson = System.Text.Json.JsonSerializer.Serialize(dto.Rules ?? Array.Empty<object>());
        var ok = _userFavs.UpdateSmartPlaylist(CurrentUsername, id,
            dto.Name, dto.Description, dto.MatchMode ?? "all",
            rulesJson, dto.SortField ?? "title", dto.SortDirection ?? "asc", dto.Limit);
        if (!ok) return NotFound();
        return Ok(new { message = "Smart playlist updated" });
    }

    [HttpDelete("smart-playlists/{id}")]
    public IActionResult DeleteSmartPlaylist(int id)
    {
        var ok = _userFavs.DeleteSmartPlaylist(CurrentUsername, id);
        if (!ok) return NotFound();
        return Ok(new { message = "Smart playlist deleted" });
    }

    [HttpGet("smart-playlists/{id}/tracks")]
    public async Task<IActionResult> GetSmartPlaylistTracks(int id)
    {
        var raw = _userFavs.GetSmartPlaylist(CurrentUsername, id);
        if (raw == null) return NotFound();
        var def = SmartPlaylistEngine.ParseDef(raw);
        var favIds = _userFavs.GetFavouriteIds(CurrentUsername, "track");
        var tracks = await SmartPlaylistEngine.Apply(_db.Tracks.AsNoTracking(), def).ToListAsync();
        foreach (var t in tracks) t.IsFavourite = favIds.Contains(t.Id);
        return Ok(tracks);
    }

    [HttpPost("smart-playlists/preview")]
    public async Task<IActionResult> PreviewSmartPlaylist([FromBody] SmartPlaylistSaveDto dto)
    {
        var rulesJson = System.Text.Json.JsonSerializer.Serialize(dto.Rules ?? Array.Empty<object>());
        var def = new SmartPlaylistDef(0, "", null,
            dto.MatchMode ?? "all",
            SmartPlaylistEngine.ParseRules(rulesJson),
            dto.SortField ?? "title", dto.SortDirection ?? "asc", dto.Limit);
        var count = await SmartPlaylistEngine.Apply(_db.Tracks.AsQueryable(), def).CountAsync();
        var sample = await SmartPlaylistEngine.Apply(_db.Tracks.AsNoTracking(), def)
            .Take(5)
            .Select(t => new { t.Id, t.Title, t.Artist, t.Album, t.Duration })
            .ToListAsync();
        return Ok(new { count, sample });
    }

    // ─── Playback / Streaming ──────────────────────────────────────

    [HttpGet("stream/{id}")]
    public async Task<IActionResult> StreamTrack(int id, [FromQuery] string? format = null, [FromQuery] int? maxBitRate = null)
    {
        var track = await _db.Tracks.FindAsync(id);
        if (track == null) return NotFound();

        if (!System.IO.File.Exists(track.FilePath))
            return NotFound("File not found on disk");

        // Update global last played + per-user play count (non-critical - never block the stream)
        try
        {
            track.LastPlayed = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to update LastPlayed for track {Id}", id); }
        _userFavs.IncrementPlayCount(CurrentUsername, "track", id);

        // Server-side scrobble: arm the timer when the client starts from byte 0.
        // This covers any client (browser, Android app, DLNA) without relying on JS.
        var rangeHeader = Request.Headers["Range"].ToString();
        if (string.IsNullOrEmpty(rangeHeader) || rangeHeader.StartsWith("bytes=0"))
        {
            _streaming.TrackStarted(
                CurrentUsername, id,
                track.Artist ?? "", track.Title ?? "", track.Album ?? "",
                (int)track.Duration);
        }

        // On-demand quality transcoding: explicit format/bitrate from client
        // (e.g. ?format=opus&maxBitRate=128 for mobile bandwidth saving).
        // Takes priority over everything - explicit client request overrides all server defaults.
        if (!string.IsNullOrEmpty(format) && format != "original" && _ffmpeg.IsAvailable)
        {
            var br = maxBitRate.HasValue ? $"{maxBitRate}k" : "128k";
            var (ffArgsQ, outMimeQ, extQ) = format switch
            {
                "mp3" => ($"-c:a libmp3lame -b:a {br} -f mp3", "audio/mpeg", "mp3"),
                "aac" => ($"-c:a aac -b:a {br} -f adts",       "audio/aac",  "aac"),
                _     => ($"-c:a libopus -b:a {br} -f opus",   "audio/ogg",  "opus"),
            };
            var qCacheDir  = Path.Combine(AppContext.BaseDirectory, "assets", "transcode-cache");
            Directory.CreateDirectory(qCacheDir);
            var qCacheFile = Path.Combine(qCacheDir, $"{id}_{format}_{br}.{extQ}");
            var qTmpFile   = qCacheFile + ".tmp";

            if (System.IO.File.Exists(qCacheFile))
            {
                var cs = new FileStream(qCacheFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                return File(cs, outMimeQ, enableRangeProcessing: true);
            }
            try { System.IO.File.Delete(qTmpFile); } catch { }
            using var qProc = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName  = _ffmpeg.FfmpegPath,
                    Arguments = $"-i \"{track.FilePath}\" {ffArgsQ} \"{qTmpFile}\"",
                    RedirectStandardError = false,
                    UseShellExecute = false,
                    CreateNoWindow  = true
                }
            };
            qProc.Start();
            await qProc.WaitForExitAsync();
            if (qProc.ExitCode == 0 && System.IO.File.Exists(qTmpFile))
            {
                System.IO.File.Move(qTmpFile, qCacheFile, overwrite: true);
                var cs = new FileStream(qCacheFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                return File(cs, outMimeQ, enableRangeProcessing: true);
            }
            // Transcode failed - fall through to raw serve
        }

        // Direct Play mode: serve audio files raw (for SONOS, audiophile DACs, high-end systems).
        // When OFF (default): transcode ALAC/AIFF to a browser-compatible format.
        // AIFF is always transcoded regardless of Direct Play - ExoPlayer (Android) and most
        // browsers have no AIFF demuxer. SONOS receives AIFF raw via DLNA (separate path).
        bool directPlay = _config.Config.Playback.TranscodingEnabled;
        bool isAlac = (track.Codec ?? "").Contains("alac", StringComparison.OrdinalIgnoreCase);
        bool isAiff = (track.MimeType ?? "").Contains("aiff", StringComparison.OrdinalIgnoreCase);

        if ((isAiff || (!directPlay && isAlac)) && _ffmpeg.IsAvailable)
        {
            var fmt     = _config.Config.Playback.TranscodeFormat ?? "aac";
            var bitrate = _config.Config.Playback.TranscodeBitrate ?? "320k";
            var (ffArgs, outMime, ext) = fmt switch
            {
                "mp3"  => ($"-c:a libmp3lame -b:a {bitrate} -f mp3", "audio/mpeg", "mp3"),
                "opus" => ($"-c:a libopus -b:a {bitrate} -f opus",   "audio/ogg",  "opus"),
                _      => ($"-c:a aac -b:a {bitrate} -f adts",       "audio/aac",  "aac"),
            };
            var cacheType = isAlac ? "alac" : "aiff";
            var cacheDir  = Path.Combine(AppContext.BaseDirectory, "assets", $"{cacheType}-cache");
            Directory.CreateDirectory(cacheDir);
            var cacheFile = Path.Combine(cacheDir, $"{cacheType}_{id}.{ext}");
            var tmpFile   = cacheFile + ".tmp";

            if (System.IO.File.Exists(cacheFile))
            {
                var cachedStream = new FileStream(cacheFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                return File(cachedStream, outMime, enableRangeProcessing: true);
            }

            // Transcode synchronously - brief delay on first play, but full seeking works immediately
            try { System.IO.File.Delete(tmpFile); } catch { }
            using var ffmpegProc = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName  = _ffmpeg.FfmpegPath,
                    Arguments = $"-i \"{track.FilePath}\" {ffArgs} \"{tmpFile}\"",
                    RedirectStandardError = false,
                    UseShellExecute = false,
                    CreateNoWindow  = true
                }
            };
            ffmpegProc.Start();
            await ffmpegProc.WaitForExitAsync();
            if (ffmpegProc.ExitCode == 0 && System.IO.File.Exists(tmpFile))
            {
                System.IO.File.Move(tmpFile, cacheFile, overwrite: true);
                var cachedStream = new FileStream(cacheFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                return File(cachedStream, outMime, enableRangeProcessing: true);
            }
            return StatusCode(500, $"{cacheType.ToUpperInvariant()} transcode failed");
        }

        var stream = new FileStream(track.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, track.MimeType ?? "application/octet-stream", enableRangeProcessing: true);
    }

    [HttpGet("cover/{albumId}")]
    public async Task<IActionResult> GetAlbumCover(int albumId)
    {
        Response.Headers["Cache-Control"] = "no-cache";
        var album = await _db.Albums.FindAsync(albumId);
        if (album == null) return NotFound();

        var artDir = Path.Combine(AppContext.BaseDirectory, "assets", "albumart");

        // 1. This row's manually-set cover art (Edit Metadata / fanart pick).
        if (!string.IsNullOrEmpty(album.CoverArtPath))
        {
            var manualPath = Path.Combine(artDir, album.CoverArtPath);
            if (System.IO.File.Exists(manualPath))
                return PhysicalFile(manualPath, "image/jpeg");
        }

        // 2. A SIBLING album row of the same name holds the manual cover. Albums are grouped by NAME
        //    in the library (compilations / duplicate (Name,Artist) rows), and a custom cover is
        //    saved on ONE row - but search and other callers pass whatever row id matched, which may
        //    be a different, cover-less row of the same album. Match on the accent-safe NameKey so
        //    accented album names resolve too (SQLite lower()/NOCASE is ASCII-only - see the music
        //    accent fix). This is why a custom fanart cover showed in the library but not in search.
        if (!string.IsNullOrEmpty(album.NameKey))
        {
            var sibCover = await _db.Albums
                .Where(a2 => a2.NameKey == album.NameKey && a2.CoverArtPath != null && a2.CoverArtPath != "")
                .Select(a2 => a2.CoverArtPath)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrEmpty(sibCover))
            {
                var sibPath = Path.Combine(artDir, sibCover);
                if (System.IO.File.Exists(sibPath))
                    return PhysicalFile(sibPath, "image/jpeg");
            }
        }

        // 3. First track with cached album art - matched by AlbumId OR by album NAME (AlbumKey), since
        //    a track may belong to this album by name without being linked to this exact row id.
        var track = await _db.Tracks
            .Where(t => (t.AlbumId == albumId || (album.NameKey != "" && t.AlbumKey == album.NameKey)) && t.HasAlbumArt)
            .OrderBy(t => t.DiscNumber ?? 0).ThenBy(t => t.TrackNumber ?? 0).ThenBy(t => t.Id)
            .FirstOrDefaultAsync();
        if (track == null) return NotFound();

        // Serve from cache if available (skip broken/empty extracts - see MinArtBytes)
        if (!string.IsNullOrEmpty(track.AlbumArtCached))
        {
            var cachedPath = Path.Combine(AppContext.BaseDirectory, "assets", "albumart", track.AlbumArtCached);
            if (System.IO.File.Exists(cachedPath) && new FileInfo(cachedPath).Length >= MinArtBytes)
                return PhysicalFile(cachedPath, "image/jpeg");
        }

        // Fallback: read from audio file
        if (!System.IO.File.Exists(track.FilePath)) return NotFound();
        try
        {
            using var tagFile = TagLib.File.Create(track.FilePath);
            var picture = tagFile.Tag.Pictures?.FirstOrDefault();
            if (picture == null || picture.Data.Data.Length < MinArtBytes) return NotFound();
            return File(picture.Data.Data, picture.MimeType ?? "image/jpeg");
        }
        catch { return NotFound(); }
    }

    // Re-scan every album's folder for a conventional cover file (cover.jpg / folder.png / front.* …)
    // and refresh its thumbnail. Manual/fanart covers are preserved - only empty rows or ones whose
    // cover is itself folder-derived (folderart_*) are (re)applied. Admin-only.
    [HttpPost("albums/refresh-covers")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> RefreshAlbumCovers()
    {
        var albums = await _db.Albums.ToListAsync();
        var artDir = Path.Combine(AppContext.BaseDirectory, "assets", "albumart");
        Directory.CreateDirectory(artDir);
        int updated = 0;
        foreach (var album in albums)
        {
            var current = album.CoverArtPath ?? "";
            var folderDerived = current.StartsWith("folderart_", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(current) && !folderDerived) continue;   // keep manual/fanart picks

            var dir = await _db.Tracks
                .Where(t => t.AlbumId == album.Id || (album.NameKey != "" && t.AlbumKey == album.NameKey))
                .Select(t => t.FilePath).FirstOrDefaultAsync();
            var cover = NexusM.Services.LibraryScannerService.FindFolderCoverFile(Path.GetDirectoryName(dir ?? ""));
            if (cover == null) continue;

            try
            {
                var filename = $"folderart_{album.Id}.jpg";
                using (var img = await Image.LoadAsync(cover))
                {
                    img.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(600, 600), Mode = ResizeMode.Max }));
                    await img.SaveAsJpegAsync(Path.Combine(artDir, filename));
                }
                album.CoverArtPath = filename;
                updated++;
            }
            catch { /* skip this album */ }
        }
        if (updated > 0) await _db.SaveChangesAsync();
        return Ok(new { success = true, updated, scanned = albums.Count });
    }

    [HttpGet("cover/track/{trackId}")]
    public async Task<IActionResult> GetTrackCover(int trackId)
    {
        Response.Headers["Cache-Control"] = "no-cache";
        var track = await _db.Tracks.FindAsync(trackId);
        if (track == null) return NotFound();

        var artDir = Path.Combine(AppContext.BaseDirectory, "assets", "albumart");

        // 1. The track's own art (cached file, then the embedded picture) when present.
        // A file/picture below MinArtBytes is a broken extract - some MP3s (e.g. the World Cup
        // compilation) carry a 0/12-byte "cover" while HasAlbumArt stays true - so treat it as
        // absent and fall through to the album cover, which resolves the real folder art.
        if (track.HasAlbumArt)
        {
            if (!string.IsNullOrEmpty(track.AlbumArtCached))
            {
                var cachedPath = Path.Combine(artDir, track.AlbumArtCached);
                if (System.IO.File.Exists(cachedPath) && new FileInfo(cachedPath).Length >= MinArtBytes)
                    return PhysicalFile(cachedPath, "image/jpeg");
            }
            if (System.IO.File.Exists(track.FilePath))
            {
                try
                {
                    using var tagFile = TagLib.File.Create(track.FilePath);
                    var picture = tagFile.Tag.Pictures?.FirstOrDefault();
                    if (picture != null && picture.Data.Data.Length >= MinArtBytes)
                        return File(picture.Data.Data, picture.MimeType ?? "image/jpeg");
                }
                catch { /* fall through to the album cover */ }
            }
        }

        // 2. Fall back to the ALBUM's manually-set cover. A cover added by hand for an
        //    undetected file is stored on the Album row (CoverArtPath = "manual_..."),
        //    NOT on the track, so a track whose file has no embedded art must resolve it
        //    here - otherwise the now-playing bar shows a placeholder while the album view
        //    shows the cover. Match by AlbumId first, then by the accent-safe album
        //    NameKey (SQLite lower()/NOCASE is ASCII-only - see the music accent fix).
        var albumCover = await _db.Albums
            .Where(a => (track.AlbumId != null && a.Id == track.AlbumId)
                     || (track.AlbumKey != "" && a.NameKey == track.AlbumKey))
            .Where(a => a.CoverArtPath != null && a.CoverArtPath != "")
            .Select(a => a.CoverArtPath)
            .FirstOrDefaultAsync();
        if (!string.IsNullOrEmpty(albumCover))
        {
            var coverPath = Path.Combine(artDir, albumCover);
            if (System.IO.File.Exists(coverPath))
                return PhysicalFile(coverPath, "image/jpeg");
        }

        // 3. A sibling track of the same album that DOES carry cached art.
        if (track.AlbumKey != "")
        {
            var sibCached = await _db.Tracks
                .Where(t => t.AlbumKey == track.AlbumKey && t.HasAlbumArt
                            && t.AlbumArtCached != null && t.AlbumArtCached != "")
                .Select(t => t.AlbumArtCached)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrEmpty(sibCached))
            {
                var sibPath = Path.Combine(artDir, sibCached);
                if (System.IO.File.Exists(sibPath) && new FileInfo(sibPath).Length >= MinArtBytes)
                    return PhysicalFile(sibPath, "image/jpeg");
            }
        }

        return NotFound();
    }

    // Smallest byte length we treat as a real image. A valid JPEG/PNG header alone exceeds
    // this; a broken/empty extract (0-12 bytes) falls below it and is skipped so cover
    // resolution moves on to the album's own art.
    private const long MinArtBytes = 200;

    // ─── Favourites Toggle ─────────────────────────────────────────

    [HttpPost("tracks/{id}/favourite")]
    public async Task<IActionResult> ToggleFavourite(int id)
    {
        var track = await _db.Tracks.FindAsync(id);
        if (track == null) return NotFound();
        var isFav = _userFavs.ToggleFavourite(CurrentUsername, "track", id);
        return Ok(new { id, isFavourite = isFav });
    }

    [HttpPost("musicvideos/{id}/favourite")]
    public async Task<IActionResult> ToggleMvFavourite(int id)
    {
        var mv = await _mvDb.MusicVideos.FindAsync(id);
        if (mv == null) return NotFound();
        var isFav = _userFavs.ToggleFavourite(CurrentUsername, "musicvideo", id);
        return Ok(new { id, isFavourite = isFav });
    }

    [HttpPost("videos/{id}/favourite")]
    public async Task<IActionResult> ToggleVideoFavourite(int id)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();
        var isFav = _userFavs.ToggleFavourite(CurrentUsername, "video", id);
        return Ok(new { id, isFavourite = isFav });
    }

    [HttpPost("videos/{id}/watched")]
    public async Task<IActionResult> ToggleVideoWatched(int id)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();
        var isWatched = _userFavs.ToggleWatched(CurrentUsername, id);
        return Ok(new { id, watched = isWatched });
    }

    /// <summary>
    /// Mark EVERY episode of a series watched or unwatched.
    ///
    /// The series 3-dot menu used to call videos/{id}/watched with the series' first
    /// episode id, so "mark series watched" only ever tagged episode 1 - the green
    /// dot users reported. A series is not a video, so it needs its own endpoint.
    ///
    /// Per-user, NOT admin-gated: watched lives in each user's own database.
    /// Body: { seriesName, mediaType?, watched }
    /// </summary>
    [HttpPost("videos/series/watched")]
    public async Task<IActionResult> SetSeriesWatched([FromBody] JsonElement body)
    {
        var seriesName = body.TryGetProperty("seriesName", out var sn) ? (sn.GetString() ?? "").Trim() : "";
        if (string.IsNullOrEmpty(seriesName)) return BadRequest(new { error = "seriesName is required" });

        var watched = !body.TryGetProperty("watched", out var w) || w.ValueKind != JsonValueKind.False;

        // mediaType narrows tv vs anime so two shows sharing a name across libraries
        // can't be marked together. Omitted = every episode under that series name.
        var mediaType = body.TryGetProperty("mediaType", out var mt) ? (mt.GetString() ?? "").Trim() : "";

        var query = _videoDb.Videos.Where(v => v.SeriesName == seriesName);
        if (mediaType.Length > 0)
            query = query.Where(v => v.MediaType == mediaType);

        var ids = await query.Select(v => v.Id).ToListAsync();
        if (ids.Count == 0) return NotFound(new { error = "No episodes found for this series" });

        var affected = _userFavs.SetWatchedBulk(CurrentUsername, ids, watched);

        return Ok(new { success = true, seriesName, watched, episodes = ids.Count, affected });
    }

    /// <summary>
    /// Mark an arbitrary set of videos watched or unwatched in one call. Backs the
    /// series page's "mark whole season" button and the multi-select episode
    /// picker - the frontend already holds the episode ids, so a season is just a
    /// subset it sends directly. Ids are validated against videos.db so a crafted
    /// body can't write watched rows for media that doesn't exist.
    ///
    /// Per-user, NOT admin-gated: watched lives in each user's own database (same
    /// as videos/{id}/watched and videos/series/watched).
    /// Body: { ids: [...], watched: bool }
    /// </summary>
    [HttpPost("videos/watched/bulk")]
    public async Task<IActionResult> BulkSetWatched([FromBody] JsonElement body)
    {
        if (!body.TryGetProperty("ids", out var idsEl) || idsEl.ValueKind != JsonValueKind.Array)
            return BadRequest(new { error = "ids array is required" });

        var requested = new List<int>();
        foreach (var el in idsEl.EnumerateArray())
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) requested.Add(n);
        if (requested.Count == 0) return BadRequest(new { error = "ids array is empty" });

        var watched = !body.TryGetProperty("watched", out var w) || w.ValueKind != JsonValueKind.False;

        // Only touch ids that actually exist as videos.
        var ids = await _videoDb.Videos.Where(v => requested.Contains(v.Id)).Select(v => v.Id).ToListAsync();
        if (ids.Count == 0) return NotFound(new { error = "No matching videos found" });

        var affected = _userFavs.SetWatchedBulk(CurrentUsername, ids, watched);
        return Ok(new { success = true, watched, count = ids.Count, affected });
    }

    /// <summary>
    /// Preview watched flags that have no real playback progress behind them -
    /// the residue of the old "mark watched when the stream starts" behaviour.
    /// Read-only: returns titles so the user can review before clearing.
    /// Operates on the CALLING user's own database only.
    /// </summary>
    [HttpGet("videos/watched/stale")]
    public async Task<IActionResult> GetStaleWatched()
    {
        var staleRows = _userFavs.GetStaleWatchedRows(CurrentUsername);
        var totalWatched = _userFavs.GetWatchedIds(CurrentUsername).Count;

        if (staleRows.Count == 0)
            return Ok(new { totalWatched, stale = 0, items = Array.Empty<object>() });

        var staleIds = staleRows.Select(r => r.VideoId).ToList();
        var videos = await _videoDb.Videos
            .Where(v => staleIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Title, v.SeriesName, v.Season, v.Episode, v.MediaType, v.Duration })
            .ToDictionaryAsync(v => v.Id);

        // Report progress against the LIBRARY duration, not the duration the
        // player recorded. A transcoded stream can report a fraction of the
        // real runtime, which inflates the stored percentage - recomputing here
        // is what makes an 8-second view read as 0.3% instead of 8.9%.
        var items = staleRows
            .Where(r => videos.ContainsKey(r.VideoId))
            .Select(r =>
            {
                var v = videos[r.VideoId];
                var realDur = v.Duration;
                var truePercent = realDur > 0 ? Math.Min(100, r.Position / realDur * 100.0) : 0;
                // The recorded duration is suspect when it falls well short of
                // the real runtime - that is the HLS partial-length bug.
                var durationSuspect = r.HasProgress && realDur > 0
                                      && r.RecordedDuration > 0
                                      && r.RecordedDuration < realDur * 0.9;
                return new
                {
                    v.Id, v.Title, v.SeriesName, v.Season, v.Episode, v.MediaType,
                    realDuration = realDur,
                    r.HasProgress,
                    position = r.Position,
                    recordedDuration = r.RecordedDuration,
                    recordedPercent = Math.Round(r.RecordedPercent, 1),
                    truePercent = Math.Round(truePercent, 1),
                    durationSuspect,
                    watchedAt = r.WatchedAt,
                    lastWatched = r.LastWatched
                };
            })
            .ToList();

        return Ok(new { totalWatched, stale = items.Count, items });
    }

    /// <summary>
    /// Clear watched flags. Body: { ids: [...] } - the ids the user reviewed in
    /// the preview. An empty/absent list is rejected rather than treated as
    /// "clear everything", so a malformed request can never wipe the history.
    /// </summary>
    [HttpPost("videos/watched/clear")]
    public IActionResult ClearWatchedFlags([FromBody] ClearWatchedRequest? body)
    {
        if (body?.Ids == null || body.Ids.Count == 0)
            return BadRequest(new { success = false, message = "No video ids supplied." });

        var removed = _userFavs.ClearWatchedFlags(CurrentUsername, body.Ids);
        _logger.LogInformation("Cleared {Removed} watched flag(s) for user '{User}'", removed, CurrentUsername);
        return Ok(new { success = true, removed });
    }

    public class ClearWatchedRequest
    {
        public List<int>? Ids { get; set; }
    }

    [HttpPost("videos/{id}/watchlist")]
    public async Task<IActionResult> ToggleVideoWatchlist(int id)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();
        var inWatchlist = _userFavs.ToggleWatchlist(CurrentUsername, id);
        return Ok(new { id, inWatchlist });
    }

    [HttpGet("watchlist")]
    public async Task<IActionResult> GetWatchlist()
    {
        var ids = _userFavs.GetWatchlistIds(CurrentUsername);
        if (ids.Count == 0) return Ok(new { videos = Array.Empty<object>() });

        var watchedIds = _userFavs.GetWatchedIds(CurrentUsername);
        var videos = await _videoDb.Videos
            .Where(v => ids.Contains(v.Id))
            .Select(v => new
            {
                v.Id, v.Title, v.SeriesName, v.Year, v.Duration, v.MediaType,
                v.ThumbnailPath, v.PosterPath, v.Overview, v.Rating, v.Genre,
                v.Season, v.Episode, v.ContentRating,
                IsWatched = watchedIds.Contains(v.Id)
            }).ToListAsync();

        // Preserve watchlist order (most recently added first)
        var ordered = ids
            .Select(id => videos.FirstOrDefault(v => v.Id == id))
            .Where(v => v != null)
            .ToList();

        return Ok(new { videos = ordered });
    }

    [HttpGet("watchlist/ids")]
    public IActionResult GetWatchlistIds()
    {
        var ids = _userFavs.GetWatchlistIds(CurrentUsername);
        return Ok(new { ids });
    }

    [HttpGet("musicvideos/favourites")]
    public async Task<IActionResult> GetMvFavourites()
    {
        var favIds = _userFavs.GetFavouriteIds(CurrentUsername, "musicvideo");
        if (favIds.Count == 0) return Ok(new { videos = Array.Empty<object>() });

        var videos = await _mvDb.MusicVideos.Where(v => favIds.Contains(v.Id))
            .OrderBy(v => v.Artist).ThenBy(v => v.Title)
            .Select(v => new
            {
                v.Id, v.Title, v.Artist, v.Duration, v.SizeBytes, v.Format,
                v.Resolution, v.Width, v.Height, v.ThumbnailPath,
                v.NeedsOptimization, v.Mp4Compliant, IsFavourite = true
            }).ToListAsync();
        return Ok(new { videos });
    }

    [HttpGet("videos/favourites")]
    public async Task<IActionResult> GetVideoFavourites()
    {
        var favIds = _userFavs.GetFavouriteIds(CurrentUsername, "video");
        if (favIds.Count == 0) return Ok(new { videos = Array.Empty<object>() });

        var videos = await _videoDb.Videos.Where(v => favIds.Contains(v.Id))
            .OrderBy(v => v.Title)
            .Select(v => new
            {
                v.Id, v.Title, v.Year, v.Duration, v.SizeBytes, v.Format,
                v.Resolution, v.Width, v.Height, v.Codec, v.HdrFormat, v.MediaType, v.Rating,
                v.SeriesName, v.Season, v.Episode,
                v.ThumbnailPath, v.PosterPath, v.Mp4Compliant, v.NeedsOptimization, IsFavourite = true
            }).ToListAsync();
        return Ok(new { videos });
    }

    [HttpPost("tracks/{id}/rate")]
    public async Task<IActionResult> RateTrack(int id, [FromBody] RateDto dto)
    {
        var track = await _db.Tracks.FindAsync(id);
        if (track == null) return NotFound();
        track.Rating = Math.Clamp(dto.Rating, 0, 5);
        await _db.SaveChangesAsync();
        return Ok(new { id, rating = track.Rating });
    }

    // ─── Library Scanning ──────────────────────────────────────────

    [HttpPost("scan")]
    public IActionResult StartScan()
    {
        if (_scanner.IsScanning)
            return Conflict(new { message = "Scan already in progress" });

        _ = _scanner.StartScanAsync();
        _semantic.RequestAutoIndex();
        return Ok(new { message = "Scan started" });
    }

    [HttpGet("scan/status")]
    public IActionResult GetScanStatus()
    {
        var p = _scanner.CurrentProgress;
        return Ok(new
        {
            p.Status,
            p.Message,
            p.TotalFiles,
            p.ProcessedFiles,
            p.NewTracks,
            p.UpdatedTracks,
            p.ErrorCount,
            p.PercentComplete,
            p.StartTime,
            isScanning = _scanner.IsScanning
        });
    }

    // ─── Search ────────────────────────────────────────────────────

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("Query parameter 'q' is required");

        var s = q.ToLower();
        var sk = MusicKey.Of(q);   // Unicode-folded key for the music LIKE fallbacks (see MusicKey)

        // ─── Music (FTS5 with LIKE fallback) ───────────────
        var trackFtsIds  = new List<int>();
        var albumFtsIds  = new List<int>();
        var artistFtsIds = new List<int>();
        try
        {
            var ftsQ = BuildFtsQuery(q);
            using var ftsConn = OpenMusicDbRaw();
            trackFtsIds  = FtsIds(ftsConn, "tracks_fts",  ftsQ, 20);
            albumFtsIds  = FtsIds(ftsConn, "albums_fts",  ftsQ, 10);
            artistFtsIds = FtsIds(ftsConn, "artists_fts", ftsQ, 10);
        }
        catch { /* index unavailable - LIKE fallback below */ }

        // Tracks
        var tracks = trackFtsIds.Count > 0
            ? (await _db.Tracks
                .Where(t => trackFtsIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Title, t.Artist, t.Album, t.Duration, t.MimeType, t.Codec, t.HasAlbumArt, t.AlbumArtCached, type = "track" })
                .ToListAsync())
                .OrderBy(t => trackFtsIds.IndexOf(t.Id)).ToList()
            : await _db.Tracks
                .Where(t => t.TitleKey.Contains(sk) || t.ArtistKey.Contains(sk) || t.AlbumKey.Contains(sk))
                .Take(20)
                .Select(t => new { t.Id, t.Title, t.Artist, t.Album, t.Duration, t.MimeType, t.Codec, t.HasAlbumArt, t.AlbumArtCached, type = "track" })
                .ToListAsync();

        // Albums
        var albums = albumFtsIds.Count > 0
            ? (await _db.Albums
                .Where(a => albumFtsIds.Contains(a.Id))
                .Select(a => new { a.Id, title = a.Name, artist = a.Artist, a.Year, type = "album" })
                .ToListAsync())
                .OrderBy(a => albumFtsIds.IndexOf(a.Id)).ToList()
            : await _db.Albums
                .Where(a => a.NameKey.Contains(sk) || a.ArtistKey.Contains(sk))
                .Take(10)
                .Select(a => new { a.Id, title = a.Name, artist = a.Artist, a.Year, type = "album" })
                .ToListAsync();

        // Artists - resolve FTS IDs → names, then enrich with track stats
        var matchedArtists = artistFtsIds.Count > 0
            ? await _db.Artists
                .Where(a => artistFtsIds.Contains(a.Id))
                .Select(a => new { a.Name, a.ImagePath })
                .ToListAsync()
            : await _db.Artists
                .Where(a => a.NameKey.Contains(sk))
                .Take(10)
                .Select(a => new { a.Name, a.ImagePath })
                .ToListAsync();

        // Preserve FTS rank order for artists
        if (artistFtsIds.Count > 0)
        {
            var nameToIdx = matchedArtists
                .Select((a, i) => (a.Name, i))
                .ToDictionary(x => x.Name, x => x.i, StringComparer.OrdinalIgnoreCase);
            matchedArtists = [.. matchedArtists.OrderBy(a =>
                nameToIdx.TryGetValue(a.Name, out var idx) ? idx : int.MaxValue)];
        }

        var artistNames = matchedArtists.Select(a => a.Name).ToList();
        var artistTrackStats = await _db.Tracks
            .Where(t => artistNames.Contains(t.Artist))
            .GroupBy(t => t.Artist)
            .Select(g => new {
                name = g.Key,
                trackCount = g.Count(),
                albumCount = g.Select(t => t.Album).Distinct().Count()
            })
            .ToListAsync();

        var artists = matchedArtists.Select(a => {
            var stats = artistTrackStats.FirstOrDefault(x =>
                x.name.Equals(a.Name, StringComparison.OrdinalIgnoreCase));
            return new {
                name = a.Name,
                albumCount = stats?.albumCount ?? 0,
                trackCount = stats?.trackCount ?? 0,
                imagePath = a.ImagePath,
                type = "artist"
            };
        }).ToList();

        // ─── Pictures ──────────────────────────────────────
        var pictures = await _picDb.Pictures
            .Where(p => p.FileName.ToLower().Contains(s)
                || p.Category.ToLower().Contains(s))
            .Take(20)
            .Select(p => new { p.Id, p.FileName, p.Width, p.Height, p.SizeBytes,
                p.Format, p.Category, p.ThumbnailPath })
            .ToListAsync();

        // ─── eBooks ────────────────────────────────────────
        var ebooks = await _ebookDb.EBooks
            .Where(e => e.Title.ToLower().Contains(s)
                || e.Author.ToLower().Contains(s)
                || e.FileName.ToLower().Contains(s)
                || (e.Publisher != null && e.Publisher.ToLower().Contains(s))
                || (e.Subject != null && e.Subject.ToLower().Contains(s))
                || (e.Series != null && e.Series.ToLower().Contains(s))
                || e.Category.ToLower().Contains(s))
            .Take(20)
            .Select(e => new { e.Id, e.FileName, e.Title, e.Author, e.Format,
                e.FileSize, e.PageCount, e.Category, e.CoverImage })
            .ToListAsync();

        // ─── Music Videos ──────────────────────────────────
        var musicVideos = await _mvDb.MusicVideos
            .Where(v => v.Title.ToLower().Contains(s)
                || v.Artist.ToLower().Contains(s)
                || v.FileName.ToLower().Contains(s)
                || v.Album.ToLower().Contains(s)
                || v.Genre.ToLower().Contains(s))
            .Take(20)
            .Select(v => new { v.Id, v.FileName, v.Title, v.Artist, v.Duration,
                v.SizeBytes, v.Format, v.Resolution, v.ThumbnailPath })
            .ToListAsync();

        // ─── Audio Books ───────────────────────────────────
        var audiobooks = await _audioBooksDb.AudioBooks
            .Where(a => a.Title.ToLower().Contains(s)
                || a.Author.ToLower().Contains(s)
                || a.FileName.ToLower().Contains(s)
                || (a.Narrator != null && a.Narrator.ToLower().Contains(s))
                || (a.Series != null && a.Series.ToLower().Contains(s))
                || a.Category.ToLower().Contains(s))
            .Take(20)
            .Select(a => new { a.Id, a.FileName, a.Title, a.Author, a.Narrator,
                a.Format, a.FileSize, a.Duration, a.Category, a.CoverImage })
            .ToListAsync();

        // ─── Movies / TV Shows ──────────────────────────────
        var videos = await _videoDb.Videos
            .Where(v => v.Title.ToLower().Contains(s)
                || (v.SeriesName != null && v.SeriesName.ToLower().Contains(s))
                || (v.Director != null && v.Director.ToLower().Contains(s))
                || (v.Cast != null && v.Cast.ToLower().Contains(s))
                || (v.Genre != null && v.Genre.ToLower().Contains(s))
                || (v.Overview != null && v.Overview.ToLower().Contains(s)))
            .Take(20)
            .Select(v => new { v.Id, v.Title, v.SeriesName, v.Year, v.MediaType,
                v.PosterPath, v.Rating, v.Genre, v.Director })
            .ToListAsync();

        // ─── Actors ─────────────────────────────────────────
        var searchActorPage = await _actorsDb.Actors
            .Where(a => a.Name.ToLower().Contains(s))
            .OrderByDescending(a => a.Popularity ?? 0)
            .Take(20)
            .Select(a => new { a.Id, a.Name, a.TmdbId, a.ImageCached, a.KnownForDepartment })
            .ToListAsync();

        // Compute grouped library count for search results via cross-DB lookup
        var searchActorIds = searchActorPage.Select(a => a.Id).ToList();
        var searchMovieActors = await _actorsDb.MovieActors
            .Where(ma => searchActorIds.Contains(ma.ActorId))
            .Select(ma => new { ma.ActorId, ma.VideoId })
            .ToListAsync();
        var searchVideoIds = searchMovieActors.Select(ma => ma.VideoId).Distinct().ToList();
        var searchVideoInfo = searchVideoIds.Count > 0
            ? await _videoDb.Videos.Where(v => searchVideoIds.Contains(v.Id))
                .Select(v => new { v.Id, v.MediaType, v.SeriesName }).ToListAsync()
            : [];
        var searchVideoById = searchVideoInfo.ToDictionary(v => v.Id);
        var actors = searchActorPage.Select(a =>
        {
            var vids = searchMovieActors.Where(ma => ma.ActorId == a.Id).Select(ma => ma.VideoId).ToList();
            var avids = vids.Where(vid => searchVideoById.ContainsKey(vid)).Select(vid => searchVideoById[vid]).ToList();
            var nonTv = avids.Count(v => v.MediaType != "tv");
            var tvSeries = avids.Where(v => v.MediaType == "tv" && !string.IsNullOrEmpty(v.SeriesName))
                .Select(v => v.SeriesName!).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            return new { a.Id, a.Name, a.TmdbId, a.ImageCached, a.KnownForDepartment, movieCount = nonTv + tvSeries };
        }).ToList();

        return Ok(new { tracks, albums, artists, pictures, ebooks, audiobooks, musicVideos, videos, actors });
    }

    // ─── Status / Shares ────────────────────────────────────────────

    [HttpGet("status/shares")]
    public async Task<IActionResult> GetSharesStatus()
    {
        var cfg = _config.Config;
        var shares = new List<object>();
        long totalSize = 0;

        // Audio Books
        if (cfg.UI.ShowAudioBooks)
        {
            var audioBookFolders = cfg.Library.GetAudioBooksFolderList();
            var audioBookActive = audioBookFolders.Count > 0 && audioBookFolders.Any(f => System.IO.Directory.Exists(f));
            long audioBookSize = audioBookActive ? await _audioBooksDb.AudioBooks.SumAsync(a => a.FileSize) : 0;
            shares.Add(new { type = "Audio Books", active = audioBookActive, configured = audioBookFolders.Count > 0, size = audioBookSize });
            totalSize += audioBookSize;
        }

        // eBooks
        if (cfg.UI.ShowEBooks)
        {
            var ebookFolders = cfg.Library.GetEBooksFolderList();
            var ebookActive = ebookFolders.Count > 0 && ebookFolders.Any(f => System.IO.Directory.Exists(f));
            long ebookSize = ebookActive ? await _ebookDb.EBooks.SumAsync(e => e.FileSize) : 0;
            shares.Add(new { type = "eBooks", active = ebookActive, configured = ebookFolders.Count > 0, size = ebookSize });
            totalSize += ebookSize;
        }

        // Music
        if (cfg.UI.ShowMusic)
        {
            var musicFolders = cfg.Library.GetMusicFolderList();
            var musicActive = musicFolders.Count > 0 && musicFolders.Any(f => System.IO.Directory.Exists(f));
            long musicSize = musicActive ? await _db.Tracks.SumAsync(t => t.FileSize) : 0;
            shares.Add(new { type = "Music", active = musicActive, configured = musicFolders.Count > 0, size = musicSize });
            totalSize += musicSize;
        }

        // Music Videos
        if (cfg.UI.ShowMusicVideos)
        {
            var mvFolders = cfg.Library.GetMusicVideosFolderList();
            var mvActive = mvFolders.Count > 0 && mvFolders.Any(f => System.IO.Directory.Exists(f));
            long mvSize = mvActive ? await _mvDb.MusicVideos.SumAsync(v => v.SizeBytes) : 0;
            shares.Add(new { type = "Music Videos", active = mvActive, configured = mvFolders.Count > 0, size = mvSize });
            totalSize += mvSize;
        }

        // Pictures
        if (cfg.UI.ShowPictures)
        {
            var picFolders = cfg.Library.GetPicturesFolderList();
            var picActive = picFolders.Count > 0 && picFolders.Any(f => System.IO.Directory.Exists(f));
            long picSize = picActive ? await _picDb.Pictures.SumAsync(p => p.SizeBytes) : 0;
            shares.Add(new { type = "Pictures", active = picActive, configured = picFolders.Count > 0, size = picSize });
            totalSize += picSize;
        }

        // Movies/TV Shows
        if (cfg.UI.ShowMovies || cfg.UI.ShowTvShows)
        {
            var allVideoFolders = cfg.Library.GetAllVideoFolderList();
            var moviesActive = allVideoFolders.Count > 0 && allVideoFolders.Any(f => System.IO.Directory.Exists(f));
            long videoSize = moviesActive ? await _videoDb.Videos.SumAsync(v => v.SizeBytes) : 0;
            shares.Add(new { type = "Videos", active = moviesActive, configured = allVideoFolders.Count > 0, size = videoSize });
            totalSize += videoSize;
        }

        var cacheStats = _transcoding.GetCacheStats();
        var cache = new
        {
            hlsSize = cacheStats.HLSTotalSizeBytes,
            hlsEntries = cacheStats.HLSEntries,
            hlsMaxGB = cacheStats.HLSMaxSizeGB,
            hlsRetentionDays = cacheStats.HLSRetentionDays,
            hlsOldestEntryAgeDays = cacheStats.HLSOldestEntryAgeDays,
            remuxSize = cacheStats.RemuxTotalSizeBytes,
            remuxEntries = cacheStats.RemuxEntries
        };

        return Ok(new { shares, totalSize, cache });
    }

    // ─── UI Templates ──────────────────────────────────────────────

    [HttpGet("templates")]
    public IActionResult GetTemplates()
    {
        var templatesDir = Path.Combine(_env.WebRootPath, "templates");
        var list = new List<object>();
        if (Directory.Exists(templatesDir))
        {
            foreach (var dir in Directory.GetDirectories(templatesDir).OrderBy(d => d))
            {
                var id = Path.GetFileName(dir);
                var metaPath = Path.Combine(dir, "template.json");
                string name = id, description = "", author = "", version = "1.0";
                bool hasScript  = System.IO.File.Exists(Path.Combine(dir, "script.js"));
                bool hasCss     = System.IO.File.Exists(Path.Combine(dir, "style.css"));
                bool hasPreview = System.IO.File.Exists(Path.Combine(dir, "preview.png"));
                if (System.IO.File.Exists(metaPath))
                {
                    try
                    {
                        var meta = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(metaPath)).RootElement;
                        if (meta.TryGetProperty("name",        out var n)) name        = n.GetString() ?? id;
                        if (meta.TryGetProperty("description", out var d)) description = d.GetString() ?? "";
                        if (meta.TryGetProperty("author",      out var a)) author      = a.GetString() ?? "";
                        if (meta.TryGetProperty("version",     out var v)) version     = v.GetString() ?? "1.0";
                    }
                    catch { /* malformed JSON - use defaults */ }
                }
                list.Add(new { id, name, description, author, version, hasScript, hasCss, hasPreview });
            }
        }
        return Ok(list);
    }

    // ─── Config Info ───────────────────────────────────────────────

    [HttpGet("config/info")]
    public IActionResult GetConfigInfo()
    {
        var cfg = _config.Config;

        // Resolve actual rolling log file (Serilog appends yyyyMMdd, e.g. nexusm20260226.log)
        var cfgDir = Path.GetDirectoryName(_config.ConfigFilePath) ?? AppContext.BaseDirectory;
        var cfgLogPath = Path.IsPathRooted(cfg.Logging.LogFile)
            ? cfg.Logging.LogFile
            : Path.Combine(cfgDir, cfg.Logging.LogFile);
        var logDir = Path.GetDirectoryName(cfgLogPath) ?? cfgDir;
        var logBase = Path.GetFileNameWithoutExtension(cfgLogPath);
        var logExt = Path.GetExtension(cfgLogPath);
        var resolvedLogFile = cfgLogPath;
        if (Directory.Exists(logDir))
        {
            var newestLog = Directory.GetFiles(logDir, logBase + "*" + logExt)
                .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
                .FirstOrDefault();
            if (newestLog != null) resolvedLogFile = newestLog;
        }

        return Ok(new
        {
            // Server
            // Number from the assembly (single source of truth: csproj), codename kept
            // literal. This value is ALSO what the Auto-Update panel shows before you
            // press Check Now - when it was hardcoded and AppVersion had drifted, the
            // panel showed one number on load and a different one after checking.
            version = $"{AutoUpdateService.AppVersion} (New World Order Edition)",
            platform = Environment.OSVersion.ToString(),
            isLinux = OperatingSystem.IsLinux(),
            isDocker = System.IO.File.Exists("/.dockerenv") ||
                       Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true",
            framework = Environment.Version.ToString(),
            serverPort = cfg.Server.ServerPort,
            serverHost = cfg.Server.ServerHost,
            workerThreads = cfg.Server.WorkerThreads,
            requestTimeout = cfg.Server.RequestTimeout,
            sessionTimeout = cfg.Server.SessionTimeout,
            showConsole = cfg.Server.ShowConsole,
            openBrowser = cfg.Server.OpenBrowser,
            runOnStartup = cfg.Server.RunOnStartup,
            configFile = _config.ConfigFilePath,
            logFile = resolvedLogFile,
            // Security
            securityByPin = cfg.Security.SecurityByPin,
            defaultAdminUser = cfg.Security.DefaultAdminUser,
            ipWhitelist = cfg.Security.IPWhitelist,
            // Library
            musicFolders = cfg.Library.MusicFolders,
            moviesTVFolders = cfg.Library.MoviesTVFolders,
            moviesFolders = cfg.Library.MoviesFolders,
            tvShowsFolders = cfg.Library.TvShowsFolders,
            picturesFolders = cfg.Library.PicturesFolders,
            musicVideosFolders = cfg.Library.MusicVideosFolders,
            ebooksFolders = cfg.Library.EBooksFolders,
            audioBooksFolders = cfg.Library.AudioBooksFolders,
            animeFolders = cfg.Library.AnimeFolders,
            audioExtensions = cfg.Library.AudioExtensions,
            imageExtensions = cfg.Library.ImageExtensions,
            ebookExtensions = cfg.Library.EBookExtensions,
            audioBooksExtensions = cfg.Library.AudioBooksExtensions,
            musicVideoExtensions = cfg.Library.MusicVideoExtensions,
            videoExtensions = cfg.Library.VideoExtensions,
            autoScanOnStartup = cfg.Library.AutoScanOnStartup,
            autoScanInterval = cfg.Library.AutoScanInterval,
            scanThreads = cfg.Library.ScanThreads,
            artistGrouping = cfg.Library.ArtistGrouping,
            dynamicCleanEnabled = cfg.Library.DynamicCleanEnabled,
            dynamicCleanIntervalMinutes = cfg.Library.DynamicCleanIntervalMinutes,
            // Playback
            transcodingEnabled = cfg.Playback.TranscodingEnabled,
            transcodeFormat = cfg.Playback.TranscodeFormat,
            transcodeBitrate = cfg.Playback.TranscodeBitrate,
            introSkipperEnabled = cfg.Playback.IntroSkipperEnabled,
            watchTogetherEnabled = cfg.Playback.WatchTogetherEnabled,
            ffmpegAvailable = _ffmpeg.IsAvailable,
            ffmpegPath = _ffmpeg.IsAvailable ? _ffmpeg.FfmpegPath : "",
            ffmpegVersion = _ffmpeg.BuildInfo?.Version ?? "",
            ffmpegHasLibx264 = _ffmpeg.BuildInfo?.HasLibx264 ?? false,
            ffmpegHasLibx265 = _ffmpeg.BuildInfo?.HasLibx265 ?? false,
            ffmpegHasLibzimg = _ffmpeg.BuildInfo?.HasLibzimg ?? false,
            ffmpegHasNvenc = _ffmpeg.BuildInfo?.HasNvenc ?? false,
            ffmpegHasNvdec = _ffmpeg.BuildInfo?.HasNvdec ?? false,
            ffmpegHasAmf = _ffmpeg.BuildInfo?.HasAmf ?? false,
            ffmpegHasVaapi = _ffmpeg.BuildInfo?.HasVaapi ?? false,
            ffmpegHasQsv = _ffmpeg.BuildInfo?.HasQsv ?? false,
            ffmpegIsBtbNBuild = _ffmpeg.BuildInfo?.IsBtbNFullBuild ?? false,
            // Transcoding (video)
            preferredEncoder = cfg.Transcoding.PreferredEncoder,
            preferredVideoCodec = cfg.Transcoding.PreferredVideoCodec,
            activeEncoder = _gpuDetection.Capabilities?.ActiveEncoder ?? "software",
            av1NvencAvailable = _gpuDetection.Av1Support.Nvenc,
            av1QsvAvailable   = _gpuDetection.Av1Support.Qsv,
            av1AmfAvailable   = _gpuDetection.Av1Support.Amf,
            detectedGpus = _gpuDetection.GpuInfo?.DetectedGPUs.Select(g => new { g.Name, vendor = g.Vendor.ToString(), g.EncoderType }) ?? [],
            videoPreset = cfg.Transcoding.VideoPreset,
            videoCRF = cfg.Transcoding.VideoCRF,
            videoMaxrate = cfg.Transcoding.VideoMaxrate,
            videoBufsize = cfg.Transcoding.VideoBufsize,
            transcodingAudioCodec = cfg.Transcoding.AudioCodec,
            transcodingAudioBitrate = cfg.Transcoding.AudioBitrate,
            transcodingAudioChannels = cfg.Transcoding.AudioChannels,
            maxConcurrentTranscodes = cfg.Transcoding.MaxConcurrentTranscodes,
            maxConcurrentPassthrough = cfg.Transcoding.MaxConcurrentPassthrough,
            ffmpegCPULimit = cfg.Transcoding.FFmpegCPULimit,
            remuxPriority = cfg.Transcoding.RemuxPriority,
            remuxThreads = cfg.Transcoding.RemuxThreads,
            transcodeMaxHeight = cfg.Transcoding.TranscodeMaxHeight,
            hdrPlaybackMode = cfg.Transcoding.HdrPlaybackMode,
            hlsSegmentDuration = cfg.Transcoding.HLSSegmentDuration,
            hlsCacheEnabled = cfg.Transcoding.HLSCacheEnabled,
            hlsCacheMaxSizeGB = cfg.Transcoding.HLSCacheMaxSizeGB,
            hlsCacheRetentionDays = cfg.Transcoding.HLSCacheRetentionDays,
            transcodeFormats = cfg.Transcoding.TranscodeFormats,
            // Logging
            logLevel = cfg.Logging.LogLevel,
            maxLogSizeMB = cfg.Logging.MaxLogSizeMB,
            // UI
            // Per-user theme wins over the server default. Resolved here rather than
            // in the frontend so the very first paint after login already uses the
            // user's theme - no flash of the admin's theme while a second call lands.
            theme = ResolveUserTheme(cfg.UI.Theme),
            // The server-wide default, so the settings UI can label the user's
            // choice as an override and offer a "use server default" reset.
            serverTheme = cfg.UI.Theme,
            defaultView = cfg.UI.DefaultView,
            language = cfg.UI.Language,
            // Global date-display format (auto/dmy/mdy/iso). Seeded into the client at
            // init so every rendered date uses the admin's chosen order.
            dateFormat = cfg.UI.DateFormat,
            showMusic = cfg.UI.ShowMusic,
            showPictures = cfg.UI.ShowPictures,
            showMoviesTV = cfg.UI.ShowMoviesTV,
            showMovies = cfg.UI.ShowMovies,
            showTvShows = cfg.UI.ShowTvShows,
            showMusicVideos = cfg.UI.ShowMusicVideos,
            showRadio = cfg.UI.ShowRadio,
            showInternetTV = cfg.UI.ShowInternetTV,
            showEBooks = cfg.UI.ShowEBooks,
            showAudioBooks = cfg.UI.ShowAudioBooks,
            showActors = cfg.UI.ShowActors,
            showPodcasts = cfg.UI.ShowPodcasts,
            showAnime = cfg.UI.ShowAnime,
            goBigDefault = cfg.UI.GoBigDefault,
            showWelcome  = cfg.UI.ShowWelcome,
            // Per-user template wins over the server default (same rule as theme).
            uiTemplate   = ResolveUserTemplate(cfg.UI.Template),
            serverTemplate = cfg.UI.Template,
            // Per-user library view modes (incl. "Recommended"), so the client seeds
            // the remembered choice at login before the first render.
            viewModes = ResolveUserViewModes(),
            // HTTPS
            httpsEnabled = cfg.Https.Enabled,
            httpsPort = cfg.Https.HttpsPort,
            httpsRedirectHttp = cfg.Https.RedirectHttpToHttps,
            httpsCertPath = cfg.Https.CertPath,
            httpsDockerHostIPs = cfg.Https.DockerHostIPs,
            httpsCertStatus = GetCertStatus(cfg),
            httpsCertExpiry = GetCertExpiry(cfg),
            // Demo mode (conf-only, server-authoritative)
            demoModeEnabled = cfg.DemoMode.Enabled,
            demoMode = cfg.DemoMode.Enabled ? (object)new {
                showMusic      = cfg.DemoMode.ShowMusic,
                showPicture    = cfg.DemoMode.ShowPicture,
                showMusicVideo = cfg.DemoMode.ShowMusicVideo,
                showRadio      = cfg.DemoMode.ShowRadio,
                showTV         = cfg.DemoMode.ShowTV,
                showPodcast    = cfg.DemoMode.ShowPodcast,
                showEBooks     = cfg.DemoMode.ShowEBooks,
                showAudioBooks = cfg.DemoMode.ShowAudioBooks,
                showSettings   = cfg.DemoMode.ShowSettings,
            } : null,
            // Google Cast: LAN-reachable address the receiver device uses to pull streams.
            // Cast over plain HTTP - the receiver cannot validate NexusM's self-signed HTTPS cert.
            castServerLanIp = DlnaService.GetLocalIpAddress(),
            castServerHttpPort = cfg.Server.ServerPort,
            // Metadata
            metadataProvider = cfg.Metadata.Provider,
            tmdbApiKey = cfg.Metadata.TmdbApiKey,
            // TMDB is always available thanks to the bundled key - the frontend gates
            // TMDB-dependent features (e.g. New Releases) on this, not on the user key.
            tmdbAvailable = cfg.Metadata.HasTmdbKey,
            watchmodeApiKey = cfg.Metadata.WatchmodeApiKey,
            fanartApiKey = cfg.Metadata.FanartApiKey,
            scrapeLanguage = cfg.Metadata.ScrapeLanguage,
            fetchMetadataOnScan = cfg.Metadata.FetchOnScan,
            fetchCastPhotos = cfg.Metadata.FetchCastPhotos,
            nfoMode = cfg.Metadata.NfoMode,
            nfoAutoExport = cfg.Metadata.NfoAutoExport,
            // Subtitles
            openSubtitlesApiKey = cfg.Subtitles.OpenSubtitlesApiKey,
            openSubtitlesUsername = cfg.Subtitles.OpenSubtitlesUsername,
            openSubtitlesPassword = cfg.Subtitles.OpenSubtitlesPassword,
            subDlApiKey = cfg.Subtitles.SubDlApiKey,
            // DLNA
            dlnaEnabled = cfg.Dlna.Enabled,
            dlnaFriendlyName = cfg.Dlna.FriendlyName,
            // Trakt
            traktClientId       = cfg.Trakt.ClientId,
            traktClientSecret   = cfg.Trakt.ClientSecret,
            traktScrobbleEnabled = cfg.Trakt.ScrobbleEnabled,
            traktConnected      = _trakt.IsConnected(CurrentUsername),
            traktUsername       = _trakt.GetTraktUsername(CurrentUsername) ?? "",
            // Last.fm / ListenBrainz
            lastFmApiKey        = cfg.LastFm.ApiKey,
            lastFmApiSecret     = cfg.LastFm.ApiSecret,
            lfmConnected        = _scrobbling.LfmIsConnected(CurrentUsername),
            lfmUsername         = _scrobbling.LfmGetUsername(CurrentUsername) ?? "",
            lbConnected         = _scrobbling.LbIsConnected(CurrentUsername),
            lbUsername          = _scrobbling.LbGetUsername(CurrentUsername) ?? "",
            // Analysis / Deep Scan
            deepScanEnabled         = cfg.Analysis.DeepScanEnabled,
            deepScanIntervalMinutes = cfg.Analysis.DeepScanIntervalMinutes,
            deepScanDelayMs         = cfg.Analysis.DeepScanDelayMs,
            deepScanBatchSize       = cfg.Analysis.DeepScanBatchSize,
            // Video Thumbnails
            videoThumbnailsEnabled  = cfg.VideoThumbnails.Enabled,
            videoThumbnailsInterval = cfg.VideoThumbnails.IntervalSeconds,
            // Video Previews (hero clips)
            videoPreviewsEnabled  = cfg.VideoPreviews.Enabled,
            videoPreviewsRate     = cfg.VideoPreviews.RatePerMinute,
            // AutoUpdate
            autoUpdateEnabled          = cfg.AutoUpdate.Enabled,
            autoUpdateCheckInterval    = cfg.AutoUpdate.CheckIntervalHours,
            autoUpdateChannel          = cfg.AutoUpdate.Channel,
            updateAvailable            = _autoUpdate.UpdateAvailable,
            // Databases - all, sorted alphabetically by display name
            databases = new (string name, string path)[]
            {
                ("Actors",         cfg.Database.ActorsDatabasePath),
                ("Audio Books",    cfg.Database.AudioBooksDatabasePath),
                ("eBooks",         cfg.Database.EBooksDatabasePath),
                ("EPG Guide TV",   cfg.Database.EpgDatabasePath),
                ("Internet TV",    cfg.Database.TvChannelsDatabasePath),
                ("Movies & TV",    cfg.Database.VideosDatabasePath),
                ("Music",          cfg.Database.DatabasePath),
                ("Music Videos",   cfg.Database.MusicVideosDatabasePath),
                ("Pictures",       cfg.Database.PicturesDatabasePath),
                ("Podcasts",       cfg.Database.PodcastsDatabasePath),
                ("Ratings",        cfg.Database.RatingsDatabasePath),
                ("Shares",         cfg.Database.SharesDatabasePath),
                // No config key - the semantic index lives at data/semantic.db (see
                // SemanticSearchService); compute the same real path so it displays like the rest.
                ("Smart Search",   System.IO.Path.Combine(AppContext.BaseDirectory, "data", "semantic.db")),
                ("Users",          cfg.Database.UsersDatabasePath)
            }.Select(e => new {
                name      = e.name,
                path      = e.path,
                exists    = System.IO.File.Exists(e.path),
                sizeBytes = System.IO.File.Exists(e.path) ? new System.IO.FileInfo(e.path).Length : 0L
            }).ToArray()
        });
    }

    // ─── HTTPS cert status helpers ───────────────────────────────────

    private string GetCertStatus(NexusM.Models.AppConfig cfg)
    {
        if (!string.IsNullOrEmpty(cfg.Https.CertPath))
            return System.IO.File.Exists(cfg.Https.CertPath) ? "custom" : "custom-missing";
        var autoCertPath = HttpsCertHelper.DefaultCertPath(_env.ContentRootPath);
        return System.IO.File.Exists(autoCertPath) ? "auto" : "none";
    }

    private string GetCertExpiry(NexusM.Models.AppConfig cfg)
    {
        try
        {
            var certPath = HttpsCertHelper.ResolveCertPath(cfg, _env.ContentRootPath);
            if (!System.IO.File.Exists(certPath)) return "";
            var certPassword = HttpsCertHelper.ResolveCertPassword(cfg);
            using var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(certPath, certPassword);
            return cert.NotAfter.ToString("yyyy-MM-dd");
        }
        catch { return ""; }
    }

    // ─── HTTPS cert download / regenerate ────────────────────────────

    /// <summary>
    /// Downloads the public NexusM certificate in DER format (.crt) for installation
    /// on Apple TV (Settings → General → VPN &amp; Device Management) or other devices.
    /// Contains the public key only - no private key is exposed.
    /// Uses application/octet-stream so all browsers treat it as a binary file download
    /// rather than trying to open/play/import it automatically.
    /// </summary>
    [HttpGet("https/download-cert")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public IActionResult DownloadCert()
    {
        var certPath = HttpsCertHelper.ResolveCertPath(_config.Config, _env.ContentRootPath);
        var certPassword = HttpsCertHelper.ResolveCertPassword(_config.Config);
        var derBytes = HttpsCertHelper.ExportPublicCert(certPath, certPassword);
        if (derBytes == null)
            return NotFound(new { error = "Certificate not found. Enable HTTPS and restart NexusM to generate it." });
        Response.Headers.ContentDisposition = "attachment; filename=\"nexusm.crt\"";
        return File(derBytes, "application/octet-stream");
    }

    /// <summary>
    /// Regenerates the auto-generated self-signed certificate immediately.
    /// Only available when no custom certificate path is configured.
    /// A server restart is still required for the new cert to take effect.
    /// </summary>
    [HttpPost("https/regenerate-cert")]
    public IActionResult RegenerateCert()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
            return Forbid();
        if (!string.IsNullOrEmpty(_config.Config.Https.CertPath))
            return BadRequest(new { error = "A custom certificate is configured. Remove the custom cert path first to use the auto-generated certificate." });
        var certPath = HttpsCertHelper.DefaultCertPath(_env.ContentRootPath);
        try
        {
            if (System.IO.File.Exists(certPath))
                System.IO.File.Delete(certPath);
            var certPassword = HttpsCertHelper.ResolveCertPassword(_config.Config);
            string expiry;
            try
            {
                var cert = HttpsCertHelper.GenerateSelfSignedCert(certPath, certPassword, _config.Config.Https.DockerHostIPs);
                expiry = cert.NotAfter.ToString("yyyy-MM-dd");
            }
            catch when (System.IO.File.Exists(certPath))
            {
                // PFX was written successfully but the in-memory reload threw
                // (Windows CryptographicException due to MachineKeySet permissions).
                // The cert on disk is valid; expiry is always 2 years from generation.
                expiry = DateTimeOffset.UtcNow.AddYears(2).ToString("yyyy-MM-dd");
            }
            return Ok(new { message = "Certificate regenerated. Restart NexusM to apply the new certificate.", expiry });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to regenerate certificate: " + ex.Message });
        }
    }

    [HttpPost("config/save")]
    public IActionResult SaveConfig([FromBody] System.Text.Json.JsonElement body)
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
            return Forbid();

        try
        {
            var cfg = _config.Config;

            // Server
            if (body.TryGetProperty("serverHost", out var sh)) cfg.Server.ServerHost = sh.GetString() ?? "0.0.0.0";
            if (body.TryGetProperty("serverPort", out var sp)) cfg.Server.ServerPort = sp.GetInt32();
            if (body.TryGetProperty("workerThreads", out var wt)) cfg.Server.WorkerThreads = wt.GetInt32();
            if (body.TryGetProperty("requestTimeout", out var rt)) cfg.Server.RequestTimeout = rt.GetInt32();
            if (body.TryGetProperty("sessionTimeout", out var st)) cfg.Server.SessionTimeout = st.GetInt32();
            if (body.TryGetProperty("showConsole", out var sc)) cfg.Server.ShowConsole = sc.GetBoolean();
            if (body.TryGetProperty("openBrowser", out var ob)) cfg.Server.OpenBrowser = ob.GetBoolean();
            if (body.TryGetProperty("runOnStartup", out var ros))
            {
                cfg.Server.RunOnStartup = ros.GetBoolean();
                if (OperatingSystem.IsWindows()) StartupRegistryHelper.SetRunOnStartup(ros.GetBoolean());
                else if (OperatingSystem.IsLinux()) StartupRegistryHelper.SetRunOnStartup(ros.GetBoolean());
            }

            // HTTPS
            if (body.TryGetProperty("httpsEnabled", out var he)) cfg.Https.Enabled = he.GetBoolean();
            if (body.TryGetProperty("httpsPort", out var hp)) cfg.Https.HttpsPort = hp.GetInt32();
            if (body.TryGetProperty("httpsRedirectHttp", out var hrh)) cfg.Https.RedirectHttpToHttps = hrh.GetBoolean();
            if (body.TryGetProperty("httpsCertPath", out var hcp)) cfg.Https.CertPath = hcp.GetString() ?? "";
            if (body.TryGetProperty("httpsCertPassword", out var hcpw) && !string.IsNullOrEmpty(hcpw.GetString()))
                cfg.Https.CertPassword = hcpw.GetString() ?? "";
            if (body.TryGetProperty("httpsDockerHostIPs", out var hdhi)) cfg.Https.DockerHostIPs = hdhi.GetString() ?? "";

            // Security
            if (body.TryGetProperty("securityByPin", out var sbp)) cfg.Security.SecurityByPin = sbp.GetBoolean();
            if (body.TryGetProperty("defaultAdminUser", out var dau)) cfg.Security.DefaultAdminUser = dau.GetString() ?? "admin";
            if (body.TryGetProperty("ipWhitelist", out var ipw)) cfg.Security.IPWhitelist = ipw.GetString() ?? "";

            // Library - folders
            if (body.TryGetProperty("musicFolders", out var mf)) cfg.Library.MusicFolders = mf.GetString() ?? "";
            if (body.TryGetProperty("moviesTVFolders", out var mtf)) cfg.Library.MoviesTVFolders = mtf.GetString() ?? "";
            if (body.TryGetProperty("moviesFolders", out var mf2)) cfg.Library.MoviesFolders = mf2.GetString() ?? "";
            if (body.TryGetProperty("tvShowsFolders", out var tvsf)) cfg.Library.TvShowsFolders = tvsf.GetString() ?? "";
            if (body.TryGetProperty("picturesFolders", out var pf)) cfg.Library.PicturesFolders = pf.GetString() ?? "";
            if (body.TryGetProperty("musicVideosFolders", out var mvf)) cfg.Library.MusicVideosFolders = mvf.GetString() ?? "";
            if (body.TryGetProperty("ebooksFolders", out var ef)) cfg.Library.EBooksFolders = ef.GetString() ?? "";
            if (body.TryGetProperty("audioBooksFolders", out var abf)) cfg.Library.AudioBooksFolders = abf.GetString() ?? "";
            if (body.TryGetProperty("animeFolders", out var anf)) cfg.Library.AnimeFolders = anf.GetString() ?? "";
            // Library - extensions
            if (body.TryGetProperty("audioExtensions", out var ae)) cfg.Library.AudioExtensions = ae.GetString() ?? "";
            if (body.TryGetProperty("imageExtensions", out var ie)) cfg.Library.ImageExtensions = ie.GetString() ?? "";
            if (body.TryGetProperty("ebookExtensions", out var ee)) cfg.Library.EBookExtensions = ee.GetString() ?? "";
            if (body.TryGetProperty("audioBooksExtensions", out var abe)) cfg.Library.AudioBooksExtensions = abe.GetString() ?? "";
            if (body.TryGetProperty("musicVideoExtensions", out var mve)) cfg.Library.MusicVideoExtensions = mve.GetString() ?? "";
            if (body.TryGetProperty("videoExtensions", out var ve)) cfg.Library.VideoExtensions = ve.GetString() ?? "";
            // Library - scan
            if (body.TryGetProperty("autoScanOnStartup", out var aso)) cfg.Library.AutoScanOnStartup = aso.GetBoolean();
            if (body.TryGetProperty("autoScanInterval", out var asi)) cfg.Library.AutoScanInterval = asi.GetInt32();
            if (body.TryGetProperty("scanThreads", out var sth)) cfg.Library.ScanThreads = sth.GetInt32();
            if (body.TryGetProperty("artistGrouping", out var agp))
            {
                var v = agp.GetString();
                cfg.Library.ArtistGrouping = v is "albumartist" or "albumartist-indexed" or "primary" ? v : "artist";
            }
            if (body.TryGetProperty("dynamicCleanEnabled", out var dce)) cfg.Library.DynamicCleanEnabled = dce.GetBoolean();
            if (body.TryGetProperty("dynamicCleanIntervalMinutes", out var dcim)) cfg.Library.DynamicCleanIntervalMinutes = dcim.GetInt32();

            // Playback
            if (body.TryGetProperty("transcodingEnabled", out var te)) cfg.Playback.TranscodingEnabled = te.GetBoolean();
            if (body.TryGetProperty("transcodeFormat", out var tf)) cfg.Playback.TranscodeFormat = tf.GetString() ?? "mp3";
            if (body.TryGetProperty("transcodeBitrate", out var tb)) cfg.Playback.TranscodeBitrate = tb.GetString() ?? "192k";
            if (body.TryGetProperty("ffmpegPath", out var fp)) cfg.Playback.FFmpegPath = fp.GetString() ?? "";
            if (body.TryGetProperty("introSkipperEnabled", out var ise)) cfg.Playback.IntroSkipperEnabled = ise.GetBoolean();
            if (body.TryGetProperty("watchTogetherEnabled", out var spe)) cfg.Playback.WatchTogetherEnabled = spe.GetBoolean();

            // Transcoding (video)
            if (body.TryGetProperty("preferredEncoder", out var pe)) cfg.Transcoding.PreferredEncoder = pe.GetString() ?? "auto";
            if (body.TryGetProperty("videoPreset", out var vp)) cfg.Transcoding.VideoPreset = vp.GetString() ?? "veryfast";
            if (body.TryGetProperty("videoCRF", out var vc)) cfg.Transcoding.VideoCRF = vc.GetInt32();
            if (body.TryGetProperty("videoMaxrate", out var vmr)) cfg.Transcoding.VideoMaxrate = vmr.GetString() ?? "5M";
            if (body.TryGetProperty("videoBufsize", out var vbs)) cfg.Transcoding.VideoBufsize = vbs.GetString() ?? "10M";
            if (body.TryGetProperty("transcodingAudioCodec", out var tac)) cfg.Transcoding.AudioCodec = tac.GetString() ?? "aac";
            if (body.TryGetProperty("transcodingAudioBitrate", out var tab)) cfg.Transcoding.AudioBitrate = tab.GetString() ?? "192k";
            if (body.TryGetProperty("transcodingAudioChannels", out var tach)) cfg.Transcoding.AudioChannels = tach.GetInt32();
            if (body.TryGetProperty("maxConcurrentTranscodes", out var mct)) cfg.Transcoding.MaxConcurrentTranscodes = mct.GetInt32();
            if (body.TryGetProperty("maxConcurrentPassthrough", out var mcp)) cfg.Transcoding.MaxConcurrentPassthrough = mcp.GetInt32();
            if (body.TryGetProperty("ffmpegCPULimit", out var fcl)) cfg.Transcoding.FFmpegCPULimit = fcl.GetInt32();
            if (body.TryGetProperty("remuxPriority", out var rp)) cfg.Transcoding.RemuxPriority = rp.GetString() ?? "abovenormal";
            if (body.TryGetProperty("remuxThreads", out var rth)) cfg.Transcoding.RemuxThreads = rth.GetInt32();
            if (body.TryGetProperty("transcodeMaxHeight", out var tmh)) cfg.Transcoding.TranscodeMaxHeight = tmh.GetInt32();
            if (body.TryGetProperty("hlsSegmentDuration", out var hsd)) cfg.Transcoding.HLSSegmentDuration = hsd.GetInt32();
            if (body.TryGetProperty("hlsCacheEnabled", out var hce)) cfg.Transcoding.HLSCacheEnabled = hce.GetBoolean();
            if (body.TryGetProperty("hlsCacheMaxSizeGB", out var hcm)) cfg.Transcoding.HLSCacheMaxSizeGB = hcm.GetInt32();
            if (body.TryGetProperty("hlsCacheRetentionDays", out var hcr)) cfg.Transcoding.HLSCacheRetentionDays = hcr.GetInt32();
            if (body.TryGetProperty("transcodeFormats", out var tfs)) cfg.Transcoding.TranscodeFormats = tfs.GetString() ?? "";
            if (body.TryGetProperty("hdrPlaybackMode", out var hpm)) cfg.Transcoding.HdrPlaybackMode = hpm.GetString() ?? "sdr";
            if (body.TryGetProperty("preferredVideoCodec", out var pvc) && pvc.GetString() is { } codec
                && (codec == "h264" || codec == "av1"))
                cfg.Transcoding.PreferredVideoCodec = codec;

            // Logging
            if (body.TryGetProperty("logLevel", out var ll)) cfg.Logging.LogLevel = ll.GetString() ?? "Information";
            if (body.TryGetProperty("maxLogSizeMB", out var mls)) cfg.Logging.MaxLogSizeMB = mls.GetInt32();

            // UI
            if (body.TryGetProperty("theme", out var th)) cfg.UI.Theme = th.GetString() ?? "dark";
            if (body.TryGetProperty("defaultView", out var dv)) cfg.UI.DefaultView = dv.GetString() ?? "grid";
            if (body.TryGetProperty("language", out var lang)) cfg.UI.Language = lang.GetString() ?? "en";
            if (body.TryGetProperty("dateFormat", out var df))
            {
                var v = (df.GetString() ?? "auto").ToLowerInvariant();
                cfg.UI.DateFormat = (v == "dmy" || v == "mdy" || v == "iso") ? v : "auto";
            }
            if (body.TryGetProperty("showMusic", out var sm)) cfg.UI.ShowMusic = sm.GetBoolean();
            if (body.TryGetProperty("showPictures", out var spic)) cfg.UI.ShowPictures = spic.GetBoolean();
            if (body.TryGetProperty("showMoviesTV", out var smt)) cfg.UI.ShowMoviesTV = smt.GetBoolean();
            if (body.TryGetProperty("showMovies", out var smov)) cfg.UI.ShowMovies = smov.GetBoolean();
            if (body.TryGetProperty("showTvShows", out var stvs)) cfg.UI.ShowTvShows = stvs.GetBoolean();
            if (body.TryGetProperty("showMusicVideos", out var smv)) cfg.UI.ShowMusicVideos = smv.GetBoolean();
            if (body.TryGetProperty("showRadio", out var sr)) cfg.UI.ShowRadio = sr.GetBoolean();
            if (body.TryGetProperty("showInternetTV", out var sit)) cfg.UI.ShowInternetTV = sit.GetBoolean();
            if (body.TryGetProperty("showEBooks", out var seb)) cfg.UI.ShowEBooks = seb.GetBoolean();
            if (body.TryGetProperty("showAudioBooks", out var sab)) cfg.UI.ShowAudioBooks = sab.GetBoolean();
            if (body.TryGetProperty("showActors", out var sac)) cfg.UI.ShowActors = sac.GetBoolean();
            if (body.TryGetProperty("showPodcasts", out var spod)) cfg.UI.ShowPodcasts = spod.GetBoolean();
            if (body.TryGetProperty("showAnime", out var san)) cfg.UI.ShowAnime = san.GetBoolean();
            if (body.TryGetProperty("goBigDefault", out var gbdf)) cfg.UI.GoBigDefault = gbdf.GetBoolean();
            if (body.TryGetProperty("uiTemplate", out var tpl)) cfg.UI.Template = tpl.GetString() ?? "";

            // Metadata
            if (body.TryGetProperty("metadataProvider", out var mp)) cfg.Metadata.Provider = mp.GetString() ?? "tmdb";
            if (body.TryGetProperty("tmdbApiKey", out var tmk) && !string.IsNullOrEmpty(tmk.GetString())) cfg.Metadata.TmdbApiKey = tmk.GetString()!;
            if (body.TryGetProperty("watchmodeApiKey", out var wmk) && !string.IsNullOrEmpty(wmk.GetString())) cfg.Metadata.WatchmodeApiKey = wmk.GetString()!;
            if (body.TryGetProperty("fanartApiKey", out var fak) && !string.IsNullOrEmpty(fak.GetString())) cfg.Metadata.FanartApiKey = fak.GetString()!;
            // Validated against the same list the pickers offer - the value goes
            // straight into a TMDB query string.
            if (body.TryGetProperty("scrapeLanguage", out var slg))
            {
                var sl = (slg.GetString() ?? "").Trim();
                if (sl.Length > 0 && ScrapeLanguages.Any(x => string.Equals(x.Code, sl, StringComparison.OrdinalIgnoreCase)))
                    cfg.Metadata.ScrapeLanguage = sl;
            }
            if (body.TryGetProperty("fetchMetadataOnScan", out var fms)) cfg.Metadata.FetchOnScan = fms.GetBoolean();
            if (body.TryGetProperty("fetchCastPhotos", out var fcp)) cfg.Metadata.FetchCastPhotos = fcp.GetBoolean();
            if (body.TryGetProperty("nfoMode", out var nfm))
            {
                var nm = (nfm.GetString() ?? "").Trim().ToLowerInvariant();
                if (nm is "off" or "online-then-nfo" or "nfo-then-online" or "nfo-only") cfg.Metadata.NfoMode = nm;
            }
            if (body.TryGetProperty("nfoAutoExport", out var nae)) cfg.Metadata.NfoAutoExport = nae.GetBoolean();

            // Subtitles
            if (body.TryGetProperty("openSubtitlesApiKey", out var osak)) cfg.Subtitles.OpenSubtitlesApiKey = osak.GetString() ?? "";
            if (body.TryGetProperty("openSubtitlesUsername", out var osu)) cfg.Subtitles.OpenSubtitlesUsername = osu.GetString() ?? "";
            if (body.TryGetProperty("openSubtitlesPassword", out var osp) && !string.IsNullOrEmpty(osp.GetString()))
                cfg.Subtitles.OpenSubtitlesPassword = osp.GetString() ?? "";
            if (body.TryGetProperty("subDlApiKey", out var sdlk)) cfg.Subtitles.SubDlApiKey = sdlk.GetString() ?? "";

            // DLNA
            if (body.TryGetProperty("dlnaEnabled", out var dlnaEn)) cfg.Dlna.Enabled = dlnaEn.GetBoolean();
            if (body.TryGetProperty("dlnaFriendlyName", out var dlnaFn) && !string.IsNullOrWhiteSpace(dlnaFn.GetString()))
                cfg.Dlna.FriendlyName = dlnaFn.GetString()!;

            // Trakt
            if (body.TryGetProperty("traktClientId",        out var tci) && !string.IsNullOrEmpty(tci.GetString()))  cfg.Trakt.ClientId        = tci.GetString()!;
            if (body.TryGetProperty("traktClientSecret",    out var tcs) && !string.IsNullOrEmpty(tcs.GetString()))  cfg.Trakt.ClientSecret    = tcs.GetString()!;
            if (body.TryGetProperty("traktScrobbleEnabled", out var tse))  cfg.Trakt.ScrobbleEnabled = tse.GetBoolean();

            // Last.fm
            if (body.TryGetProperty("lastFmApiKey",    out var lfk) && !string.IsNullOrEmpty(lfk.GetString())) cfg.LastFm.ApiKey    = lfk.GetString()!;
            if (body.TryGetProperty("lastFmApiSecret", out var lfs)) cfg.LastFm.ApiSecret = lfs.GetString() ?? "";

            // Analysis / Deep Scan
            if (body.TryGetProperty("deepScanEnabled",         out var dse))  cfg.Analysis.DeepScanEnabled         = dse.GetBoolean();
            if (body.TryGetProperty("deepScanIntervalMinutes", out var dsim)) cfg.Analysis.DeepScanIntervalMinutes = dsim.GetInt32();
            if (body.TryGetProperty("deepScanDelayMs",         out var dsdm)) cfg.Analysis.DeepScanDelayMs         = dsdm.GetInt32();
            if (body.TryGetProperty("deepScanBatchSize",       out var dsbs)) cfg.Analysis.DeepScanBatchSize       = dsbs.GetInt32();

            // Video Thumbnails
            if (body.TryGetProperty("videoThumbnailsEnabled",  out var vte))  cfg.VideoThumbnails.Enabled         = vte.GetBoolean();
            if (body.TryGetProperty("videoThumbnailsInterval", out var vti))  cfg.VideoThumbnails.IntervalSeconds = Math.Clamp(vti.GetInt32(), 5, 60);

            // Video Previews
            if (body.TryGetProperty("videoPreviewsEnabled", out var vpe)) cfg.VideoPreviews.Enabled       = vpe.GetBoolean();
            if (body.TryGetProperty("videoPreviewsRate",    out var vpr)) cfg.VideoPreviews.RatePerMinute = Math.Clamp(vpr.GetInt32(), 1, 20);

            // AutoUpdate
            if (body.TryGetProperty("autoUpdateEnabled",       out var aue))  cfg.AutoUpdate.Enabled            = aue.GetBoolean();
            if (body.TryGetProperty("autoUpdateCheckInterval", out var auci)) cfg.AutoUpdate.CheckIntervalHours = Math.Clamp(auci.GetInt32(), 1, 168);
            if (body.TryGetProperty("autoUpdateChannel",       out var auc))  cfg.AutoUpdate.Channel            = string.Equals(auc.GetString()?.Trim(), "testing", StringComparison.OrdinalIgnoreCase) ? "testing" : "stable";

            _config.SaveConfig();

            // Check which configured folder paths are not accessible on the filesystem.
            // This is most common in Docker where a path is saved to NexusM.conf but the
            // host directory has not yet been mounted as a container volume.
            // Each value may contain multiple comma-separated paths - check each one individually.
            var folderRawValues = new[]
            {
                cfg.Library.MusicFolders, cfg.Library.MoviesFolders, cfg.Library.TvShowsFolders,
                cfg.Library.MusicVideosFolders, cfg.Library.PicturesFolders, cfg.Library.EBooksFolders,
                cfg.Library.AudioBooksFolders, cfg.Library.AnimeFolders
            };
            var inaccessibleFolders = folderRawValues
                .SelectMany(v => (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(p => !string.IsNullOrWhiteSpace(p) && !Directory.Exists(p))
                .ToList();

            return Ok(new { success = true, message = "Settings saved successfully", inaccessibleFolders });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration");
            return StatusCode(500, new { success = false, message = "Failed to save settings: " + ex.Message });
        }
    }

    // ─── Subtitle Search & Download ─────────────────────────────────────────────

    [HttpGet("videos/{id}/subtitles")]
    public async Task<IActionResult> SearchSubtitles(int id, [FromQuery] string language = "en")
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();

        // For anime use the series name as the search title - subtitle DBs (SUBDL, OpenTitles)
        // index by series, not by episode title. Jikan provides the correct English series name.
        var searchTitle = video.MediaType == "anime" && !string.IsNullOrEmpty(video.SeriesName)
            ? video.SeriesName
            : video.Title;
        var results = await _subtitles.SearchAsync(
            video.ImdbId, searchTitle, video.Year, language,
            video.MediaType, video.Season, video.Episode);
        return Ok(new { results });
    }

    [HttpPost("videos/{id}/subtitles/download")]
    public async Task<IActionResult> DownloadSubtitle(int id, [FromBody] System.Text.Json.JsonElement body)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();

        if (!body.TryGetProperty("provider", out var provEl) ||
            !body.TryGetProperty("fileId",   out var fidEl)  ||
            !body.TryGetProperty("language", out var langEl))
            return BadRequest(new { message = "provider, fileId and language are required" });

        var provider = provEl.GetString() ?? "";
        var fileId   = fidEl.GetString()  ?? "";
        var language = langEl.GetString() ?? "en";

        var vttUrl = await _subtitles.DownloadAsync(id, provider, fileId, language);
        if (vttUrl == null)
            return StatusCode(502, new { message = "Subtitle download failed. Check API keys and try again." });

        return Ok(new { vttUrl });
    }

    [HttpGet("videos/{id}/embedded-subtitles")]
    public async Task<IActionResult> GetEmbeddedSubtitles(int id)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();

        var probe = await _ffmpeg.ProbeAsync(video.FilePath);
        if (probe == null)
            return StatusCode(500, new { message = "ffprobe unavailable or file not found" });

        var bitmapFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hdmv_pgs_subtitle", "pgssub", "dvd_subtitle", "dvdsub", "dvb_subtitle", "dvb_teletext"
        };

        var tracks = new List<object>();
        int subtitleIndex = 0;

        foreach (var stream in probe.RootElement.GetProperty("streams").EnumerateArray())
        {
            if (!stream.TryGetProperty("codec_type", out var ct) || ct.GetString() != "subtitle")
                continue;

            var codec = stream.TryGetProperty("codec_name", out var cn) ? cn.GetString() ?? "" : "";

            if (!bitmapFormats.Contains(codec))
            {
                var tags = stream.TryGetProperty("tags", out var tg) ? tg : (JsonElement?)null;
                var disp = stream.TryGetProperty("disposition", out var dp) ? dp : (JsonElement?)null;

                var lang = tags.HasValue && tags.Value.TryGetProperty("language", out var lg)
                    ? lg.GetString() ?? "und" : "und";
                var title = tags.HasValue && tags.Value.TryGetProperty("title", out var ti)
                    ? ti.GetString() ?? "" : "";
                var forced = disp.HasValue && disp.Value.TryGetProperty("forced", out var fo)
                    && fo.GetInt32() == 1;
                var hi = disp.HasValue && disp.Value.TryGetProperty("hearing_impaired", out var h)
                    && h.GetInt32() == 1;

                tracks.Add(new { trackIndex = subtitleIndex, lang, title, codec, forced, hi });
            }

            subtitleIndex++;
        }

        return Ok(new { tracks });
    }

    [HttpGet("subtitles/embedded/{videoId}/{trackIndex}")]
    public async Task<IActionResult> GetEmbeddedSubtitleVtt(int videoId, int trackIndex)
    {
        var video = await _videoDb.Videos.FindAsync(videoId);
        if (video == null) return NotFound();

        var cacheDir = Path.Combine(_env.ContentRootPath, "assets", "subtitles", videoId.ToString());
        Directory.CreateDirectory(cacheDir);
        var vttPath = Path.Combine(cacheDir, $"embedded_{trackIndex}.vtt");

        if (!System.IO.File.Exists(vttPath))
        {
            if (_ffmpeg.FfmpegPath == null)
                return StatusCode(500, new { message = "FFmpeg not available" });

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = _ffmpeg.FfmpegPath,
                Arguments = $"-v quiet -i \"{video.FilePath}\" -map 0:s:{trackIndex} -c:s webvtt \"{vttPath}\" -y",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try { process.Start(); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start FFmpeg for subtitle extraction");
                return StatusCode(500, new { message = "Failed to start FFmpeg" });
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var completed = await Task.Run(() => process.WaitForExit(60000));
            await stdoutTask;
            var stderr = await stderrTask;

            if (!completed)
            {
                try { process.Kill(true); } catch { }
                return StatusCode(500, new { message = "FFmpeg timed out extracting subtitle" });
            }

            if (process.ExitCode != 0 || !System.IO.File.Exists(vttPath))
            {
                _logger.LogWarning("FFmpeg subtitle extract failed (exit {Code}): {Err}", process.ExitCode, stderr);
                return StatusCode(500, new { message = "Failed to extract subtitle track" });
            }
        }

        return PhysicalFile(vttPath, "text/vtt");
    }

    // ─── Custom (Uploaded) Subtitles ────────────────────────────────────────────

    [HttpGet("videos/{id}/subtitles/custom")]
    public async Task<IActionResult> GetCustomSubtitles(int id)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();

        var results = new List<object>();
        using var conn = OpenVideoDbRaw();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Language, Label, VttFileName, UploadedAt FROM CustomSubtitles WHERE VideoId = @vid ORDER BY UploadedAt";
        cmd.Parameters.AddWithValue("@vid", id);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(new
            {
                id         = reader.GetInt32(0),
                language   = reader.GetString(1),
                label      = reader.GetString(2),
                vttUrl     = $"/subtitle/{id}/{reader.GetString(3)}",
                uploadedAt = reader.GetString(4)
            });
        return Ok(new { subtitles = results });
    }

    [HttpPost("videos/{id}/subtitles/custom")]
    [RequestSizeLimit(15_000_000)]
    public async Task<IActionResult> UploadCustomSubtitle(int id, IFormFile file,
        [FromForm] string language = "und", [FromForm] string label = "")
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".srt" && ext != ".vtt")
            return BadRequest(new { message = "Only .srt and .vtt files are supported" });

        string content;
        using (var sr = new StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            content = await sr.ReadToEndAsync();

        var vttContent = ext == ".srt" ? SubtitleService.ConvertSrtToVtt(content) : content;
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var vttFileName = await _subtitles.SaveCustomSubtitleFileAsync(id, uniqueId, vttContent);

        if (string.IsNullOrWhiteSpace(label))
            label = Path.GetFileNameWithoutExtension(file.FileName);

        using var conn = OpenVideoDbRaw();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO CustomSubtitles (VideoId, Language, Label, VttFileName) VALUES (@vid, @lang, @label, @vtt)";
        cmd.Parameters.AddWithValue("@vid", id);
        cmd.Parameters.AddWithValue("@lang", language);
        cmd.Parameters.AddWithValue("@label", label);
        cmd.Parameters.AddWithValue("@vtt", vttFileName);
        cmd.ExecuteNonQuery();

        return Ok(new { vttUrl = $"/subtitle/{id}/{vttFileName}", label });
    }

    [HttpDelete("videos/{id}/subtitles/custom/{subtitleId:int}")]
    public async Task<IActionResult> DeleteCustomSubtitle(int id, int subtitleId)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();

        using var conn = OpenVideoDbRaw();
        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT VttFileName FROM CustomSubtitles WHERE Id = @sid AND VideoId = @vid";
        selectCmd.Parameters.AddWithValue("@sid", subtitleId);
        selectCmd.Parameters.AddWithValue("@vid", id);
        var vttFileName = (string?)selectCmd.ExecuteScalar();
        if (vttFileName == null) return NotFound();

        using var deleteCmd = conn.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM CustomSubtitles WHERE Id = @sid AND VideoId = @vid";
        deleteCmd.Parameters.AddWithValue("@sid", subtitleId);
        deleteCmd.Parameters.AddWithValue("@vid", id);
        deleteCmd.ExecuteNonQuery();

        _subtitles.DeleteCustomSubtitleFile(id, vttFileName);
        return Ok(new { success = true });
    }

    // ─── Sidecar .SRT Detection ─────────────────────────────────────────────────

    [HttpGet("videos/{id}/subtitles/sidecar")]
    public async Task<IActionResult> GetSidecarSubtitles(int id)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();
        var sidecars = Services.SubtitleService.FindSidecarSubtitles(video.FilePath);
        return Ok(new { sidecars = sidecars.Select(s => new { fileName = s.FileName, language = s.Language, label = s.Label }) });
    }

    [HttpGet("subtitles/sidecar/{videoId:int}/{fileName}")]
    public async Task<IActionResult> GetSidecarSubtitleVtt(int videoId, string fileName)
    {
        var video = await _videoDb.Videos.FindAsync(videoId);
        if (video == null) return NotFound();
        var vttUrl = await _subtitles.GetSidecarVttAsync(videoId, video.FilePath, fileName);
        if (vttUrl == null) return NotFound();
        // Serve the cached VTT directly
        var vttPath = Path.Combine(_env.ContentRootPath, "assets", "subtitles",
            videoId.ToString(), Path.GetFileName(vttUrl));
        if (!System.IO.File.Exists(vttPath)) return NotFound();
        return PhysicalFile(vttPath, "text/vtt");
    }

    [HttpPost("videos/fetch-metadata")]
    public async Task<IActionResult> FetchVideoMetadata([FromQuery] bool refetchAll = false, [FromQuery] bool overwrite = false)
    {
        if (_metadata.IsFetching)
            return Ok(new { message = "Metadata fetch already in progress" });

        if (refetchAll)
        {
            // Reset MetadataFetched flag for all videos so they get re-fetched
            var allVideos = await _videoDb.Videos.ToListAsync();
            foreach (var v in allVideos)
                v.MetadataFetched = false;
            await _videoDb.SaveChangesAsync();
            _logger.LogInformation("Reset MetadataFetched flag for {Count} videos (overwrite={Overwrite})", allVideos.Count, overwrite);
        }

        // overwrite=true (user confirmed "overwrite manual edits") replaces every editable field,
        // including on ManuallyEdited rows; otherwise the fetch only fills empty fields.
        _ = Task.Run(() => _metadata.EnrichLibraryAsync(null, overwrite: overwrite));
        return Ok(new { message = refetchAll ? "Re-fetching all metadata in background" : "Fetching missing metadata in background" });
    }

    [HttpGet("videos/fetch-metadata/status")]
    public IActionResult FetchMetadataStatus()
    {
        var (processed, total, message) = _metadata.FetchProgress;
        var (matched, noMatch, errors) = _metadata.FetchCounts;
        return Ok(new
        {
            isFetching = _metadata.IsFetching,
            processed,
            total,
            message,
            matched,
            noMatch,
            errors,
            percentComplete = total > 0 ? Math.Round((double)processed / total * 100, 1) : 0
        });
    }

    [HttpPost("videos/{id}/refresh-metadata")]
    public async Task<IActionResult> RefreshVideoMetadata(int id, [FromQuery] bool overwrite = false)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();

        await _metadata.FetchForVideoAsync(video, forceRefresh: true, overwrite: overwrite);
        return Ok(new { success = true });
    }

    // ─── Per-video metadata language ────────────────────────────────────────

    /// <summary>
    /// Languages offered by the metadata-language pickers. Kept to the set NexusM
    /// ships a UI translation for, plus the regional variants TMDB treats as distinct
    /// (pt-BR vs pt-PT, zh-CN vs zh-TW), so the two lists stay recognisable to users.
    /// </summary>
    private static readonly (string Code, string Name)[] ScrapeLanguages =
    {
        ("en-US", "English"),           ("fr-FR", "Français"),        ("de-DE", "Deutsch"),
        ("es-ES", "Español"),           ("it-IT", "Italiano"),        ("nl-NL", "Nederlands"),
        ("pt-PT", "Português"),         ("pt-BR", "Português (BR)"),  ("pl-PL", "Polski"),
        ("ru-RU", "Русский"),           ("uk-UA", "Українська"),      ("sv-SE", "Svenska"),
        ("no-NO", "Norsk"),             ("fi-FI", "Suomi"),           ("et-EE", "Eesti"),
        ("lt-LT", "Lietuvių"),          ("sl-SI", "Slovenščina"),     ("sq-AL", "Shqip"),
        ("sr-RS", "Српски"),            ("ro-RO", "Română"),          ("th-TH", "ไทย"),
        ("vi-VN", "Tiếng Việt"),        ("id-ID", "Bahasa Indonesia"),("hi-IN", "हिन्दी"),
        ("ja-JP", "日本語"),             ("ko-KR", "한국어"),           ("zh-CN", "中文 (简体)"),
        ("zh-TW", "中文 (繁體)"),        ("fa-IR", "فارسی")
    };

    [HttpGet("metadata/languages")]
    public IActionResult GetScrapeLanguages() => Ok(new
    {
        serverDefault = _config.Config.Metadata.ScrapeLanguage,
        languages = ScrapeLanguages.Select(l => new { code = l.Code, name = l.Name })
    });

    /// <summary>
    /// Sets (or clears) a per-video metadata language and immediately re-scrapes.
    /// Body: { language: "fr-FR" } - empty/null clears the override and falls back to
    /// the server setting. Admin-only: this rewrites catalog text for every user.
    /// </summary>
    [HttpPost("videos/{id}/metadata-language")]
    public async Task<IActionResult> SetVideoMetadataLanguage(int id, [FromBody] JsonElement body)
    {
        if (!User.IsInRole("admin"))
        {
            _logger.LogWarning("Non-admin {User} attempted to change metadata language for video {Id}", CurrentUsername, id);
            return Forbid();
        }

        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();

        var language = body.TryGetProperty("language", out var l) ? (l.GetString() ?? "").Trim() : "";
        if (language.Length > 0 && !ScrapeLanguages.Any(sl => string.Equals(sl.Code, language, StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new { error = "Unsupported language" });

        if (string.IsNullOrWhiteSpace(_config.Config.Metadata.EffectiveTmdbApiKey))
            return BadRequest(new { error = "noTmdbKey" });

        video.MetadataLanguage = language.Length == 0 ? null : language;

        // Save the override BEFORE fetching: FetchForVideoAsync re-reads the video in
        // its own scope, so an unsaved change would not be visible to it.
        // forceRefresh also bypasses the MetadataFetched/ManuallyEdited early-returns.
        await _videoDb.SaveChangesAsync();
        await _metadata.FetchForVideoAsync(video, forceRefresh: true);

        // Re-read: FetchForVideoAsync works on its own DbContext scope, so the entity
        // tracked here is stale by now.
        var updated = await _videoDb.Videos.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id);
        var effective = updated?.MetadataLanguageApplied ?? language;
        var requested = language.Length == 0 ? _config.Config.Metadata.ScrapeLanguage : language;

        return Ok(new
        {
            success = true,
            language = video.MetadataLanguage,
            effective,
            // True when TMDB had no translation and English was used instead, so the
            // UI can say so rather than leaving the user wondering why nothing changed.
            fellBackToEnglish = !string.Equals(effective, requested, StringComparison.OrdinalIgnoreCase)
                                && effective.StartsWith("en", StringComparison.OrdinalIgnoreCase),
            overview = updated?.Overview ?? "",
            title = updated?.Title ?? ""
        });
    }

    /// <summary>
    /// Sets the metadata language for an ENTIRE series and re-scrapes every episode.
    /// A language is a property of the show, not of one episode - picking French on
    /// episode 3 and leaving the rest English would be worse than useless.
    /// Body: { seriesName, language } - empty language clears the override.
    /// </summary>
    [HttpPost("videos/series/metadata-language")]
    public async Task<IActionResult> SetSeriesMetadataLanguage([FromBody] JsonElement body)
    {
        if (!User.IsInRole("admin"))
        {
            _logger.LogWarning("Non-admin {User} attempted to change series metadata language", CurrentUsername);
            return Forbid();
        }

        var seriesName = body.TryGetProperty("seriesName", out var sn) ? (sn.GetString() ?? "").Trim() : "";
        if (string.IsNullOrEmpty(seriesName)) return BadRequest(new { error = "seriesName is required" });

        var language = body.TryGetProperty("language", out var l) ? (l.GetString() ?? "").Trim() : "";
        if (language.Length > 0 && !ScrapeLanguages.Any(sl => string.Equals(sl.Code, language, StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new { error = "Unsupported language" });

        if (string.IsNullOrWhiteSpace(_config.Config.Metadata.EffectiveTmdbApiKey))
            return BadRequest(new { error = "noTmdbKey" });

        // Anime is Jikan-sourced and has no TMDB language support - excluded.
        var episodes = await _videoDb.Videos
            .Where(v => v.SeriesName == seriesName && v.MediaType == "tv")
            .ToListAsync();
        if (episodes.Count == 0) return NotFound(new { error = "No episodes found for this series" });

        foreach (var ep in episodes)
            ep.MetadataLanguage = language.Length == 0 ? null : language;
        await _videoDb.SaveChangesAsync();

        // Sequential on purpose: MetadataService rate-limits TMDB globally, and firing
        // 80+ episode fetches concurrently would just queue behind that same limiter
        // while multiplying the chance of a 429.
        var failed = 0;
        foreach (var ep in episodes)
        {
            try { await _metadata.FetchForVideoAsync(ep, forceRefresh: true); }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "Series language re-fetch failed for video {Id}", ep.Id);
            }
        }

        var ids = episodes.Select(e => e.Id).ToList();
        var updated = await _videoDb.Videos.AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.Overview, v.MetadataLanguageApplied })
            .ToListAsync();

        var requested = language.Length == 0 ? _config.Config.Metadata.ScrapeLanguage : language;
        // Report a fallback only when EVERY episode came back English despite asking
        // for something else - a couple of untranslated episodes in a mostly
        // translated series is normal and not worth alarming the user about.
        var fellBack = !IsEnglishCode(requested) && updated.Count > 0
                       && updated.All(u => IsEnglishCode(u.MetadataLanguageApplied ?? "en-US"));

        return Ok(new
        {
            success = true,
            language = language.Length == 0 ? null : language,
            episodes = episodes.Count,
            failed,
            fellBackToEnglish = fellBack,
            // First non-empty overview, so the series header can refresh in place.
            overview = updated.Select(u => u.Overview).FirstOrDefault(o => !string.IsNullOrEmpty(o)) ?? ""
        });
    }

    private static bool IsEnglishCode(string code) =>
        string.IsNullOrWhiteSpace(code) || code.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    // ─── Network Shares ──────────────────────────────────────────

    [HttpGet("shares")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> GetShares()
    {
        var shares = await _shareService.GetAllSharesAsync();
        var result = shares.Select(s => new {
            s.Id, s.SharePath, s.MountPoint, s.ShareType,
            s.Username, s.Domain, s.MountOptions, s.FolderType,
            s.Enabled, s.IsMounted, s.LastError,
            s.DateCreated, s.LastMounted,
            hasPassword = !string.IsNullOrEmpty(s.EncryptedPassword)
        });
        return Ok(result);
    }

    [HttpPost("shares")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> AddShare([FromBody] System.Text.Json.JsonElement body)
    {
        var sharePath = body.GetProperty("sharePath").GetString() ?? "";
        var username = body.GetProperty("username").GetString() ?? "";
        var password = body.GetProperty("password").GetString() ?? "";
        var domain = body.TryGetProperty("domain", out var d) ? d.GetString() ?? "" : "";
        var shareType = body.TryGetProperty("shareType", out var st) ? st.GetString() ?? "smb" : "smb";
        var mountPoint = body.TryGetProperty("mountPoint", out var mp) ? mp.GetString() ?? "" : "";
        var mountOptions = body.TryGetProperty("mountOptions", out var mo) ? mo.GetString() ?? "" : "";
        var folderType = body.TryGetProperty("folderType", out var ft) ? ft.GetString() ?? "" : "";

        if (string.IsNullOrWhiteSpace(sharePath))
            return BadRequest(new { message = "Share path is required" });

        try
        {
            var share = await _shareService.AddShareAsync(
                sharePath, username, password, domain, shareType, mountPoint, mountOptions, folderType);
            return Ok(new { success = true, id = share.Id });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("shares/{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdateShare(int id, [FromBody] System.Text.Json.JsonElement body)
    {
        var sharePath = body.TryGetProperty("sharePath", out var sp) ? sp.GetString() : null;
        var username = body.TryGetProperty("username", out var u) ? u.GetString() : null;
        var password = body.TryGetProperty("password", out var p) ? p.GetString() : null;
        var domain = body.TryGetProperty("domain", out var dd) ? dd.GetString() : null;
        var shareType = body.TryGetProperty("shareType", out var stt) ? stt.GetString() : null;
        var mountPoint = body.TryGetProperty("mountPoint", out var mpt) ? mpt.GetString() : null;
        var mountOptions = body.TryGetProperty("mountOptions", out var mop) ? mop.GetString() : null;
        var folderType = body.TryGetProperty("folderType", out var ftt) ? ftt.GetString() : null;
        bool? enabled = body.TryGetProperty("enabled", out var e) ? e.GetBoolean() : null;

        try
        {
            var ok = await _shareService.UpdateShareAsync(
                id, sharePath, username, password, domain, shareType, mountPoint, mountOptions, enabled, folderType);
            return ok ? Ok(new { success = true }) : NotFound(new { message = "Share not found" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("shares/{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeleteShare(int id)
    {
        var ok = await _shareService.DeleteShareAsync(id);
        return ok ? Ok(new { success = true }) : NotFound(new { message = "Share not found" });
    }

    [HttpPost("shares/{id}/mount")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> MountShare(int id)
    {
        var share = await _shareService.GetShareAsync(id);
        if (share == null) return NotFound(new { message = "Share not found" });

        var (success, message) = await _shareService.MountShareAsync(share);
        return Ok(new { success, message });
    }

    [HttpPost("shares/{id}/unmount")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UnmountShare(int id)
    {
        var share = await _shareService.GetShareAsync(id);
        if (share == null) return NotFound(new { message = "Share not found" });

        var (success, message) = await _shareService.UnmountShareAsync(share);
        return Ok(new { success, message });
    }

    [HttpPost("shares/test")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> TestShareConnection([FromBody] System.Text.Json.JsonElement body)
    {
        var sharePath = body.GetProperty("sharePath").GetString() ?? "";
        var username = body.GetProperty("username").GetString() ?? "";
        var password = body.GetProperty("password").GetString() ?? "";
        var domain = body.TryGetProperty("domain", out var dd) ? dd.GetString() ?? "" : "";

        try
        {
            var (success, message) = await _shareService.TestConnectionAsync(
                sharePath, username, password, domain);
            return Ok(new { success, message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("shares/mount-all")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> MountAllShares()
    {
        await _shareService.MountAllEnabledAsync();
        var shares = await _shareService.GetAllSharesAsync();
        return Ok(new {
            success = true,
            mounted = shares.Count(s => s.IsMounted),
            total = shares.Count(s => s.Enabled)
        });
    }

    // ─── Filesystem Browser ──────────────────────────────────────
    [HttpGet("filesystem/browse")]
    [Authorize(Roles = "admin")]
    public IActionResult BrowseFilesystem([FromQuery] string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                if (System.OperatingSystem.IsWindows())
                {
                    var drives = System.IO.DriveInfo.GetDrives()
                        .Select(d =>
                        {
                            try { return new { name = d.IsReady && !string.IsNullOrEmpty(d.VolumeLabel) ? $"{d.Name.TrimEnd('\\')} ({d.VolumeLabel})" : d.Name.TrimEnd('\\'), path = d.RootDirectory.FullName, accessible = d.IsReady }; }
                            catch { return new { name = d.Name.TrimEnd('\\'), path = d.RootDirectory.FullName, accessible = false }; }
                        })
                        .ToList<object>();
                    return Ok(new { path = "", parent = (string?)null, isDriveList = true, items = drives });
                }
                else
                {
                    var roots = new[] { "/", "/home", "/media", "/mnt", "/srv", "/data", "/nas", "/storage", "/volume1", "/volume2", "/shares" }
                        .Where(System.IO.Directory.Exists)
                        .Select(d => (object)new { name = d == "/" ? "/ (root)" : d.TrimStart('/'), path = d, accessible = true })
                        .ToList();
                    return Ok(new { path = "", parent = (string?)null, isDriveList = true, items = roots });
                }
            }

            var normalized = System.IO.Path.GetFullPath(path);
            if (!System.IO.Directory.Exists(normalized))
                return BadRequest(new { message = "Path does not exist" });

            var parent = normalized == "/" ? null : System.IO.Path.GetDirectoryName(normalized);
            // On Windows, GetDirectoryName("C:\") returns null - treat as drive root, parent = "" (drives list)
            if (System.OperatingSystem.IsWindows() && parent == null && normalized.Length <= 3)
                parent = "";

            List<object?> items;
            try
            {
                items = System.IO.Directory.GetDirectories(normalized)
                    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                    .Select(d =>
                    {
                        try
                        {
                            var info = new System.IO.DirectoryInfo(d);
                            if (info.Attributes.HasFlag(System.IO.FileAttributes.Hidden) ||
                                info.Attributes.HasFlag(System.IO.FileAttributes.System))
                                return (object?)null;
                            return (object?)new { name = info.Name, path = d, accessible = true };
                        }
                        catch { return (object?)new { name = System.IO.Path.GetFileName(d), path = d, accessible = false }; }
                    })
                    .Where(x => x != null)
                    .ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.IO.IOException)
            {
                // The folder exists but this process can't read it (common when NexusM runs as a
                // dedicated non-root account and the media lives in another user's home). Return a
                // friendly, navigable result instead of a 400 so the UI shows the reason and keeps Back.
                _logger.LogWarning("Filesystem browse denied for path '{Path}': {Msg}", normalized, ex.Message);
                return Ok(new { path = normalized, parent, isDriveList = false, items = new List<object>(),
                    error = "Access denied. The NexusM service account cannot read this folder. Add the account to the group that owns it (or run NexusM as a user that can), then restart." });
            }

            return Ok(new { path = normalized, parent, isDriveList = false, items });
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Filesystem browse error for path '{Path}': {Msg}", path, ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("config/validate-tmdb")]
    public async Task<IActionResult> ValidateTmdbKey([FromBody] System.Text.Json.JsonElement body)
    {
        var key = body.TryGetProperty("apiKey", out var k) ? k.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(key))
            return Ok(new { valid = false, message = "No API key provided" });

        var valid = await _metadata.ValidateTmdbKeyAsync(key);
        return Ok(new { valid, message = valid ? "API key is valid" : "Invalid API key" });
    }


    /// <summary>
    /// Returns the last N lines of the server log file for display in the Settings UI.
    /// Admin only. Opens the file with ReadWrite share so Serilog's write lock is respected.
    /// </summary>
    [HttpGet("config/logs")]
    public IActionResult GetServerLogs([FromQuery] int lines = 300)
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
            return Forbid();

        lines = Math.Clamp(lines, 10, 1000);

        var configDir = Path.GetDirectoryName(_config.ConfigFilePath) ?? AppContext.BaseDirectory;
        var configuredPath = Path.IsPathRooted(_config.Config.Logging.LogFile)
            ? _config.Config.Logging.LogFile
            : Path.Combine(configDir, _config.Config.Logging.LogFile);

        // Serilog RollingInterval.Day appends yyyyMMdd before the extension
        // (e.g. nexusm.log → nexusm20260226.log). Find the most recently written match.
        var logDir = Path.GetDirectoryName(configuredPath) ?? configDir;
        var baseName = Path.GetFileNameWithoutExtension(configuredPath); // "nexusm"
        var ext = Path.GetExtension(configuredPath);                     // ".log"
        var logFilePath = configuredPath; // fallback to bare name
        if (Directory.Exists(logDir))
        {
            var newest = Directory.GetFiles(logDir, baseName + "*" + ext)
                .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
                .FirstOrDefault();
            if (newest != null) logFilePath = newest;
        }

        if (!System.IO.File.Exists(logFilePath))
            return Ok(new { lines = Array.Empty<string>(), logFile = logFilePath, error = "Log file not found yet. The server may not have written any log entries." });

        try
        {
            const int maxReadBytes = 512 * 1024; // read at most the last 512 KB

            using var fs = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long startPos = Math.Max(0, fs.Length - maxReadBytes);
            fs.Seek(startPos, SeekOrigin.Begin);
            using var reader = new StreamReader(fs, System.Text.Encoding.UTF8);
            var content = reader.ReadToEnd();

            var allLines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            // If we seeked mid-file the first line is likely partial - drop it
            var startIdx = startPos > 0 && allLines.Length > 0 ? 1 : 0;
            var result = allLines.Skip(startIdx).TakeLast(lines).ToArray();

            return Ok(new { lines = result, logFile = logFilePath });
        }
        catch (Exception ex)
        {
            return Ok(new { lines = Array.Empty<string>(), logFile = logFilePath, error = ex.Message });
        }
    }

    // ─── Pictures ─────────────────────────────────────────────────────

    [HttpGet("pictures")]
    public async Task<IActionResult> GetPictures(
        [FromQuery] string? category = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = "recent",
        [FromQuery] string? place = null,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 100)
    {
        var query = _picDb.Pictures.AsQueryable();

        if (category != null)
        {
            if (category == "__none__")
                query = query.Where(p => p.Category == null || p.Category == "");
            else
                query = query.Where(p => p.Category.ToLower() == category.ToLower());
        }

        if (place != null)
            query = query.Where(p => p.PlaceName == place);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(p => p.FileName.ToLower().Contains(s) || p.Category.ToLower().Contains(s));
        }

        query = sort switch
        {
            "name" => query.OrderBy(p => p.FileName),
            "date" => query.OrderByDescending(p => p.DateTaken ?? p.LastModified),
            "size" => query.OrderByDescending(p => p.SizeBytes),
            _ => query.OrderByDescending(p => p.DateAdded)
        };

        var total = await query.CountAsync();
        var pictures = await query.Skip((page - 1) * limit).Take(limit)
            .Select(p => new
            {
                p.Id, p.FileName, p.Width, p.Height, p.SizeBytes,
                p.Format, p.DateTaken, p.Category, p.ThumbnailPath,
                p.CameraMake, p.CameraModel, p.DateAdded, p.Is360, p.IsLivePhoto
            }).ToListAsync();

        return Ok(new { total, page, limit, pictures });
    }

    [HttpGet("pictures/timeline")]
    public async Task<IActionResult> GetPicturesTimeline([FromQuery] string? category = null)
    {
        var query = _picDb.Pictures.AsQueryable();
        if (category != null)
        {
            if (category == "__none__")
                query = query.Where(p => p.Category == null || p.Category == "");
            else
                query = query.Where(p => p.Category.ToLower() == category.ToLower());
        }

        var pictures = await query
            .OrderByDescending(p => p.DateTaken ?? p.LastModified)
            .Select(p => new { p.Id, p.DateTaken, p.ThumbnailPath, p.FileName })
            .ToListAsync();

        return Ok(pictures);
    }

    [HttpGet("pictures/memories")]
    public async Task<IActionResult> GetPicturesMemories()
    {
        var currentYear = DateTime.UtcNow.Year;

        var allPics = await _picDb.Pictures
            .Where(p => p.DateTaken != null)
            .OrderByDescending(p => p.DateTaken)
            .Select(p => new { p.Id, p.DateTaken, p.ThumbnailPath, p.FileName })
            .ToListAsync();

        var groups = new List<object>();
        for (int i = 1; i <= 3; i++)
        {
            int yr = currentYear - i;
            var bucket = allPics.Where(p => p.DateTaken!.Value.Year == yr).ToList();
            if (bucket.Count > 0)
                groups.Add(new { bracket = i, year = yr, count = bucket.Count, pictures = bucket.Take(80) });
        }

        var older = allPics.Where(p => p.DateTaken!.Value.Year < currentYear - 3).ToList();
        if (older.Count > 0)
            groups.Add(new { bracket = 0, year = 0, count = older.Count, pictures = older.Take(80) });

        return Ok(new { groups });
    }

    [HttpGet("pictures/geomap")]
    public async Task<IActionResult> GetPictureGeomap()
    {
        var pics = await _picDb.Pictures
            .Where(p => p.GpsLat != null && p.GpsLon != null)
            .Select(p => new { p.Id, lat = p.GpsLat!.Value, lon = p.GpsLon!.Value, p.ThumbnailPath, p.FileName })
            .ToListAsync();
        return Ok(pics);
    }

    [HttpGet("pictures/places")]
    public async Task<IActionResult> GetPicturePlaces()
    {
        var rows = await _picDb.Pictures
            .Where(p => p.PlaceName != null && p.PlaceName != "")
            .Select(p => new { p.PlaceName, p.ThumbnailPath })
            .ToListAsync();

        var places = rows
            .GroupBy(p => p.PlaceName!)
            .Select(g => new
            {
                name = g.Key,
                count = g.Count(),
                thumbnailPath = g.Select(p => p.ThumbnailPath).FirstOrDefault(t => t != null)
            })
            .OrderByDescending(g => g.count)
            .ToList();

        return Ok(places);
    }

    [HttpPost("pictures/resolve-places")]
    [Authorize(Roles = "admin")]
    public IActionResult StartResolvePlaces()
    {
        _ = Task.Run(() => _pictureGeo.ResolveAllAsync());
        return Ok(new { started = true });
    }

    [HttpGet("pictures/resolve-places/status")]
    public IActionResult GetResolvePlacesStatus()
    {
        return Ok(new
        {
            inProgress      = _pictureGeo.IsResolveInProgress,
            total           = _pictureGeo.TotalToResolve,
            resolved        = _pictureGeo.ResolvedCount,
            geoReady        = _offlineGeo.IsReady,
            geoDownloading  = false,
            geoStatus       = _offlineGeo.StatusMessage
        });
    }

    [HttpGet("pictures/categories")]
    public async Task<IActionResult> GetPictureCategories()
    {
        var categories = await _picDb.Pictures
            .GroupBy(p => p.Category)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderBy(g => g.name)
            .ToListAsync();
        return Ok(categories);
    }

    [HttpGet("pictures/stats")]
    public async Task<IActionResult> GetPictureStats()
    {
        var stats = new
        {
            totalPictures = await _picDb.Pictures.CountAsync(),
            totalSize = await _picDb.Pictures.SumAsync(p => p.SizeBytes),
            totalCategories = await _picDb.Pictures
                .Select(p => p.Category).Distinct().CountAsync(),
            recentlyAdded = await _picDb.Pictures
                .CountAsync(p => p.DateAdded > DateTime.UtcNow.AddDays(-7)),
            formats = await _picDb.Pictures
                .GroupBy(p => p.Format)
                .Select(g => new { name = g.Key, count = g.Count() })
                .OrderByDescending(g => g.count)
                .ToListAsync()
        };
        return Ok(stats);
    }

    [HttpGet("pictures/{id}/metadata")]
    public async Task<IActionResult> GetPictureMetadata(int id)
    {
        var pic = await _picDb.Pictures.FindAsync(id);
        if (pic == null) return NotFound();
        return Ok(new
        {
            pic.Id, pic.FileName, pic.FilePath, pic.Width, pic.Height,
            pic.SizeBytes, pic.Format, pic.DateTaken,
            pic.CameraMake, pic.CameraModel, pic.Category,
            pic.IsoSpeed, pic.ExposureTime, pic.FNumber, pic.FocalLength,
            pic.Flash, pic.Orientation, pic.DpiX, pic.DpiY,
            pic.LensModel, pic.Software, pic.DateAdded, pic.LastModified,
            pic.GpsLat, pic.GpsLon, pic.Is360, pic.IsLivePhoto
        });
    }

    [HttpGet("pictures/{id}/live-video")]
    public async Task<IActionResult> GetPictureLiveVideo(int id)
    {
        var pic = await _picDb.Pictures.FindAsync(id);
        if (pic == null || !pic.IsLivePhoto || string.IsNullOrEmpty(pic.LivePhotoVideoPath))
            return NotFound();
        if (!System.IO.File.Exists(pic.LivePhotoVideoPath))
            return NotFound();
        var mime = Path.GetExtension(pic.LivePhotoVideoPath).ToLowerInvariant() == ".mov"
            ? "video/quicktime" : "video/mp4";
        return PhysicalFile(pic.LivePhotoVideoPath, mime, enableRangeProcessing: true);
    }

    [HttpPost("pictures/{id}/toggle360")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> TogglePicture360(int id)
    {
        var pic = await _picDb.Pictures.FindAsync(id);
        if (pic == null) return NotFound();
        pic.Is360 = !pic.Is360;
        await _picDb.SaveChangesAsync();
        return Ok(new { is360 = pic.Is360 });
    }

    [HttpPut("pictures/{id}/metadata")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdatePictureMetadata(int id, [FromBody] JsonElement body)
    {
        var pic = await _picDb.Pictures.FindAsync(id);
        if (pic == null) return NotFound();
        if (!System.IO.File.Exists(pic.FilePath))
            return NotFound(new { message = "Image file not found on disk" });

        string? dateTaken   = body.TryGetProperty("dateTaken",   out var dt)  && dt.ValueKind  != JsonValueKind.Null ? dt.GetString()   : null;
        double? gpsLat      = body.TryGetProperty("gpsLat",      out var glat) && glat.ValueKind != JsonValueKind.Null ? glat.GetDouble() : (double?)null;
        double? gpsLon      = body.TryGetProperty("gpsLon",      out var glon) && glon.ValueKind != JsonValueKind.Null ? glon.GetDouble() : (double?)null;
        bool    clearGps    = body.TryGetProperty("clearGps",    out var cg)  && cg.ValueKind   == JsonValueKind.True;
        string? cameraMake  = body.TryGetProperty("cameraMake",  out var cm)  && cm.ValueKind   != JsonValueKind.Null ? cm.GetString()   : null;
        string? cameraModel = body.TryGetProperty("cameraModel", out var cmo) && cmo.ValueKind  != JsonValueKind.Null ? cmo.GetString()  : null;
        string? lensModel   = body.TryGetProperty("lensModel",   out var lm)  && lm.ValueKind   != JsonValueKind.Null ? lm.GetString()   : null;
        int?    isoSpeed    = body.TryGetProperty("isoSpeed",    out var iso) && iso.ValueKind   != JsonValueKind.Null ? iso.GetInt32()   : (int?)null;
        string? software    = body.TryGetProperty("software",    out var sw)  && sw.ValueKind   != JsonValueKind.Null ? sw.GetString()   : null;

        var ext = Path.GetExtension(pic.FilePath).ToLowerInvariant();
        bool supportsExifWrite = ext != ".heic" && ext != ".heif" && ext != ".gif" && ext != ".bmp";

        string? backupName = null;
        if (supportsExifWrite)
        {
            // Backup original before first modification
            var dir      = Path.GetDirectoryName(pic.FilePath)!;
            var filename  = Path.GetFileName(pic.FilePath);
            var backupPath = Path.Combine(dir, "original_" + filename);
            if (!System.IO.File.Exists(backupPath))
            {
                try   { System.IO.File.Copy(pic.FilePath, backupPath); backupName = "original_" + filename; }
                catch (Exception ex) { return StatusCode(500, new { message = "Could not create backup: " + ex.Message }); }
            }

            try
            {
                using var image = Image.Load(pic.FilePath);
                var exif = image.Metadata.ExifProfile ?? new ExifProfile();

                if (dateTaken != null && DateTime.TryParse(dateTaken, out var parsedDate))
                {
                    var exifDateStr = parsedDate.ToString("yyyy:MM:dd HH:mm:ss");
                    exif.SetValue(ExifTag.DateTimeOriginal, exifDateStr);
                    exif.SetValue(ExifTag.DateTime,         exifDateStr);
                }

                if (clearGps)
                {
                    exif.RemoveValue(ExifTag.GPSLatitude);
                    exif.RemoveValue(ExifTag.GPSLatitudeRef);
                    exif.RemoveValue(ExifTag.GPSLongitude);
                    exif.RemoveValue(ExifTag.GPSLongitudeRef);
                    exif.RemoveValue(ExifTag.GPSVersionID);
                }
                else if (gpsLat.HasValue && gpsLon.HasValue)
                {
                    WritePictureGps(exif, gpsLat.Value, gpsLon.Value);
                }

                if (!string.IsNullOrWhiteSpace(cameraMake))  exif.SetValue(ExifTag.Make,      cameraMake);
                if (!string.IsNullOrWhiteSpace(cameraModel)) exif.SetValue(ExifTag.Model,     cameraModel);
                if (!string.IsNullOrWhiteSpace(lensModel))   exif.SetValue(ExifTag.LensModel, lensModel);
                if (!string.IsNullOrWhiteSpace(software))    exif.SetValue(ExifTag.Software,  software);
                if (isoSpeed.HasValue)
                    exif.SetValue(ExifTag.ISOSpeedRatings, new ushort[] { (ushort)Math.Clamp(isoSpeed.Value, 0, 65535) });

                image.Metadata.ExifProfile = exif;

                if (ext == ".jpg" || ext == ".jpeg")
                    image.Save(pic.FilePath, new JpegEncoder { Quality = 97 });
                else
                    image.Save(pic.FilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write EXIF to {File}", pic.FilePath);
                return StatusCode(500, new { message = "Failed to write EXIF to file: " + ex.Message });
            }
        }

        // Update database record
        if (dateTaken != null && DateTime.TryParse(dateTaken, out var dbDate))
            pic.DateTaken = dbDate;

        if (clearGps)    { pic.GpsLat = null; pic.GpsLon = null; pic.PlaceName = null; }
        else if (gpsLat.HasValue && gpsLon.HasValue) { pic.GpsLat = gpsLat; pic.GpsLon = gpsLon; }

        if (!string.IsNullOrWhiteSpace(cameraMake))  pic.CameraMake  = cameraMake;
        if (!string.IsNullOrWhiteSpace(cameraModel)) pic.CameraModel = cameraModel;
        if (!string.IsNullOrWhiteSpace(lensModel))   pic.LensModel   = lensModel;
        if (!string.IsNullOrWhiteSpace(software))    pic.Software    = software;
        if (isoSpeed.HasValue)                       pic.IsoSpeed    = isoSpeed;

        await _picDb.SaveChangesAsync();

        return Ok(new
        {
            pic.Id, pic.FileName, pic.FilePath, pic.Width, pic.Height,
            pic.SizeBytes, pic.Format, pic.DateTaken,
            pic.CameraMake, pic.CameraModel, pic.Category,
            pic.IsoSpeed, pic.ExposureTime, pic.FNumber, pic.FocalLength,
            pic.Flash, pic.Orientation, pic.DpiX, pic.DpiY,
            pic.LensModel, pic.Software, pic.DateAdded, pic.LastModified,
            pic.GpsLat, pic.GpsLon,
            backupCreated = backupName != null,
            backupName,
            fileUpdated = supportsExifWrite
        });
    }

    private static void WritePictureGps(ExifProfile exif, double lat, double lon)
    {
        static Rational[] ToRationals(double d)
        {
            d = Math.Abs(d);
            var deg    = (uint)Math.Floor(d);
            var minFrac = (d - deg) * 60.0;
            var min    = (uint)Math.Floor(minFrac);
            var secNum = (uint)Math.Round((minFrac - min) * 60.0 * 10000);
            return [new Rational(deg, 1), new Rational(min, 1), new Rational(secNum, 10000)];
        }
        exif.SetValue(ExifTag.GPSVersionID,     new byte[] { 2, 3, 0, 0 });
        exif.SetValue(ExifTag.GPSLatitudeRef,   lat >= 0 ? "N" : "S");
        exif.SetValue(ExifTag.GPSLatitude,      ToRationals(lat));
        exif.SetValue(ExifTag.GPSLongitudeRef,  lon >= 0 ? "E" : "W");
        exif.SetValue(ExifTag.GPSLongitude,     ToRationals(lon));
    }

    private static readonly HashSet<string> _heicExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".heic", ".heif" };

    private static readonly HashSet<string> _rawExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".srf", ".sr2",
            ".dng", ".orf", ".rw2", ".raf", ".srw", ".pef", ".x3f", ".3fr",
            ".mef", ".mrw", ".dcr", ".kdc", ".raw"
        };

    [HttpGet("image/{id}")]
    public async Task<IActionResult> GetFullImage(int id)
    {
        var pic = await _picDb.Pictures.FindAsync(id);
        if (pic == null) return NotFound();

        if (!System.IO.File.Exists(pic.FilePath))
            return NotFound("Image file not found on disk");

        var ext = Path.GetExtension(pic.FilePath).ToLowerInvariant();

        // HEIC/HEIF and RAW camera formats: convert to JPEG via FFmpeg on demand.
        // No -map flag - let FFmpeg auto-select the primary image stream.
        // HEIC/HEIF: FFmpeg on-demand conversion (established path, kept as-is)
        if (_heicExtensions.Contains(ext) && _ffmpeg.IsAvailable)
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"nexusm_img_{id}_{Guid.NewGuid():N}.jpg");
            try
            {
                var args = $"-y -i \"{pic.FilePath}\" -frames:v 1 -update 1 -q:v 2 \"{tempFile}\"";
                using var proc = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo(_ffmpeg.FfmpegPath!, args)
                    {
                        RedirectStandardError = true,
                        RedirectStandardOutput = false,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                proc.Start();
                var drainStderr = proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();
                await drainStderr;
                if (proc.ExitCode == 0 && System.IO.File.Exists(tempFile))
                    return File(await System.IO.File.ReadAllBytesAsync(tempFile), "image/jpeg");
                return StatusCode(500, "HEIC conversion failed");
            }
            finally
            {
                if (System.IO.File.Exists(tempFile)) System.IO.File.Delete(tempFile);
            }
        }

        // RAW camera formats: true LibRaw decode - full demosaic, sRGB output
        if (_rawExtensions.Contains(ext))
        {
            var rawJpeg = await Task.Run(() => NexusM.Services.RawImageService.DecodeFullJpeg(pic.FilePath));
            if (rawJpeg != null) return File(rawJpeg, "image/jpeg");
            return StatusCode(500, "Could not decode RAW file");
        }

        var mimeType = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".tiff" => "image/tiff",
            _ => "application/octet-stream"
        };

        var stream = new FileStream(pic.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, mimeType, enableRangeProcessing: true);
    }

    [HttpGet("picthumb/{id:int}")]
    public async Task<IActionResult> GetPictureThumbnail(int id)
    {
        var pic = await _picDb.Pictures.FindAsync(id);
        if (pic == null) return NotFound();

        if (!string.IsNullOrEmpty(pic.ThumbnailPath))
        {
            var thumbPath = Path.Combine(AppContext.BaseDirectory, "assets", "thumbs", pic.ThumbnailPath);
            if (System.IO.File.Exists(thumbPath))
                return PhysicalFile(thumbPath, "image/jpeg");
        }

        // Fallback: serve full image
        if (System.IO.File.Exists(pic.FilePath))
        {
            var stream = new FileStream(pic.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var mime = Path.GetExtension(pic.FilePath).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                _ => "image/jpeg"
            };
            return File(stream, mime);
        }

        return NotFound();
    }

    // ─── Pictures Scanning ────────────────────────────────────────────

    [HttpPost("scan/pictures")]
    [Authorize(Roles = "admin")]
    public IActionResult StartPictureScan()
    {
        if (_picScanner.IsScanning)
            return Conflict(new { message = "Pictures scan already in progress" });

        _ = _picScanner.StartScanAsync();
        return Ok(new { message = "Pictures scan started" });
    }

    [HttpPost("scan/pictures/reread-exif")]
    [Authorize(Roles = "admin")]
    public IActionResult StartPictureExifRescan()
    {
        if (_picScanner.IsScanning)
            return Conflict(new { message = "Pictures scan already in progress" });

        _ = _picScanner.StartScanAsync(forceExif: true);
        return Ok(new { message = "EXIF re-read started" });
    }

    [HttpGet("scan/pictures/status")]
    public IActionResult GetPictureScanStatus()
    {
        var p = _picScanner.CurrentProgress;
        return Ok(new
        {
            p.Status,
            p.Message,
            p.TotalFiles,
            p.ProcessedFiles,
            p.NewPictures,
            p.UpdatedPictures,
            p.NewVideos,
            p.UpdatedVideos,
            p.ErrorCount,
            p.PercentComplete,
            p.StartTime,
            isScanning = _picScanner.IsScanning
        });
    }

    // ─── Picture Videos ───────────────────────────────────────────────

    [HttpGet("picture-videos")]
    public async Task<IActionResult> GetPictureVideos(
        [FromQuery] string? category = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = "recent",
        [FromQuery] int page = 1,
        [FromQuery] int limit = 200)
    {
        var query = _picDb.PictureVideos.AsQueryable();

        if (category != null)
        {
            if (category == "__none__")
                query = query.Where(v => v.Category == null || v.Category == "");
            else
                query = query.Where(v => v.Category.ToLower() == category.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(v => v.FileName.ToLower().Contains(s) || v.Category.ToLower().Contains(s));
        }

        query = sort switch
        {
            "name" => query.OrderBy(v => v.FileName),
            "date" => query.OrderByDescending(v => v.DateTaken ?? v.LastModified),
            "size" => query.OrderByDescending(v => v.SizeBytes),
            _ => query.OrderByDescending(v => v.DateAdded)
        };

        var total = await query.CountAsync();
        var videos = await query.Skip((page - 1) * limit).Take(limit)
            .Select(v => new
            {
                v.Id, v.FileName, v.Width, v.Height, v.SizeBytes, v.Format,
                v.DurationSeconds, v.DateTaken, v.Category, v.ThumbnailPath, v.DateAdded
            }).ToListAsync();

        return Ok(new { total, page, limit, videos });
    }

    [HttpGet("picture-videos/categories")]
    public async Task<IActionResult> GetPictureVideoCategories()
    {
        var categories = await _picDb.PictureVideos
            .GroupBy(v => v.Category)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderBy(g => g.name)
            .ToListAsync();
        return Ok(categories);
    }

    // Rename a picture/video category (updates Category field on all matching rows in both tables)
    public record RenameCategoryDto(string OldName, string NewName);
    [HttpPost("pictures/categories/rename")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> RenamePictureCategory([FromBody] RenameCategoryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.NewName)) return BadRequest("New name required");
        var isUncategorized = string.IsNullOrEmpty(dto.OldName) || dto.OldName == "__none__";
        // Update pictures
        var pics = isUncategorized
            ? await _picDb.Pictures.Where(p => p.Category == null || p.Category == "").ToListAsync()
            : await _picDb.Pictures.Where(p => p.Category == dto.OldName).ToListAsync();
        pics.ForEach(p => p.Category = dto.NewName.Trim());
        // Update videos
        var vids = isUncategorized
            ? await _picDb.PictureVideos.Where(v => v.Category == null || v.Category == "").ToListAsync()
            : await _picDb.PictureVideos.Where(v => v.Category == dto.OldName).ToListAsync();
        vids.ForEach(v => v.Category = dto.NewName.Trim());
        await _picDb.SaveChangesAsync();
        return Ok(new { updated = pics.Count + vids.Count });
    }

    // Rename an album
    public record RenameAlbumDto(string Name);
    [HttpPatch("picture-albums/{id:int}/rename")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> RenamePictureAlbum(int id, [FromBody] RenameAlbumDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name required");
        var album = await _picDb.PictureAlbums.FindAsync(id);
        if (album == null) return NotFound();
        if (album.Username != CurrentUsername) return Forbid();
        album.Name = dto.Name.Trim();
        await _picDb.SaveChangesAsync();
        return Ok(new { id = album.Id, name = album.Name });
    }

    [HttpGet("picture-videos/{id}/metadata")]
    public async Task<IActionResult> GetPictureVideoMetadata(int id)
    {
        var v = await _picDb.PictureVideos.FindAsync(id);
        if (v == null) return NotFound();
        return Ok(new
        {
            v.Id, v.FileName, v.FilePath, v.Width, v.Height, v.SizeBytes,
            v.Format, v.DurationSeconds, v.DateTaken, v.Category,
            v.DateAdded, v.LastModified, v.ThumbnailPath
        });
    }

    [HttpGet("pvthumb/{id:int}")]
    public async Task<IActionResult> GetPictureVideoThumbnail(int id)
    {
        var v = await _picDb.PictureVideos.FindAsync(id);
        if (v == null) return NotFound();

        if (!string.IsNullOrEmpty(v.ThumbnailPath))
        {
            var thumbPath = Path.Combine(AppContext.BaseDirectory, "assets", "thumbs", v.ThumbnailPath);
            if (System.IO.File.Exists(thumbPath))
                return PhysicalFile(thumbPath, "image/jpeg");
        }
        return NotFound();
    }

    [HttpGet("picture-video/{id}")]
    public async Task<IActionResult> GetPictureVideoFile(int id)
    {
        var v = await _picDb.PictureVideos.FindAsync(id);
        if (v == null) return NotFound();
        if (!System.IO.File.Exists(v.FilePath)) return NotFound("File not found on disk");

        var ext = Path.GetExtension(v.FilePath).ToLowerInvariant();
        var mimeType = ext switch
        {
            ".mp4" or ".m4v" => "video/mp4",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            ".webm" => "video/webm",
            ".wmv" => "video/x-ms-wmv",
            ".3gp" => "video/3gpp",
            ".flv" => "video/x-flv",
            ".ts" or ".mts" or ".m2ts" => "video/mp2t",
            _ => "video/mp4"
        };

        var stream = new FileStream(v.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, mimeType, enableRangeProcessing: true);
    }

    // ─── Picture/Video Albums ─────────────────────────────────────────

    [HttpGet("picture-albums")]
    public async Task<IActionResult> GetPictureAlbums()
    {
        var username = CurrentUsername;
        var albums = await _picDb.PictureAlbums
            .Where(a => a.Username == username)
            .OrderBy(a => a.Name)
            .ToListAsync();

        var albumIds = albums.Select(a => a.Id).ToList();
        var counts = await _picDb.PictureAlbumItems
            .Where(i => albumIds.Contains(i.AlbumId))
            .GroupBy(i => i.AlbumId)
            .Select(g => new { albumId = g.Key, count = g.Count() })
            .ToListAsync();
        var countMap = counts.ToDictionary(c => c.albumId, c => c.count);

        var result = albums.Select(a => new
        {
            a.Id, a.Name, a.Description, a.CreatedAt,
            a.CoverThumbnail, a.CoverMediaType, a.CoverMediaId,
            itemCount = countMap.GetValueOrDefault(a.Id, 0)
        });
        return Ok(result);
    }

    [HttpPost("picture-albums")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> CreatePictureAlbum([FromBody] CreateAlbumDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Album name is required" });

        var album = new PictureAlbum
        {
            Name = dto.Name.Trim(),
            Description = dto.Description,
            Username = CurrentUsername,
            CreatedAt = DateTime.UtcNow
        };
        _picDb.PictureAlbums.Add(album);
        await _picDb.SaveChangesAsync();
        return Ok(new { album.Id, album.Name, album.Description, album.CreatedAt, itemCount = 0 });
    }

    [HttpPut("picture-albums/{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> RenamePictureAlbum(int id, [FromBody] CreateAlbumDto dto)
    {
        var album = await _picDb.PictureAlbums.FindAsync(id);
        if (album == null || album.Username != CurrentUsername) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "Name required" });

        album.Name = dto.Name.Trim();
        album.Description = dto.Description;
        await _picDb.SaveChangesAsync();
        return Ok(new { album.Id, album.Name });
    }

    [HttpDelete("picture-albums/{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeletePictureAlbum(int id)
    {
        var album = await _picDb.PictureAlbums.FindAsync(id);
        if (album == null || album.Username != CurrentUsername) return NotFound();

        var items = _picDb.PictureAlbumItems.Where(i => i.AlbumId == id);
        _picDb.PictureAlbumItems.RemoveRange(items);
        _picDb.PictureAlbums.Remove(album);
        await _picDb.SaveChangesAsync();
        return Ok(new { message = "Deleted" });
    }

    /// <summary>
    /// Deletes a picture: the file on disk, its cached thumbnail, any album entries
    /// referencing it, and the DB row. Admin-only and irreversible - there is no
    /// recycle bin, the original file is gone.
    /// </summary>
    [HttpDelete("pictures/{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeletePicture(int id)
    {
        var picture = await _picDb.Pictures.FindAsync(id);
        if (picture == null) return NotFound();

        // Root containment: refuses anything outside a configured Pictures folder, so
        // a tampered DB row can't be used to delete arbitrary files off the server.
        var (ok, refused, error) = TryDeleteFileWithinRoots(
            picture.FilePath, _config.Config.Library.GetPicturesFolderList());

        if (!ok)
        {
            // Keep the DB row when the file survives, so the library still reflects
            // what is actually on disk and the user can retry.
            _logger.LogWarning("DeletePicture {Id} failed: {Error}", id, error);
            return StatusCode(refused ? 403 : 500, new { error = error ?? "Could not delete the file." });
        }

        // Cached thumbnail - best effort; a leftover thumb is harmless, a failure here
        // must not abort a delete whose original file is already gone.
        if (!string.IsNullOrWhiteSpace(picture.ThumbnailPath))
        {
            try
            {
                var thumb = Path.Combine(AppContext.BaseDirectory, "assets", "thumbs", picture.ThumbnailPath);
                if (System.IO.File.Exists(thumb)) System.IO.File.Delete(thumb);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not remove thumbnail for picture {Id}", id); }
        }

        var albumItems = _picDb.PictureAlbumItems.Where(i => i.MediaId == id && i.MediaType == "picture");
        _picDb.PictureAlbumItems.RemoveRange(albumItems);
        _picDb.Pictures.Remove(picture);
        await _picDb.SaveChangesAsync();

        _logger.LogInformation("Picture {Id} ('{File}') deleted by {User}", id, picture.FileName, CurrentUsername);
        return Ok(new { success = true, id });
    }

    /// <summary>
    /// Same as DeletePicture, for the videos that live in the Pictures library
    /// (phone clips alongside photos). Separate table, so it needs its own route.
    /// </summary>
    [HttpDelete("picture-videos/{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeletePictureVideo(int id)
    {
        var video = await _picDb.PictureVideos.FindAsync(id);
        if (video == null) return NotFound();

        var (ok, refused, error) = TryDeleteFileWithinRoots(
            video.FilePath, _config.Config.Library.GetPicturesFolderList());

        if (!ok)
        {
            _logger.LogWarning("DeletePictureVideo {Id} failed: {Error}", id, error);
            return StatusCode(refused ? 403 : 500, new { error = error ?? "Could not delete the file." });
        }

        if (!string.IsNullOrWhiteSpace(video.ThumbnailPath))
        {
            try
            {
                var thumb = Path.Combine(AppContext.BaseDirectory, "assets", "thumbs", video.ThumbnailPath);
                if (System.IO.File.Exists(thumb)) System.IO.File.Delete(thumb);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not remove thumbnail for picture-video {Id}", id); }
        }

        var albumItems = _picDb.PictureAlbumItems.Where(i => i.MediaId == id && i.MediaType == "video");
        _picDb.PictureAlbumItems.RemoveRange(albumItems);
        _picDb.PictureVideos.Remove(video);
        await _picDb.SaveChangesAsync();

        _logger.LogInformation("Picture-video {Id} ('{File}') deleted by {User}", id, video.FileName, CurrentUsername);
        return Ok(new { success = true, id });
    }

    [HttpGet("picture-albums/{id:int}/items")]
    public async Task<IActionResult> GetPictureAlbumItems(int id)
    {
        var album = await _picDb.PictureAlbums.FindAsync(id);
        if (album == null || album.Username != CurrentUsername) return NotFound();

        var items = await _picDb.PictureAlbumItems
            .Where(i => i.AlbumId == id)
            .OrderBy(i => i.Position).ThenBy(i => i.AddedAt)
            .ToListAsync();

        // Two batched lookups instead of one query per album row.
        var picIds = items.Where(i => i.MediaType == "picture").Select(i => i.MediaId).Distinct().ToList();
        var vidIds = items.Where(i => i.MediaType != "picture").Select(i => i.MediaId).Distinct().ToList();
        var pics = picIds.Count == 0 ? new Dictionary<int, Picture>()
            : await _picDb.Pictures.AsNoTracking().Where(p => picIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var vids = vidIds.Count == 0 ? new Dictionary<int, PictureVideo>()
            : await _picDb.PictureVideos.AsNoTracking().Where(v => vidIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id);

        var result = new List<object>();
        foreach (var item in items)
        {
            if (item.MediaType == "picture")
            {
                if (pics.TryGetValue(item.MediaId, out var pic))
                    result.Add(new
                    {
                        item.Id, item.AlbumId, item.MediaId, item.MediaType, item.Position,
                        pic.FileName, pic.Width, pic.Height, pic.SizeBytes, pic.Format,
                        pic.ThumbnailPath, pic.DateTaken, pic.Category
                    });
            }
            else
            {
                if (vids.TryGetValue(item.MediaId, out var vid))
                    result.Add(new
                    {
                        item.Id, item.AlbumId, item.MediaId, item.MediaType, item.Position,
                        vid.FileName, vid.Width, vid.Height, vid.SizeBytes, vid.Format,
                        vid.ThumbnailPath, DateTaken = vid.DateTaken, vid.Category,
                        vid.DurationSeconds
                    });
            }
        }
        return Ok(new { album.Id, album.Name, items = result });
    }

    [HttpPost("picture-albums/{id:int}/items")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> AddPictureAlbumItem(int id, [FromBody] AlbumItemDto dto)
    {
        var album = await _picDb.PictureAlbums.FindAsync(id);
        if (album == null || album.Username != CurrentUsername) return NotFound();

        var mediaType = dto.MediaType?.ToLower() == "video" ? "video" : "picture";

        var already = await _picDb.PictureAlbumItems
            .AnyAsync(i => i.AlbumId == id && i.MediaId == dto.MediaId && i.MediaType == mediaType);
        if (already) return Ok(new { message = "Already in album" });

        var maxPos = await _picDb.PictureAlbumItems
            .Where(i => i.AlbumId == id)
            .Select(i => (int?)i.Position)
            .MaxAsync() ?? -1;

        var item = new PictureAlbumItem
        {
            AlbumId = id,
            MediaId = dto.MediaId,
            MediaType = mediaType,
            Position = maxPos + 1,
            AddedAt = DateTime.UtcNow
        };
        _picDb.PictureAlbumItems.Add(item);

        if (album.CoverMediaId == null)
        {
            var thumbPath = mediaType == "picture"
                ? (await _picDb.Pictures.FindAsync(dto.MediaId))?.ThumbnailPath
                : (await _picDb.PictureVideos.FindAsync(dto.MediaId))?.ThumbnailPath;
            album.CoverThumbnail = thumbPath;
            album.CoverMediaType = mediaType;
            album.CoverMediaId = dto.MediaId;
        }

        await _picDb.SaveChangesAsync();
        return Ok(new { item.Id, item.AlbumId, item.MediaId, item.MediaType, item.Position });
    }

    [HttpDelete("picture-albums/{albumId:int}/items/{itemId:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> RemovePictureAlbumItem(int albumId, int itemId)
    {
        var album = await _picDb.PictureAlbums.FindAsync(albumId);
        if (album == null || album.Username != CurrentUsername) return NotFound();

        var item = await _picDb.PictureAlbumItems.FindAsync(itemId);
        if (item == null || item.AlbumId != albumId) return NotFound();

        _picDb.PictureAlbumItems.Remove(item);

        if (album.CoverMediaId == item.MediaId && album.CoverMediaType == item.MediaType)
        {
            var next = await _picDb.PictureAlbumItems
                .Where(i => i.AlbumId == albumId && i.Id != itemId)
                .OrderBy(i => i.Position)
                .FirstOrDefaultAsync();
            if (next != null)
            {
                var thumbPath = next.MediaType == "picture"
                    ? (await _picDb.Pictures.FindAsync(next.MediaId))?.ThumbnailPath
                    : (await _picDb.PictureVideos.FindAsync(next.MediaId))?.ThumbnailPath;
                album.CoverThumbnail = thumbPath;
                album.CoverMediaType = next.MediaType;
                album.CoverMediaId = next.MediaId;
            }
            else
            {
                album.CoverThumbnail = null;
                album.CoverMediaType = null;
                album.CoverMediaId = null;
            }
        }

        await _picDb.SaveChangesAsync();
        return Ok(new { message = "Removed" });
    }

    // ─── eBooks ──────────────────────────────────────────────────────

    [HttpGet("ebooks")]
    public async Task<IActionResult> GetEBooks(
        [FromQuery] string? category = null,
        [FromQuery] string? search = null,
        [FromQuery] string? format = null,
        [FromQuery] string? sort = "recent",
        [FromQuery] int page = 1,
        [FromQuery] int limit = 100)
    {
        var query = _ebookDb.EBooks.AsQueryable();

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(e => e.Category.ToLower() == category.ToLower());

        if (!string.IsNullOrWhiteSpace(format))
        {
            if (format.Equals("comic", StringComparison.OrdinalIgnoreCase))
                query = query.Where(e => e.Format == "CBZ" || e.Format == "CBR");
            else
                query = query.Where(e => e.Format.ToLower() == format.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(e => e.Title.ToLower().Contains(s)
                || e.Author.ToLower().Contains(s)
                || e.FileName.ToLower().Contains(s)
                || (e.Series != null && e.Series.ToLower().Contains(s)));
        }

        query = sort switch
        {
            "title" => query.OrderBy(e => e.Title),
            "author" => query.OrderBy(e => e.Author).ThenBy(e => e.Title),
            "name" => query.OrderBy(e => e.FileName),
            "size" => query.OrderByDescending(e => e.FileSize),
            _ => query.OrderByDescending(e => e.DateAdded)
        };

        var total = await query.CountAsync();
        var rows = await query.Skip((page - 1) * limit).Take(limit)
            .Select(e => new
            {
                e.Id, e.FileName, e.Title, e.Author, e.Format,
                e.FileSize, e.PageCount, e.Category, e.DateAdded,
                e.Publisher, e.Language, e.Subject, e.CoverImage,
                e.Series, e.SeriesIndex
            }).ToListAsync();

        // Per-user "read" flag (mirrors video watched state).
        var readIds = _userFavs.GetReadEBookIds(CurrentUsername);
        var ebooks = rows.Select(e => new
        {
            e.Id, e.FileName, e.Title, e.Author, e.Format,
            e.FileSize, e.PageCount, e.Category, e.DateAdded,
            e.Publisher, e.Language, e.Subject, e.CoverImage,
            e.Series, e.SeriesIndex,
            isRead = readIds.Contains(e.Id)
        }).ToList();

        return Ok(new { total, page, limit, ebooks });
    }

    [HttpGet("ebooks/categories")]
    public async Task<IActionResult> GetEBookCategories()
    {
        var categories = await _ebookDb.EBooks
            .GroupBy(e => e.Category)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderBy(g => g.name)
            .ToListAsync();
        return Ok(categories);
    }

    // ─── Open Library metadata lookup (eBooks + audiobooks) ──────────
    // Free, no API key. Fills missing details in the metadata editors on demand.

    [HttpGet("metadata/openlibrary/search")]
    public async Task<IActionResult> OpenLibrarySearch([FromQuery] string? title = null, [FromQuery] string? author = null, [FromQuery] int limit = 10)
    {
        var svc = HttpContext.RequestServices.GetRequiredService<OpenLibraryService>();
        var results = await svc.SearchAsync(title, author, limit);
        return Ok(new { results });
    }

    [HttpGet("metadata/openlibrary/work")]
    public async Task<IActionResult> OpenLibraryWork([FromQuery] string key)
    {
        var svc = HttpContext.RequestServices.GetRequiredService<OpenLibraryService>();
        var (description, subjects) = await svc.GetWorkAsync(key ?? "");
        return Ok(new { description, subjects });
    }

    // Downloads an Open Library cover (whitelisted host only) into the given assets subdir,
    // replacing any previous OL cover for that id. Returns the stored filename or null.
    private async Task<string?> DownloadOpenLibraryCoverAsync(int id, string coverUrl, string subDir)
    {
        if (!Uri.TryCreate(coverUrl, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("covers.openlibrary.org", StringComparison.OrdinalIgnoreCase))
            return null;   // SSRF guard: only Open Library's cover host
        try
        {
            using var http = Http(15);
            var bytes = await http.GetByteArrayAsync(uri);
            if (bytes.Length < 800) return null;   // OL serves a tiny blank when no cover exists
            var dir = Path.Combine(AppContext.BaseDirectory, "assets", subDir);
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.GetFiles(dir, $"ol_cover_{id}_*.jpg"))
                try { System.IO.File.Delete(old); } catch { /* ignore */ }
            var fileName = $"ol_cover_{id}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.jpg";
            await System.IO.File.WriteAllBytesAsync(Path.Combine(dir, fileName), bytes);
            return fileName;
        }
        catch { return null; }
    }

    [HttpGet("ebooks/stats")]
    public async Task<IActionResult> GetEBookStats()
    {
        var stats = new
        {
            totalEBooks = await _ebookDb.EBooks.CountAsync(),
            totalSize = await _ebookDb.EBooks.SumAsync(e => e.FileSize),
            totalCategories = await _ebookDb.EBooks
                .Select(e => e.Category).Distinct().CountAsync(),
            totalPdf = await _ebookDb.EBooks.CountAsync(e => e.Format == "PDF"),
            totalEpub = await _ebookDb.EBooks.CountAsync(e => e.Format == "EPUB"),
            recentlyAdded = await _ebookDb.EBooks
                .CountAsync(e => e.DateAdded > DateTime.UtcNow.AddDays(-7)),
            totalAuthors = await _ebookDb.EBooks
                .Where(e => e.Author != "")
                .Select(e => e.Author).Distinct().CountAsync()
        };
        return Ok(stats);
    }

    [HttpGet("ebooks/{id}")]
    public async Task<IActionResult> GetEBook(int id)
    {
        var ebook = await _ebookDb.EBooks.FindAsync(id);
        if (ebook == null) return NotFound();
        return Ok(new
        {
            ebook.Id, ebook.FileName, ebook.FilePath, ebook.Title, ebook.Author,
            ebook.Format, ebook.FileSize, ebook.PageCount, ebook.Category,
            ebook.Publisher, ebook.Language, ebook.ISBN, ebook.Description,
            ebook.Subject, ebook.DateAdded, ebook.LastModified, ebook.CoverImage,
            ebook.Series, ebook.SeriesIndex,
            isRead = _userFavs.IsEBookRead(CurrentUsername, ebook.Id)
        });
    }

    // ─── eBook "read" flag (per-user, like video watched state) ──────────
    // Not admin-gated: read/unread lives in the caller's own users/{name}.db.

    public class EBookReadRequest { public bool? Read { get; set; } }

    [HttpPost("ebooks/{id}/read")]
    public async Task<IActionResult> SetEBookRead(int id, [FromBody] EBookReadRequest? req)
    {
        var exists = await _ebookDb.EBooks.AnyAsync(e => e.Id == id);
        if (!exists) return NotFound();

        bool read;
        if (req?.Read is bool r)   // explicit state supplied
        {
            _userFavs.SetEBooksReadBulk(CurrentUsername, new[] { id }, r);
            read = r;
        }
        else                        // no state → toggle
        {
            read = _userFavs.ToggleEBookRead(CurrentUsername, id);
        }
        return Ok(new { success = true, isRead = read });
    }

    public class EBookReadBulkRequest { public List<int>? Ids { get; set; } public bool Read { get; set; } }

    [HttpPost("ebooks/read/bulk")]
    public IActionResult SetEBooksReadBulk([FromBody] EBookReadBulkRequest req)
    {
        if (req?.Ids == null || req.Ids.Count == 0)
            return BadRequest(new { error = "No eBooks specified" });
        var affected = _userFavs.SetEBooksReadBulk(CurrentUsername, req.Ids, req.Read);
        return Ok(new { success = true, read = req.Read, count = req.Ids.Distinct().Count(), affected });
    }

    [HttpPut("ebooks/{id}")]
    public async Task<IActionResult> UpdateEBook(int id, [FromBody] UpdateEBookRequest req)
    {
        var ebook = await _ebookDb.EBooks.FindAsync(id);
        if (ebook == null) return NotFound();

        if (req.Title != null)       ebook.Title       = req.Title.Trim();
        if (req.Author != null)      ebook.Author      = req.Author.Trim();
        if (req.Genre != null)       ebook.Category    = req.Genre.Trim();
        if (req.Description != null) ebook.Description = req.Description.Trim();
        if (req.Series != null)
        {
            var s = req.Series.Trim();
            ebook.Series = s.Length == 0 ? null : s;   // empty clears the collection
            if (s.Length == 0) ebook.SeriesIndex = null;
        }
        if (req.SeriesIndex.HasValue)
            ebook.SeriesIndex = req.SeriesIndex.Value < 0 ? null : req.SeriesIndex.Value;
        if (!string.IsNullOrWhiteSpace(req.CoverUrl))
        {
            var cover = await DownloadOpenLibraryCoverAsync(ebook.Id, req.CoverUrl, "ebookcovers");
            if (cover != null) ebook.CoverImage = cover;
        }

        _ebookDb.EBooks.Update(ebook);
        await _ebookDb.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // Upload a custom cover for an eBook. Stored on disk in assets/ebookcovers/ (served at
    // /ebookcover/{file}) and referenced by EBooks.CoverImage - NOT in browser storage.
    [HttpPost("ebooks/{id}/cover")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<IActionResult> UploadEBookCover(int id, IFormFile? file)
    {
        var ebook = await _ebookDb.EBooks.FindAsync(id);
        if (ebook == null) return NotFound();
        if (file == null || file.Length == 0) return BadRequest(new { error = "No file provided" });

        var cover = await SaveCustomBookCoverAsync(id, file, "ebookcovers");
        if (cover == null) return BadRequest(new { error = "Invalid image file" });

        ebook.CoverImage = cover;
        _ebookDb.EBooks.Update(ebook);
        await _ebookDb.SaveChangesAsync();
        return Ok(new { success = true, coverImage = cover, v = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
    }

    // Resize (aspect preserved, max 800x1200) an uploaded book cover to JPEG and save it into
    // assets/{subDir}/ as custom_cover_{id}_{ts}.jpg, pruning this id's previous custom covers.
    private async Task<string?> SaveCustomBookCoverAsync(int id, IFormFile file, string subDir)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "assets", subDir);
        Directory.CreateDirectory(dir);
        var fileName = $"custom_cover_{id}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.jpg";
        var dest = Path.Combine(dir, fileName);
        try
        {
            using var img = await Image.LoadAsync(file.OpenReadStream());
            img.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(800, 1200), Mode = ResizeMode.Max }));
            await img.SaveAsJpegAsync(dest);
        }
        catch { return null; }
        // Prune previous custom covers for this id so they don't accumulate.
        foreach (var old in Directory.GetFiles(dir, $"custom_cover_{id}_*.jpg"))
            if (!string.Equals(Path.GetFileName(old), fileName, StringComparison.OrdinalIgnoreCase))
                try { System.IO.File.Delete(old); } catch { /* ignore */ }
        return fileName;
    }

    // Batch-assign several eBooks to a collection at once (multi-select in the eBooks grid).
    // Series == null/absent  -> leave collection unchanged; "" -> clear; a name -> set.
    [HttpPost("ebooks/batch")]
    public async Task<IActionResult> BatchUpdateEBooks([FromBody] BatchEBookRequest req)
    {
        if (req?.Ids == null || req.Ids.Count == 0)
            return BadRequest(new { error = "No eBooks specified" });

        var ids = req.Ids.Distinct().ToList();
        var books = await _ebookDb.EBooks.Where(b => ids.Contains(b.Id)).ToListAsync();
        if (books.Count == 0) return NotFound();

        bool setSeries = req.Series != null;
        string? series = req.Series?.Trim();
        if (setSeries && series!.Length == 0) series = null;   // blank clears the collection

        if (setSeries && series != null && req.AutoNumber)
        {
            // Order by title (natural: "Book 2" before "Book 10") and number sequentially.
            double idx = req.StartIndex ?? 1;
            foreach (var b in books.OrderBy(b => NaturalSortKey(b.Title ?? ""), StringComparer.OrdinalIgnoreCase))
            {
                b.Series = series;
                b.SeriesIndex = idx;
                idx += 1;
            }
        }
        else if (setSeries)
        {
            foreach (var b in books)
            {
                b.Series = series;
                if (series == null) b.SeriesIndex = null;   // clearing the collection clears the index
            }
        }

        await _ebookDb.SaveChangesAsync();
        return Ok(new { success = true, count = books.Count });
    }

    [HttpGet("ebooks/{id}/download")]
    public async Task<IActionResult> DownloadEBook(int id)
    {
        var ebook = await _ebookDb.EBooks.FindAsync(id);
        if (ebook == null) return NotFound();

        if (!System.IO.File.Exists(ebook.FilePath))
            return NotFound("eBook file not found on disk");

        var mimeType = ebook.Format.ToUpperInvariant() switch
        {
            "PDF" => "application/pdf",
            "EPUB" => "application/epub+zip",
            _ => "application/octet-stream"
        };

        var stream = new FileStream(ebook.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, mimeType, ebook.FileName, enableRangeProcessing: true);
    }

    [HttpGet("ebooks/{id}/view")]
    public async Task<IActionResult> ViewEBook(int id)
    {
        var ebook = await _ebookDb.EBooks.FindAsync(id);
        if (ebook == null) return NotFound();

        if (!System.IO.File.Exists(ebook.FilePath))
            return NotFound("eBook file not found on disk");

        var mimeType = ebook.Format.ToUpperInvariant() switch
        {
            "PDF"  => "application/pdf",
            "EPUB" => "application/epub+zip",
            "CBZ"  => "application/zip",
            "CBR"  => "application/x-rar-compressed",
            _      => "application/octet-stream"
        };

        var stream = new FileStream(ebook.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Response.Headers["Content-Disposition"] = "inline";
        return File(stream, mimeType, enableRangeProcessing: true);
    }

    private static readonly HashSet<string> _comicImageExts = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" };

    [HttpGet("ebooks/{id}/pagecount")]
    public async Task<IActionResult> GetComicPageCount(int id)
    {
        var ebook = await _ebookDb.EBooks.FindAsync(id);
        if (ebook == null) return NotFound();
        if (!System.IO.File.Exists(ebook.FilePath)) return NotFound("File not found on disk");

        try
        {
            int count;
            if (ebook.Format.Equals("CBZ", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(ebook.FilePath);
                count = zip.Entries.Count(e => _comicImageExts.Contains(Path.GetExtension(e.Name)));
            }
            else
            {
                using var archive = ArchiveFactory.OpenArchive(new FileInfo(ebook.FilePath));
                count = archive.Entries.Count(e => !e.IsDirectory && _comicImageExts.Contains(Path.GetExtension(e.Key ?? "")));
            }
            return Ok(new { pageCount = count });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpGet("ebooks/{id}/page/{pageIndex:int}")]
    public async Task<IActionResult> GetComicPage(int id, int pageIndex)
    {
        var ebook = await _ebookDb.EBooks.FindAsync(id);
        if (ebook == null) return NotFound();
        if (!System.IO.File.Exists(ebook.FilePath)) return NotFound("File not found on disk");

        try
        {
            byte[] imageBytes;
            string ext;

            if (ebook.Format.Equals("CBZ", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(ebook.FilePath);
                var pages = zip.Entries
                    .Where(e => _comicImageExts.Contains(Path.GetExtension(e.Name)))
                    .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (pageIndex < 0 || pageIndex >= pages.Count) return NotFound("Page out of range");
                ext = Path.GetExtension(pages[pageIndex].Name).ToLowerInvariant();
                using var stream = pages[pageIndex].Open();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                imageBytes = ms.ToArray();
            }
            else
            {
                using var archive = ArchiveFactory.OpenArchive(new FileInfo(ebook.FilePath));
                var pages = archive.Entries
                    .Where(e => !e.IsDirectory && _comicImageExts.Contains(Path.GetExtension(e.Key ?? "")))
                    .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (pageIndex < 0 || pageIndex >= pages.Count) return NotFound("Page out of range");
                ext = Path.GetExtension(pages[pageIndex].Key ?? "").ToLowerInvariant();
                using var stream = pages[pageIndex].OpenEntryStream();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                imageBytes = ms.ToArray();
            }

            var contentType = ext switch
            {
                ".png"  => "image/png",
                ".webp" => "image/webp",
                ".gif"  => "image/gif",
                _       => "image/jpeg"
            };
            return File(imageBytes, contentType);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // ─── eBooks Scanning ─────────────────────────────────────────────

    [HttpPost("scan/ebooks")]
    public IActionResult StartEBookScan()
    {
        if (_ebookScanner.IsScanning)
            return Conflict(new { message = "eBooks scan already in progress" });

        _ = _ebookScanner.StartScanAsync();
        _semantic.RequestAutoIndex();
        return Ok(new { message = "eBooks scan started" });
    }

    [HttpGet("scan/ebooks/status")]
    public IActionResult GetEBookScanStatus()
    {
        var p = _ebookScanner.CurrentProgress;
        return Ok(new
        {
            p.Status,
            p.Message,
            p.TotalFiles,
            p.ProcessedFiles,
            p.NewBooks,
            p.UpdatedBooks,
            p.ErrorCount,
            p.PercentComplete,
            p.StartTime,
            isScanning = _ebookScanner.IsScanning
        });
    }

    // ─── Audio Books ─────────────────────────────────────────────────

    [HttpGet("audiobooks")]
    public async Task<IActionResult> GetAudioBooks(
        [FromQuery] string? category = null,
        [FromQuery] string? search = null,
        [FromQuery] string? format = null,
        [FromQuery] string? sort = "recent",
        [FromQuery] int page = 1,
        [FromQuery] int limit = 100)
    {
        var query = _audioBooksDb.AudioBooks.AsQueryable();

        if (!string.IsNullOrWhiteSpace(category))
        {
            if (category == "__none__")
                query = query.Where(a => a.Category == "");
            else
                query = query.Where(a => a.Category.ToLower() == category.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(format))
            query = query.Where(a => a.Format.ToLower() == format.ToLower());

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(a => a.Title.ToLower().Contains(s)
                || a.Author.ToLower().Contains(s)
                || a.Narrator.ToLower().Contains(s)
                || a.FileName.ToLower().Contains(s));
        }

        query = sort switch
        {
            "title" => query.OrderBy(a => a.Title),
            "author" => query.OrderBy(a => a.Author).ThenBy(a => a.Title),
            "duration" => query.OrderByDescending(a => a.Duration),
            "size" => query.OrderByDescending(a => a.FileSize),
            _ => query.OrderByDescending(a => a.DateAdded)
        };

        var total = await query.CountAsync();
        var audioBooks = await query.Skip((page - 1) * limit).Take(limit)
            .Select(a => new
            {
                a.Id, a.FileName, a.Title, a.Author, a.Narrator, a.Format,
                a.FileSize, a.Duration, a.Category, a.DateAdded,
                a.Year, a.Series, a.SeriesIndex, a.CoverImage
            }).ToListAsync();

        var favIds = _userFavs.GetFavouriteIds(CurrentUsername, "audiobook");
        var result = audioBooks.Select(a => new
        {
            a.Id, a.FileName, a.Title, a.Author, a.Narrator, a.Format,
            a.FileSize, a.Duration, a.Category, a.DateAdded,
            a.Year, a.Series, a.SeriesIndex, a.CoverImage,
            isFavourite = favIds.Contains(a.Id)
        }).ToList();

        return Ok(new { total, page, limit, audioBooks = result });
    }

    [HttpGet("audiobooks/categories")]
    public async Task<IActionResult> GetAudioBookCategories()
    {
        var categories = await _audioBooksDb.AudioBooks
            .GroupBy(a => a.Category)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderBy(g => g.name)
            .ToListAsync();
        return Ok(categories);
    }

    [HttpGet("audiobooks/stats")]
    public async Task<IActionResult> GetAudioBookStats()
    {
        var stats = new
        {
            totalAudioBooks = await _audioBooksDb.AudioBooks.CountAsync(),
            totalSize = await _audioBooksDb.AudioBooks.SumAsync(a => a.FileSize),
            totalDuration = await _audioBooksDb.AudioBooks.SumAsync(a => a.Duration),
            totalCategories = await _audioBooksDb.AudioBooks
                .Select(a => a.Category).Distinct().CountAsync(),
            totalMp3 = await _audioBooksDb.AudioBooks.CountAsync(a => a.Format == "MP3"),
            totalM4B = await _audioBooksDb.AudioBooks.CountAsync(a => a.Format == "M4B"),
            recentlyAdded = await _audioBooksDb.AudioBooks
                .CountAsync(a => a.DateAdded > DateTime.UtcNow.AddDays(-7)),
            totalAuthors = await _audioBooksDb.AudioBooks
                .Where(a => a.Author != "")
                .Select(a => a.Author).Distinct().CountAsync()
        };
        return Ok(stats);
    }

    [HttpGet("audiobooks/favourites")]
    public async Task<IActionResult> GetAudioBookFavourites()
    {
        var favIds = _userFavs.GetFavouriteIds(CurrentUsername, "audiobook");
        if (!favIds.Any()) return Ok(new { audioBooks = Array.Empty<object>() });
        var books = await _audioBooksDb.AudioBooks
            .Where(a => favIds.Contains(a.Id))
            .OrderBy(a => a.Title)
            .Select(a => new
            {
                a.Id, a.FileName, a.Title, a.Author, a.Format,
                a.FileSize, a.Duration, a.Category, a.CoverImage,
                isFavourite = true
            }).ToListAsync();
        return Ok(new { audioBooks = books });
    }

    [HttpPost("audiobooks/{id}/favourite")]
    public async Task<IActionResult> ToggleAudioBookFavourite(int id)
    {
        var book = await _audioBooksDb.AudioBooks.FindAsync(id);
        if (book == null) return NotFound();
        var isFav = _userFavs.ToggleFavourite(CurrentUsername, "audiobook", id);
        return Ok(new { id, isFavourite = isFav });
    }

    [HttpGet("audiobooks/{id}")]
    public async Task<IActionResult> GetAudioBook(int id)
    {
        var book = await _audioBooksDb.AudioBooks.FindAsync(id);
        if (book == null) return NotFound();
        var favIds = _userFavs.GetFavouriteIds(CurrentUsername, "audiobook");
        return Ok(new
        {
            book.Id, book.FileName, book.FilePath, book.Title, book.Author,
            book.Narrator, book.Format, book.FileSize, book.Duration, book.Category,
            book.Series, book.SeriesIndex, book.Year, book.Description, book.Publisher,
            book.Language, book.DateAdded, book.LastModified, book.CoverImage,
            isFavourite = favIds.Contains(book.Id)
        });
    }

    [HttpGet("audiobooks/{id}/stream")]
    public async Task<IActionResult> StreamAudioBook(int id)
    {
        var book = await _audioBooksDb.AudioBooks.FindAsync(id);
        if (book == null) return NotFound();

        if (!System.IO.File.Exists(book.FilePath))
            return NotFound("Audio book file not found on disk");

        var mimeType = book.Format.ToUpperInvariant() switch
        {
            "MP3"  => "audio/mpeg",
            "M4B"  => "audio/mp4",
            "M4A"  => "audio/mp4",
            "AAC"  => "audio/aac",
            "OGG"  => "audio/ogg",
            "OPUS" => "audio/ogg",
            "FLAC" => "audio/flac",
            _      => "audio/octet-stream"
        };

        var stream = new FileStream(book.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Response.Headers["Content-Disposition"] = "inline";
        return File(stream, mimeType, enableRangeProcessing: true);
    }

    [HttpGet("audiobooks/{id}/download")]
    public async Task<IActionResult> DownloadAudioBook(int id)
    {
        var book = await _audioBooksDb.AudioBooks.FindAsync(id);
        if (book == null) return NotFound();

        if (!System.IO.File.Exists(book.FilePath))
            return NotFound("Audio book file not found on disk");

        var mimeType = book.Format.ToUpperInvariant() switch
        {
            "MP3"  => "audio/mpeg",
            "M4B"  => "audio/mp4",
            "M4A"  => "audio/mp4",
            "AAC"  => "audio/aac",
            "OGG"  => "audio/ogg",
            "OPUS" => "audio/ogg",
            "FLAC" => "audio/flac",
            _      => "audio/octet-stream"
        };

        var stream = new FileStream(book.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, mimeType, book.FileName, enableRangeProcessing: true);
    }

    // ─── AudioBook - Update Metadata ─────────────────────────────────

    [HttpPut("audiobooks/{id}")]
    public async Task<IActionResult> UpdateAudioBook(int id, [FromBody] UpdateAudioBookRequest req)
    {
        var book = await _audioBooksDb.AudioBooks.FindAsync(id);
        if (book == null) return NotFound();

        if (req.Title       != null) book.Title       = req.Title.Trim();
        if (req.Author      != null) book.Author      = req.Author.Trim();
        if (req.Narrator    != null) book.Narrator    = req.Narrator.Trim();
        if (req.Category    != null) book.Category    = req.Category.Trim();
        if (req.Series      != null)
        {
            book.Series = string.IsNullOrWhiteSpace(req.Series) ? null : req.Series.Trim();
            if (book.Series == null) book.SeriesIndex = null;   // clearing the collection clears the index
        }
        if (req.SeriesIndex.HasValue) book.SeriesIndex = req.SeriesIndex.Value < 0 ? null : req.SeriesIndex.Value;
        if (req.Description != null) book.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        if (req.Year.HasValue)       book.Year        = req.Year.Value > 0 ? req.Year.Value : null;
        if (!string.IsNullOrWhiteSpace(req.CoverUrl))
        {
            var cover = await DownloadOpenLibraryCoverAsync(book.Id, req.CoverUrl, "audiobookcovers");
            if (cover != null) book.CoverImage = cover;
        }

        _audioBooksDb.AudioBooks.Update(book);
        await _audioBooksDb.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // Upload a custom cover for an audiobook. Stored in assets/audiobookcovers/ (served at
    // /audiobookcover/{file}) and referenced by AudioBooks.CoverImage.
    [HttpPost("audiobooks/{id}/cover")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<IActionResult> UploadAudioBookCover(int id, IFormFile? file)
    {
        var book = await _audioBooksDb.AudioBooks.FindAsync(id);
        if (book == null) return NotFound();
        if (file == null || file.Length == 0) return BadRequest(new { error = "No file provided" });

        var cover = await SaveCustomBookCoverAsync(id, file, "audiobookcovers");
        if (cover == null) return BadRequest(new { error = "Invalid image file" });

        book.CoverImage = cover;
        _audioBooksDb.AudioBooks.Update(book);
        await _audioBooksDb.SaveChangesAsync();
        return Ok(new { success = true, coverImage = cover, v = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
    }

    // ─── AudioBook - All-Progress Map (for card progress bars) ───────

    [HttpGet("audiobooks/all-progress")]
    public IActionResult GetAllAudioBookProgress()
    {
        var map = _userFavs.GetAllAudioBookProgress(CurrentUsername);
        return Ok(map);
    }

    // ─── AudioBook - Continue Listening ──────────────────────────────

    [HttpGet("audiobooks/continue-listening")]
    public async Task<IActionResult> GetContinueListening()
    {
        var inProgress = _userFavs.GetContinueListening(CurrentUsername, 20);
        if (inProgress.Count == 0) return Ok(Array.Empty<object>());

        var ids = inProgress.Select(x => x.Id).ToList();
        var books = await _audioBooksDb.AudioBooks
            .Where(b => ids.Contains(b.Id))
            .Select(b => new { b.Id, b.Title, b.Author, b.CoverImage, b.Duration })
            .ToListAsync();

        var bookMap = books.ToDictionary(b => b.Id);
        var result = inProgress
            .Where(x => bookMap.ContainsKey(x.Id))
            .Select(x => new
            {
                id         = x.Id,
                title      = bookMap[x.Id].Title,
                author     = bookMap[x.Id].Author,
                coverImage = bookMap[x.Id].CoverImage,
                duration   = bookMap[x.Id].Duration,
                position   = x.Position,
                percent    = x.Percent
            })
            .ToList();

        return Ok(result);
    }

    // ─── AudioBook - Listening Progress ──────────────────────────────

    [HttpGet("audiobooks/{id}/progress")]
    public IActionResult GetAudioBookProgress(int id)
    {
        var (position, duration, percent, completed) = _userFavs.GetAudioBookProgress(CurrentUsername, id);
        return Ok(new { position, duration, percent, completed });
    }

    [HttpPost("audiobooks/{id}/progress")]
    public IActionResult SaveAudioBookProgress(int id, [FromBody] System.Text.Json.JsonElement body)
    {
        var position = body.TryGetProperty("position", out var p) ? p.GetDouble() : 0;
        var duration = body.TryGetProperty("duration", out var d) ? d.GetDouble() : 0;
        _userFavs.SaveAudioBookProgress(CurrentUsername, id, position, duration);
        return Ok(new { saved = true });
    }

    // ─── AudioBook - Chapters ─────────────────────────────────────────

    [HttpGet("audiobooks/{id}/chapters")]
    public async Task<IActionResult> GetAudioBookChapters(int id)
    {
        var book = await _audioBooksDb.AudioBooks.FindAsync(id);
        if (book == null) return NotFound();
        if (string.IsNullOrEmpty(book.ChaptersJson))
            return Ok(new { chapters = Array.Empty<object>() });
        try
        {
            var chapters = System.Text.Json.JsonSerializer.Deserialize<object>(book.ChaptersJson);
            return Ok(new { chapters });
        }
        catch { return Ok(new { chapters = Array.Empty<object>() }); }
    }

    // ─── AudioBook - Bookmarks ────────────────────────────────────────

    [HttpGet("audiobooks/{id}/bookmarks")]
    public IActionResult GetAudioBookBookmarks(int id)
    {
        var bookmarks = _userFavs.GetAudioBookBookmarks(CurrentUsername, id);
        return Ok(bookmarks);
    }

    [HttpPost("audiobooks/{id}/bookmarks")]
    public IActionResult AddAudioBookBookmark(int id, [FromBody] System.Text.Json.JsonElement body)
    {
        var position = body.TryGetProperty("position", out var p) ? p.GetDouble() : 0;
        var title    = body.TryGetProperty("title",    out var t) ? t.GetString() ?? "" : "";
        var note     = body.TryGetProperty("note",     out var n) ? n.GetString() ?? "" : "";
        var newId = _userFavs.AddAudioBookBookmark(CurrentUsername, id, position, title, note);
        return Ok(new { id = newId, position, title, note });
    }

    [HttpDelete("audiobooks/{id}/bookmarks/{bookmarkId}")]
    public IActionResult DeleteAudioBookBookmark(int id, int bookmarkId)
    {
        _userFavs.DeleteAudioBookBookmark(CurrentUsername, bookmarkId);
        return Ok(new { deleted = true });
    }

    // ─── Download (Offline) ───────────────────────────────────────────

    [HttpGet("download/track/{id}")]
    public async Task<IActionResult> DownloadTrack(int id)
    {
        var track = await _db.Tracks.FindAsync(id);
        if (track == null) return NotFound();
        if (!System.IO.File.Exists(track.FilePath))
            return NotFound("Track file not found on disk");

        var ext = Path.GetExtension(track.FilePath);
        var rawName = $"{track.Artist} - {track.Title}{ext}";
        var safeName = Path.GetInvalidFileNameChars().Aggregate(rawName, (s, c) => s.Replace(c, '_'));
        var stream = new FileStream(track.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, "application/octet-stream", safeName, enableRangeProcessing: true);
    }

    [HttpGet("download/video/{id}")]
    // Optional trailing filename segment (ignored): lets external players (MPV/VLC) derive a title
    // from the URL. Used by the external-player handoff, which serves the raw file for direct play.
    [HttpGet("download/video/{id}/{name}")]
    public async Task<IActionResult> DownloadVideo(int id)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();
        if (!System.IO.File.Exists(video.FilePath))
            return NotFound("Video file not found on disk");

        // Name the download after the ACTUAL on-disk filename, not video.Title.
        // Title is the scraped (and possibly localized) episode name - on a French
        // server that turned "Daredevil Born Again S01E01.mkv" into
        // "Une demi-heure au Paradis.mkv". The file on disk is what the user expects.
        var rawName = Path.GetFileName(video.FilePath);
        var safeName = Path.GetInvalidFileNameChars().Aggregate(rawName, (s, c) => s.Replace(c, '_'));
        var stream = new FileStream(video.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, "application/octet-stream", safeName, enableRangeProcessing: true);
    }

    [HttpGet("download/musicvideo/{id}")]
    [HttpGet("download/musicvideo/{id}/{name}")]
    public async Task<IActionResult> DownloadMusicVideo(int id)
    {
        var mv = await _mvDb.MusicVideos.FindAsync(id);
        if (mv == null) return NotFound();
        if (!System.IO.File.Exists(mv.FilePath))
            return NotFound("Music video file not found on disk");

        // Name the download after the actual on-disk filename (mirrors DownloadVideo).
        var rawName = Path.GetFileName(mv.FilePath);
        var safeName = Path.GetInvalidFileNameChars().Aggregate(rawName, (s, c) => s.Replace(c, '_'));
        var stream = new FileStream(mv.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, "application/octet-stream", safeName, enableRangeProcessing: true);
    }

    // ─── Audio Books Scanning ─────────────────────────────────────────

    [HttpPost("scan/audiobooks")]
    public IActionResult StartAudioBookScan()
    {
        if (_audioBooksScanner.IsScanning)
            return Conflict(new { message = "Audio books scan already in progress" });

        _ = _audioBooksScanner.StartScanAsync();
        _semantic.RequestAutoIndex();
        return Ok(new { message = "Audio books scan started" });
    }

    [HttpGet("scan/audiobooks/status")]
    public IActionResult GetAudioBookScanStatus()
    {
        var p = _audioBooksScanner.CurrentProgress;
        return Ok(new
        {
            p.Status,
            p.Message,
            p.TotalFiles,
            p.ProcessedFiles,
            p.NewBooks,
            p.UpdatedBooks,
            p.ErrorCount,
            p.PercentComplete,
            p.StartTime,
            isScanning = _audioBooksScanner.IsScanning
        });
    }

    // ─── Music Videos ─────────────────────────────────────────────────

    [HttpGet("musicvideos")]
    public async Task<IActionResult> GetMusicVideos(
        [FromQuery] string? artist = null,
        [FromQuery] string? search = null,
        [FromQuery] int? year = null,
        [FromQuery] string? sort = "recent",
        [FromQuery] int page = 1,
        [FromQuery] int limit = 100,
        [FromQuery] string? quality = null)
    {
        var query = _mvDb.MusicVideos.AsQueryable();

        if (!string.IsNullOrWhiteSpace(artist))
            query = query.Where(v => v.Artist.ToLower() == artist.ToLower());
        if (year.HasValue)
            query = query.Where(v => v.Year == year.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(v => v.Title.ToLower().Contains(s)
                || v.Artist.ToLower().Contains(s)
                || v.FileName.ToLower().Contains(s));
        }
        // Snapshot before the quality filter - the Quality menu's per-bucket counts are
        // computed from this so every bucket shows its full total regardless of selection.
        var mvQualFacetQuery = query;

        // Video-quality filter (same buckets as the Movies/TV pages): 4k >= 2160,
        // hd 720..2159, sd 1..719. Height 0 (unknown) shows only under "All".
        if (!string.IsNullOrWhiteSpace(quality))
        {
            switch (quality.Trim().ToLowerInvariant())
            {
                case "4k": query = query.Where(v => v.Height >= 2160); break;
                case "hd": query = query.Where(v => v.Height >= 720 && v.Height < 2160); break;
                case "sd": query = query.Where(v => v.Height > 0 && v.Height < 720); break;
            }
        }

        query = sort switch
        {
            "title" => query.OrderBy(v => v.Title),
            "artist" => query.OrderBy(v => v.Artist).ThenBy(v => v.Title),
            "year" => query.OrderByDescending(v => v.Year).ThenBy(v => v.Title),
            "size" => query.OrderByDescending(v => v.SizeBytes),
            "duration" => query.OrderByDescending(v => v.Duration),
            _ => query.OrderByDescending(v => v.DateAdded)
        };

        var total = await query.CountAsync();
        var mvFavIds = _userFavs.GetFavouriteIds(CurrentUsername, "musicvideo");
        var videos = await query.Skip((page - 1) * limit).Take(limit)
            .Select(v => new
            {
                v.Id, v.FileName, v.Title, v.Artist, v.Year,
                v.Duration, v.SizeBytes, v.Format, v.Resolution,
                v.Width, v.Height, v.Codec, v.Bitrate, v.Genre,
                v.ThumbnailPath, v.NeedsOptimization, v.Mp4Compliant,
                v.AudioChannels, IsFavourite = mvFavIds.Contains(v.Id), v.DateAdded
            }).ToListAsync();

        // Per-quality counts for the Quality menu (flat list - each MV is one item).
        var mvHeights = await mvQualFacetQuery.Select(v => v.Height).ToListAsync();
        int mvUhd = 0, mvHd = 0, mvSd = 0;
        foreach (var ht in mvHeights)
        {
            if (ht >= 2160) mvUhd++; else if (ht >= 720) mvHd++; else if (ht > 0) mvSd++;
        }
        var qualityCounts = new { all = mvHeights.Count, uhd = mvUhd, hd = mvHd, sd = mvSd };

        return Ok(new { total, page, limit, videos, qualityCounts });
    }

    [HttpGet("musicvideos/stats")]
    public async Task<IActionResult> GetMusicVideoStats()
    {
        var stats = new
        {
            totalVideos = await _mvDb.MusicVideos.CountAsync(),
            totalSize = await _mvDb.MusicVideos.SumAsync(v => v.SizeBytes),
            totalDuration = await _mvDb.MusicVideos.SumAsync(v => v.Duration),
            totalArtists = await _mvDb.MusicVideos
                .Where(v => v.Artist != "").Select(v => v.Artist).Distinct().CountAsync(),
            needsOptimization = await _mvDb.MusicVideos.CountAsync(v => v.NeedsOptimization),
            nonCompliant = await _mvDb.MusicVideos.CountAsync(v => !v.Mp4Compliant),
            withThumbnails = await _mvDb.MusicVideos.CountAsync(v => v.ThumbnailPath != null && v.ThumbnailPath != ""),
            ffmpegAvailable = _ffmpeg.IsAvailable
        };
        return Ok(stats);
    }

    [HttpGet("musicvideos/artists")]
    public async Task<IActionResult> GetMusicVideoArtists()
    {
        var artists = await _mvDb.MusicVideos
            .Where(v => v.Artist != "")
            .GroupBy(v => v.Artist)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderBy(g => g.name)
            .ToListAsync();

        var names = artists.Select(a => a.name).ToList();
        // Folded key set for case-insensitive match (SQLite IN / lower() are ASCII-only, so
        // compare on the Unicode-folded NameKey instead - see MusicKey).
        var lowerNames = names.Select(n => MusicKey.Of(n)).ToHashSet();

        // Priority 1: music library portraits - Unicode-correct case-insensitive match
        // so "STORMZY" (MV) finds "Stormzy" (music library) etc.
        var musicRows = await _db.Artists
            .Where(a => lowerNames.Contains(a.NameKey))
            .Select(a => new { a.Name, a.ImagePath })
            .ToListAsync();
        var musicImageMap = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in musicRows) musicImageMap.TryAdd(r.Name, r.ImagePath);

        // Priority 2: MV-specific portraits fetched during scan
        var mvRows = await _mvDb.MvArtistImages
            .Where(a => lowerNames.Contains(a.ArtistName.ToLower()) && a.ImagePath != null)
            .Select(a => new { a.ArtistName, a.ImagePath })
            .ToListAsync();
        var mvImageMap = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in mvRows) mvImageMap.TryAdd(r.ArtistName, r.ImagePath);

        var result = artists.Select(a => new {
            a.name,
            a.count,
            imagePath = musicImageMap.TryGetValue(a.name, out var img1) && !string.IsNullOrEmpty(img1) ? img1
                      : mvImageMap.TryGetValue(a.name, out var img2) ? img2
                      : null
        });
        return Ok(result);
    }

    [HttpGet("musicvideos/random")]
    public async Task<IActionResult> GetRandomMusicVideo([FromQuery] string? artist = null)
    {
        var query = _mvDb.MusicVideos.AsQueryable();
        if (!string.IsNullOrWhiteSpace(artist))
            query = query.Where(v => v.Artist.ToLower() == artist.ToLower());
        var count = await query.CountAsync();
        if (count == 0) return NotFound();
        var skip = new Random().Next(count);
        var video = await query.Skip(skip).FirstAsync();
        return Ok(new { video.Id });
    }

    [HttpGet("musicvideos/{id}")]
    public async Task<IActionResult> GetMusicVideo(int id)
    {
        var video = await _mvDb.MusicVideos.FindAsync(id);
        if (video == null) return NotFound();

        // Lazy backfill: rows scanned before AudioCodec existed have it empty. Probe once on
        // demand and persist, so existing libraries populate without a full re-scan.
        if (string.IsNullOrEmpty(video.AudioCodec) && !string.IsNullOrEmpty(video.FilePath) && System.IO.File.Exists(video.FilePath))
        {
            try
            {
                using var doc = await _ffmpeg.ProbeAsync(video.FilePath);
                if (doc != null && doc.RootElement.TryGetProperty("streams", out var streams))
                {
                    foreach (var s in streams.EnumerateArray())
                    {
                        if (s.TryGetProperty("codec_type", out var ct) && ct.GetString() == "audio")
                        {
                            if (s.TryGetProperty("codec_name", out var acn))
                            {
                                video.AudioCodec = acn.GetString() ?? "";
                                if (!string.IsNullOrEmpty(video.AudioCodec)) await _mvDb.SaveChangesAsync();
                            }
                            break;
                        }
                    }
                }
            }
            catch { /* ffprobe unavailable or probe failed - fall through with empty codec */ }
        }

        var isFavourite = _userFavs.IsFavourite(CurrentUsername, "musicvideo", id);
        return Ok(new
        {
            video.Id, video.FileName, video.FilePath, video.Title, video.Artist,
            video.Album, video.Year, video.Duration, video.SizeBytes, video.Format,
            video.Resolution, video.Width, video.Height, video.Codec, video.Bitrate,
            video.Genre, video.ThumbnailPath, video.MoovPosition, video.NeedsOptimization,
            video.Mp4Compliant, video.AudioChannels, video.AudioCodec, video.DateAdded, video.LastModified,
            video.LastPlayed, isFavourite
        });
    }

    [HttpPut("musicvideos/{id}")]
    public async Task<IActionResult> UpdateMusicVideo(int id, [FromBody] JsonElement body)
    {
        var video = await _mvDb.MusicVideos.FindAsync(id);
        if (video == null) return NotFound();
        if (body.TryGetProperty("title", out var t)) video.Title = t.GetString() ?? video.Title;
        if (body.TryGetProperty("artist", out var a)) video.Artist = a.GetString() ?? video.Artist;
        await _mvDb.SaveChangesAsync();
        return Ok(new { video.Id, video.Title, video.Artist });
    }

    // File-level deletion of a music video: deletes the file from disk + its DB row + cached
    // thumbnail. ADMIN-ONLY. If the file can't be deleted the row is KEPT (never orphan an entry).
    [HttpDelete("musicvideos/{id}/file")]
    public async Task<IActionResult> DeleteMusicVideoFile(int id)
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
        {
            _logger.LogWarning("DeleteMusicVideoFile: forbidden - non-admin user '{User}' attempted to delete music video {Id}", CurrentUsername, id);
            return Forbid();
        }

        var mv = await _mvDb.MusicVideos.FindAsync(id);
        if (mv == null) return NotFound();

        var title = mv.Title;
        var filePath = mv.FilePath;
        var (ok, refused, error) = TryDeleteFileWithinRoots(filePath, _config.Config.Library.GetMusicVideosFolderList());
        if (refused) return StatusCode(403, new { success = false, message = "Refused: " + error });
        if (!ok) return StatusCode(500, new { success = false, message = "Could not delete the file on disk: " + error });

        // Best-effort thumbnail cleanup (assets/mvthumbs).
        try
        {
            var thumbDir = Path.Combine(AppContext.BaseDirectory, "assets", "mvthumbs");
            if (!string.IsNullOrWhiteSpace(mv.ThumbnailPath))
            {
                var p = Path.Combine(thumbDir, Path.GetFileName(mv.ThumbnailPath));
                if (System.IO.File.Exists(p)) { try { System.IO.File.Delete(p); } catch { } }
            }
            var byId = Path.Combine(thumbDir, $"mvthumb_{id}.jpg");
            if (System.IO.File.Exists(byId)) { try { System.IO.File.Delete(byId); } catch { } }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DeleteMusicVideoFile: thumbnail cleanup partial for music video {Id}", id);
        }

        _mvDb.MusicVideos.Remove(mv);
        await _mvDb.SaveChangesAsync();

        _logger.LogInformation("Music video id={Id} ('{Title}') DELETED (file + metadata) by {User}. Path='{Path}'", id, title, CurrentUsername, filePath);
        return Ok(new { success = true });
    }

    [HttpGet("stream-musicvideo/{id}")]
    public async Task<IActionResult> StreamMusicVideo(int id)
    {
        var video = await _mvDb.MusicVideos.FindAsync(id);
        if (video == null) return NotFound();
        if (!System.IO.File.Exists(video.FilePath))
            return NotFound("Video file not found on disk");

        // Update global last played + per-user play count
        video.LastPlayed = DateTime.UtcNow;
        await _mvDb.SaveChangesAsync();
        _userFavs.IncrementPlayCount(CurrentUsername, "musicvideo", id);

        // If MP4 compliant, stream directly with byte-range support
        if (video.Mp4Compliant && video.Format.Equals("MP4", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Music video {Id} is MP4 compliant, streaming directly", id);
            var stream = new FileStream(video.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, "video/mp4", enableRangeProcessing: true);
        }

        _logger.LogInformation("Music video {Id} needs remux (Format={Format}, Mp4Compliant={Compliant}, Channels={Ch})",
            id, video.Format, video.Mp4Compliant, video.AudioChannels);

        // Check for cached remux
        var cacheDir = _transcoding.RemuxCachePath;
        var cacheKey = $"mv_{video.Id}_{video.LastModified.Ticks}";
        var cachePath = Path.Combine(cacheDir, $"{cacheKey}.mp4");

        if (System.IO.File.Exists(cachePath))
        {
            _logger.LogDebug("Serving cached remux for music video {Id}: {Path}", id, cachePath);
            var stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, "video/mp4", enableRangeProcessing: true);
        }

        // If FFmpeg available, use TranscodingService for enhanced remux (shared per cache key,
        // cancelled when the client leaves and nobody else waits - see RunSharedRemuxAsync).
        if (_ffmpeg.IsAvailable && _transcoding != null)
        {
            var ready = await RunSharedRemuxAsync(cacheKey, cachePath, video.FilePath,
                video.AudioChannels, 0, $"music video {id}");
            if (ready == null) return new EmptyResult();   // client disconnected
            if (ready == true)
            {
                var stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return File(stream, "video/mp4", enableRangeProcessing: true);
            }
        }
        else if (_ffmpeg.IsAvailable)
        {
            bool success;
            if (video.AudioChannels > 2)
                success = await _ffmpeg.RemuxStereoDownmixAsync(video.FilePath, cachePath);
            else
                success = await _ffmpeg.RemuxFaststartAsync(video.FilePath, cachePath);

            if (success && System.IO.File.Exists(cachePath))
            {
                var stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return File(stream, "video/mp4", enableRangeProcessing: true);
            }
        }
        else
        {
            _logger.LogWarning("FFmpeg not available, cannot remux music video {Id}", id);
        }

        // Fallback: direct stream (may not play in browser)
        _logger.LogWarning("Falling back to direct stream for music video {Id}", id);
        var fallbackMime = video.Format.ToUpperInvariant() switch
        {
            "MKV" => "video/x-matroska",
            "AVI" => "video/x-msvideo",
            "WEBM" => "video/webm",
            "MOV" => "video/quicktime",
            _ => "video/mp4"
        };
        var fallbackStream = new FileStream(video.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(fallbackStream, fallbackMime, enableRangeProcessing: true);
    }

    // ─── Music Videos Scanning ────────────────────────────────────────

    [HttpPost("scan/musicvideos")]
    public IActionResult StartMusicVideoScan()
    {
        if (_mvScanner.IsScanning)
            return Conflict(new { message = "Music videos scan already in progress" });
        _ = _mvScanner.StartScanAsync();
        _semantic.RequestAutoIndex();
        return Ok(new { message = "Music videos scan started" });
    }

    [HttpGet("scan/musicvideos/status")]
    public IActionResult GetMusicVideoScanStatus()
    {
        var p = _mvScanner.CurrentProgress;
        return Ok(new
        {
            p.Status, p.Message, p.TotalFiles, p.ProcessedFiles,
            p.NewVideos, p.UpdatedVideos, p.ErrorCount,
            p.PercentComplete, p.StartTime,
            isScanning = _mvScanner.IsScanning
        });
    }

    [HttpPost("musicvideo/generate-thumbnails")]
    public IActionResult GenerateMvThumbnails()
    {
        _ = _mvScanner.GenerateAllThumbnailsAsync();
        return Ok(new { message = "Thumbnail generation started" });
    }

    [HttpPost("musicvideo/analyze-mp4s")]
    public IActionResult AnalyzeMvMp4s()
    {
        _ = _mvScanner.AnalyzeMp4ComplianceAsync();
        return Ok(new { message = "MP4 analysis started" });
    }

    [HttpPost("musicvideo/fix-mp4/{id}")]
    public async Task<IActionResult> FixMp4(int id)
    {
        var success = await _mvScanner.FixMp4Async(id);
        return success ? Ok(new { message = "MP4 fixed successfully" })
                       : BadRequest(new { message = "Failed to fix MP4" });
    }

    [HttpPost("musicvideo/fix-all-mp4s")]
    public async Task<IActionResult> FixAllMp4s()
    {
        var videos = await _mvDb.MusicVideos
            .Where(v => v.NeedsOptimization)
            .Select(v => v.Id).ToListAsync();

        int fixed_ = 0, failed = 0;
        foreach (var id in videos)
        {
            var success = await _mvScanner.FixMp4Async(id);
            if (success) fixed_++; else failed++;
        }
        return Ok(new { message = $"Fixed {fixed_}, failed {failed}", fixedCount = fixed_, failedCount = failed });
    }

    [HttpPost("musicvideo/clear-remux-cache")]
    public IActionResult ClearRemuxCache()
    {
        var cacheDir = _transcoding.RemuxCachePath;
        if (!Directory.Exists(cacheDir))
            return Ok(new { message = "Cache directory does not exist", freed = 0 });

        long totalSize = 0;
        int count = 0;
        foreach (var file in Directory.GetFiles(cacheDir))
        {
            totalSize += new FileInfo(file).Length;
            System.IO.File.Delete(file);
            count++;
        }
        return Ok(new { message = $"Cleared {count} cached files", freed = totalSize, count });
    }

    [HttpGet("musicvideo/remux-cache-stats")]
    public IActionResult GetRemuxCacheStats()
    {
        var cacheDir = _transcoding.RemuxCachePath;
        if (!Directory.Exists(cacheDir))
            return Ok(new { totalSize = 0L, fileCount = 0 });

        var files = Directory.GetFiles(cacheDir);
        return Ok(new
        {
            totalSize = files.Sum(f => new FileInfo(f).Length),
            fileCount = files.Length
        });
    }

    // ─── Videos (Movies/TV Shows) ─────────────────────────────────────

    [HttpGet("videos")]
    public async Task<IActionResult> GetVideos(
        [FromQuery] string? mediaType = null,
        [FromQuery] string? genre = null,
        [FromQuery] string? series = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = "recent",
        [FromQuery] bool grouped = false,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 100,
        [FromQuery] string? customCategory = null,
        [FromQuery] string? customGenreId = null,
        [FromQuery] bool hideWatched = false,
        [FromQuery] string? quality = null,
        [FromQuery] string? videoKind = null)
    {
        var query = _videoDb.Videos.AsQueryable();

        // DVD filter: "dvd" = only preserved DVD-Video discs; "file" = only ordinary videos
        // (exclude discs). Used by the Movies "DVDs" tile. Null/empty = no filter.
        if (!string.IsNullOrWhiteSpace(videoKind))
        {
            if (videoKind == "dvd") query = query.Where(v => v.VideoKind == "dvd");
            else if (videoKind == "file") query = query.Where(v => v.VideoKind != "dvd");
        }

        // ── Child user content filter ─────────────────────────────────────────
        var currentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (currentRole == "child")
        {
            // Read this child's age-rating permissions from their saved settings
            var childSettingsJson = await _usersDb.Users.AsNoTracking()
                .Where(u => u.Username == CurrentUsername)
                .Select(u => u.ChildSettings)
                .FirstOrDefaultAsync() ?? "";

            bool allowPG13 = false;
            bool allowTV14 = false;
            if (!string.IsNullOrEmpty(childSettingsJson))
            {
                try
                {
                    var cs = System.Text.Json.JsonDocument.Parse(childSettingsJson).RootElement;
                    allowPG13 = cs.TryGetProperty("allowPG13", out var p13) && p13.GetBoolean();
                    allowTV14 = cs.TryGetProperty("allowTV14", out var t14) && t14.GetBoolean();
                }
                catch { }
            }

            query = query.Where(v =>
                v.SafeForChildren ||
                v.ContentRating == "G" || v.ContentRating == "PG" || v.ContentRating == "PG-12" ||
                (allowPG13 && v.ContentRating == "PG-13") ||
                v.ContentRating == "TV-G" || v.ContentRating == "TV-PG" ||
                v.ContentRating == "TV-Y" || v.ContentRating == "TV-Y7" ||
                (allowTV14 && v.ContentRating == "TV-14") ||
                v.ContentRating == "U" || v.ContentRating == "K" || v.ContentRating == "ALL");
        }
        // ─────────────────────────────────────────────────────────────────────

        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            var types = mediaType.ToLower().Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
            if (types.Count == 1)
                query = query.Where(v => v.MediaType == types[0]);
            else
                query = query.Where(v => types.Contains(v.MediaType));
        }
        else
            query = query.Where(v => v.MediaType != "anime"); // anime has its own dedicated section

        if (!string.IsNullOrWhiteSpace(customCategory))
        {
            query = query.Where(v => v.CustomCategory.ToLower() == customCategory.ToLower());
        }
        else if (!string.IsNullOrWhiteSpace(customGenreId))
        {
            string cgRulesJson = "[]";
            var directIds = new HashSet<int>();
            using (var conn = OpenVideoDbRaw())
            {
                using var cmdR = conn.CreateCommand();
                cmdR.CommandText = "SELECT Rules FROM CustomGenres WHERE Id = @id LIMIT 1";
                cmdR.Parameters.AddWithValue("@id", customGenreId);
                cgRulesJson = cmdR.ExecuteScalar()?.ToString() ?? "[]";

                using var cmdD = conn.CreateCommand();
                cmdD.CommandText = "SELECT VideoId FROM CustomGenreItems WHERE GenreId = @id";
                cmdD.Parameters.AddWithValue("@id", customGenreId);
                using var rD = cmdD.ExecuteReader();
                while (rD.Read()) directIds.Add(rD.GetInt32(0));
            }
            var cgRules = TryDeserializeRules(cgRulesJson);
            var genreVals = cgRules.Where(r => r.Type == "genre").Select(r => r.Value).ToList();
            var folderVals = cgRules.Where(r => r.Type == "folder").Select(r => r.Value).ToList();
            HashSet<int> ruleIds;
            if (genreVals.Count > 0 || folderVals.Count > 0)
            {
                ruleIds = (await _videoDb.Videos
                    .Select(v => new { v.Id, Genre = v.Genre ?? "", CustomCategory = v.CustomCategory ?? "" })
                    .ToListAsync())
                    .Where(v =>
                        genreVals.Any(gv => v.Genre.Contains(gv, StringComparison.OrdinalIgnoreCase)) ||
                        folderVals.Any(fv => v.CustomCategory.Equals(fv, StringComparison.OrdinalIgnoreCase)))
                    .Select(v => v.Id)
                    .ToHashSet();
            }
            else ruleIds = new HashSet<int>();

            var allMatchIds = ruleIds.Union(directIds).ToHashSet();
            if (allMatchIds.Count > 0)
                query = query.Where(v => allMatchIds.Contains(v.Id));
            else
                query = query.Where(v => false);
        }
        else
        {
            var videoHidden = _userFavs.GetVideoCategoryHidden(CurrentUsername);
            if (videoHidden.Count > 0)
                query = query.Where(v => !videoHidden.Contains(v.CustomCategory));
        }

        // Snapshot the query BEFORE the quality filter (section filters only: role +
        // mediaType + custom-category). The Quality menu's per-bucket counts are computed
        // from this, so every bucket shows its full total regardless of which one is active.
        var qualFacetQuery = query;

        // Video-quality filter (folded into the Sort dropdown, remembered per section).
        // Buckets map to Video.Height. Applied BEFORE queryBeforeGenre so the grouped
        // TV-with-genre path (which re-reads queryBeforeGenre) stays consistent. A series
        // appears when at least one of its episodes matches the chosen quality; its
        // episode/size/duration totals then reflect only the matching-quality episodes.
        // Height == 0 (not yet analysed) is treated as "unknown" and excluded from every
        // bucket, so it only shows under "All qualities".
        if (!string.IsNullOrWhiteSpace(quality))
        {
            switch (quality.Trim().ToLowerInvariant())
            {
                case "4k": query = query.Where(v => v.Height >= 2160); break;
                case "hd": query = query.Where(v => v.Height >= 720 && v.Height < 2160); break;
                case "sd": query = query.Where(v => v.Height > 0 && v.Height < 720); break;
                // "all" or anything unrecognised: no filter
            }
        }

        // Save query before genre filter so grouped TV lookup can fetch all episodes of matching series
        var queryBeforeGenre = query;

        if (!string.IsNullOrWhiteSpace(genre))
        {
            // Normalize the requested genre so "Science Fiction" and "Sci-Fi & Fantasy" hit the same bucket.
            // We do the split+normalize in memory and filter by ID so EF Core doesn't need to translate it.
            var canonicalGenre = MetadataService.NormalizeGenreToken(genre.Trim());
            var allGenreRows = await _videoDb.Videos
                .Where(v => v.Genre != null && v.Genre != "")
                .Select(v => new { v.Id, v.Genre })
                .ToListAsync();
            var matchingIds = allGenreRows
                .Where(v => v.Genre!
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(MetadataService.NormalizeGenreToken)
                    .Contains(canonicalGenre, StringComparer.OrdinalIgnoreCase))
                .Select(v => v.Id)
                .ToHashSet();

            // "Documentary" genre also includes all videos the user has classified as MediaType="documentary",
            // but only when the request scope includes documentaries (TV/Docs page or no mediaType filter).
            // On the Movies page (mediaType=movie), documentary-typed items are excluded - they live in
            // the TV Shows/Docs section and should not bleed into the movie grid.
            bool scopeIncludesDocs = string.IsNullOrWhiteSpace(mediaType) ||
                mediaType.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                         .Any(t => t.Equals("documentary", StringComparison.OrdinalIgnoreCase));
            if (scopeIncludesDocs && canonicalGenre.Equals("Documentary", StringComparison.OrdinalIgnoreCase))
            {
                var docIds = await _videoDb.Videos
                    .Where(v => v.MediaType == "documentary")
                    .Select(v => v.Id)
                    .ToListAsync();
                foreach (var did in docIds) matchingIds.Add(did);
            }

            query = query.Where(v => matchingIds.Contains(v.Id));
        }
        if (!string.IsNullOrWhiteSpace(series))
            query = query.Where(v => v.SeriesName.ToLower() == series.ToLower());
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(v => v.Title.ToLower().Contains(s)
                || v.SeriesName.ToLower().Contains(s)
                || v.FileName.ToLower().Contains(s)
                || v.Director.ToLower().Contains(s)
                || v.Cast.ToLower().Contains(s));
        }

        // Grouped mode: return movies as-is + one entry per TV series + one entry per Anime series
        if (grouped && string.IsNullOrWhiteSpace(series))
        {
            // Get movies / standalone items (exclude TV episodes and anime episodes that have a series name)
            // TV items with no SeriesName (e.g. documentaries re-classified as "tv") are also included
            // here so they never fall through the cracks and disappear from all views.
            var movieQuery = query.Where(v =>
                (v.MediaType != "tv"
                    && !(v.MediaType == "anime" && v.SeriesName != null && v.SeriesName != "")
                    && !(v.MediaType == "documentary" && v.SeriesName != null && v.SeriesName != ""))
                || (v.MediaType == "tv" && (v.SeriesName == null || v.SeriesName == "")));

            // For TV series with an active genre filter: find series where ANY episode has the genre,
            // then load ALL episodes of those series (not just genre-matching ones). This ensures that
            // series with incomplete genre metadata (only some episodes tagged) still appear in full.
            IQueryable<Video> tvQuery;
            if (!string.IsNullOrWhiteSpace(genre))
            {
                var matchingSeriesNames = await query
                    .Where(v => v.MediaType == "tv" && v.SeriesName != null && v.SeriesName != "")
                    .Select(v => v.SeriesName)
                    .Distinct()
                    .ToListAsync();
                tvQuery = queryBeforeGenre
                    .Where(v => v.MediaType == "tv" && v.SeriesName != null && v.SeriesName != ""
                             && matchingSeriesNames.Contains(v.SeriesName));
            }
            else
            {
                tvQuery = query.Where(v => v.MediaType == "tv" && v.SeriesName != null && v.SeriesName != "");
            }

            var animeSeriesQuery = query.Where(v => v.MediaType == "anime" && v.SeriesName != null && v.SeriesName != "");
            // Documentaries that carry a SeriesName group into one series card (like TV),
            // instead of fragmenting into one card per episode. Docs with no SeriesName
            // stay standalone via movieQuery above.
            var docSeriesQuery = query.Where(v => v.MediaType == "documentary" && v.SeriesName != null && v.SeriesName != "");

            var vidFavIds = _userFavs.GetFavouriteIds(CurrentUsername, "video");
            var vidWatchedIds = _userFavs.GetWatchedIds(CurrentUsername);

            // ── Series summaries ─────────────────────────────────────────────────
            // One light row per episode (ids, numbers, short paths - no Overview / Genre /
            // ContentRating text), grouped in memory case-insensitively exactly as before.
            // The three text fields are filled further down, only for the series that land
            // on the requested page.
            var seriesGroups      = await BuildSeriesGroupsAsync(tvQuery, vidWatchedIds);
            var animeSeriesGroups = await BuildSeriesGroupsAsync(animeSeriesQuery, vidWatchedIds);
            var docSeriesGroups   = await BuildSeriesGroupsAsync(docSeriesQuery, vidWatchedIds);

            // ── Movies: sort keys only ───────────────────────────────────────────
            // Id plus the five sortable columns. The full rows (Overview, Cast, ...) are
            // fetched below for the page that is returned, never for the whole library.
            var movieKeys = await movieQuery
                .Select(v => new GroupedSortKey
                {
                    Kind = 0, Id = v.Id, Title = v.Title, Year = v.Year,
                    Size = v.SizeBytes, Duration = v.Duration, Date = v.DateAdded
                })
                .ToListAsync();

            // ── Merge (same insertion order as before: movies, TV, anime, docs) ──
            var keys = new List<GroupedSortKey>(movieKeys.Count + seriesGroups.Count
                                                + animeSeriesGroups.Count + docSeriesGroups.Count);
            foreach (var mk in movieKeys)
            {
                if (hideWatched && vidWatchedIds.Contains(mk.Id)) continue;
                keys.Add(mk);
            }
            AddSeriesSortKeys(keys, seriesGroups,      1, hideWatched);
            AddSeriesSortKeys(keys, animeSeriesGroups, 2, hideWatched);
            AddSeriesSortKeys(keys, docSeriesGroups,   3, hideWatched);

            // ── Sort + page ──────────────────────────────────────────────────────
            // Identical orders and tie behaviour to the previous implementation: LINQ
            // OrderBy is stable and uses the default (current-culture) string comparer,
            // and the insertion order above breaks ties. Typed keys instead of reflection.
            IEnumerable<GroupedSortKey> ordered = sort switch
            {
                "title"    => keys.OrderBy(k => k.Title),
                "year"     => keys.OrderByDescending(k => k.Year).ThenBy(k => k.Title),
                "size"     => keys.OrderByDescending(k => k.Size),
                "duration" => keys.OrderByDescending(k => k.Duration),
                _          => keys.OrderByDescending(k => k.Date)
            };
            var total = keys.Count;
            var pageKeys = ordered.Skip((page - 1) * limit).Take(limit).ToList();

            // ── Full movie rows for this page only ───────────────────────────────
            var pageMovieIds = pageKeys.Where(k => k.Kind == 0).Select(k => k.Id).ToList();
            var movieRowList = pageMovieIds.Count == 0
                ? new()
                : await _videoDb.Videos
                    .Where(v => pageMovieIds.Contains(v.Id))
                    .Select(v => new
                    {
                        v.Id, v.FileName, v.Title, v.Year, v.Duration, v.SizeBytes,
                        v.Format, v.Resolution, v.Width, v.Height, v.Codec, v.HdrFormat,
                        v.AudioCodec, v.AudioChannels, v.AudioLanguages, v.SubtitleLanguages,
                        v.Genre, v.Director, v.Cast, v.Overview, v.Rating, v.ContentRating, v.SafeForChildren,
                        v.MediaType, v.VideoKind, v.SeriesName, v.Season, v.Episode,
                        v.ThumbnailPath, v.PosterPath, v.BackdropPath,
                        v.Mp4Compliant, v.NeedsOptimization, v.DateAdded
                    })
                    .ToListAsync();
            var movieRows = movieRowList.ToDictionary(r => r.Id);

            // ── Genre / Overview / ContentRating for the series on this page only ─
            await FillSeriesTextAsync(tvQuery,          pageKeys.Where(k => k.Kind == 1).Select(k => k.Series!));
            await FillSeriesTextAsync(animeSeriesQuery, pageKeys.Where(k => k.Kind == 2).Select(k => k.Series!));
            await FillSeriesTextAsync(docSeriesQuery,   pageKeys.Where(k => k.Kind == 3).Select(k => k.Series!));

            // ── Output objects (same shapes and property names as before) ────────
            var paged = new List<object>(pageKeys.Count);
            foreach (var k in pageKeys)
            {
                if (k.Kind == 0)
                {
                    if (!movieRows.TryGetValue(k.Id, out var m)) continue; // removed between the two queries
                    paged.Add(new
                    {
                        type = "video",
                        m.Id, m.FileName, m.Title, m.Year, m.Duration, m.SizeBytes,
                        m.Format, m.Resolution, m.Width, m.Height, m.Codec, m.HdrFormat,
                        m.AudioCodec, m.AudioChannels, m.AudioLanguages, m.SubtitleLanguages,
                        m.Genre, m.Director, m.Cast, m.Overview, m.Rating, m.ContentRating, m.SafeForChildren,
                        m.MediaType, m.VideoKind, m.SeriesName, m.Season, m.Episode,
                        m.ThumbnailPath, m.PosterPath, m.BackdropPath,
                        m.Mp4Compliant, m.NeedsOptimization,
                        IsFavourite = vidFavIds.Contains(m.Id),
                        IsWatched = vidWatchedIds.Contains(m.Id), m.DateAdded
                    });
                }
                else
                {
                    var s2 = k.Series!;
                    paged.Add(new
                    {
                        type = "series",
                        id = s2.FirstEpisodeId,
                        seriesName = s2.SeriesName,
                        episodeCount = s2.EpisodeCount,
                        seasonCount = s2.SeasonCount,
                        duration = s2.TotalDuration,
                        sizeBytes = s2.TotalSize,
                        thumbnailPath = s2.ThumbnailPath ?? "",
                        posterPath = s2.PosterPath ?? "",
                        backdropPath = s2.BackdropPath ?? "",
                        rating = s2.Rating,
                        genre = s2.Genre,
                        overview = s2.Overview,
                        contentRating = s2.ContentRating,
                        height = s2.Height,
                        hdrFormat = s2.HdrFormat ?? "",
                        year = s2.LatestYear,
                        dateAdded = s2.LatestAdded,
                        mediaType = k.Kind == 1 ? "tv" : k.Kind == 2 ? "anime" : "documentary",
                        allWatched = s2.AllWatched
                    });
                }
            }

            // Per-quality counts for the Quality menu, computed from the pre-quality
            // section query (ignores genre/watched - it is a library-inventory hint).
            // Series (tv/anime/doc with a SeriesName) count as ONE item and contribute
            // to every bucket they have an episode in, matching the "any episode" filter.
            var qualFacetRows = await qualFacetQuery
                .Select(v => new { v.MediaType, v.SeriesName, v.Height })
                .ToListAsync();
            int fUhd = 0, fHd = 0, fSd = 0, fAll = 0;
            var facetSeries = new Dictionary<string, (bool u, bool h, bool s)>();
            foreach (var r in qualFacetRows)
            {
                int b = r.Height >= 2160 ? 3 : r.Height >= 720 ? 2 : r.Height > 0 ? 1 : 0;
                bool isSeries = (r.MediaType == "tv" || r.MediaType == "anime" || r.MediaType == "documentary")
                                && !string.IsNullOrEmpty(r.SeriesName);
                if (isSeries)
                {
                    var key = r.MediaType + "|" + r.SeriesName!.Trim().ToLowerInvariant();
                    facetSeries.TryGetValue(key, out var cur);
                    facetSeries[key] = (cur.u || b == 3, cur.h || b == 2, cur.s || b == 1);
                }
                else
                {
                    fAll++;
                    if (b == 3) fUhd++; else if (b == 2) fHd++; else if (b == 1) fSd++;
                }
            }
            foreach (var s in facetSeries.Values)
            {
                fAll++;
                if (s.u) fUhd++;
                if (s.h) fHd++;
                if (s.s) fSd++;
            }
            var qualityCounts = new { all = fAll, uhd = fUhd, hd = fHd, sd = fSd };

            return Ok(new { total, page, limit, videos = paged, qualityCounts });
        }

        // Non-grouped mode (original behaviour)
        query = sort switch
        {
            "title" => query.OrderBy(v => v.Title),
            "year" => query.OrderByDescending(v => v.Year).ThenBy(v => v.Title),
            "size" => query.OrderByDescending(v => v.SizeBytes),
            "duration" => query.OrderByDescending(v => v.Duration),
            "series" => query.OrderBy(v => v.SeriesName).ThenBy(v => v.Season).ThenBy(v => v.Episode),
            _ => query.OrderByDescending(v => v.DateAdded)
        };

        var totalUngrouped = await query.CountAsync();
        var vidFavIdsU = _userFavs.GetFavouriteIds(CurrentUsername, "video");
        var vidWatchedIdsU = _userFavs.GetWatchedIds(CurrentUsername);
        var videos = await query.Skip((page - 1) * limit).Take(limit)
            .Select(v => new
            {
                type = "video",
                v.Id, v.FileName, v.Title, v.Year, v.Duration, v.SizeBytes,
                v.Format, v.Resolution, v.Width, v.Height, v.Codec, v.HdrFormat,
                v.AudioCodec, v.AudioChannels, v.AudioLanguages, v.SubtitleLanguages,
                v.Genre, v.Director, v.Cast, v.Overview, v.Rating, v.ContentRating, v.SafeForChildren,
                v.MetadataLanguage,
                v.MediaType, v.VideoKind, v.SeriesName, v.Season, v.Episode,
                v.ThumbnailPath, v.PosterPath, v.BackdropPath, v.CastJson, v.DirectorJson, v.WriterJson, v.StudiosJson,
                v.Mp4Compliant, v.NeedsOptimization,
                IsFavourite = vidFavIdsU.Contains(v.Id),
                IsWatched = vidWatchedIdsU.Contains(v.Id), v.DateAdded
            }).ToListAsync();

        // Per-video resume position, keyed by video id. Sent as a sibling map
        // rather than folded into each item so existing callers are unaffected.
        // Only ids present in this page are included.
        var progress = _userFavs
            .GetVideoProgressMap(CurrentUsername, videos.Select(v => v.Id).ToHashSet())
            .ToDictionary(
                kv => kv.Key.ToString(),
                kv => new { position = kv.Value.Position, duration = kv.Value.Duration, percent = kv.Value.Percent });

        return Ok(new { total = totalUngrouped, page, limit, videos, progress });
    }

    // ─── Grouped video listing helpers (typed, no reflection) ─────────────────

    /// <summary>One sortable entry of the merged movies + series list.</summary>
    private sealed class GroupedSortKey
    {
        public int Kind { get; set; }           // 0 movie, 1 tv series, 2 anime series, 3 documentary series
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public int? Year { get; set; }
        public long Size { get; set; }
        public double Duration { get; set; }
        public DateTime Date { get; set; }
        public SeriesGroup? Series { get; set; }
    }

    /// <summary>A series card: aggregates over its (filtered) episodes.</summary>
    private sealed class SeriesGroup
    {
        public string Key { get; set; } = "";               // trimmed lower-case series name
        public string SeriesName { get; set; } = "";
        public string[] NameVariants { get; set; } = Array.Empty<string>(); // exact spellings seen in the DB
        public int EpisodeCount { get; set; }
        public int SeasonCount { get; set; }
        public double TotalDuration { get; set; }
        public long TotalSize { get; set; }
        public DateTime LatestAdded { get; set; }
        public int? LatestYear { get; set; }
        public string? ThumbnailPath { get; set; }
        public string? PosterPath { get; set; }
        public string? BackdropPath { get; set; }
        public double Rating { get; set; }
        public string Genre { get; set; } = "";              // filled per page by FillSeriesTextAsync
        public string Overview { get; set; } = "";
        public string ContentRating { get; set; } = "";
        public int FirstEpisodeId { get; set; }
        public int Height { get; set; }
        public string? HdrFormat { get; set; }
        public bool AllWatched { get; set; }
    }

    /// <summary>
    /// Groups the episodes of <paramref name="src"/> into series cards. Reads only the light
    /// columns (no Overview / Genre / ContentRating). Grouping is in memory and
    /// case-insensitive so "GAME OF THRONES" and "Game of Thrones" are one series, preferring
    /// a mixed-case spelling for the display name - unchanged behaviour.
    /// </summary>
    private static async Task<List<SeriesGroup>> BuildSeriesGroupsAsync(IQueryable<Video> src, HashSet<int> watchedIds)
    {
        var raw = await src
            .Select(v => new { v.Id, v.SeriesName, v.Season, v.Episode, v.Duration, v.SizeBytes,
                v.DateAdded, v.Year, v.ThumbnailPath, v.PosterPath, v.BackdropPath,
                v.Rating, v.Height, v.HdrFormat })
            .ToListAsync();

        return raw
            .GroupBy(v => (v.SeriesName ?? "").Trim().ToLowerInvariant())
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g =>
            {
                var bestName = g.Select(v => v.SeriesName ?? "")
                    .Where(n => !string.IsNullOrEmpty(n))
                    .OrderBy(n => n.Any(char.IsLower) ? 0 : 1)
                    .First();
                return new SeriesGroup
                {
                    Key           = g.Key,
                    SeriesName    = bestName,
                    NameVariants  = g.Select(v => v.SeriesName ?? "").Where(n => n.Length > 0).Distinct().ToArray(),
                    EpisodeCount  = g.Count(),
                    SeasonCount   = g.Select(v => v.Season).Distinct().Count(),
                    TotalDuration = g.Sum(v => v.Duration),
                    TotalSize     = g.Sum(v => v.SizeBytes),
                    LatestAdded   = g.Max(v => v.DateAdded),
                    LatestYear    = g.Max(v => v.Year),
                    ThumbnailPath = g.Where(v => !string.IsNullOrEmpty(v.ThumbnailPath))
                        .OrderBy(v => v.Season).ThenBy(v => v.Episode)
                        .Select(v => v.ThumbnailPath).FirstOrDefault(),
                    PosterPath    = g.Where(v => !string.IsNullOrEmpty(v.PosterPath))
                        .OrderBy(v => v.Season).ThenBy(v => v.Episode)
                        .Select(v => v.PosterPath).FirstOrDefault(),
                    BackdropPath  = g.Where(v => !string.IsNullOrEmpty(v.BackdropPath))
                        .OrderBy(v => v.Season).ThenBy(v => v.Episode)
                        .Select(v => v.BackdropPath).FirstOrDefault(),
                    Rating        = g.Max(v => v.Rating),
                    FirstEpisodeId = g.OrderBy(v => v.Season).ThenBy(v => v.Episode).Select(v => v.Id).FirstOrDefault(),
                    Height        = g.Max(v => v.Height),
                    HdrFormat     = g.Where(v => !string.IsNullOrEmpty(v.HdrFormat)).Select(v => v.HdrFormat).FirstOrDefault(),
                    // Dim the poster like a watched movie only when EVERY local episode is watched.
                    AllWatched    = g.All(v => watchedIds.Contains(v.Id))
                };
            })
            .ToList();
    }

    private static void AddSeriesSortKeys(List<GroupedSortKey> keys, List<SeriesGroup> groups, int kind, bool hideWatched)
    {
        foreach (var g in groups)
        {
            if (hideWatched && g.AllWatched) continue;
            keys.Add(new GroupedSortKey
            {
                Kind = kind, Id = g.FirstEpisodeId, Title = g.SeriesName, Year = g.LatestYear,
                Size = g.TotalSize, Duration = g.TotalDuration, Date = g.LatestAdded, Series = g
            });
        }
    }

    /// <summary>
    /// Fills Genre / Overview / ContentRating for the given series cards: the first non-empty
    /// value among the series' episodes (lowest Id first), read from the same filtered query
    /// the cards were built from. Matched on the exact spellings seen while grouping, so the
    /// SQL side needs no case folding or trimming.
    /// </summary>
    private static async Task FillSeriesTextAsync(IQueryable<Video> src, IEnumerable<SeriesGroup> groups)
    {
        var byKey = new Dictionary<string, SeriesGroup>();
        foreach (var g in groups) byKey[g.Key] = g;
        if (byKey.Count == 0) return;

        var names = byKey.Values.SelectMany(g => g.NameVariants).Distinct().ToList();
        var rows = await src
            .Where(v => names.Contains(v.SeriesName)
                        && (v.Genre != "" || v.Overview != "" || v.ContentRating != ""))
            .OrderBy(v => v.Id)
            .Select(v => new { v.SeriesName, v.Genre, v.Overview, v.ContentRating })
            .ToListAsync();

        foreach (var r in rows)
        {
            if (!byKey.TryGetValue((r.SeriesName ?? "").Trim().ToLowerInvariant(), out var g)) continue;
            if (g.Genre.Length == 0 && !string.IsNullOrEmpty(r.Genre)) g.Genre = r.Genre;
            if (g.Overview.Length == 0 && !string.IsNullOrEmpty(r.Overview)) g.Overview = r.Overview;
            if (g.ContentRating.Length == 0 && !string.IsNullOrEmpty(r.ContentRating)) g.ContentRating = r.ContentRating;
        }
    }



    [HttpGet("videos/stats")]
    public async Task<IActionResult> GetVideoStats()
    {
        var stats = new
        {
            totalVideos = await _videoDb.Videos.CountAsync(v => v.MediaType != "anime"),
            totalAnime = await _videoDb.Videos.CountAsync(v => v.MediaType == "anime"),
            totalMovies = await _videoDb.Videos.CountAsync(v => v.MediaType == "movie"),
            totalTvEpisodes = await _videoDb.Videos.CountAsync(v => v.MediaType == "tv"),
            totalDocumentaries = await _videoDb.Videos.CountAsync(v => v.MediaType == "documentary"),
            totalSeries = await _videoDb.Videos.Where(v => v.SeriesName != "" && v.MediaType != "anime")
                .Select(v => v.SeriesName).Distinct().CountAsync(),
            totalTvSeries = await _videoDb.Videos.Where(v => v.SeriesName != "" && v.MediaType == "tv")
                .Select(v => v.SeriesName).Distinct().CountAsync(),
            totalSize = await _videoDb.Videos.SumAsync(v => v.SizeBytes),
            totalDuration = await _videoDb.Videos.SumAsync(v => v.Duration),
            needsOptimization = await _videoDb.Videos.CountAsync(v => v.NeedsOptimization),
            movieTotalSize = await _videoDb.Videos.Where(v => v.MediaType == "movie").SumAsync(v => v.SizeBytes),
            movieTotalDuration = await _videoDb.Videos.Where(v => v.MediaType == "movie").SumAsync(v => v.Duration),
            movieNeedsOptimization = await _videoDb.Videos.CountAsync(v => v.NeedsOptimization && v.MediaType == "movie"),
            tvTotalSize = await _videoDb.Videos.Where(v => v.MediaType == "tv" || v.MediaType == "documentary").SumAsync(v => v.SizeBytes),
            tvTotalDuration = await _videoDb.Videos.Where(v => v.MediaType == "tv" || v.MediaType == "documentary").SumAsync(v => v.Duration),
            tvNeedsOptimization = await _videoDb.Videos.CountAsync(v => v.NeedsOptimization && (v.MediaType == "tv" || v.MediaType == "documentary")),
            withThumbnails = await _videoDb.Videos.CountAsync(v => v.ThumbnailPath != null && v.ThumbnailPath != "")
        };
        return Ok(stats);
    }

    // ─── Per-user preferences (self-scoped, NOT admin-gated) ─────────────
    //
    // These live in the signed-in user's OWN database (the UserPrefs key/value
    // table), so every user gets their own value and it survives a restart.
    // Deliberately keyed off CurrentUsername rather than taking a username
    // parameter - unlike the admin-only category-settings/user/{username}
    // endpoints, there is no way to read or write another user's preferences here.

    /// <summary>Themes a user may pick. Anything else is rejected - the value is
    /// echoed into a body CSS class, so it must never be free text.</summary>
    private static readonly HashSet<string> AllowedThemes = new(StringComparer.OrdinalIgnoreCase)
        { "dark", "blue", "purple", "emerald", "sky-grey", "sky-blue" };

    /// <summary>User's own theme if they picked one and it is still valid, else the
    /// server default. Never throws - a missing/corrupt user DB must not break config.</summary>
    private string ResolveUserTheme(string serverTheme)
    {
        try
        {
            var theme = _userFavs.GetUserPref(CurrentUsername, "ui_theme");
            return !string.IsNullOrEmpty(theme) && AllowedThemes.Contains(theme) ? theme : serverTheme;
        }
        catch { return serverTheme; }
    }

    /// <summary>
    /// True when <paramref name="id"/> names a real template folder. The id is
    /// interpolated straight into "/templates/{id}/style.css" by the frontend, so it
    /// MUST be matched against the actual directory listing - never merely sanitised.
    /// Empty means "no template" (the built-in default) and is always valid.
    /// </summary>
    private bool IsValidTemplateId(string? id)
    {
        if (string.IsNullOrEmpty(id)) return true;
        try
        {
            var templatesDir = Path.Combine(_env.WebRootPath, "templates");
            if (!Directory.Exists(templatesDir)) return false;
            return Directory.GetDirectories(templatesDir)
                .Any(d => string.Equals(Path.GetFileName(d), id, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    /// <summary>User's own template if still installed, else the server default. A
    /// template the admin has since deleted falls back rather than 404-ing its assets.</summary>
    private string ResolveUserTemplate(string serverTemplate)
    {
        try
        {
            var tpl = _userFavs.GetUserPref(CurrentUsername, "ui_template");
            if (tpl == null) return serverTemplate;           // never chosen → server default
            return IsValidTemplateId(tpl) ? tpl : serverTemplate; // "" = explicit default
        }
        catch { return serverTemplate; }
    }

    // ── Per-user library view modes (Poster/Banner/List/Table/Recommended) ──
    // Persisted per user in UserPrefs so the chosen view (including "Recommended")
    // follows the account across logins and devices, not just the browser's
    // localStorage. Seeded into the client at login via config/info (below).
    private static readonly HashSet<string> AllowedViewModes = new(StringComparer.OrdinalIgnoreCase)
        { "poster", "banner", "list", "table", "recommended" };
    private static readonly string[] ViewModeSections = { "home", "movies", "tvshows", "musicvideos", "inprogress" };

    /// <summary>This user's saved view mode per section (only valid, explicitly-chosen ones).</summary>
    private Dictionary<string, string> ResolveUserViewModes()
    {
        var map = new Dictionary<string, string>();
        try
        {
            foreach (var s in ViewModeSections)
            {
                var v = _userFavs.GetUserPref(CurrentUsername, "vidview_" + s);
                if (!string.IsNullOrEmpty(v) && AllowedViewModes.Contains(v)) map[s] = v.ToLowerInvariant();
            }
        }
        catch { }
        return map;
    }

    [HttpPost("me/view-mode")]
    public IActionResult SaveMyViewMode([FromBody] JsonElement body)
    {
        var section = body.TryGetProperty("section", out var se) ? (se.GetString() ?? "") : "";
        var mode = body.TryGetProperty("mode", out var mo) ? (mo.GetString() ?? "") : "";
        if (!ViewModeSections.Contains(section, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = "Unknown section" });
        if (!AllowedViewModes.Contains(mode))
            return BadRequest(new { error = "Unknown mode" });
        _userFavs.SetUserPref(CurrentUsername, "vidview_" + section.ToLowerInvariant(), mode.ToLowerInvariant());
        return Ok(new { success = true });
    }

    [HttpGet("me/preferences")]
    public IActionResult GetMyPreferences()
    {
        var theme = _userFavs.GetUserPref(CurrentUsername, "ui_theme");
        var template = _userFavs.GetUserPref(CurrentUsername, "ui_template");
        return Ok(new
        {
            // Falls back to the server-wide default when the user has never chosen.
            theme = !string.IsNullOrEmpty(theme) && AllowedThemes.Contains(theme) ? theme : _config.Config.UI.Theme,
            isPersonal = !string.IsNullOrEmpty(theme),
            template = ResolveUserTemplate(_config.Config.UI.Template),
            // null means "never chosen"; "" is a deliberate pick of the built-in
            // default. The UI needs the difference to highlight the right card.
            templateIsPersonal = template != null
        });
    }

    [HttpPost("me/preferences")]
    public IActionResult SaveMyPreferences([FromBody] JsonElement body)
    {
        if (body.TryGetProperty("theme", out var th))
        {
            var theme = th.GetString() ?? "";
            if (!AllowedThemes.Contains(theme))
                return BadRequest(new { error = "Unknown theme" });
            _userFavs.SetUserPref(CurrentUsername, "ui_theme", theme.ToLowerInvariant());
        }
        if (body.TryGetProperty("template", out var tp))
        {
            var template = tp.GetString() ?? "";
            if (!IsValidTemplateId(template))
                return BadRequest(new { error = "Unknown template" });
            _userFavs.SetUserPref(CurrentUsername, "ui_template", template);
        }
        return Ok(new { success = true });
    }

    [HttpGet("videos/continue-watching")]
    /// <param name="limit">
    /// Home shows a short row and leaves this at the default; the In Progress page
    /// asks for the full list. Clamped so a crafted value can't ask for everything.
    /// </param>
    public async Task<IActionResult> GetContinueWatching([FromQuery] int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 500);
        var progressList = _userFavs.GetContinueWatching(CurrentUsername, limit);
        if (progressList.Count == 0) return Ok(new List<object>());

        var videoIds = progressList.Select(p => (int)p["videoId"]).ToList();
        var videos = await _videoDb.Videos
            .Where(v => videoIds.Contains(v.Id))
            .ToListAsync();

        var videoMap = videos.ToDictionary(v => v.Id);
        var result = new List<object>();
        foreach (var prog in progressList)
        {
            var vid = (int)prog["videoId"];
            if (!videoMap.TryGetValue(vid, out var v)) continue;
            result.Add(new
            {
                id = v.Id,
                title = v.Title,
                seriesName = v.SeriesName,
                season = v.Season,
                episode = v.Episode,
                mediaType = v.MediaType,
                thumbnailPath = v.ThumbnailPath,
                posterPath = v.PosterPath,
                duration = v.Duration,
                lastPosition = prog["lastPosition"],
                percentWatched = prog["percentWatched"]
            });
        }
        return Ok(result);
    }

    [HttpGet("videos/next-up")]
    /// <summary>
    /// Jellyfin-style "Next Up" / "À suivre": for every series the user is
    /// actively working through, the NEXT unwatched episode after the most
    /// recently watched one. Distinct from Continue Watching, which only lists
    /// PARTIALLY-watched episodes - this surfaces the episode after you finish
    /// one, which otherwise appears nowhere on the home page.
    /// </summary>
    /// <param name="limit">
    /// Home shows a short row and leaves this at the default; clamped so a
    /// crafted value can't ask for everything.
    /// </param>
    public async Task<IActionResult> GetNextUp([FromQuery] int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 500);

        // Watched episodes with the date they were flagged watched. The list is
        // ASC by WatchedAt, so keeping the last write per id lands the newest date.
        var watchedList = _userFavs.GetWatchedWithDates(CurrentUsername);
        if (watchedList.Count == 0) return Ok(new List<object>());
        var watchedAt = new Dictionary<int, string>();
        foreach (var (mid, wa) in watchedList) watchedAt[mid] = wa ?? "";
        var watchedIds = watchedAt.Keys.ToHashSet();

        // In-progress episodes belong to the Continue Watching row. Exclude a
        // series here while its next episode is still partially watched, so the
        // two rows never show the same "next" card (see 2026-07-30 Next Up work).
        var inProgress = _userFavs.GetContinueWatching(CurrentUsername, 500);
        var inProgressIds = inProgress.Select(p => (int)p["videoId"]).ToHashSet();

        // Which series have at least one watched episode?
        var watchedEpisodes = await _videoDb.Videos
            .Where(v => watchedIds.Contains(v.Id)
                     && (v.MediaType == "tv" || v.MediaType == "anime")
                     && v.SeriesName != null && v.SeriesName != "")
            .Select(v => new { v.Id, v.SeriesName })
            .ToListAsync();
        if (watchedEpisodes.Count == 0) return Ok(new List<object>());

        var seriesNames = watchedEpisodes.Select(v => v.SeriesName!).Distinct().ToList();

        // Every episode of those series, so we can find the one after the anchor.
        var allEpisodes = await _videoDb.Videos
            .Where(v => seriesNames.Contains(v.SeriesName)
                     && (v.MediaType == "tv" || v.MediaType == "anime"))
            .ToListAsync();

        // Series-level poster fallback: an episode row without its own PosterPath (common
        // for not-yet-enriched anime) borrows the poster of any sibling episode that has one,
        // so the home row shows the show poster instead of a raw episode frame-grab.
        var seriesPoster = allEpisodes
            .GroupBy(v => v.SeriesName!)
            .ToDictionary(g => g.Key, g => g.FirstOrDefault(v => !string.IsNullOrEmpty(v.PosterPath))?.PosterPath);

        var candidates = new List<(Video ep, string anchorDate)>();
        foreach (var group in allEpisodes.GroupBy(v => v.SeriesName))
        {
            var ordered = group.OrderBy(v => v.Season).ThenBy(v => v.Episode).ToList();

            // Anchor = the watched episode with the newest WatchedAt (ISO strings
            // sort lexically). That's the last one the user actually finished.
            int anchorIdx = -1;
            string anchorDate = "";
            for (int i = 0; i < ordered.Count; i++)
            {
                if (watchedAt.TryGetValue(ordered[i].Id, out var wa) &&
                    string.CompareOrdinal(wa, anchorDate) >= 0)
                {
                    anchorDate = wa;
                    anchorIdx = i;
                }
            }
            if (anchorIdx < 0) continue; // no watched episode in this series

            // First unwatched episode after the anchor.
            Video? next = null;
            for (int i = anchorIdx + 1; i < ordered.Count; i++)
            {
                if (!watchedIds.Contains(ordered[i].Id)) { next = ordered[i]; break; }
            }
            if (next == null) continue;                       // caught up on this series
            if (inProgressIds.Contains(next.Id)) continue;    // already in Continue Watching

            candidates.Add((next, anchorDate));
        }

        var result = candidates
            .OrderByDescending(c => c.anchorDate, StringComparer.Ordinal)
            .Take(limit)
            .Select(c => new
            {
                id = c.ep.Id,
                title = c.ep.Title,
                seriesName = c.ep.SeriesName,
                season = c.ep.Season,
                episode = c.ep.Episode,
                mediaType = c.ep.MediaType,
                thumbnailPath = c.ep.ThumbnailPath,
                posterPath = !string.IsNullOrEmpty(c.ep.PosterPath)
                    ? c.ep.PosterPath
                    : (seriesPoster.TryGetValue(c.ep.SeriesName!, out var sp) ? sp : null),
                duration = c.ep.Duration
            })
            .ToList<object>();

        return Ok(result);
    }

    [HttpGet("videos/{id}/progress")]
    public IActionResult GetVideoProgress(int id)
    {
        var (pos, dur, pct, completed) = _userFavs.GetVideoProgress(CurrentUsername, id);
        return Ok(new { position = pos, duration = dur, percentWatched = pct, completed });
    }

    [HttpPost("videos/{id}/progress")]
    public IActionResult SaveVideoProgress(int id, [FromBody] System.Text.Json.JsonElement body)
    {
        if (!body.TryGetProperty("position", out var posEl) ||
            !body.TryGetProperty("duration", out var durEl))
            return BadRequest("position and duration required");

        var position = posEl.GetDouble();
        var duration = durEl.GetDouble();

        _userFavs.SaveVideoProgress(CurrentUsername, id, position, duration);

        return Ok();
    }

    [HttpGet("videos/{id}")]
    public async Task<IActionResult> GetVideo(int id)
    {
        var v = await _videoDb.Videos.FindAsync(id);
        if (v == null) return NotFound();

        // Lazy backfill ordered per-stream track arrays. Legacy rows scanned before deep analysis
        // have null AudioTracks; probe once on demand and persist (same pattern as GetVideoChapters).
        // The array index of each audio track == ffmpeg's 0:a:N stream index, so the client can map
        // a chosen audio language back to ?audioTrack=N reliably - the AudioLanguages CSV cannot
        // (it dedupes languages and drops untagged streams, so its index is not positional).
        if (string.IsNullOrEmpty(v.AudioTracks) && _ffmpeg.IsProbeAvailable
            && !string.IsNullOrEmpty(v.FilePath) && System.IO.File.Exists(v.FilePath))
        {
            try
            {
                using var probe = await _ffmpeg.ProbeAsync(v.FilePath);
                if (probe != null)
                {
                    var (audioJson, subJson) = MediaAnalysisService.ParseTrackArrays(probe.RootElement);
                    if (!string.IsNullOrEmpty(audioJson))
                    {
                        v.AudioTracks    = audioJson;
                        v.SubtitleTracks = subJson;
                        try { await _videoDb.SaveChangesAsync(); } catch { /* best-effort cache */ }
                    }
                }
            }
            catch { /* probe failed - fall back to the CSV-only response */ }
        }

        // Deserialize the stored JSON strings so they serialize as proper nested arrays (not strings).
        object? audioTracks = null, subtitleTracks = null;
        if (!string.IsNullOrEmpty(v.AudioTracks))
            try { audioTracks = System.Text.Json.JsonDocument.Parse(v.AudioTracks).RootElement.Clone(); } catch { }
        if (!string.IsNullOrEmpty(v.SubtitleTracks))
            try { subtitleTracks = System.Text.Json.JsonDocument.Parse(v.SubtitleTracks).RootElement.Clone(); } catch { }

        var isWatched = _userFavs.IsWatched(CurrentUsername, id);
        var isFavourite = _userFavs.IsFavourite(CurrentUsername, "video", id);
        var inWatchlist = _userFavs.IsInWatchlist(CurrentUsername, id);
        return Ok(new
        {
            v.Id, v.FileName, v.FilePath, v.Title, v.Year, v.Duration, v.SizeBytes,
            v.Format, v.Resolution, v.Width, v.Height, v.Codec, v.HdrFormat, v.VideoBitrate,
            v.AudioCodec, v.AudioChannels, v.AudioLanguages, v.SubtitleLanguages,
            audioTracks, subtitleTracks,
            v.Genre, v.Director, v.Cast, v.Overview, v.Rating, v.ContentRating, v.SafeForChildren,
            v.MetadataLanguage, v.Edition,
            v.CollectionId, v.CollectionName,
            v.MediaType, v.VideoKind, v.DvdDevicePath, v.SeriesName, v.Season, v.Episode,
            v.ThumbnailPath, v.PosterPath, v.BackdropPath, v.CastJson, v.DirectorJson, v.WriterJson, v.StudiosJson,
            v.TmdbId, v.TvMazeId, v.ImdbId, v.MetadataFetched,
            v.Mp4Compliant, v.NeedsOptimization,
            v.DateAdded, v.LastModified, v.LastPlayed, v.PlayCount, isWatched, isFavourite, inWatchlist
        });
    }

    [HttpGet("videos/{id}/intro")]
    public async Task<IActionResult> GetVideoIntro(int id)
    {
        var v = await _videoDb.Videos.FindAsync(id);
        if (v == null) return NotFound();

        // Derive ALL intro-keyword segments (e.g. Recap AND Intro) from the cached chapter list
        // so the player can offer a sequential skip for each. IntroStart/IntroEnd (the first
        // segment) are still returned as start/end for backward compatibility (Android client).
        var segments = new List<object>();
        if (!string.IsNullOrEmpty(v.ChaptersJson))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(v.ChaptersJson);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var ch in doc.RootElement.EnumerateArray())
                    {
                        var title = ch.TryGetProperty("title", out var tt) ? (tt.GetString() ?? "") : "";
                        if (!Services.FFmpegService.IsIntroChapterTitle(title)) continue;
                        var start = ch.TryGetProperty("start", out var ss) ? ss.GetDouble() : 0;
                        var end = ch.TryGetProperty("end", out var ee) ? ee.GetDouble() : 0;
                        if (end <= start) continue;
                        var lower = title.Trim().ToLowerInvariant();
                        var kind = (lower.StartsWith("recap") || lower.StartsWith("previously")) ? "recap" : "intro";
                        segments.Add(new { start, end, kind });
                    }
                }
            }
            catch { /* malformed cache - fall through to the single-range path below */ }
        }

        if (segments.Count > 0)
        {
            var first = (dynamic)segments[0];
            return Ok(new { hasIntro = true, start = (double)first.start, end = (double)first.end, segments });
        }

        // Legacy fallback: no chapter cache but a single intro range was probed at scan time.
        if (!v.IntroStart.HasValue || !v.IntroEnd.HasValue)
            return Ok(new { hasIntro = false });
        return Ok(new
        {
            hasIntro = true,
            start = v.IntroStart.Value,
            end = v.IntroEnd.Value,
            segments = new[] { new { start = v.IntroStart.Value, end = v.IntroEnd.Value, kind = "intro" } }
        });
    }

    [HttpGet("videos/{id}/chapters")]
    public async Task<IActionResult> GetVideoChapters(int id)
    {
        var v = await _videoDb.Videos.FindAsync(id);
        if (v == null) return NotFound();

        // Lazy backfill: legacy rows scanned before chapters were tracked have null ChaptersJson.
        // Probe once on demand and persist ("[]" when none) so we never re-probe the same file.
        if (v.ChaptersJson == null)
        {
            string json = "[]";
            if (_ffmpeg.IsProbeAvailable && !string.IsNullOrEmpty(v.FilePath) && System.IO.File.Exists(v.FilePath))
            {
                var chapters = await _ffmpeg.ProbeChaptersAsync(v.FilePath);
                if (chapters != null)
                    json = System.Text.Json.JsonSerializer.Serialize(
                        chapters.Select(c => new { title = c.Title, start = c.Start, end = c.End }));
            }
            v.ChaptersJson = json;
            try { await _videoDb.SaveChangesAsync(); } catch { /* best-effort cache */ }
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(v.ChaptersJson);
            var hasChapters = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                              && doc.RootElement.GetArrayLength() > 0;
            return Ok(new { hasChapters, chapters = doc.RootElement.Clone() });
        }
        catch { return Ok(new { hasChapters = false, chapters = Array.Empty<object>() }); }
    }

    // ── fanart.tv alternative artwork ──────────────────────────────────────

    /// <summary>Lists alternative posters/backdrops from fanart.tv for this video.
    /// Movies look up by TMDB/IMDB id; TV shows resolve a TheTVDB id (cached on the row).</summary>
    [HttpGet("videos/{id}/fanart")]
    public async Task<IActionResult> GetVideoFanart(int id)
    {
        var v = await _videoDb.Videos.FindAsync(id);
        if (v == null) return NotFound();

        var fanartKey = _config.Config.Metadata.FanartApiKey;
        if (string.IsNullOrWhiteSpace(fanartKey))
            return Ok(new { configured = false, images = Array.Empty<object>() });

        var fanart = new FanartService();
        List<FanartService.FanartImage>? images = null;

        if (v.MediaType is "tv" or "anime")
        {
            // Need a TheTVDB id; resolve once and persist.
            if (string.IsNullOrWhiteSpace(v.TvdbId))
            {
                var tvdb = await fanart.ResolveTvdbIdAsync(_config.Config.Metadata.EffectiveTmdbApiKey, v.TmdbId, v.TvMazeId);
                if (!string.IsNullOrWhiteSpace(tvdb))
                {
                    v.TvdbId = tvdb;
                    try { await _videoDb.SaveChangesAsync(); } catch { /* best-effort cache */ }
                }
            }
            if (!string.IsNullOrWhiteSpace(v.TvdbId))
                images = await fanart.GetTvImagesAsync(fanartKey, v.TvdbId!);
        }
        else
        {
            // movie / documentary
            var movieId = !string.IsNullOrWhiteSpace(v.TmdbId) ? v.TmdbId
                        : !string.IsNullOrWhiteSpace(v.ImdbId) ? v.ImdbId
                        : null;
            if (!string.IsNullOrWhiteSpace(movieId))
                images = await fanart.GetMovieImagesAsync(fanartKey, movieId!);
        }

        if (images == null)
            return Ok(new { configured = true, images = Array.Empty<object>() });

        return Ok(new
        {
            configured = true,
            images = images.Select(i => new { type = i.Type, url = i.Url, lang = i.Lang, likes = i.Likes })
        });
    }

    public class FanartApplyDto { public string? PosterUrl { get; set; } public string? BackdropUrl { get; set; } }

    /// <summary>Downloads the chosen fanart.tv poster/backdrop into assets/videometa and sets the
    /// PosterPath / BackdropPath on the video. Marks the row as manually edited so auto-fetch won't overwrite it.</summary>
    [HttpPost("videos/{id}/fanart/apply")]
    public async Task<IActionResult> ApplyVideoFanart(int id, [FromBody] FanartApplyDto dto)
    {
        var v = await _videoDb.Videos.FindAsync(id);
        if (v == null) return NotFound();
        if (dto == null || (string.IsNullOrWhiteSpace(dto.PosterUrl) && string.IsNullOrWhiteSpace(dto.BackdropUrl)))
            return BadRequest(new { error = "No image selected" });

        // Only accept fanart.tv asset URLs - prevents the endpoint being used to fetch arbitrary URLs.
        static bool IsFanartUrl(string? u) =>
            !string.IsNullOrEmpty(u) &&
            Uri.TryCreate(u, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            (uri.Host.Equals("fanart.tv", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".fanart.tv", StringComparison.OrdinalIgnoreCase));

        var fanart = new FanartService();
        var metaDir = Path.Combine(AppContext.BaseDirectory, "assets", "videometa");
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (IsFanartUrl(dto.PosterUrl))
        {
            var file = await fanart.DownloadToVideoMetaAsync(dto.PosterUrl!, $"fanart_poster_{v.Id}_{stamp}.jpg");
            if (file != null)
            {
                // Remove a previous user-set poster (manual upload or earlier fanart pick).
                if (!string.IsNullOrEmpty(v.PosterPath) &&
                    (v.PosterPath.StartsWith("manual_") || v.PosterPath.StartsWith("fanart_poster_")))
                {
                    var old = Path.Combine(metaDir, v.PosterPath);
                    if (System.IO.File.Exists(old)) try { System.IO.File.Delete(old); } catch { }
                }
                v.PosterPath = file;
            }
        }

        if (IsFanartUrl(dto.BackdropUrl))
        {
            var file = await fanart.DownloadToVideoMetaAsync(dto.BackdropUrl!, $"fanart_backdrop_{v.Id}_{stamp}.jpg");
            if (file != null)
            {
                if (!string.IsNullOrEmpty(v.BackdropPath) && v.BackdropPath.StartsWith("fanart_backdrop_"))
                {
                    var old = Path.Combine(metaDir, v.BackdropPath);
                    if (System.IO.File.Exists(old)) try { System.IO.File.Delete(old); } catch { }
                }
                v.BackdropPath = file;
            }
        }

        v.ManuallyEdited = true;
        v.MetadataFetched = true;
        await _videoDb.SaveChangesAsync();

        return Ok(new { success = true, posterPath = v.PosterPath, backdropPath = v.BackdropPath });
    }

    [HttpGet("videos/{id}/next")]
    public async Task<IActionResult> GetNextEpisode(int id)
    {
        var v = await _videoDb.Videos.FindAsync(id);
        if (v == null || (v.MediaType != "tv" && v.MediaType != "anime") || string.IsNullOrWhiteSpace(v.SeriesName))
            return NotFound();

        var seriesLower = v.SeriesName.ToLower();
        var next = await _videoDb.Videos
            .Where(e => e.MediaType == v.MediaType
                     && e.SeriesName.ToLower() == seriesLower
                     && (e.Season > v.Season
                         || (e.Season == v.Season && e.Episode > v.Episode)))
            .OrderBy(e => e.Season)
            .ThenBy(e => e.Episode)
            .FirstOrDefaultAsync();

        if (next == null) return NotFound();

        return Ok(new {
            id             = next.Id,
            title          = next.Title,
            seriesName     = next.SeriesName,
            season         = next.Season,
            episode        = next.Episode,
            thumbnailPath  = next.ThumbnailPath,
            posterPath     = next.PosterPath,
            backdropPath   = next.BackdropPath,
            duration       = next.Duration
        });
    }

    [HttpGet("videos/{id}/previous")]
    public async Task<IActionResult> GetPreviousEpisode(int id)
    {
        var v = await _videoDb.Videos.FindAsync(id);
        if (v == null || (v.MediaType != "tv" && v.MediaType != "anime") || string.IsNullOrWhiteSpace(v.SeriesName))
            return NotFound();

        var seriesLower = v.SeriesName.ToLower();
        var prev = await _videoDb.Videos
            .Where(e => e.MediaType == v.MediaType
                     && e.SeriesName.ToLower() == seriesLower
                     && (e.Season < v.Season
                         || (e.Season == v.Season && e.Episode < v.Episode)))
            .OrderByDescending(e => e.Season)
            .ThenByDescending(e => e.Episode)
            .FirstOrDefaultAsync();

        if (prev == null) return NotFound();

        return Ok(new {
            id             = prev.Id,
            title          = prev.Title,
            seriesName     = prev.SeriesName,
            season         = prev.Season,
            episode        = prev.Episode,
            thumbnailPath  = prev.ThumbnailPath,
            posterPath     = prev.PosterPath,
            backdropPath   = prev.BackdropPath,
            duration       = prev.Duration
        });
    }

    [HttpPut("videos/{id}")]
    public async Task<IActionResult> UpdateVideo(int id, [FromBody] System.Text.Json.JsonElement body)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();

        var oldMediaType  = video.MediaType;  // capture before change for series propagation
        var oldSeriesName = video.SeriesName; // used to find siblings even after a rename
        var oldYear       = video.Year;

        if (body.TryGetProperty("title", out var t)) video.Title = t.GetString() ?? "";
        if (body.TryGetProperty("year", out var y) && y.ValueKind == System.Text.Json.JsonValueKind.Number) video.Year = y.GetInt32();
        string? newGenre = null;
        if (body.TryGetProperty("genre", out var g)) { newGenre = g.GetString() ?? ""; video.Genre = newGenre; }
        if (body.TryGetProperty("director", out var d)) video.Director = d.GetString() ?? "";
        if (body.TryGetProperty("cast", out var c)) video.Cast = c.GetString() ?? "";
        if (body.TryGetProperty("overview", out var o)) video.Overview = o.GetString() ?? "";
        if (body.TryGetProperty("rating", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.Number) video.Rating = r.GetDouble();
        if (body.TryGetProperty("contentRating", out var cr)) video.ContentRating = cr.GetString() ?? "";
        if (body.TryGetProperty("safeForChildren", out var sfc) &&
            sfc.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
            video.SafeForChildren = sfc.GetBoolean();
        if (body.TryGetProperty("mediaType", out var mt)) video.MediaType = mt.GetString() ?? "movie";
        if (body.TryGetProperty("seriesName", out var sn)) video.SeriesName = sn.GetString() ?? "";
        if (body.TryGetProperty("season", out var se) && se.ValueKind == System.Text.Json.JsonValueKind.Number) video.Season = se.GetInt32();
        if (body.TryGetProperty("episode", out var ep) && ep.ValueKind == System.Text.Json.JsonValueKind.Number) video.Episode = ep.GetInt32();

        // Edition / cut label (empty string clears it back to unspecified).
        if (body.TryGetProperty("edition", out var ed))
        {
            var edVal = ed.GetString();
            video.Edition = string.IsNullOrWhiteSpace(edVal) ? null : edVal.Trim();
        }

        // Collection membership. collectionId 0/null removes the movie from its collection;
        // a real TMDB collection id assigns it, copying the poster/total-count from an existing
        // member so the card and completeness bar render correctly without a re-fetch.
        if (body.TryGetProperty("collectionId", out var cid))
        {
            int? newColId = cid.ValueKind == System.Text.Json.JsonValueKind.Number ? cid.GetInt32() : (int?)null;
            if (newColId is null or 0)
            {
                video.CollectionId = null;
                video.CollectionName = null;
                video.CollectionPosterPath = null;
                video.CollectionTotalCount = null;
            }
            else
            {
                video.CollectionId = newColId;
                var sibling = await _videoDb.Videos
                    .FirstOrDefaultAsync(v => v.CollectionId == newColId && v.Id != id);
                if (sibling != null)
                {
                    video.CollectionName = sibling.CollectionName;
                    video.CollectionPosterPath = sibling.CollectionPosterPath;
                    video.CollectionTotalCount = sibling.CollectionTotalCount;
                }
                else if (body.TryGetProperty("collectionName", out var cn))
                {
                    video.CollectionName = cn.GetString();
                }
            }
        }

        // Handle poster image upload (base64 data URI)
        if (body.TryGetProperty("posterImage", out var pi))
        {
            var dataUri = pi.GetString();
            if (!string.IsNullOrEmpty(dataUri) && dataUri.Contains(","))
            {
                var base64 = dataUri[(dataUri.IndexOf(',') + 1)..];
                var bytes = Convert.FromBase64String(base64);
                var metaDir = Path.Combine(AppContext.BaseDirectory, "assets", "videometa");
                if (!Directory.Exists(metaDir)) Directory.CreateDirectory(metaDir);
                var filename = $"manual_{video.Id}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.jpg";
                var filePath = Path.Combine(metaDir, filename);
                await System.IO.File.WriteAllBytesAsync(filePath, bytes);

                // Remove old manual poster if exists
                if (!string.IsNullOrEmpty(video.PosterPath) && video.PosterPath.StartsWith("manual_"))
                {
                    var oldPath = Path.Combine(metaDir, video.PosterPath);
                    if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                }
                video.PosterPath = filename;
            }
        }

        video.ManuallyEdited = true;
        video.MetadataFetched = true;
        video.LastModified = DateTime.UtcNow;
        await _videoDb.SaveChangesAsync();

        // Propagate series-level changes to ALL other episodes of the same series, UNLESS the
        // caller explicitly opted out. Series-level fields are MediaType, Genre, SeriesName and
        // Year - a change to any of them on one episode must apply to the whole show, or the
        // grouped view fragments it into multiple cards (this is exactly what a Year/name edit on
        // a single episode used to do). Per-episode fields (Title, Season, Episode) are NEVER
        // propagated. Siblings are matched by the OLD series name so a rename still finds the
        // episodes that still carry the previous name.
        var mediaTypeChanged  = video.MediaType != oldMediaType;
        var seriesNameChanged = !string.Equals(video.SeriesName, oldSeriesName, StringComparison.Ordinal);
        var yearChanged       = video.Year != oldYear;
        bool propagateToSeries = true;
        if (body.TryGetProperty("propagateToSeries", out var pts) &&
            pts.ValueKind is System.Text.Json.JsonValueKind.False)
            propagateToSeries = false;

        // Match on the pre-edit name (falls back to the new name if there was none before).
        var matchName = !string.IsNullOrWhiteSpace(oldSeriesName) ? oldSeriesName : video.SeriesName;
        if (propagateToSeries && !string.IsNullOrWhiteSpace(matchName)
            && (mediaTypeChanged || newGenre != null || seriesNameChanged || yearChanged))
        {
            var matchNameLower = matchName.ToLower();
            var siblings = await _videoDb.Videos
                .Where(v => v.Id != id && v.SeriesName.ToLower() == matchNameLower)
                .ToListAsync();
            foreach (var sibling in siblings)
            {
                if (mediaTypeChanged)  { sibling.MediaType = video.MediaType; sibling.ManuallyEdited = true; }
                if (newGenre != null)  sibling.Genre = newGenre;
                if (seriesNameChanged) { sibling.SeriesName = video.SeriesName; sibling.ManuallyEdited = true; }
                if (yearChanged)       sibling.Year = video.Year;
                sibling.LastModified = DateTime.UtcNow;
            }
            if (siblings.Count > 0)
                await _videoDb.SaveChangesAsync();
        }

        // Optionally mirror the edit out to a sidecar .nfo so it is portable to Kodi/Jellyfin.
        if (_config.Config.Metadata.NfoAutoExport)
            await _nfo.WriteForVideoAsync(video);

        return Ok(new { success = true });
    }

    // ── NFO (Kodi/Jellyfin sidecar) import / export ──────────────────────────
    [HttpPost("videos/{id}/nfo/import")]
    public async Task<IActionResult> ImportVideoNfo(int id)
    {
        if (User.IsInRole("child")) return Forbid();
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();
        var nfoPath = NfoService.LocateItemNfo(video.FilePath, video.MediaType);
        var showPath = video.MediaType == "tv" ? NfoService.LocateTvShowNfo(video.FilePath) : null;
        if (nfoPath == null && showPath == null)
            return Ok(new { success = false, found = false, message = "No .nfo file found next to this video." });

        // Manual import is an explicit request to overwrite from the local file.
        var changed = await _nfo.ApplyForVideoAsync(video, overwrite: true);
        if (changed)
        {
            video.MetadataFetched = true;
            video.LastModified = DateTime.UtcNow;
            await _videoDb.SaveChangesAsync();
        }
        return Ok(new { success = true, found = true, changed, nfoPath = nfoPath ?? showPath });
    }

    [HttpPost("videos/{id}/nfo/export")]
    public async Task<IActionResult> ExportVideoNfo(int id)
    {
        if (User.IsInRole("child")) return Forbid();
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();
        var path = await _nfo.WriteForVideoAsync(video);
        return path == null
            ? Ok(new { success = false, message = "Could not write the .nfo (folder missing or not writable)." })
            : Ok(new { success = true, path });
    }

    [HttpPost("videos/nfo/export-all")]
    public async Task<IActionResult> ExportAllNfo()
    {
        if (!User.IsInRole("admin")) return Forbid();
        var count = await _nfo.ExportLibraryAsync();
        return Ok(new { success = true, count });
    }

    [HttpPost("videos/nfo/import-all")]
    public async Task<IActionResult> ImportAllNfo()
    {
        if (!User.IsInRole("admin")) return Forbid();
        var count = await _nfo.ApplyLibraryAsync(overwrite: true);
        return Ok(new { success = true, count });
    }

    // Pads each run of digits so an ordinal string sort matches natural (human) order:
    // "Show Ep 2" sorts before "Show Ep 10".
    private static string NaturalSortKey(string s)
        => System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\d+", m => m.Value.PadLeft(10, '0'));

    [HttpPost("videos/batch")]
    public async Task<IActionResult> BatchUpdateVideos([FromBody] BatchUpdateDto dto)
    {
        if (dto.Ids == null || dto.Ids.Count == 0) return BadRequest(new { error = "No IDs provided" });
        if (dto.Ids.Count > 500) return BadRequest(new { error = "Maximum 500 items per batch" });

        var batchNewMediaType = dto.Fields.TryGetProperty("mediaType", out var batchMt) ? batchMt.GetString() : null;
        var batchNewGenre     = dto.Fields.TryGetProperty("genre",      out var batchG)  ? batchG.GetString()  : null;
        // Bulk TV-episode assignment: give a whole folder of mis-detected "movies" a series
        // name, a season, and sequential episode numbers in one pass - this closes the
        // "tv with no SeriesName" dead zone where converted episodes never group into a
        // browsable series. seriesName is intentionally ignored when blank so a stray tick
        // can't wipe existing series names.
        var batchSeriesName = (dto.Fields.TryGetProperty("seriesName", out var batchSn) &&
                               !string.IsNullOrWhiteSpace(batchSn.GetString())) ? batchSn.GetString() : null;
        int? batchSeason = (dto.Fields.TryGetProperty("season", out var batchSe) &&
                            batchSe.ValueKind == System.Text.Json.JsonValueKind.Number) ? batchSe.GetInt32() : null;
        bool autoNumber = dto.Fields.TryGetProperty("autoNumberEpisodes", out var batchAn) &&
                          batchAn.ValueKind is System.Text.Json.JsonValueKind.True;
        int startEpisode = (dto.Fields.TryGetProperty("startEpisode", out var batchStart) &&
                            batchStart.ValueKind == System.Text.Json.JsonValueKind.Number) ? batchStart.GetInt32() : 1;

        var videos = await _videoDb.Videos.Where(v => dto.Ids.Contains(v.Id)).ToListAsync();

        // Episode auto-numbering walks the selection in NATURAL filename order (so
        // "Ep 2" precedes "Ep 10") and assigns startEpisode, startEpisode+1, …
        if (autoNumber)
        {
            int ep = startEpisode;
            foreach (var v in videos.OrderBy(v => NaturalSortKey(v.FileName), StringComparer.OrdinalIgnoreCase))
                v.Episode = ep++;
        }

        foreach (var video in videos)
        {
            if (batchNewMediaType != null) video.MediaType = batchNewMediaType;
            if (batchNewGenre     != null) video.Genre     = batchNewGenre;
            if (batchSeriesName   != null) video.SeriesName = batchSeriesName;
            if (batchSeason.HasValue)      video.Season     = batchSeason.Value;
            if (dto.Fields.TryGetProperty("year", out var y) && y.ValueKind == System.Text.Json.JsonValueKind.Number) video.Year = y.GetInt32();
            if (dto.Fields.TryGetProperty("contentRating", out var cr)) video.ContentRating = cr.GetString() ?? "";
            if (dto.Fields.TryGetProperty("safeForChildren", out var sfc) &&
                sfc.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                video.SafeForChildren = sfc.GetBoolean();
            video.ManuallyEdited = true;
            video.LastModified = DateTime.UtcNow;
        }
        await _videoDb.SaveChangesAsync();

        // Propagate MediaType / Genre changes to all series siblings not already in the batch.
        if (batchNewMediaType != null || batchNewGenre != null)
        {
            var batchIdSet = dto.Ids.ToHashSet();
            var seriesNames = videos
                .Where(v => !string.IsNullOrWhiteSpace(v.SeriesName))
                .Select(v => v.SeriesName.ToLower())
                .Distinct()
                .ToList();
            if (seriesNames.Count > 0)
            {
                var siblings = await _videoDb.Videos
                    .Where(v => !batchIdSet.Contains(v.Id) && seriesNames.Contains(v.SeriesName.ToLower()))
                    .ToListAsync();
                foreach (var sibling in siblings)
                {
                    if (batchNewMediaType != null) { sibling.MediaType = batchNewMediaType; sibling.ManuallyEdited = true; }
                    if (batchNewGenre     != null) sibling.Genre = batchNewGenre;
                    sibling.LastModified = DateTime.UtcNow;
                }
                if (siblings.Count > 0)
                    await _videoDb.SaveChangesAsync();
            }
        }

        return Ok(new { success = true, updated = videos.Count });
    }

    /// <summary>
    /// Change MediaType for every episode in a named series at once.
    /// Used by the series-card 3-dot menu (TV ↔ Anime conversions).
    /// Documentaries are not allowed here - they must be standalone videos.
    /// </summary>
    [HttpPost("videos/batch-by-series")]
    public async Task<IActionResult> BatchUpdateBySeries([FromBody] System.Text.Json.JsonElement body)
    {
        if (!body.TryGetProperty("seriesName", out var snProp) || string.IsNullOrWhiteSpace(snProp.GetString()))
            return BadRequest(new { error = "seriesName is required" });
        if (!body.TryGetProperty("mediaType", out var mtProp))
            return BadRequest(new { error = "mediaType is required" });

        var seriesName = snProp.GetString()!;
        var newMediaType = mtProp.GetString() ?? "tv";

        if (newMediaType == "documentary")
            return BadRequest(new { error = "A series cannot be classified as Documentary. Documentaries must be standalone single videos." });

        var seriesNameLower = seriesName.ToLower();
        var episodes = await _videoDb.Videos
            .Where(v => v.SeriesName.ToLower() == seriesNameLower)
            .ToListAsync();

        if (episodes.Count == 0) return NotFound(new { error = "Series not found" });

        foreach (var ep in episodes)
        {
            ep.MediaType = newMediaType;
            ep.ManuallyEdited = true;
            ep.LastModified = DateTime.UtcNow;
        }
        await _videoDb.SaveChangesAsync();
        return Ok(new { success = true, updated = episodes.Count });
    }

    [HttpPost("videos/{id}/fetch-anime")]
    public async Task<IActionResult> FetchAnimeMetadata(int id)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();
        if (video.MediaType != "anime")
            return BadRequest(new { error = "Video is not classified as anime" });

        // Await synchronously so the caller can reload once metadata is ready
        await _animeScanner.EnrichSingleAsync(id);
        return Ok(new { message = "Jikan fetch complete" });
    }

    // Re-fetch Jikan metadata for the WHOLE anime library - backfills the new per-episode
    // titles + series posters into an existing library. Admin-only; runs in the background
    // because Jikan is rate-limited (a large library can take minutes).
    [HttpPost("anime/refetch-all")]
    public IActionResult RefetchAllAnimeMetadata()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
            return Forbid();

        if (_animeScanner.IsRefetching)
            return Ok(new { message = "Anime metadata re-fetch already running", alreadyRunning = true });

        _ = _animeScanner.RefetchAllMetadataAsync();
        return Ok(new { message = "Anime metadata re-fetch started" });
    }

    // Live progress for the anime re-fetch job - polled by the frontend to drive a progress bar.
    // Details of every refresh (series matched, per-episode title changes) go to logs/metadata.log.
    [HttpGet("anime/refetch-status")]
    public IActionResult GetAnimeRefetchStatus()
    {
        var p = _animeScanner.CurrentRefetchProgress;
        return Ok(new
        {
            isRunning = _animeScanner.IsRefetching,
            status = p.Status,
            message = p.Message,
            currentSeries = p.CurrentSeries,
            totalSeries = p.TotalSeries,
            processedSeries = p.ProcessedSeries,
            matchedSeries = p.MatchedSeries,
            notFoundSeries = p.NotFoundSeries,
            apiErrorSeries = p.ApiErrorSeries,
            totalEpisodes = p.TotalEpisodes,
            processedEpisodes = p.ProcessedEpisodes,
            updatedTitles = p.UpdatedTitles,
            percentComplete = p.PercentComplete
        });
    }

    [HttpDelete("videos/{id}")]
    public async Task<IActionResult> DeleteVideo(int id)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();

        _videoDb.Videos.Remove(video);
        await _videoDb.SaveChangesAsync();

        _logger.LogInformation("Video id={Id} ('{Title}') removed from database by {User}", id, video.Title, CurrentUsername);
        return Ok(new { success = true });
    }

    // Deletes a media file only if it resolves to a path inside one of `roots` (containment
    // guard, defense-in-depth). Clears a read-only attribute first (a common Windows delete
    // blocker). Shared by movie/TV and music-video deletion. Cross-platform: System.IO + Path.
    // Returns: ok=true when the file is gone (deleted or already absent); refused=true when the
    // path is outside every root; otherwise ok=false with the error (caller keeps the DB row).
    private (bool ok, bool refused, string? error) TryDeleteFileWithinRoots(string? filePath, List<string> roots)
    {
        string fullTarget;
        try { fullTarget = Path.GetFullPath(filePath ?? ""); }
        catch { return (false, true, "Invalid file path."); }

        var normRoots = roots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => { try { return Path.GetFullPath(r); } catch { return null; } })
            .Where(r => r != null)!
            .ToList();
        var pathCmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        bool inside = normRoots.Count > 0 && normRoots.Any(root =>
        {
            var withSep = root!.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            return fullTarget.StartsWith(withSep, pathCmp);
        });
        if (!inside)
        {
            _logger.LogWarning("TryDeleteFileWithinRoots: refused - '{Path}' is not inside any configured media folder", fullTarget);
            return (false, true, "this file is not inside a configured media folder.");
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(filePath) && System.IO.File.Exists(filePath))
            {
                try
                {
                    var attrs = System.IO.File.GetAttributes(filePath);
                    if (attrs.HasFlag(FileAttributes.ReadOnly))
                        System.IO.File.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);
                }
                catch { /* fall through - Delete will report the real error */ }

                System.IO.File.Delete(filePath);
            }
            else
            {
                _logger.LogWarning("TryDeleteFileWithinRoots: file not found on disk ('{Path}') - nothing to delete", filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TryDeleteFileWithinRoots: could not delete '{Path}'", filePath);
            return (false, false, ex.Message);
        }
        return (true, false, null);
    }

    // Shared purge for ONE Video: containment + file delete (via TryDeleteFileWithinRoots) →
    // best-effort cached-asset cleanup → remove non-cascaded CustomSubtitles. Does NOT remove
    // the EF Video row - the caller removes rows (one or a whole series) and saves once.
    // Returns: ok=true when the file is gone and the row is safe to drop; refused=true when the
    // path is outside every configured media folder; otherwise ok=false (keep the row).
    private (bool ok, bool refused, string? error) PurgeVideoFilesInternal(Video video)
    {
        var roots = _config.Config.Library.GetAllVideoFolderList()
            .Concat(_config.Config.Library.GetAnimeFolderList())
            .ToList();
        var (ok, refused, error) = TryDeleteFileWithinRoots(video.FilePath, roots);
        if (!ok) return (ok, refused, error);

        // Best-effort cached-asset cleanup (never fatal).
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var metaDir = Path.Combine(baseDir, "assets", "videometa");
            foreach (var art in new[] { video.PosterPath, video.BackdropPath })
            {
                if (string.IsNullOrWhiteSpace(art)) continue;
                var p = Path.Combine(metaDir, Path.GetFileName(art));
                if (System.IO.File.Exists(p)) { try { System.IO.File.Delete(p); } catch { } }
            }
            var subDir = Path.Combine(baseDir, "assets", "subtitles", video.Id.ToString());
            if (Directory.Exists(subDir)) { try { Directory.Delete(subDir, true); } catch { } }
            var thumbDir = Path.Combine(baseDir, "assets", "vthumbs", $"video_{video.Id}");
            if (Directory.Exists(thumbDir)) { try { Directory.Delete(thumbDir, true); } catch { } }
            var prevClip = Path.Combine(baseDir, "assets", "vpreviews", $"video_{video.Id}.mp4");
            if (System.IO.File.Exists(prevClip)) { try { System.IO.File.Delete(prevClip); } catch { } }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PurgeVideoFiles: asset cleanup partial for video {Id}", video.Id);
        }

        // Remove non-cascaded CustomSubtitles rows.
        try
        {
            using var conn = OpenVideoDbRaw();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM CustomSubtitles WHERE VideoId = @id";
            cmd.Parameters.AddWithValue("@id", video.Id);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PurgeVideoFiles: CustomSubtitles cleanup failed for video {Id} (non-fatal)", video.Id);
        }

        return (true, false, null);
    }

    // File-level deletion of a SINGLE video (movie/documentary/episode): deletes the file +
    // all metadata. ADMIN-ONLY. If the file cannot be deleted, the DB row is KEPT so the
    // library never points at a phantom entry.
    [HttpDelete("videos/{id}/file")]
    public async Task<IActionResult> DeleteVideoFile(int id)
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
        {
            _logger.LogWarning("DeleteVideoFile: forbidden - non-admin user '{User}' attempted to delete video {Id}", CurrentUsername, id);
            return Forbid();
        }

        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();

        var title = video.Title;
        var filePath = video.FilePath;
        var (ok, refused, error) = PurgeVideoFilesInternal(video);
        if (refused) return StatusCode(403, new { success = false, message = "Refused: " + error });
        if (!ok) return StatusCode(500, new { success = false, message = "Could not delete the file on disk: " + error });

        _videoDb.Videos.Remove(video);
        await _videoDb.SaveChangesAsync();

        _logger.LogInformation("Video id={Id} ('{Title}') DELETED (file + metadata) by {User}. Path='{Path}'", id, title, CurrentUsername, filePath);
        return Ok(new { success = true });
    }

    // Whole-TV-show deletion: permanently deletes EVERY episode file + metadata for a series.
    // ADMIN-ONLY. Best-effort per episode - any episode whose file can't be deleted keeps its
    // DB row and is reported back, so the caller can show a partial-failure message.
    [HttpDelete("videos/series/files")]
    public async Task<IActionResult> DeleteSeriesFiles([FromQuery] string name, [FromQuery] string? mediaType = null)
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
        {
            _logger.LogWarning("DeleteSeriesFiles: forbidden - non-admin user '{User}' attempted to delete series '{Name}'", CurrentUsername, name);
            return Forbid();
        }
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { success = false, message = "Series name is required." });

        var query = _videoDb.Videos.Where(v => v.SeriesName == name);
        if (!string.IsNullOrWhiteSpace(mediaType))
            query = query.Where(v => v.MediaType == mediaType);
        var episodes = await query.ToListAsync();
        if (episodes.Count == 0) return NotFound(new { success = false, message = "No episodes found for this series." });

        int deleted = 0;
        var failures = new List<string>();
        foreach (var ep in episodes)
        {
            var (ok, _, error) = PurgeVideoFilesInternal(ep);
            if (ok)
            {
                _videoDb.Videos.Remove(ep);
                deleted++;
            }
            else
            {
                var fn = string.IsNullOrWhiteSpace(ep.FilePath) ? $"video {ep.Id}" : Path.GetFileName(ep.FilePath);
                failures.Add($"{fn}: {error}");
            }
        }
        await _videoDb.SaveChangesAsync();

        _logger.LogInformation("Series '{Name}' ({Media}) deleted by {User}: {Deleted}/{Total} episodes removed, {Failed} failed",
            name, mediaType ?? "any", CurrentUsername, deleted, episodes.Count, failures.Count);

        if (failures.Count > 0)
        {
            var msg = $"Deleted {deleted} of {episodes.Count} episode(s). {failures.Count} could not be deleted:\n"
                    + string.Join("\n", failures.Take(8));
            if (failures.Count > 8) msg += $"\n…and {failures.Count - 8} more.";
            return Ok(new { success = false, deleted, total = episodes.Count, failed = failures.Count, message = msg });
        }
        return Ok(new { success = true, deleted, total = episodes.Count });
    }

    // Batch file-level deletion of selected duplicate files across every media type.
    // ADMIN-ONLY. Same safeties as the single-media delete: each file must live inside a
    // configured media folder (TryDeleteFileWithinRoots / PurgeVideoFilesInternal), and any
    // file that can't be deleted keeps its DB row and is reported back for a partial-failure
    // message. The web caller adds its own 2-stage "type GO" confirmation before calling this.
    public sealed class DuplicateDeleteItem
    {
        public string? Type { get; set; }   // music | videos | tvEpisodes | musicVideos | ebooks | audiobooks
        public int Id { get; set; }
    }
    public sealed class DuplicateDeleteRequest
    {
        public List<DuplicateDeleteItem>? Items { get; set; }
    }

    [HttpPost("analysis/duplicates/delete")]
    public async Task<IActionResult> DeleteDuplicateFiles([FromBody] DuplicateDeleteRequest req)
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
        {
            _logger.LogWarning("DeleteDuplicateFiles: forbidden - non-admin user '{User}' attempted a batch duplicate delete", CurrentUsername);
            return Forbid();
        }
        if (req?.Items == null || req.Items.Count == 0)
            return BadRequest(new { success = false, message = "No items to delete." });

        int deleted = 0;
        var failures = new List<string>();

        // Group by type so each DbContext is saved once.
        bool videoTouched = false, mvTouched = false, trackTouched = false, ebookTouched = false, abTouched = false;

        foreach (var item in req.Items)
        {
            var type = (item.Type ?? "").Trim();
            try
            {
                switch (type)
                {
                    case "videos":
                    case "tvEpisodes":
                    {
                        var v = await _videoDb.Videos.FindAsync(item.Id);
                        if (v == null) { failures.Add($"video {item.Id}: not found"); break; }
                        var (ok, _, error) = PurgeVideoFilesInternal(v);
                        if (ok) { _videoDb.Videos.Remove(v); videoTouched = true; deleted++; }
                        else failures.Add($"{FileLabel(v.FilePath, item.Id)}: {error}");
                        break;
                    }
                    case "musicVideos":
                    {
                        var mv = await _mvDb.MusicVideos.FindAsync(item.Id);
                        if (mv == null) { failures.Add($"music video {item.Id}: not found"); break; }
                        var (ok, _, error) = TryDeleteFileWithinRoots(mv.FilePath, _config.Config.Library.GetMusicVideosFolderList());
                        if (ok)
                        {
                            try
                            {
                                var thumbDir = Path.Combine(AppContext.BaseDirectory, "assets", "mvthumbs");
                                if (!string.IsNullOrWhiteSpace(mv.ThumbnailPath))
                                {
                                    var p = Path.Combine(thumbDir, Path.GetFileName(mv.ThumbnailPath));
                                    if (System.IO.File.Exists(p)) { try { System.IO.File.Delete(p); } catch { } }
                                }
                                var byId = Path.Combine(thumbDir, $"mvthumb_{item.Id}.jpg");
                                if (System.IO.File.Exists(byId)) { try { System.IO.File.Delete(byId); } catch { } }
                            }
                            catch { }
                            _mvDb.MusicVideos.Remove(mv); mvTouched = true; deleted++;
                        }
                        else failures.Add($"{FileLabel(mv.FilePath, item.Id)}: {error}");
                        break;
                    }
                    case "music":
                    {
                        var t = await _db.Tracks.FindAsync(item.Id);
                        if (t == null) { failures.Add($"track {item.Id}: not found"); break; }
                        var (ok, _, error) = TryDeleteFileWithinRoots(t.FilePath, _config.Config.Library.GetMusicFolderList());
                        if (ok) { _db.Tracks.Remove(t); trackTouched = true; deleted++; }
                        else failures.Add($"{FileLabel(t.FilePath, item.Id)}: {error}");
                        break;
                    }
                    case "ebooks":
                    {
                        var e = await _ebookDb.EBooks.FindAsync(item.Id);
                        if (e == null) { failures.Add($"ebook {item.Id}: not found"); break; }
                        var (ok, _, error) = TryDeleteFileWithinRoots(e.FilePath, _config.Config.Library.GetEBooksFolderList());
                        if (ok) { _ebookDb.EBooks.Remove(e); ebookTouched = true; deleted++; }
                        else failures.Add($"{FileLabel(e.FilePath, item.Id)}: {error}");
                        break;
                    }
                    case "audiobooks":
                    {
                        var a = await _audioBooksDb.AudioBooks.FindAsync(item.Id);
                        if (a == null) { failures.Add($"audiobook {item.Id}: not found"); break; }
                        var (ok, _, error) = TryDeleteFileWithinRoots(a.FilePath, _config.Config.Library.GetAudioBooksFolderList());
                        if (ok) { _audioBooksDb.AudioBooks.Remove(a); abTouched = true; deleted++; }
                        else failures.Add($"{FileLabel(a.FilePath, item.Id)}: {error}");
                        break;
                    }
                    default:
                        failures.Add($"{type} {item.Id}: unknown media type");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteDuplicateFiles: error deleting {Type} {Id}", type, item.Id);
                failures.Add($"{type} {item.Id}: {ex.Message}");
            }
        }

        if (videoTouched) await _videoDb.SaveChangesAsync();
        if (mvTouched)    await _mvDb.SaveChangesAsync();
        if (trackTouched) await _db.SaveChangesAsync();
        if (ebookTouched) await _ebookDb.SaveChangesAsync();
        if (abTouched)    await _audioBooksDb.SaveChangesAsync();

        _logger.LogInformation("Duplicate batch delete by {User}: {Deleted}/{Total} file(s) removed, {Failed} failed",
            CurrentUsername, deleted, req.Items.Count, failures.Count);

        if (failures.Count > 0)
        {
            var msg = $"Deleted {deleted} of {req.Items.Count} file(s). {failures.Count} could not be deleted:\n"
                    + string.Join("\n", failures.Take(8));
            if (failures.Count > 8) msg += $"\n…and {failures.Count - 8} more.";
            return Ok(new { success = false, deleted, total = req.Items.Count, failed = failures.Count, message = msg });
        }
        return Ok(new { success = true, deleted, total = req.Items.Count });

        static string FileLabel(string? path, int id) =>
            string.IsNullOrWhiteSpace(path) ? $"item {id}" : Path.GetFileName(path);
    }

    [HttpGet("stream-video/{id}")]
    public async Task<IActionResult> StreamVideo(int id, [FromQuery] int audioTrack = 0)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();
        if (!System.IO.File.Exists(video.FilePath))
            return NotFound(new { message = "Video file not found on disk" });

        // Deduplicate with stream-info: web UI always calls stream-info first (~100 ms before stream-video).
        // Skip IncrementPlayCount if stream-info already ran within the last 30 seconds,
        // but always refresh LastPlayed so "Continue Watching" stays accurate.
        // NOTE: "Watched" is NOT marked here - starting playback is not watching. A video is
        // marked watched only once VideoProgress reaches the 80% threshold (SaveVideoProgress),
        // or via a manual toggle. This prevents "watched 15s then left = watched".
        var svRecentlyTracked = video.LastPlayed.HasValue &&
            (DateTime.UtcNow - video.LastPlayed.Value).TotalSeconds < 30;
        video.LastPlayed = DateTime.UtcNow;
        await _videoDb.SaveChangesAsync();
        if (!svRecentlyTracked)
        {
            _userFavs.IncrementPlayCount(CurrentUsername, "video", id);
        }

        // Server-side Trakt tracking - only arm session on fresh plays from byte 0;
        // subsequent buffering/buffering-ahead range requests must not reset the timer.
        // When a seek lands at ≥80% of the file we fire stop immediately (credits skip etc.)
        var vRangeHeader = Request.Headers["Range"].ToString();
        if (string.IsNullOrEmpty(vRangeHeader) || vRangeHeader.StartsWith("bytes=0"))
        {
            _streaming.VideoStarted(CurrentUsername, video);
        }
        else if (_streaming.HasVideoSession(CurrentUsername, video.Id) && vRangeHeader.StartsWith("bytes="))
        {
            var dashAt = vRangeHeader.IndexOf('-', 6);
            if (dashAt > 6 && long.TryParse(vRangeHeader.AsSpan(6, dashAt - 6), out var seekByte) && seekByte > 0)
            {
                var fileSize = new FileInfo(video.FilePath).Length;
                if (fileSize > 0 && (double)seekByte / fileSize >= 0.80)
                    _streaming.VideoSeekPast80(CurrentUsername, video);
            }
        }

        // If MP4 compliant and using default audio track, stream directly with byte-range support
        if (video.Mp4Compliant && video.Format.Equals("MP4", StringComparison.OrdinalIgnoreCase) && audioTrack == 0)
        {
            _logger.LogDebug("Video {Id} is MP4 compliant, streaming directly", id);
            var stream = new FileStream(video.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, "video/mp4", enableRangeProcessing: true);
        }

        // Direct play for browser-decodable content in a NON-MP4 container (AV1/Opus in MKV).
        // Without this the decision engine can say "direct" while this endpoint still falls
        // through to remux - and these codecs cannot be remuxed into HLS at all, so the file
        // would be needlessly re-encoded. Byte-range served, which is what makes seeking
        // instant and free: the browser simply asks for the bytes it wants.
        if (audioTrack == 0)
        {
            var directDecision = await _transcoding.GetStreamingModeAsync(
                video.FilePath, video.Format, video.Codec, video.AudioCodec, video.AudioChannels);
            if (directDecision.Mode == "direct")
            {
                _logger.LogInformation("Video {Id}: direct play ({Reason})", id, directDecision.Reason);
                var stream = new FileStream(video.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return File(stream, ContentTypeForContainer(video.Format), enableRangeProcessing: true);
            }
        }

        _logger.LogInformation("Video {Id} needs remux (Format={Format}, Mp4Compliant={Compliant}, Channels={Ch})",
            id, video.Format, video.Mp4Compliant, video.AudioChannels);

        // Check for cached remux (include audio track in cache key)
        var cacheDir = _transcoding.RemuxCachePath;
        var cacheKey = audioTrack > 0
            ? $"vid_{video.Id}_{video.LastModified.Ticks}_a{audioTrack}"
            : $"vid_{video.Id}_{video.LastModified.Ticks}";
        var cachePath = Path.Combine(cacheDir, $"{cacheKey}.mp4");

        // HEVC content: redirect to HLS passthrough instead of a blocking full-file remux.
        // Remuxing a large HEVC MKV synchronously blocks the HTTP connection for the entire
        // remux duration (minutes for HD files). HLS passthrough copies the video stream
        // unchanged into fMP4 segments and transcodes only the audio - exactly what Jellyfin
        // calls DirectStream. The client follows the 302 redirect to the .m3u8 playlist and
        // starts playback as soon as the first segment is ready (typically within a second).
        // MPEG-2 / VC-1 / other codecs Android can't hardware-decode: redirect to HLS transcode.
        // A blocking full-file transcode would time out before producing a single byte.
        // GetSmartStreamAsync already classifies these as "transcode" mode and handles the HLS pipeline.
        bool needsTranscode = video.Codec != null &&
            (video.Codec.StartsWith("mpeg2", StringComparison.OrdinalIgnoreCase) ||
             video.Codec.Equals("mpeg1video", StringComparison.OrdinalIgnoreCase) ||
             video.Codec.Equals("vc1", StringComparison.OrdinalIgnoreCase) ||
             video.Codec.StartsWith("wmv", StringComparison.OrdinalIgnoreCase));

        if (needsTranscode && _ffmpeg.IsAvailable && _transcoding != null)
        {
            _logger.LogInformation("Video {Id}: codec {Codec} requires transcode, redirecting to HLS", id, video.Codec);
            var result = await _transcoding.GetSmartStreamAsync(
                video.Id, video.FilePath, video.Format ?? "",
                video.Codec, video.AudioCodec, video.AudioChannels,
                video.Duration, audioTrack);
            if (result.PlaylistUrl != null)
                return Redirect(result.PlaylistUrl);
            _logger.LogWarning("Video {Id}: HLS transcode start failed, falling back to remux", id);
        }

        bool isHevcContent = video.Codec?.Equals("hevc", StringComparison.OrdinalIgnoreCase) == true
            || video.Codec?.StartsWith("hvc", StringComparison.OrdinalIgnoreCase) == true
            || video.Codec?.StartsWith("h265", StringComparison.OrdinalIgnoreCase) == true;

        if (isHevcContent && _ffmpeg.IsAvailable && _transcoding != null)
        {
            var passthroughStarted = await _transcoding.StartHdrPassthroughAsync(
                video.Id, video.FilePath,
                video.AudioCodec ?? "", video.AudioChannels,
                video.Duration, audioTrack);

            if (passthroughStarted)
            {
                var hlsId = TranscodingService.GetHdrTranscodeId(video.Id, audioTrack);
                _logger.LogInformation("Video {Id}: redirecting HEVC to HLS passthrough → {HlsId}", id, hlsId);
                return Redirect($"/api/hls/{hlsId}/playlist.m3u8");
            }
            _logger.LogWarning("Video {Id}: HEVC passthrough failed, falling back to blocking remux", id);
        }

        // Non-HEVC or passthrough fallback: blocking remux to MP4 with faststart, shared per
        // cache key (one FFmpeg per output file, concurrent range requests wait and are served
        // from the cache) and cancelled when the client that started it is gone and nobody else
        // is waiting - see RunSharedRemuxAsync.
        if (_ffmpeg.IsAvailable && _transcoding != null)
        {
            var ready = await RunSharedRemuxAsync(cacheKey, cachePath, video.FilePath,
                video.AudioChannels, audioTrack, $"video {id}");
            if (ready == null) return new EmptyResult();   // client disconnected
            if (ready == true)
            {
                var stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return File(stream, "video/mp4", enableRangeProcessing: true);
            }
        }
        else if (_ffmpeg.IsAvailable)
        {
            // Fallback to basic FFmpegService remux
            bool success;
            if (video.AudioChannels > 2)
                success = await _ffmpeg.RemuxStereoDownmixAsync(video.FilePath, cachePath, audioTrack);
            else
                success = await _ffmpeg.RemuxFaststartAsync(video.FilePath, cachePath, audioTrack);

            if (success && System.IO.File.Exists(cachePath))
            {
                var stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return File(stream, "video/mp4", enableRangeProcessing: true);
            }
        }
        else
        {
            _logger.LogWarning("FFmpeg not available, cannot remux video {Id}", id);
        }

        // Fallback: direct stream (may not play in browser)
        _logger.LogWarning("Falling back to direct stream for video {Id}", id);
        var fallbackMime = (video.Format ?? "").ToUpperInvariant() switch
        {
            "MKV" => "video/x-matroska",
            "AVI" => "video/x-msvideo",
            "WEBM" => "video/webm",
            "MOV" => "video/quicktime",
            "WMV" => "video/x-ms-wmv",
            "TS" => "video/mp2t",
            _ => "video/mp4"
        };
        var fallbackStream = new FileStream(video.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(fallbackStream, fallbackMime, enableRangeProcessing: true);
    }

    // ─── Smart Stream Info (determines direct vs HLS for client) ──────

    [HttpGet("stream-info/{id}")]
    public async Task<IActionResult> GetStreamInfo(int id, [FromQuery] int audioTrack = 0, [FromQuery] bool forceTranscode = false, [FromQuery] int maxBitrate = 0,
        [FromQuery] bool hevcDirect = false, [FromQuery(Name = "hevcMaxH")] int hevcMaxHeight = 0, [FromQuery(Name = "hevc10")] bool hevc10bit = false,
        [FromQuery] bool av1Direct = false, [FromQuery(Name = "av1MaxH")] int av1MaxHeight = 0,
        [FromQuery] string? adec = null, [FromQuery] string? apass = null, [FromQuery] bool sdrOnly = false,
        [FromQuery] bool preferHls = false)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();
        if (!System.IO.File.Exists(video.FilePath))
            return NotFound(new { message = "Video file not found on disk" });

        // Snap the client's bitrate cap to the configured ladder. The cache key carries the
        // cap, so honouring an arbitrary value would mint a separate transcode + cache folder
        // per distinct number - 5000 and 4800 would encode the same film twice. Snapping DOWN
        // never exceeds what the client asked for.
        maxBitrate = TranscodingService.SnapBitrateToLadder(maxBitrate, _config.Config.Transcoding.GetBitrateLadder());

        _streaming.VideoStarted(CurrentUsername, video);

        // Record a play for all clients - HLS TV shows never hit stream-video, so this is the
        // only chance to count them as played. Deduplication: only increment once within a
        // 30-second window (prevents double-count when web UI plays direct and hits both
        // stream-info and stream-video, or when Android retries via stream-info after a decode error).
        // NOTE: "Watched" is NOT marked here - see StreamVideo. Watched is set only when
        // VideoProgress reaches the 80% threshold, or via manual toggle.
        var siRecentlyTracked = video.LastPlayed.HasValue &&
            (DateTime.UtcNow - video.LastPlayed.Value).TotalSeconds < 30;
        video.LastPlayed = DateTime.UtcNow;
        await _videoDb.SaveChangesAsync();
        if (!siRecentlyTracked)
        {
            _userFavs.IncrementPlayCount(CurrentUsername, "video", id);
        }

        // HEVC passthrough: for any HEVC file in "auto" mode, copy the video stream directly
        // into HLS fMP4 segments (no re-encode). Mirrors Jellyfin's DirectStream behaviour.
        // Near-zero CPU, instant startup, full quality - HDR metadata is preserved so the
        // display handles tonemapping natively. Audio is transcoded to AAC if needed.
        // Skipped when forceTranscode=true (client detected HEVC is unsupported, needs SDR).
        // Falls through to regular transcode only on failure or when mode is "sdr".
        bool isHevc = video.Codec?.Equals("hevc", StringComparison.OrdinalIgnoreCase) == true
            || video.Codec?.StartsWith("hvc", StringComparison.OrdinalIgnoreCase) == true
            || video.Codec?.StartsWith("h265", StringComparison.OrdinalIgnoreCase) == true;

        // AV1 cannot be stream-copied into HLS (the Matroska demuxer sets no keyframe flags - see
        // TranscodingService.HlsCopyUnsafeVideoCodecs), so unlike HEVC there is no passthrough-to-HLS
        // path: the ONLY zero-transcode option for AV1 is RAW direct play (plain byte-range HTTP,
        // which the native client's ExoPlayer/MPV handle fine). A capable client advertises this
        // via av1Direct=true + av1MaxH; anything it can't decode falls through to a full transcode.
        bool isAv1 = video.Codec?.StartsWith("av1", StringComparison.OrdinalIgnoreCase) == true
            || video.Codec?.StartsWith("av01", StringComparison.OrdinalIgnoreCase) == true;

        // ── Adaptive HEVC decision, driven by the CLIENT's actual decode capabilities ──────────
        // A native client (hevcDirect=true) advertises what it can decode: hevcMaxH (max HEVC
        // height), hevc10 (10-bit/Main10 support), adec (CSV of decodable audio codecs), sdrOnly
        // (SDR-only display). The web UI sends none of these and is treated as fully capable, so
        // its behaviour is unchanged. Three outcomes:
        //   1. client decodes BOTH this video AND its audio      → RAW direct play (no ffmpeg)
        //   2. client decodes the video but NOT the audio         → hevc-passthrough (copy video,
        //                                                            transcode only audio → AAC)
        //   3. client CANNOT decode the video (e.g. weak device,  → full transcode to H.264
        //      4K on a 1080p-only decoder) or HDR needs tonemap
        // ExoPlayer's own decode-failure fallback is the final safety net for an optimistic probe.
        bool isHdrFile = !string.IsNullOrEmpty(video.HdrFormat);
        bool serverForcesSdr = _config.Config.Transcoding.HdrPlaybackMode.Equals("sdr", StringComparison.OrdinalIgnoreCase);
        bool clientCapsProvided = hevcDirect;   // native client always sends caps alongside this flag
        // Gate on RESOLUTION only (reliably reported, and correctly stops e.g. 4K on a 1080p decoder).
        // Do NOT gate on 10-bit: Android HEVC decoders very commonly decode Main10 while omitting the
        // HEVCProfileMain10 profile from MediaCodecList, so gating on hevc10bit produces false
        // negatives that needlessly transcode near-universal 10-bit HEVC. If a device genuinely can't
        // decode 10-bit, ExoPlayer's decode-failure fallback (retryWithForceTranscode) covers it.
        bool clientCanVideo = !clientCapsProvided || (hevcMaxHeight >= video.Height);
        // AV1 gated on resolution, same principle as HEVC. Only a client that EXPLICITLY advertised
        // AV1 decode (av1Direct=true) is offered AV1 raw direct; otherwise it transcodes.
        bool clientCanAv1 = av1Direct && av1MaxHeight >= video.Height;
        bool needTonemap = isHdrFile && (sdrOnly || serverForcesSdr);
        // Audio for RAW direct play: codecs that decode reliably on virtually every client are always
        // served raw. The client's MediaCodecList-derived list (adec) is deliberately NOT trusted -
        // it over-reports Dolby/DTS (an advertised E-AC3 decoder can still freeze on raw playback).
        //
        // The `apass` list IS trusted, and this is the key to full direct play of Dolby/DTS/Atmos:
        // it comes from AudioTrack.isDirectPlaybackSupported, i.e. what the actual HDMI/AVR audio sink
        // will BITSTREAM. When the sink accepts AC3/E-AC3/DTS/TrueHD we serve the file raw with that
        // lossless surround track intact and start no ffmpeg at all (matches Plex's bitstream path).
        // A client that can't passthrough sends an empty apass and still lands on hevc-passthrough
        // below (video copied, audio → AAC), so nothing regresses.
        var rawSafeAudio = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "aac", "mp3", "opus", "vorbis", "flac", "alac" };
        foreach (var c in (apass ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            rawSafeAudio.Add(c);
        bool audioRawSafe = rawSafeAudio.Contains(video.AudioCodec ?? "");

        // 0) AV1 RAW direct play - client advertised AV1 decode, resolution fits, audio is raw-safe
        //    (base set or a trusted apass codec), and no tonemap is needed. AV1 has no HLS-copy path,
        //    so raw direct is its only zero-transcode option; if this doesn't match, it transcodes.
        // preferHls (browser clients): raw byte-range serves the original .mkv, which no
        // browser <video>/MSE can demux. A browser that decodes the codec still needs it
        // REMUXED into an fMP4 HLS container, so browsers set preferHls=true to skip the two
        // raw branches below and land on HEVC passthrough (Direct Stream) / transcode instead.
        if (av1Direct && !forceTranscode && !preferHls && isAv1 && clientCanAv1 && audioRawSafe && !needTonemap)
        {
            var av1TrackSuffix = audioTrack > 0 ? $"?audioTrack={audioTrack}" : "";
            _logger.LogInformation("Video {Id}: AV1 direct play (client decodes {H}p {Codec} + {Audio})",
                id, video.Height, video.Codec, video.AudioCodec);
            return Ok(new
            {
                Type = "direct",
                streamUrl = $"/api/download/video/{video.Id}{av1TrackSuffix}",
                playlistUrl = (string?)null,
                masterPlaylistUrl = (string?)null,
                transcodeId = (string?)null,
                playSessionId = (string?)null,
                Mode = "direct",
                Reason = "AV1 direct play (client-side decode)",
                isHdr = isHdrFile,
                Duration = video.Duration,
                audioTrack
            });
        }

        // 1) RAW direct play - client decodes the video and the audio is a universally-safe codec.
        //    Bitrate caps don't apply to a raw file (use Force Transcode to cap). No ffmpeg started.
        if (hevcDirect && !forceTranscode && !preferHls && isHevc && clientCanVideo && audioRawSafe && !needTonemap)
        {
            var directTrackSuffix = audioTrack > 0 ? $"?audioTrack={audioTrack}" : "";
            _logger.LogInformation("Video {Id}: HEVC direct play (client decodes {H}p {Codec} + {Audio})",
                id, video.Height, video.Codec, video.AudioCodec);
            return Ok(new
            {
                Type = "direct",
                streamUrl = $"/api/download/video/{video.Id}{directTrackSuffix}",
                playlistUrl = (string?)null,
                masterPlaylistUrl = (string?)null,
                transcodeId = (string?)null,
                playSessionId = (string?)null,
                Mode = "direct",
                Reason = "HEVC direct play (client-side decode)",
                isHdr = isHdrFile,
                Duration = video.Duration,
                audioTrack
            });
        }

        // 2) HEVC passthrough - copy the HEVC video, transcode only the audio to AAC. Requires the
        //    client to be able to decode the HEVC VIDEO (a copied stream it can't decode is useless
        //    → falls through to the full transcode below). maxBitrate==0 because a cap needs a real
        //    re-encode. HDR that needs tonemapping also falls through (passthrough can't tonemap).
        //    Requires hevcDirect: the client must have EXPLICITLY advertised HEVC decode
        //    capability. Web browsers send no caps (hevcDirect=false) and mostly CANNOT decode
        //    HEVC via MSE (Chrome/Firefox), so a copied HEVC stream would never render - they
        //    must fall through to a real H.264 transcode instead. Native clients (Android/Samsung)
        //    that send hevcDirect=true and can decode still get the cheap passthrough.
        if (hevcDirect && !forceTranscode && maxBitrate == 0 && isHevc && clientCanVideo && !needTonemap)
        {
            var passthroughStarted = await _transcoding.StartHdrPassthroughAsync(
                video.Id, video.FilePath,
                video.AudioCodec ?? "", video.AudioChannels,
                video.Duration, audioTrack);

            if (passthroughStarted)
            {
                var passthroughId = TranscodingService.GetHdrTranscodeId(video.Id, audioTrack);
                var hdrReason = string.IsNullOrEmpty(video.HdrFormat)
                    ? "HEVC copied to HLS fMP4 (no re-encode)"
                    : $"HEVC {video.HdrFormat} copied to HLS fMP4 (HDR preserved, no re-encode)";
                _logger.LogInformation("Video {Id}: HEVC passthrough → {TranscodeId} [{Reason}]",
                    id, passthroughId, hdrReason);
                return Ok(new
                {
                    Type = "hls",
                    streamUrl = (string?)null,
                    playlistUrl = $"/api/hls/{passthroughId}/playlist.m3u8",
                    masterPlaylistUrl = (string?)null,
                    transcodeId = passthroughId,
                    // One viewer of this output. Several viewers share the FFmpeg process
                    // (see ActiveTranscode.Sessions) but each gets its own session id, so
                    // teardown and idle-timeout are scoped to the viewer, not the file.
                    playSessionId = _transcoding.AttachSession(passthroughId),
                    Mode = "hevc-passthrough",
                    Reason = hdrReason,
                    Duration = video.Duration,
                    audioTrack
                });
            }
            // Passthrough failed - fall through to regular transcode
            _logger.LogWarning("Video {Id}: HEVC passthrough failed, falling back to transcode", id);
        }

        var result = await _transcoding.GetSmartStreamAsync(
            video.Id, video.FilePath, video.Format,
            video.Codec, video.AudioCodec, video.AudioChannels,
            video.Duration, audioTrack, video.HdrFormat ?? "", video.Codec ?? "",
            maxBitrateKbps: maxBitrate);

        if (result.Error != null)
            return StatusCode(503, new { error = result.Error });

        // Append audioTrack param to stream URLs so the direct-play endpoint also uses the right track
        var trackSuffix = audioTrack > 0 ? $"?audioTrack={audioTrack}" : "";

        return Ok(new
        {
            result.Type,
            streamUrl = result.StreamUrl != null ? result.StreamUrl + trackSuffix : result.StreamUrl,
            playlistUrl = result.PlaylistUrl,
            masterPlaylistUrl = (string?)null,
            transcodeId = result.TranscodeId,
            // Null for direct play - there is no FFmpeg job to attach to.
            playSessionId = result.TranscodeId != null ? _transcoding.AttachSession(result.TranscodeId) : null,
            result.Mode,
            result.Reason,
            result.Duration,
            audioTrack
        });
    }

    // ─── HLS Endpoints ──────────────────────────────────────────────

    /// <summary>
    /// Content type for a direct-played container. Browsers sniff the actual content, but a
    /// wrong declared type can make them reject the source outright, so MKV must not be
    /// announced as video/mp4.
    /// </summary>
    private static string ContentTypeForContainer(string? format) =>
        (format ?? "").ToUpperInvariant() switch
        {
            "MKV" => "video/x-matroska",
            "WEBM" => "video/webm",
            "MP4" or "M4V" => "video/mp4",
            "MOV" => "video/quicktime",
            _ => "video/mp4"
        };

    [HttpGet("hls/{transcodeId}/playlist.m3u8")]
    public async Task<IActionResult> GetHLSPlaylist(string transcodeId)
    {
        // Wait for playlist to become available (up to 30 seconds)
        // HLS.js requests init.mp4 first, so we must ensure both playlist AND init segment exist
        const int maxWaitMs = 30_000;
        const int pollIntervalMs = 500;
        int waited = 0;

        string? content = null;
        while (waited < maxWaitMs)
        {
            // Fail fast if transcode process already crashed
            if (_transcoding.IsTranscodeFailed(transcodeId))
                return StatusCode(500, new { error = "Transcode failed - check server logs for FFmpeg error details" });

            content = _transcoding.GetPlaylistContent(transcodeId);
            var initExists = _transcoding.GetSegmentPath(transcodeId, "init.mp4") != null;
            // A synthetic VOD playlist lists every segment up front, so waiting for FFmpeg to
            // produce one is pointless - only init.mp4 has to exist before hls.js can start.
            // Segments themselves are resolved on demand by EnsureSegmentAsync.
            if (content != null && content.Contains("#EXTINF:") && initExists)
                break;

            await Task.Delay(pollIntervalMs);
            waited += pollIntervalMs;
        }

        if (content == null)
            return NotFound(new { error = "Playlist not ready - transcode may have failed" });

        return Content(content, "application/vnd.apple.mpegurl");
    }

    /// <summary>
    /// Returns a dual-track HLS master playlist for HDR passthrough streams.
    /// The transcodeId parameter is the HDR passthrough ID (e.g. "video-5-hdr").
    /// Lists the HEVC/HDR10 rendition first (for Safari/Apple TV native HLS) and
    /// the H.264/SDR rendition second (selected by HLS.js on Chrome via codec negotiation).
    /// </summary>
    [HttpGet("hls/{transcodeId}/master.m3u8")]
    public IActionResult GetHLSMasterPlaylist(string transcodeId)
    {
        // Derive SDR transcodeId from the HDR one (e.g. "video-5-hdr" → "video-5")
        var sdrTranscodeId = transcodeId.Replace("-hdr", "");

        var hdrPlaylistUrl = $"/api/hls/{transcodeId}/playlist.m3u8";
        var sdrPlaylistUrl = $"/api/hls/{sdrTranscodeId}/playlist.m3u8";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine("#EXT-X-VERSION:7");
        // Start at the beginning of the file. The media playlists carry the same tag
        // (TranscodingService.GetPlaylistContent), but hls.js prioritises the offset
        // declared in the multivariant playlist so that the video, audio and subtitle
        // renditions all agree on a start time - without it here, the HDR path could
        // still open at the live edge of a not-yet-finished transcode. See the comment
        // in GetPlaylistContent for the full explanation.
        sb.AppendLine("#EXT-X-START:TIME-OFFSET=0,PRECISE=YES");
        // HDR (HEVC/Main10) rendition - Safari and Apple TV pick this natively
        // hvc1.2.4.L153.B0 = HEVC Main10, Level 5.1 (covers 4K 60fps)
        sb.AppendLine("#EXT-X-STREAM-INF:BANDWIDTH=10000000,CODECS=\"hvc1.2.4.L153.B0,mp4a.40.2\",VIDEO-RANGE=PQ");
        sb.AppendLine(hdrPlaylistUrl);
        // SDR (H.264) fallback - HLS.js selects this via MediaSource.isTypeSupported()
        sb.AppendLine("#EXT-X-STREAM-INF:BANDWIDTH=4000000,CODECS=\"avc1.640028,mp4a.40.2\",VIDEO-RANGE=SDR");
        sb.AppendLine(sdrPlaylistUrl);

        return Content(sb.ToString(), "application/vnd.apple.mpegurl");
    }

    [HttpGet("hls/{transcodeId}/{segment}")]
    public async Task<IActionResult> GetHLSSegment(string transcodeId, string segment, [FromQuery] string? playSessionId = null)
    {
        // Validate segment name (prevent path traversal)
        if (segment.Contains("..") || segment.Contains('/') || segment.Contains('\\'))
            return BadRequest();

        // Keep THIS viewer's idle clock alive. Without it a second viewer's fetches
        // would be the only thing refreshing the job, and a paused viewer could be
        // reaped (or kept alive) based on someone else's activity.
        _transcoding.TouchSession(transcodeId, playSessionId);

        // The playlist advertises the whole runtime, so a request can legitimately land
        // beyond what FFmpeg has encoded. A miss is therefore not an error - it means
        // "produce this", which may be a short wait or a reposition of the encoder.
        var path = await _transcoding.EnsureSegmentAsync(transcodeId, segment, playSessionId, HttpContext.RequestAborted);
        if (path == null)
            return NotFound();

        // Track HLS playback position for server-side Trakt reporting (works for all clients)
        if (segment.EndsWith(".m4s"))
            _streaming.CheckHlsProgress(CurrentUsername, transcodeId, segment,
                _config.Config.Transcoding.HLSSegmentDuration);

        var contentType = segment.EndsWith(".m4s") ? "video/iso.segment"
            : segment.EndsWith(".mp4") ? "video/mp4"
            : segment.EndsWith(".ts") ? "video/mp2t"
            : "application/octet-stream";

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, contentType, enableRangeProcessing: true);
    }

    /// <summary>
    /// Client teardown for a playback. Detaches THIS viewer's play session; the FFmpeg
    /// process is killed only once the last viewer has left, so closing a tab can no
    /// longer cut off another household member watching the same title.
    ///
    /// `playSessionId` is optional for backwards compatibility: an older client (or an
    /// Android/Samsung build predating sessions) sends none, and is treated as the sole
    /// viewer - i.e. exactly the previous behaviour.
    /// </summary>
    [HttpPost("stop-transcode/{transcodeId}")]
    [HttpGet("stop-transcode/{transcodeId}")]
    public IActionResult StopTranscode(string transcodeId, [FromQuery] string? playSessionId = null)
    {
        var stopped = string.IsNullOrWhiteSpace(playSessionId)
            ? _transcoding.StopTranscode(transcodeId)
            : _transcoding.DetachSession(transcodeId, playSessionId);
        return Ok(new { success = stopped, transcodeId, playSessionId });
    }

    [HttpGet("transcode/status")]
    public IActionResult GetTranscodeStatus()
    {
        var active = _transcoding.GetActiveTranscodes();
        return Ok(new { active, count = active.Count });
    }

    [HttpGet("transcode/cache-stats")]
    public IActionResult GetTranscodeCacheStats()
    {
        var stats = _transcoding.GetCacheStats();
        return Ok(new
        {
            hls = new { entries = stats.HLSEntries, totalSize = stats.HLSTotalSizeBytes },
            remux = new { entries = stats.RemuxEntries, totalSize = stats.RemuxTotalSizeBytes }
        });
    }

    [HttpPost("transcode/clear-hls-cache")]
    public IActionResult ClearHLSCache()
    {
        var (count, freed) = _transcoding.ClearHLSCache();
        return Ok(new { message = $"Cleared {count} cached transcodes", count, freed });
    }

    [HttpGet("videos/genres")]
    public async Task<IActionResult> GetVideoGenres([FromQuery] string? mediaType = null)
    {
        var q = _videoDb.Videos.AsQueryable();
        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            var gtypes = mediaType.ToLower().Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
            if (gtypes.Count == 1)
                q = q.Where(v => v.MediaType == gtypes[0]);
            else
                q = q.Where(v => gtypes.Contains(v.MediaType));
        }
        else
            q = q.Where(v => v.MediaType != "anime");

        var rawVideos = await q
            .Where(v => v.Genre != null && v.Genre != "")
            .Select(v => new { v.Genre, v.MediaType, v.SeriesName, v.Id, v.PosterPath })
            .ToListAsync();

        // Also fetch documentary-typed items that may have no Genre tag set - they are added to the
        // "Documentary" genre bucket below, but only when the scope includes documentaries.
        var docItems = await _videoDb.Videos
            .Where(v => v.MediaType == "documentary")
            .Select(v => new { Genre = (string?)null, v.MediaType, v.SeriesName, v.Id, v.PosterPath })
            .ToListAsync();

        // Count display-level items per genre: each TV series counts as 1 (not per-episode),
        // each movie/documentary/standalone counts as 1 - so the chip counts match what the grid shows.
        var genreMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        // Collect all available poster filenames per genre (for unique-poster assignment below)
        var genrePosters = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in rawVideos)
        {
            var displayKey = v.MediaType == "tv" && !string.IsNullOrEmpty(v.SeriesName)
                ? $"tv::{v.SeriesName.Trim().ToLowerInvariant()}"
                : $"id::{v.Id}";
            foreach (var raw in (v.Genre ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var g = MetadataService.NormalizeGenreToken(raw);
                if (!genreMap.TryGetValue(g, out var set))
                    genreMap[g] = set = new HashSet<string>();
                set.Add(displayKey);
                if (!string.IsNullOrEmpty(v.PosterPath))
                {
                    if (!genrePosters.TryGetValue(g, out var plist))
                        genrePosters[g] = plist = new List<string>();
                    plist.Add(v.PosterPath);
                }
            }
        }

        // Ensure all MediaType="documentary" items appear in the "Documentary" genre bucket,
        // but only when the request scope includes documentaries. The Movies page passes mediaType=movie
        // and must not count documentary-typed items - otherwise the tile shows a non-zero count that
        // produces empty results when clicked (docs are excluded by the mediaType=movie filter).
        bool genresScopeIncludesDocs = string.IsNullOrWhiteSpace(mediaType) ||
            mediaType.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                     .Any(t => t.Equals("documentary", StringComparison.OrdinalIgnoreCase));
        const string docGenreKey = "Documentary";
        if (genresScopeIncludesDocs && docItems.Count > 0)
        {
            if (!genreMap.TryGetValue(docGenreKey, out var docSet))
                genreMap[docGenreKey] = docSet = new HashSet<string>();
            if (!genrePosters.TryGetValue(docGenreKey, out var docPosterList))
                genrePosters[docGenreKey] = docPosterList = new List<string>();
            foreach (var d in docItems)
            {
                docSet.Add($"id::{d.Id}");
                if (!string.IsNullOrEmpty(d.PosterPath))
                    docPosterList.Add(d.PosterPath);
            }
        }

        // Assign one unique poster per genre: prefer a poster not already assigned to a previous genre
        var usedPosters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var genreSamplePoster = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var genreName in genreMap.Keys.OrderBy(k => k))
        {
            if (!genrePosters.TryGetValue(genreName, out var candidates) || candidates.Count == 0) continue;
            var pick = candidates.FirstOrDefault(p => !usedPosters.Contains(p)) ?? candidates[0];
            genreSamplePoster[genreName] = pick;
            usedPosters.Add(pick);
        }

        var genres = genreMap
            .Select(kv => new {
                name = kv.Key,
                count = kv.Value.Count,
                samplePoster = genreSamplePoster.GetValueOrDefault(kv.Key),
                samplePosters = genrePosters.GetValueOrDefault(kv.Key, new List<string>())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(_ => Random.Shared.Next())
                    .Take(12).ToArray()
            })
            .OrderBy(g => g.name)
            .ToList();

        return Ok(genres);
    }

    [HttpGet("video-posters")]
    public IActionResult GetVideoPosters([FromQuery] int page = 1, [FromQuery] int limit = 12, [FromQuery] string? search = null)
    {
        var dir = Path.Combine(_env.ContentRootPath, "assets", "videometa");
        if (!Directory.Exists(dir)) return Ok(new { total = 0, page, limit, posters = Array.Empty<string>() });
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };
        var files = Directory.EnumerateFiles(dir)
            .Where(f => exts.Contains(Path.GetExtension(f)))
            .Select(Path.GetFileName)
            .Where(f => f != null && f.StartsWith("poster_", StringComparison.OrdinalIgnoreCase))
            .Cast<string>();
        if (!string.IsNullOrWhiteSpace(search))
            files = files.Where(f => f.Contains(search, StringComparison.OrdinalIgnoreCase));
        var sorted = files.OrderBy(f => f).ToList();
        var total = sorted.Count;
        var posters = sorted.Skip((page - 1) * limit).Take(limit).ToList();
        return Ok(new { total, page, limit, posters });
    }

    [HttpGet("videos/custom-categories")]
    public async Task<IActionResult> GetVideoCustomCategories([FromQuery] string? mediaType = null)
    {
        var q = _videoDb.Videos.AsQueryable();
        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            var types = mediaType.ToLower().Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
            if (types.Count == 1)
                q = q.Where(v => v.MediaType == types[0]);
            else
                q = q.Where(v => types.Contains(v.MediaType));
        }
        else
            q = q.Where(v => v.MediaType != "anime");

        // Count display-level items: 1 per TV series, 1 per movie/doc/standalone
        var rawVideos = await q
            .Where(v => v.CustomCategory != null && v.CustomCategory != "")
            .Select(v => new { v.CustomCategory, v.MediaType, v.SeriesName, v.Id })
            .ToListAsync();

        var catMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in rawVideos)
        {
            var displayKey = v.MediaType == "tv" && !string.IsNullOrEmpty(v.SeriesName)
                ? $"tv::{v.SeriesName.Trim().ToLowerInvariant()}"
                : $"id::{v.Id}";
            var cat = v.CustomCategory!;
            if (!catMap.TryGetValue(cat, out var set))
                catMap[cat] = set = new HashSet<string>();
            set.Add(displayKey);
        }

        var videoHiddenSet = new HashSet<string>(_userFavs.GetVideoCategoryHidden(CurrentUsername), StringComparer.OrdinalIgnoreCase);
        var categories = catMap
            .Where(kv => !videoHiddenSet.Contains(kv.Key))
            .Select(kv => new { name = kv.Key, count = kv.Value.Count })
            .OrderBy(c => c.name)
            .ToList();

        return Ok(categories);
    }

    [HttpGet("videos/series")]
    public async Task<IActionResult> GetVideoSeries()
    {
        var series = await _videoDb.Videos
            .Where(v => v.SeriesName != "")
            .GroupBy(v => v.SeriesName)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderBy(g => g.name)
            .ToListAsync();
        return Ok(series);
    }

    // ─── Videos Scanning ─────────────────────────────────────────────

    [HttpGet("collections")]
    public async Task<IActionResult> GetCollections()
    {
        var watchedIds = _userFavs.GetWatchedIds(CurrentUsername);
        var movies = await _videoDb.Videos
            .Where(v => v.CollectionId != null && v.MediaType == "movie")
            .Select(v => new { v.Id, v.CollectionId, v.CollectionName, v.CollectionPosterPath, v.PosterPath, v.Year, v.Title, v.CollectionTotalCount })
            .ToListAsync();

        var collections = movies
            .GroupBy(v => v.CollectionId!.Value)
            .Select(g =>
            {
                var ordered = g.OrderBy(v => v.Year ?? 9999).ToList();
                var watchedCount = ordered.Count(v => watchedIds.Contains(v.Id));
                var poster = ordered.FirstOrDefault(v => !string.IsNullOrEmpty(v.CollectionPosterPath))?.CollectionPosterPath
                          ?? ordered.FirstOrDefault(v => !string.IsNullOrEmpty(v.PosterPath))?.PosterPath;
                return new
                {
                    id = g.Key,
                    name = g.First().CollectionName ?? "Unknown Collection",
                    posterPath = poster,
                    ownedCount = ordered.Count,
                    totalCount = ordered.Max(v => v.CollectionTotalCount) ?? 0,
                    watchedCount,
                    movies = ordered.Select(v => new { v.Id, v.Title, v.Year, v.PosterPath })
                };
            })
            .OrderBy(c => c.name)
            .ToList();

        return Ok(collections);
    }

    // ─── Studios (browse movies by production company) ──────────────
    // Mirrors the Collections view. Studios are stored per movie in
    // Video.StudiosJson (up to 5 {name, logo}); a Disney/Pixar film therefore
    // appears under both studios. Grouped by NAME - the logo filename encodes the
    // TMDB id, and TMDB names are stable, so name is a reliable, logo-free key.

    private class StudioJsonEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")] public string? Name { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("logo")] public string? Logo { get; set; }
    }

    [HttpGet("studios")]
    public async Task<IActionResult> GetStudios()
    {
        var watchedIds = _userFavs.GetWatchedIds(CurrentUsername);
        var movies = await _videoDb.Videos
            .Where(v => v.MediaType == "movie" && v.StudiosJson != null && v.StudiosJson != "")
            .Select(v => new { v.Id, v.StudiosJson, v.PosterPath })
            .ToListAsync();

        // name -> aggregate
        var agg = new Dictionary<string, (string Name, string Logo, int Count, int Watched, List<string> Posters)>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in movies)
        {
            List<StudioJsonEntry>? studios;
            try { studios = JsonSerializer.Deserialize<List<StudioJsonEntry>>(m.StudiosJson!); }
            catch { continue; }
            if (studios == null) continue;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // a movie counts once per studio
            var isWatched = watchedIds.Contains(m.Id);
            foreach (var s in studios)
            {
                var name = s.Name?.Trim();
                if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
                agg.TryGetValue(name, out var cur);
                var logo = !string.IsNullOrEmpty(cur.Logo) ? cur.Logo : (s.Logo ?? "");
                var posters = cur.Posters ?? new List<string>();
                if (posters.Count < 4 && !string.IsNullOrEmpty(m.PosterPath)) posters.Add(m.PosterPath!);
                agg[name] = (name, logo, cur.Count + 1, cur.Watched + (isWatched ? 1 : 0), posters);
            }
        }

        var result = agg.Values
            .OrderByDescending(a => a.Count).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => new { name = a.Name, logo = a.Logo, count = a.Count, watchedCount = a.Watched, posters = a.Posters })
            .ToList();

        return Ok(result);
    }

    [HttpGet("studios/movies")]
    public async Task<IActionResult> GetStudioMovies([FromQuery] string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return BadRequest(new { error = "name is required" });

        var watchedIds = _userFavs.GetWatchedIds(CurrentUsername);
        // Contains() is a coarse SQL LIKE prefilter (EF parameterises it); the
        // exact studio match is confirmed in memory below.
        var candidates = await _videoDb.Videos
            .Where(v => v.MediaType == "movie" && v.StudiosJson != null && v.StudiosJson.Contains(name))
            .Select(v => new { v.Id, v.StudiosJson, v.Title, v.Year, v.PosterPath, v.Height, v.HdrFormat, v.Rating })
            .ToListAsync();

        var matched = candidates
            .Where(m =>
            {
                try
                {
                    var st = JsonSerializer.Deserialize<List<StudioJsonEntry>>(m.StudiosJson!);
                    return st != null && st.Any(x => string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
                }
                catch { return false; }
            })
            .OrderBy(m => m.Year ?? 9999)
            .Select(m => new
            {
                id = m.Id, title = m.Title, year = m.Year, posterPath = m.PosterPath,
                height = m.Height, hdrFormat = m.HdrFormat, rating = m.Rating,
                isWatched = watchedIds.Contains(m.Id)
            })
            .ToList();

        // Logo for the header (from the first movie that carries one).
        string logo = "";
        foreach (var m in candidates)
        {
            try
            {
                var st = JsonSerializer.Deserialize<List<StudioJsonEntry>>(m.StudiosJson!);
                var hit = st?.FirstOrDefault(x => string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
                if (hit != null && !string.IsNullOrEmpty(hit.Logo)) { logo = hit.Logo!; break; }
            }
            catch { }
        }

        return Ok(new
        {
            name,
            logo,
            count = matched.Count,
            watchedCount = matched.Count(v => v.isWatched),
            movies = matched
        });
    }

    // ─── Crew: browse movies/TV by director or writer ───────────────
    // Directors/writers are stored per video in Video.DirectorJson / WriterJson
    // as [{name, photo}] (photo is a local file in assets/videometa/). Unlike
    // actors they have NO dedicated table/id, so they are keyed by NAME.
    // A person appears once per WORK: a standalone movie/documentary is one work;
    // a TV/anime series counts once regardless of how many of its episodes credit
    // the person. Spans every media type - this is the movies-and-TV browse the
    // user asked for after Studios.

    private class CrewJsonEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")] public string? Name { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("photo")] public string? Photo { get; set; }
    }

    private class CrewWorkState { public int Total; public int Watched; }
    private class CrewAgg
    {
        public string Name = "";
        public string Photo = "";
        public Dictionary<string, CrewWorkState> Works = new();
        public List<string> Posters = new();
    }

    private static bool CrewJsonHasName(string? json, string name)
    {
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            var people = JsonSerializer.Deserialize<List<CrewJsonEntry>>(json);
            return people != null && people.Any(p => string.Equals(p.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    [HttpGet("crew")]
    public async Task<IActionResult> GetCrew([FromQuery] string role)
    {
        role = (role ?? "director").Trim().ToLowerInvariant();
        if (role != "writer") role = "director";
        var writer = role == "writer";
        var watchedIds = _userFavs.GetWatchedIds(CurrentUsername);

        var vids = await _videoDb.Videos
            .Where(v => writer ? (v.WriterJson != null && v.WriterJson != "")
                               : (v.DirectorJson != null && v.DirectorJson != ""))
            .Select(v => new { v.Id, v.DirectorJson, v.WriterJson, v.PosterPath, v.MediaType, v.SeriesName })
            .ToListAsync();

        var agg = new Dictionary<string, CrewAgg>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in vids)
        {
            var json = writer ? v.WriterJson : v.DirectorJson;
            if (string.IsNullOrEmpty(json)) continue;
            List<CrewJsonEntry>? people;
            try { people = JsonSerializer.Deserialize<List<CrewJsonEntry>>(json); }
            catch { continue; }
            if (people == null) continue;

            // Group TV/anime episodes under their series; movies/standalone by id.
            var workKey = !string.IsNullOrEmpty(v.SeriesName)
                ? "s:" + v.SeriesName.Trim().ToLowerInvariant()
                : "m:" + v.Id;
            var isWatched = watchedIds.Contains(v.Id);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // a person counts once per video
            foreach (var p in people)
            {
                var name = p.Name?.Trim();
                if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
                if (!agg.TryGetValue(name, out var a)) { a = new CrewAgg { Name = name }; agg[name] = a; }
                if (string.IsNullOrEmpty(a.Photo) && !string.IsNullOrEmpty(p.Photo)) a.Photo = p.Photo!;
                if (!a.Works.TryGetValue(workKey, out var ws)) { ws = new CrewWorkState(); a.Works[workKey] = ws; }
                ws.Total++;
                if (isWatched) ws.Watched++;
                if (a.Posters.Count < 4 && !string.IsNullOrEmpty(v.PosterPath)) a.Posters.Add(v.PosterPath!);
            }
        }

        var result = agg.Values
            .Select(a => new
            {
                name = a.Name,
                photo = a.Photo,
                count = a.Works.Count,
                // A work is "watched" only when every credited video of it is watched
                // (so a fully-seen series counts, a partly-seen one does not).
                watchedCount = a.Works.Values.Count(w => w.Total > 0 && w.Watched >= w.Total),
                posters = a.Posters
            })
            .OrderBy(a => a.name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(result);
    }

    [HttpGet("crew/videos")]
    public async Task<IActionResult> GetCrewVideos([FromQuery] string role, [FromQuery] string name)
    {
        role = (role ?? "director").Trim().ToLowerInvariant();
        if (role != "writer") role = "director";
        var writer = role == "writer";
        name = (name ?? "").Trim();
        if (name.Length == 0) return BadRequest(new { error = "name is required" });

        var watchedIds = _userFavs.GetWatchedIds(CurrentUsername);

        // Contains() is a coarse SQL LIKE prefilter; exact match confirmed in memory.
        var candidates = await _videoDb.Videos
            .Where(v => writer ? (v.WriterJson != null && v.WriterJson.Contains(name))
                               : (v.DirectorJson != null && v.DirectorJson.Contains(name)))
            .Select(v => new { v.Id, v.DirectorJson, v.WriterJson, v.Title, v.Year, v.PosterPath, v.BackdropPath, v.Height, v.HdrFormat, v.Rating, v.MediaType, v.SeriesName })
            .ToListAsync();

        var matched = candidates
            .Where(v => CrewJsonHasName(writer ? v.WriterJson : v.DirectorJson, name))
            .ToList();

        // Header portrait: first credited work that carries a photo.
        string photo = "";
        foreach (var v in matched)
        {
            try
            {
                var people = JsonSerializer.Deserialize<List<CrewJsonEntry>>((writer ? v.WriterJson : v.DirectorJson)!);
                var hit = people?.FirstOrDefault(p => string.Equals(p.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
                if (hit != null && !string.IsNullOrEmpty(hit.Photo)) { photo = hit.Photo!; break; }
            }
            catch { }
        }

        var works = new List<object>();
        int watchedCount = 0;

        // Standalone titles (movies / documentaries with no series name)
        foreach (var v in matched.Where(x => string.IsNullOrEmpty(x.SeriesName)).OrderBy(x => x.Year ?? 9999).ThenBy(x => x.Title))
        {
            var w = watchedIds.Contains(v.Id);
            if (w) watchedCount++;
            works.Add(new
            {
                kind = "movie",
                id = v.Id, title = v.Title, year = v.Year, posterPath = v.PosterPath,
                height = v.Height, hdrFormat = v.HdrFormat, rating = v.Rating,
                mediaType = v.MediaType, isWatched = w
            });
        }

        // TV / anime series - grouped by series name (credited episodes only)
        foreach (var g in matched.Where(x => !string.IsNullOrEmpty(x.SeriesName))
                                  .GroupBy(x => x.SeriesName!, StringComparer.OrdinalIgnoreCase)
                                  .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var eps = g.ToList();
            var poster = eps.FirstOrDefault(e => !string.IsNullOrEmpty(e.PosterPath))?.PosterPath
                      ?? eps.FirstOrDefault(e => !string.IsNullOrEmpty(e.BackdropPath))?.BackdropPath;
            var mt = eps.Any(e => e.MediaType == "anime") ? "anime" : eps[0].MediaType;
            var w = eps.All(e => watchedIds.Contains(e.Id));
            if (w) watchedCount++;
            works.Add(new
            {
                kind = "series",
                seriesName = g.Key,
                mediaType = mt,
                posterPath = poster,
                rating = eps.Max(e => e.Rating),
                episodeCount = eps.Count,   // episodes THIS person is credited on
                isWatched = w
            });
        }

        return Ok(new { role, name, photo, count = works.Count, watchedCount, works });
    }

    private static string NormalizeCrewName(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in (s ?? "").ToLowerInvariant())
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    // Resolve a director/writer NAME to an Actor row (reusing the whole actor TMDB
    // pipeline - bio, social, filmography, known-for - which is all keyed by an
    // Actor row + TMDB person id). Rows created here are flagged HiddenFromBrowse so
    // they never appear in the Actors grid. If the person already exists as a real
    // actor (matched by TMDB id) that row is reused as-is. Results are stored in the
    // DB permanently; the actor endpoints only re-hit TMDB on their own staleness
    // windows, so historical data is preserved.
    [HttpGet("crew/resolve")]
    public async Task<IActionResult> ResolveCrewPerson([FromQuery] string role, [FromQuery] string name)
    {
        role = (role ?? "director").Trim().ToLowerInvariant();
        if (role != "writer") role = "director";
        name = (name ?? "").Trim();
        if (name.Length == 0) return BadRequest(new { error = "name is required" });

        var norm = NormalizeCrewName(name);

        // Already resolved before (either a real actor or a prior crew resolve)?
        var existing = await _actorsDb.Actors
            .Where(a => a.NormalizedName == norm && a.TmdbId != null)
            .OrderBy(a => a.HiddenFromBrowse ? 1 : 0) // prefer a real actor row if both exist
            .FirstOrDefaultAsync();
        if (existing != null)
            return Ok(new { actorId = existing.Id, tmdbId = existing.TmdbId, imageCached = existing.ImageCached, found = true });

        var key = _config.Config.Metadata.EffectiveTmdbApiKey;
        if (string.IsNullOrWhiteSpace(key))
            return Ok(new { actorId = (int?)null, found = false });

        int personId = 0;
        string? profilePath = null;
        string department = role == "writer" ? "Writing" : "Directing";
        try
        {
            using var http = Http(12);
            var url = $"https://api.themoviedb.org/3/search/person?api_key={key}&query={Uri.EscapeDataString(name)}";
            var resp = await http.GetAsync(url);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0)
                {
                    var list = results.EnumerateArray().ToList();
                    // Prefer an exact (normalised) name match; otherwise the top (most popular) result.
                    JsonElement pick = list.FirstOrDefault(r =>
                        r.TryGetProperty("name", out var rn) && rn.ValueKind == JsonValueKind.String &&
                        NormalizeCrewName(rn.GetString() ?? "") == norm);
                    if (pick.ValueKind != JsonValueKind.Object) pick = list[0];

                    if (pick.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
                        personId = idEl.GetInt32();
                    if (pick.TryGetProperty("profile_path", out var pp) && pp.ValueKind == JsonValueKind.String)
                        profilePath = pp.GetString();
                    if (pick.TryGetProperty("known_for_department", out var kd) && kd.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(kd.GetString()))
                        department = kd.GetString()!;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TMDB person search failed for crew {Name}", name);
        }

        if (personId == 0)
            return Ok(new { actorId = (int?)null, found = false });

        // Someone else may already hold this TMDB id (e.g. an actor who also directs).
        var byTmdb = await _actorsDb.Actors.FirstOrDefaultAsync(a => a.TmdbId == personId);
        if (byTmdb != null)
            return Ok(new { actorId = byTmdb.Id, tmdbId = byTmdb.TmdbId, imageCached = byTmdb.ImageCached, found = true });

        // Download the profile photo to the shared actor-photo cache (same convention).
        string? imageCached = null;
        if (!string.IsNullOrEmpty(profilePath))
        {
            try
            {
                var actorsDir = Path.Combine(AppContext.BaseDirectory, "assets", "actors");
                Directory.CreateDirectory(actorsDir);
                var photoFile = $"actor_{personId}.jpg";
                var photoFull = Path.Combine(actorsDir, photoFile);
                if (System.IO.File.Exists(photoFull))
                {
                    imageCached = photoFile;
                }
                else
                {
                    using var http = Http(12);
                    var imgResp = await http.GetAsync($"https://image.tmdb.org/t/p/w300{profilePath}");
                    if (imgResp.IsSuccessStatusCode)
                    {
                        var bytes = await imgResp.Content.ReadAsByteArrayAsync();
                        if (bytes.Length > 500)
                        {
                            await System.IO.File.WriteAllBytesAsync(photoFull, bytes);
                            imageCached = photoFile;
                        }
                    }
                }
            }
            catch { /* photo is best-effort */ }
        }

        var actor = new Actor
        {
            Name = name,
            NormalizedName = norm,
            TmdbId = personId,
            ProfilePath = profilePath,
            ImageCached = imageCached,
            KnownForDepartment = department,
            HiddenFromBrowse = true,
            LastUpdated = DateTime.UtcNow
        };
        _actorsDb.Actors.Add(actor);
        await _actorsDb.SaveChangesAsync();

        return Ok(new { actorId = actor.Id, tmdbId = actor.TmdbId, imageCached = actor.ImageCached, found = true });
    }

    [HttpGet("collections/{id:int}")]
    public async Task<IActionResult> GetCollection(int id)
    {
        var watchedIds = _userFavs.GetWatchedIds(CurrentUsername);
        var favIds    = _userFavs.GetFavouriteIds(CurrentUsername, "video");

        // Local movies that belong to this collection
        var localMovies = await _videoDb.Videos
            .Where(v => v.CollectionId == id && v.MediaType == "movie")
            .OrderBy(v => v.Year ?? 9999)
            .ToListAsync();

        if (!localMovies.Any()) return NotFound();

        var collectionName   = localMovies[0].CollectionName ?? "Unknown Collection";
        var collectionPoster = localMovies.FirstOrDefault(v => !string.IsNullOrEmpty(v.CollectionPosterPath))?.CollectionPosterPath
                            ?? localMovies.FirstOrDefault(v => !string.IsNullOrEmpty(v.PosterPath))?.PosterPath;

        // Build lookup: TMDB movie ID → ALL local copies sharing it.
        // A collection can legitimately contain several owned copies of the same movie under
        // one TMDB id - e.g. the theatrical AND extended editions of a Lord of the Rings film.
        // We keep the full list per id (grouped into a versions[] array below) rather than
        // collapsing to First(); collapsing silently hid every extra edition from the detail
        // view even though the library owned them.
        var localByTmdbId = localMovies
            .Where(v => !string.IsNullOrEmpty(v.TmdbId))
            .GroupBy(v => v.TmdbId!)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Tracks which local rows have already been placed on a TMDB part, so any owned file whose
        // TMDB id matches no part (or has no id) can be appended afterwards instead of vanishing.
        var usedIds = new HashSet<int>();

        // Declared here (before the local functions that capture it) - a local function may not
        // reference a local variable declared textually later.
        var allMovies = new List<object>();

        string ResLabel(int h) => h >= 2160 ? "4K" : h >= 1080 ? "1080p" : h >= 720 ? "720p" : h > 0 ? h + "p" : "";

        // One owned copy of a film, described enough for the client to label/pick it.
        object BuildVersion(Video mv) => new
        {
            id = mv.Id,
            edition = mv.Edition ?? "",
            resolution = ResLabel(mv.Height),
            height = mv.Height,
            hdrFormat = mv.HdrFormat ?? "",
            format = mv.Format ?? "",
            sizeBytes = mv.SizeBytes,
            duration = mv.Duration,
            isWatched = watchedIds.Contains(mv.Id)
        };

        // One card for a film, carrying every owned version. The highest-resolution copy is the
        // "primary" that drives the card's poster/title; watched/favourite are true if ANY copy is.
        object BuildOwnedEntry(List<Video> vers, string? cachedPoster, string? tmdbPoster)
        {
            var ordered = vers.OrderByDescending(x => x.Height).ThenByDescending(x => x.SizeBytes).ToList();
            var primary = ordered[0];
            return new
            {
                id = primary.Id, title = primary.Title, year = primary.Year,
                duration = primary.Duration, sizeBytes = ordered.Sum(x => x.SizeBytes),
                posterPath = primary.PosterPath ?? cachedPoster, thumbnailPath = primary.ThumbnailPath,
                overview = primary.Overview, rating = primary.Rating,
                hdrFormat = primary.HdrFormat, format = primary.Format,
                width = primary.Width, height = primary.Height, genre = primary.Genre,
                isWatched = ordered.Any(x => watchedIds.Contains(x.Id)),
                isFavourite = ordered.Any(x => favIds.Contains(x.Id)),
                owned = true, tmdbPosterUrl = tmdbPoster,
                versionCount = ordered.Count,
                versions = ordered.Select(BuildVersion).ToList()
            };
        }

        // Group a set of local rows into owned cards: copies sharing a TMDB id become one
        // multi-version card; rows without an id each stand alone.
        void AppendOwnedGroups(IEnumerable<Video> rows)
        {
            var list = rows.ToList();
            foreach (var grp in list.Where(v => !string.IsNullOrEmpty(v.TmdbId)).GroupBy(v => v.TmdbId!))
                allMovies.Add(BuildOwnedEntry(grp.ToList(), null, null));
            foreach (var mv in list.Where(v => string.IsNullOrEmpty(v.TmdbId)))
                allMovies.Add(BuildOwnedEntry(new List<Video> { mv }, null, null));
        }

        // Try to fetch full collection from TMDB so we can show movies not yet in the library
        var tmdbKey = _config.Config.Metadata.EffectiveTmdbApiKey;

        if (!string.IsNullOrWhiteSpace(tmdbKey))
        {
            try
            {
                using var http = Http(10);
                var json = await http.GetStringAsync(
                    $"https://api.themoviedb.org/3/collection/{id}?api_key={tmdbKey}");
                using var doc = JsonDocument.Parse(json);

                var parts = doc.RootElement.GetProperty("parts")
                    .EnumerateArray()
                    .OrderBy(p => p.TryGetProperty("release_date", out var d) ? d.GetString() ?? "" : "")
                    .ToList();

                var metaDir = Path.Combine(AppContext.BaseDirectory, "assets", "videometa");
                Directory.CreateDirectory(metaDir);

                foreach (var part in parts)
                {
                    var tmdbMovieId = part.GetProperty("id").GetInt32().ToString();
                    var tmdbTitle   = part.TryGetProperty("title",   out var t)  ? t.GetString()  ?? "" : "";
                    var relDate     = part.TryGetProperty("release_date", out var rd) ? rd.GetString() ?? "" : "";
                    var tmdbYear    = relDate.Length >= 4 && int.TryParse(relDate[..4], out var y) ? (int?)y : null;
                    var posterPath  = part.TryGetProperty("poster_path",  out var pp) ? pp.GetString() : null;
                    var tmdbRating  = part.TryGetProperty("vote_average", out var va) ? va.GetDouble() : 0.0;
                    var overview    = part.TryGetProperty("overview", out var ov) ? ov.GetString() ?? "" : "";

                    // Cache poster locally so repeat views don't re-download from TMDB
                    string? cachedPosterPath = null;
                    if (!string.IsNullOrEmpty(posterPath))
                    {
                        var localFilename = $"col_{tmdbMovieId}.jpg";
                        var localFile = Path.Combine(metaDir, localFilename);
                        if (!System.IO.File.Exists(localFile))
                        {
                            try
                            {
                                var imgBytes = await http.GetByteArrayAsync($"https://image.tmdb.org/t/p/w342{posterPath}");
                                if (imgBytes.Length > 500) await System.IO.File.WriteAllBytesAsync(localFile, imgBytes);
                            }
                            catch { /* fall back to TMDB URL below */ }
                        }
                        cachedPosterPath = System.IO.File.Exists(localFile) ? localFilename : null;
                    }
                    var tmdbPoster = cachedPosterPath == null && !string.IsNullOrEmpty(posterPath)
                        ? $"https://image.tmdb.org/t/p/w342{posterPath}" : null;

                    if (localByTmdbId.TryGetValue(tmdbMovieId, out var owned))
                    {
                        foreach (var mv in owned) usedIds.Add(mv.Id);
                        allMovies.Add(BuildOwnedEntry(owned, cachedPosterPath, tmdbPoster));
                    }
                    else
                    {
                        allMovies.Add(new
                        {
                            id = 0, title = tmdbTitle, year = tmdbYear,
                            duration = 0.0, sizeBytes = 0L,
                            posterPath = cachedPosterPath, thumbnailPath = (string?)null,
                            overview, rating = tmdbRating,
                            hdrFormat = "", format = "", width = 0, height = 0, genre = "",
                            isWatched = false, isFavourite = false,
                            owned = false, tmdbPosterUrl = tmdbPoster
                        });
                    }
                }

                // Owned files whose TMDB id matches no part in the collection (mis-tagged, or a
                // bonus/spin-off) would otherwise be invisible here - append them as their own cards.
                AppendOwnedGroups(localMovies.Where(v => !usedIds.Contains(v.Id)));
            }
            catch { /* fall through to local-only */ }
        }

        // Fallback: no TMDB key or fetch failed - show local movies only, still grouped by version.
        if (!allMovies.Any())
        {
            AppendOwnedGroups(localMovies);
        }

        return Ok(new
        {
            id,
            name        = collectionName,
            posterPath  = collectionPoster,
            totalCount  = allMovies.Count,
            ownedCount  = localMovies.Count,
            watchedCount = localMovies.Count(v => watchedIds.Contains(v.Id)),
            movies      = allMovies
        });
    }

    [HttpPost("scan/videos")]
    public IActionResult StartVideoScan([FromQuery] string? scope = null)
    {
        if (_videoScanner.IsScanning)
            return Conflict(new { message = "Videos scan already in progress" });
        _ = _videoScanner.StartScanAsync(scope);
        _semantic.RequestAutoIndex();
        return Ok(new { message = "Videos scan started" });
    }

    [HttpGet("scan/videos/status")]
    public IActionResult GetVideoScanStatus()
    {
        var p = _videoScanner.CurrentProgress;
        return Ok(new
        {
            isScanning = _videoScanner.IsScanning,
            status = p.Status,
            message = p.Message,
            totalFiles = p.TotalFiles,
            processedFiles = p.ProcessedFiles,
            newVideos = p.NewVideos,
            updatedVideos = p.UpdatedVideos,
            errorCount = p.ErrorCount,
            percentComplete = p.PercentComplete
        });
    }

    [HttpPost("videos/refresh-hdr")]
    public IActionResult RefreshHdrMetadata()
    {
        if (!_ffmpeg.IsProbeAvailable)
            return BadRequest(new { message = "ffprobe not available" });
        if (_videoScanner.IsScanning)
            return Conflict(new { message = "A videos scan is already in progress. Try again once it finishes." });
        _ = _videoScanner.RefreshHdrMetadataAsync();
        return Ok(new { message = "HDR metadata refresh started in background..." });
    }

    [HttpPost("video/generate-thumbnails")]
    public IActionResult GenerateVideoThumbnails()
    {
        if (!_ffmpeg.IsAvailable)
            return BadRequest(new { message = "FFmpeg not available" });
        _ = _videoScanner.GenerateAllThumbnailsAsync();
        return Ok(new { message = "Generating video thumbnails in background..." });
    }

    // ─── Anime (Jikan/MyAnimeList) ──────────────────────────────────

    [HttpPost("scan/anime")]
    public IActionResult StartAnimeScan()
    {
        _ = _animeScanner.StartScanAsync();
        _semantic.RequestAutoIndex();
        return Ok(new { message = "Anime scan started" });
    }

    [HttpGet("scan/anime/status")]
    public IActionResult GetAnimeScanStatus()
    {
        var p = _animeScanner.CurrentProgress;
        return Ok(new
        {
            isScanning = _animeScanner.IsScanning,
            status = p.Status,
            message = p.Message,
            totalFiles = p.TotalFiles,
            processedFiles = p.ProcessedFiles,
            newVideos = p.NewVideos,
            updatedVideos = p.UpdatedVideos,
            errorCount = p.ErrorCount,
            percentComplete = p.PercentComplete
        });
    }

    // ─── Dynamic Library Cleanup ────────────────────────────────────

    [HttpGet("dynamic-clean/status")]
    public IActionResult GetDynamicCleanStatus()
    {
        var r = _dynamicClean.LastResult;
        return Ok(new
        {
            isRunning  = _dynamicClean.IsRunning,
            lastRunTime = _dynamicClean.LastRunTime,
            lastResult = r == null ? null : new
            {
                r.TotalRemoved,
                r.TotalChecked,
                r.TracksRemoved,
                r.TracksChecked,
                r.VideosRemoved,
                r.VideosChecked,
                r.MusicVideosRemoved,
                r.MusicVideosChecked,
                r.EBooksRemoved,
                r.EBooksChecked,
                r.AudioBooksRemoved,
                r.AudioBooksChecked,
                r.PicturesRemoved,
                r.PicturesChecked,
                r.LastError,
                r.CompletedAt,
                r.DurationSeconds
            }
        });
    }

[HttpPost("dynamic-clean/run-now")]
    public IActionResult TriggerDynamicClean()
    {
        if (_dynamicClean.IsRunning)
            return Ok(new { message = "already_running" });
        _ = _dynamicClean.RunCleanAsync();
        return Ok(new { message = "started" });
    }

    // ─── Most Played (all media) ────────────────────────────────────

    [HttpGet("videos/mostplayed")]
    public async Task<IActionResult> GetMostPlayedVideos([FromQuery] int limit = 10)
    {
        var userPlays = _userFavs.GetMostPlayed(CurrentUsername, "video", limit);
        if (userPlays.Count == 0) return Ok(Array.Empty<object>());

        var ids = userPlays.Select(p => p.MediaId).ToList();
        var videos = await _videoDb.Videos.Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.Title, v.Year, v.MediaType, v.SeriesName, v.Season, v.Episode, v.ThumbnailPath, v.PosterPath, v.Duration })
            .ToListAsync();

        var playMap = userPlays.ToDictionary(p => p.MediaId, p => p.Count);
        var result = videos
            .Select(v => new { v.Id, v.Title, v.Year, v.MediaType, v.SeriesName, v.Season, v.Episode, v.ThumbnailPath, v.PosterPath, v.Duration, PlayCount = playMap.GetValueOrDefault(v.Id) })
            .OrderByDescending(v => v.PlayCount)
            .ToList();
        return Ok(result);
    }

    [HttpGet("musicvideos/mostplayed")]
    public async Task<IActionResult> GetMostPlayedMusicVideos([FromQuery] int limit = 10)
    {
        var userPlays = _userFavs.GetMostPlayed(CurrentUsername, "musicvideo", limit);
        if (userPlays.Count == 0) return Ok(Array.Empty<object>());

        var ids = userPlays.Select(p => p.MediaId).ToList();
        var videos = await _mvDb.MusicVideos.Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.Title, v.Artist, v.ThumbnailPath, v.Duration })
            .ToListAsync();

        var playMap = userPlays.ToDictionary(p => p.MediaId, p => p.Count);
        var result = videos
            .Select(v => new { v.Id, v.Title, v.Artist, v.ThumbnailPath, v.Duration, PlayCount = playMap.GetValueOrDefault(v.Id) })
            .OrderByDescending(v => v.PlayCount)
            .ToList();
        return Ok(result);
    }

    [HttpGet("radio/mostplayed")]
    public IActionResult GetMostPlayedRadio([FromQuery] int limit = 10)
    {
        var userPlays = _userFavs.GetMostPlayed(CurrentUsername, "radio", limit);
        if (userPlays.Count == 0) return Ok(Array.Empty<object>());

        var stations = _radio.GetStations();
        var playMap = userPlays.ToDictionary(p => p.MediaId, p => p.Count);
        var result = stations
            .Where(s => playMap.ContainsKey(s.Id))
            .Select(s => { s.PlayCount = playMap.GetValueOrDefault(s.Id); return s; })
            .OrderByDescending(s => s.PlayCount)
            .Take(limit)
            .ToList();
        return Ok(result);
    }

    [HttpPost("radio/{id}/play")]
    public IActionResult TrackRadioPlay(int id)
    {
        var stations = _radio.GetStations();
        var station = stations.FirstOrDefault(s => s.Id == id);
        if (station == null) return NotFound();
        _userFavs.IncrementPlayCount(CurrentUsername, "radio", id);
        return Ok(new { success = true });
    }

    [HttpPost("playcounts/reset")]
    public IActionResult ResetPlayCounts()
    {
        _userFavs.ResetPlayCounts(CurrentUsername);
        return Ok(new { success = true, message = "Play counts have been reset" });
    }

    // ─── Internet TV ──────────────────────────────────────────────────

    [HttpGet("tvchannels")]
    public async Task<IActionResult> GetTvChannels(
        [FromQuery] string? country = null,
        [FromQuery] string? genre = null,
        [FromQuery] string? language = null,
        [FromQuery] string? search = null)
    {
        var query = _tvDb.TvChannels.AsQueryable();

        if (!string.IsNullOrWhiteSpace(country))
            query = query.Where(c => c.Country == country);
        if (!string.IsNullOrWhiteSpace(genre))
            query = query.Where(c => c.Genre == genre);
        if (!string.IsNullOrWhiteSpace(language))
            query = query.Where(c => c.Language == language);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(q) || c.Description.ToLower().Contains(q) || c.Country.ToLower().Contains(q) || c.Language.ToLower().Contains(q));
        }

        var channels = await query.OrderBy(c => c.Name).ToListAsync();

        var favIds = _userFavs.GetFavouriteIds(CurrentUsername, "tvchannel");
        foreach (var c in channels) c.IsFavourite = favIds.Contains(c.Id);

        var countries = await _tvDb.TvChannels.Where(c => c.Country != "").Select(c => c.Country).Distinct().OrderBy(c => c).ToListAsync();
        var genres = await _tvDb.TvChannels.Where(c => c.Genre != "").Select(c => c.Genre).Distinct().OrderBy(g => g).ToListAsync();
        var languages = await _tvDb.TvChannels.Where(c => c.Language != "").Select(c => c.Language).Distinct().OrderBy(l => l).ToListAsync();

        return Ok(new { total = channels.Count, countries, genres, languages, channels });
    }

    [HttpPost("tvchannels/import")]
    [RequestSizeLimit(50_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 50_000_000)]
    public async Task<IActionResult> ImportM3u(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file uploaded" });

        if (!file.FileName.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) &&
            !file.FileName.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Only .m3u and .m3u8 files are supported" });

        using var stream = file.OpenReadStream();
        var (channels, epgUrl) = _tvService.ParseM3u(stream, file.FileName);

        if (channels.Count == 0)
            return BadRequest(new { message = "No channels found in the M3U file" });

        var result = await ImportChannelsAsync(channels);
        if (!string.IsNullOrEmpty(epgUrl))
            await _epgService.RegisterSourceAsync(epgUrl, "Auto-detected from " + file.FileName, isAuto: true);
        _logger.LogInformation("TV import from {File}: {I} new, {U} updated, {S} unchanged", file.FileName, result.imported, result.updated, result.skipped);
        return Ok(new { message = BuildImportMessage(result, channels.Count), result.imported, result.updated, result.skipped, total = channels.Count, epgDetected = !string.IsNullOrEmpty(epgUrl) });
    }

    [HttpPost("tvchannels/import-url")]
    public async Task<IActionResult> ImportM3uFromUrl([FromBody] ImportUrlRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.Url))
            return BadRequest(new { message = "URL is required" });

        if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return BadRequest(new { message = "Invalid URL - must be http or https" });

        (List<TvChannel> channels, string? epgUrl) parseResult;
        try
        {
            parseResult = await _tvService.ParseM3uFromUrlAsync(req.Url);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch M3U from URL: {Url}", req.Url);
            return BadRequest(new { message = $"Failed to fetch M3U: {ex.Message}" });
        }

        var (channels2, epgUrl2) = parseResult;
        if (channels2.Count == 0)
            return BadRequest(new { message = "No channels found at that URL" });

        var result = await ImportChannelsAsync(channels2);
        if (!string.IsNullOrEmpty(epgUrl2))
            await _epgService.RegisterSourceAsync(epgUrl2, "Auto-detected from M3U", isAuto: true);
        _logger.LogInformation("TV import from URL {Url}: {I} new, {U} updated, {S} unchanged", req.Url, result.imported, result.updated, result.skipped);
        return Ok(new { message = BuildImportMessage(result, channels2.Count), result.imported, result.updated, result.skipped, total = channels2.Count, epgDetected = !string.IsNullOrEmpty(epgUrl2) });
    }

    private static string BuildImportMessage((int imported, int updated, int skipped) r, int total)
    {
        var parts = new List<string>();
        if (r.imported > 0) parts.Add($"{r.imported} new channel{(r.imported != 1 ? "s" : "")} added");
        if (r.updated > 0) parts.Add($"{r.updated} existing channel{(r.updated != 1 ? "s" : "")} updated");
        if (r.skipped > 0) parts.Add($"{r.skipped} unchanged");
        return parts.Count > 0 ? string.Join(", ", parts) + $" (of {total} total)" : $"No changes ({total} already up to date)";
    }

    private async Task<(int imported, int updated, int skipped)> ImportChannelsAsync(List<TvChannel> channels)
    {
        // Load all existing channels into a dictionary keyed by stream URL.
        // This lets us both detect duplicates AND update their metadata - one query, no N+1.
        var existing = await _tvDb.TvChannels
            .ToDictionaryAsync(c => c.StreamUrl, c => c, StringComparer.OrdinalIgnoreCase);

        int imported = 0, updated = 0, skipped = 0, pendingChanges = 0;
        const int batchSize = 500;

        foreach (var channel in channels)
        {
            if (existing.TryGetValue(channel.StreamUrl, out var dbChannel))
            {
                // Enrich existing record with any metadata the new playlist provides,
                // but never overwrite a field that already has a value.
                bool changed = false;
                if (!string.IsNullOrEmpty(channel.Country) && string.IsNullOrEmpty(dbChannel.Country))
                { dbChannel.Country = channel.Country; changed = true; }
                if (!string.IsNullOrEmpty(channel.Genre) && string.IsNullOrEmpty(dbChannel.Genre))
                { dbChannel.Genre = channel.Genre; changed = true; }
                if (!string.IsNullOrEmpty(channel.Language) && string.IsNullOrEmpty(dbChannel.Language))
                { dbChannel.Language = channel.Language; changed = true; }

                if (changed) { updated++; pendingChanges++; }
                else skipped++;
            }
            else
            {
                _tvDb.TvChannels.Add(channel);
                existing[channel.StreamUrl] = channel;
                imported++;
                pendingChanges++;
            }

            if (pendingChanges >= batchSize)
            {
                await _tvDb.SaveChangesAsync();
                pendingChanges = 0;
            }
        }

        if (pendingChanges > 0)
            await _tvDb.SaveChangesAsync();

        return (imported, updated, skipped);
    }

    [HttpDelete("tvchannels/{id}")]
    public async Task<IActionResult> DeleteTvChannel(int id)
    {
        var channel = await _tvDb.TvChannels.FindAsync(id);
        if (channel == null) return NotFound();
        _tvDb.TvChannels.Remove(channel);
        await _tvDb.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("tvchannels/{id}/favourite")]
    public IActionResult ToggleTvChannelFavourite(int id)
    {
        var isFav = _userFavs.ToggleFavourite(CurrentUsername, "tvchannel", id);
        return Ok(new { isFavourite = isFav });
    }

    [HttpPost("tvchannels/fetch-logos")]
    public async Task<IActionResult> FetchTvLogos()
    {
        if (_tvService.FetchProgress.IsFetching)
            return Conflict(new { message = "Logo fetch already in progress" });

        var channels = await _tvDb.TvChannels.ToListAsync();
        if (channels.Count == 0)
            return BadRequest(new { message = "No TV channels to fetch logos for. Import channels first." });

        var scopeFactory = _scopeFactory;
        _ = Task.Run(async () =>
        {
            await _tvService.FetchLogosAsync(channels, updatedChannel =>
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<TvChannelsDbContext>();
                    var dbChannel = db.TvChannels.Find(updatedChannel.Id);
                    if (dbChannel != null)
                    {
                        dbChannel.Logo = updatedChannel.Logo;
                        db.SaveChanges();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save logo update for channel {Id}", updatedChannel.Id);
                }
            });
        });

        return Ok(new { message = "Logo fetch started" });
    }

    [HttpGet("tvchannels/fetch-logos/status")]
    public IActionResult GetTvLogoFetchStatus()
    {
        var p = _tvService.FetchProgress;
        return Ok(new { isFetching = p.IsFetching, progress = p.Progress, total = p.Total, success = p.Success, failed = p.Failed, status = p.Status });
    }

    // ─── Radio ────────────────────────────────────────────────────────

    [HttpGet("radio/stations")]
    public IActionResult GetRadioStations(
        [FromQuery] string? country = null,
        [FromQuery] string? genre = null,
        [FromQuery] string? search = null)
    {
        var stations = _radio.GetStations();

        // Mark favourites for the current user
        var favIds = _userFavs.GetFavouriteIds(CurrentUsername, "radio");
        foreach (var s in stations) s.IsFavourite = favIds.Contains(s.Id);

        if (!string.IsNullOrWhiteSpace(country))
            stations = stations.Where(s => s.Country.Equals(country, StringComparison.OrdinalIgnoreCase)).ToList();

        if (!string.IsNullOrWhiteSpace(genre))
            stations = stations.Where(s => s.Genre.Equals(genre, StringComparison.OrdinalIgnoreCase)).ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.ToLowerInvariant();
            stations = stations.Where(s =>
                s.Name.ToLower().Contains(q) ||
                s.Description.ToLower().Contains(q) ||
                s.Country.ToLower().Contains(q) ||
                s.Genre.ToLower().Contains(q)).ToList();
        }

        return Ok(new
        {
            total = stations.Count,
            countries = _radio.GetCountries(),
            genres = _radio.GetGenres(),
            stations
        });
    }

    [HttpPost("radio/fetch-logos")]
    public IActionResult FetchRadioLogos()
    {
        if (_radio.IsFetchingLogos)
            return Conflict(new { message = "Logo fetch already in progress" });

        _ = _radio.FetchLogosAsync();
        return Ok(new { message = "Logo fetch started" });
    }

    [HttpGet("radio/fetch-logos/status")]
    public IActionResult GetFetchLogosStatus()
    {
        return Ok(new
        {
            isFetching = _radio.IsFetchingLogos,
            progress = _radio.FetchProgress,
            total = _radio.FetchTotal,
            success = _radio.FetchSuccess,
            failed = _radio.FetchFailed,
            status = _radio.FetchStatus
        });
    }

    [HttpPost("radio/{id}/favourite")]
    public IActionResult ToggleRadioFavourite(int id)
    {
        var stations = _radio.GetStations();
        var station = stations.FirstOrDefault(s => s.Id == id);
        if (station == null) return NotFound();

        var isFav = _userFavs.ToggleFavourite(CurrentUsername, "radio", id);
        return Ok(new { isFavourite = isFav });
    }

    [HttpGet("radio/favourites")]
    public IActionResult GetRadioFavourites()
    {
        var favIds = _userFavs.GetFavouriteIds(CurrentUsername, "radio");
        var stations = _radio.GetStations();
        var favourites = stations.Where(s => favIds.Contains(s.Id)).ToList();
        foreach (var s in favourites) s.IsFavourite = true;
        return Ok(new { total = favourites.Count, stations = favourites });
    }

    /// <summary>
    /// Server-side ICY/Icecast/Shoutcast metadata proxy.
    /// Fetches stream headers and inline StreamTitle to avoid browser CORS restrictions.
    /// </summary>
    [HttpGet("radio/stream-metadata")]
    public async Task<IActionResult> GetRadioStreamMetadata([FromQuery] string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Ok(new { success = false, error = "No URL provided" });

        // SSRF protection - scheme + DNS pre-flight for a clear error message. The hard
        // enforcement is inside NetGuard's connect callback below, which re-vets every address
        // actually dialled (covers redirects and DNS rebinding).
        if (await NetGuard.ValidateOutboundUrlAsync(url, HttpContext.RequestAborted) is { } urlError)
            return Ok(new { success = false, error = urlError });

        try
        {
            using var client = NetGuard.CreateOutboundClient(_httpFactory, TimeSpan.FromSeconds(8), "NexusM/2026");

            var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");

            using var response = await client.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);

            var headers = response.Headers;

            string? icyName        = headers.TryGetValues("icy-name",        out var v1) ? string.Join("", v1) : null;
            string? icyGenre       = headers.TryGetValues("icy-genre",       out var v2) ? string.Join("", v2) : null;
            string? icyBitrate     = headers.TryGetValues("icy-br",          out var v3) ? string.Join("", v3) : null;
            string? icyDescription = headers.TryGetValues("icy-description", out var v4) ? string.Join("", v4) : null;
            string? metaIntStr     = headers.TryGetValues("icy-metaint",     out var v5) ? string.Join("", v5) : null;

            string? streamTitle = null;
            string? rawMeta     = null;

            if (int.TryParse(metaIntStr, out int metaInt) && metaInt > 0)
            {
                using var stream = await response.Content.ReadAsStreamAsync();

                // Skip metaInt bytes of audio data
                var skip = new byte[metaInt];
                int skipped = 0;
                while (skipped < metaInt)
                {
                    int r = await stream.ReadAsync(skip, skipped, metaInt - skipped);
                    if (r == 0) break;
                    skipped += r;
                }

                // Next byte = metadata block length (multiply by 16 for actual byte count)
                var lenBuf = new byte[1];
                if (await stream.ReadAsync(lenBuf, 0, 1) == 1 && lenBuf[0] > 0)
                {
                    int metaLen = lenBuf[0] * 16;
                    var metaBuf = new byte[metaLen];
                    int metaRead = 0;
                    while (metaRead < metaLen)
                    {
                        int r = await stream.ReadAsync(metaBuf, metaRead, metaLen - metaRead);
                        if (r == 0) break;
                        metaRead += r;
                    }

                    rawMeta = System.Text.Encoding.UTF8.GetString(metaBuf).TrimEnd('\0');
                    var m = System.Text.RegularExpressions.Regex.Match(rawMeta, @"StreamTitle='([^']*)';?");
                    if (m.Success) streamTitle = m.Groups[1].Value.Trim();
                }
            }

            return Ok(new
            {
                success     = true,
                icyName,
                icyGenre,
                icyBitrate,
                icyDescription,
                streamTitle,
                rawMetadata = rawMeta
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, error = ex.Message });
        }
    }

    [HttpGet("tvchannels/favourites")]
    public async Task<IActionResult> GetTvChannelFavourites()
    {
        var favIds = _userFavs.GetFavouriteIds(CurrentUsername, "tvchannel");
        var channels = await _tvDb.TvChannels.Where(c => favIds.Contains(c.Id)).ToListAsync();
        foreach (var c in channels) c.IsFavourite = true;
        return Ok(new { total = channels.Count, channels });
    }

    // ─── Smart Insights ─────────────────────────────────────────────

    [HttpGet("insights")]
    public async Task<IActionResult> GetInsights()
    {
        var username = CurrentUsername;

        // ── Watched video IDs ──
        var watchedIds = _userFavs.GetWatchedIds(username);
        var watchedVideos = watchedIds.Count > 0
            ? await _videoDb.Videos.Where(v => watchedIds.Contains(v.Id)).ToListAsync()
            : new List<Video>();

        var moviesWatched = watchedVideos.Count(v => v.MediaType == "movie");
        var tvEpisodesWatched = watchedVideos.Count(v => v.MediaType == "tv");
        var docsWatched = watchedVideos.Count(v => v.MediaType == "documentary");
        var animeWatched = watchedVideos.Count(v => v.MediaType == "anime");
        var totalWatched = watchedVideos.Count;
        var totalWatchTimeHours = Math.Round(watchedVideos.Sum(v => v.Duration) / 3600.0, 1);

        // ── Favorite genres (from watched videos) ──
        var genreCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in watchedVideos)
        {
            if (string.IsNullOrWhiteSpace(v.Genre)) continue;
            foreach (var g in v.Genre.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                genreCounts.TryGetValue(g, out var count);
                genreCounts[g] = count + 1;
            }
        }
        var favoriteGenres = genreCounts
            .OrderByDescending(kv => kv.Value)
            .Take(8)
            .Select(kv => new { name = kv.Key, count = kv.Value })
            .ToList();

        // ── Recently completed (most recent watched) ──
        var recentWatchedIds = watchedIds.Take(10).ToList();
        var recentWatchedVideos = recentWatchedIds.Count > 0
            ? await _videoDb.Videos.Where(v => recentWatchedIds.Contains(v.Id)).ToListAsync()
            : new List<Video>();
        var recentlyCompleted = recentWatchedIds
            .Select(id => recentWatchedVideos.FirstOrDefault(v => v.Id == id))
            .Where(v => v != null)
            .Select(v => new { v!.Id, v.Title, v.MediaType, v.ThumbnailPath, v.PosterPath, v.Year, v.Rating })
            .ToList();

        // ── Library stats ──
        var totalLibraryVideos = await _videoDb.Videos.CountAsync();
        var totalLibraryMovies = await _videoDb.Videos.CountAsync(v => v.MediaType == "movie");
        var totalLibraryTv = await _videoDb.Videos.CountAsync(v => v.MediaType == "tv");
        var totalLibraryDocs = await _videoDb.Videos.CountAsync(v => v.MediaType == "documentary");
        var totalLibraryAnime = await _videoDb.Videos.CountAsync(v => v.MediaType == "anime");

        // ── Monthly watch activity (last 12 months) ──
        var watchedWithDates = _userFavs.GetWatchedWithDates(username);
        var now = DateTime.UtcNow;
        var monthlyActivity = Enumerable.Range(0, 12).Select(i =>
        {
            var month = now.AddMonths(-(11 - i));
            var prefix = $"{month.Year}-{month.Month:D2}";
            return new { year = month.Year, month = month.Month, label = month.ToString("MMM"), count = watchedWithDates.Count(w => w.WatchedAt.StartsWith(prefix)) };
        }).ToList();

        // ── Watchlist stats ──
        var watchlistWithDates = _userFavs.GetWatchlistWithDates(username);
        var watchlistTotal = watchlistWithDates.Count;
        var watchlistWatchedCount = watchlistWithDates.Count(w => watchedIds.Contains(w.VideoId));
        object? oldestWatchlistItem = null;
        var unwatchedWatchlist = watchlistWithDates.Where(w => !watchedIds.Contains(w.VideoId)).ToList();
        if (unwatchedWatchlist.Count > 0)
        {
            var oldest = unwatchedWatchlist[0];
            if (DateTime.TryParse(oldest.AddedAt, out var addedDate))
            {
                var daysAgo = Math.Max(0, (int)(DateTime.UtcNow - addedDate).TotalDays);
                var wlVideo = await _videoDb.Videos.Where(v => v.Id == oldest.VideoId)
                    .Select(v => new { v.Id, v.Title, v.PosterPath, v.MediaType })
                    .FirstOrDefaultAsync();
                if (wlVideo != null)
                    oldestWatchlistItem = new { wlVideo.Id, wlVideo.Title, daysAgo, wlVideo.PosterPath, wlVideo.MediaType };
            }
        }

        // ── Genre recommendations (because you like top genre) ──
        object? genreRecommendations = null;
        if (favoriteGenres.Count > 0)
        {
            var topGenreName = favoriteGenres[0].name;
            var genreRecItems = await _videoDb.Videos
                .Where(v => !watchedIds.Contains(v.Id)
                    && v.Genre != null && v.Genre.Contains(topGenreName)
                    && v.PosterPath != null && v.PosterPath != "")
                .OrderByDescending(v => v.Rating)
                .Take(15)
                .Select(v => new { v.Id, v.Title, v.Rating, v.MediaType, v.PosterPath, v.Year })
                .ToListAsync();
            genreRecommendations = new { genre = topGenreName, items = genreRecItems };
        }

        // ── Unfinished series ──
        var unfinishedSeries = new List<object>();
        if (tvEpisodesWatched > 0)
        {
            var watchedTvGroups = watchedVideos
                .Where(v => v.MediaType == "tv" && !string.IsNullOrEmpty(v.SeriesName))
                .GroupBy(v => v.SeriesName)
                .OrderByDescending(g => g.Count())
                .Take(15);

            foreach (var sg in watchedTvGroups)
            {
                var sName = sg.Key!;
                var totalInLib = await _videoDb.Videos.CountAsync(v => v.SeriesName == sName && v.MediaType == "tv");
                var watchedCount = sg.Count();
                if (watchedCount >= totalInLib) continue;

                var rep = await _videoDb.Videos
                    .Where(v => v.SeriesName == sName && v.MediaType == "tv" && v.PosterPath != null && v.PosterPath != "")
                    .OrderBy(v => v.Season).ThenBy(v => v.Episode)
                    .Select(v => new { v.Id, v.PosterPath, v.ThumbnailPath, v.Year })
                    .FirstOrDefaultAsync();

                if (rep == null) continue;
                unfinishedSeries.Add(new { seriesName = sName, watchedCount, totalCount = totalInLib, representative = rep });
                if (unfinishedSeries.Count >= 10) break;
            }
        }

        // ── Hidden gems (high-rated classics not yet watched) ──
        var hiddenGems = await _videoDb.Videos
            .Where(v => v.Rating >= 8.0 && v.Year <= 2005
                && !watchedIds.Contains(v.Id)
                && v.PosterPath != null && v.PosterPath != "")
            .OrderByDescending(v => v.Rating)
            .ThenBy(v => v.Year)
            .Take(15)
            .Select(v => new { v.Id, v.Title, v.Rating, v.MediaType, v.PosterPath, v.Year })
            .ToListAsync();

        return Ok(new
        {
            totalWatched,
            moviesWatched,
            tvEpisodesWatched,
            docsWatched,
            animeWatched,
            totalWatchTimeHours,
            favoriteGenres,
            recentlyCompleted,
            library = new { total = totalLibraryVideos, movies = totalLibraryMovies, tvEpisodes = totalLibraryTv, docs = totalLibraryDocs, anime = totalLibraryAnime },
            monthlyActivity,
            watchlistStats = new { total = watchlistTotal, watchedCount = watchlistWatchedCount, oldestItem = oldestWatchlistItem },
            genreRecommendations,
            unfinishedSeries,
            hiddenGems
        });
    }

    // ─── Mood definitions (hardcoded, matching PS1 version) ──────

    private static readonly MoodDef[] MoodDefinitions =
    [
        new("romantic",   "Feeling Romantic",    "mood-heart",      "#ec4899", "Character-driven stories with emotional depth",   "Romance",                                                "any"),
        new("sad",        "Feeling Sad",         "mood-cloud",      "#6366f1", "Introspective content, familiar comfort shows",   "Drama,Family",                                           "any"),
        new("learning",   "Want to Learn",       "mood-book",       "#10b981", "Documentaries and educational content",           "Documentary,History",                                    "any"),
        new("light",      "Something Light",     "mood-sun",        "#f59e0b", "Easy watching, feel-good content",                "Comedy,Animation,Family",                                "short"),
        new("immersive",  "Deep Immersion",      "mood-eye",        "#8b5cf6", "Epic stories requiring full attention",           "Science Fiction,Sci-Fi & Fantasy,Fantasy,Mystery,Crime", "long"),
        new("background", "Background Watching", "mood-coffee",     "#64748b", "Content that works while multitasking",           "Comedy,Animation,Reality",                               "any"),
        new("thrilling",  "Feeling Adventurous", "mood-zap",        "#ef4444", "High-energy action and suspense",                 "Action,Action & Adventure,Thriller,Adventure,Horror",    "any"),
        new("nostalgic",  "Feeling Nostalgic",   "mood-rewind",     "#d946ef", "Rewatches and comfort classics",                  "any",                                                    "any")
    ];

    [HttpGet("insights/moods")]
    public IActionResult GetMoods()
    {
        return Ok(MoodDefinitions.Select(m => new { m.Key, m.Name, m.Icon, m.Color, m.Description }));
    }

    [HttpGet("insights/mood-recommendations")]
    public async Task<IActionResult> GetMoodRecommendations([FromQuery] string mood, [FromQuery] int limit = 20)
    {
        var moodDef = MoodDefinitions.FirstOrDefault(m => m.Key == mood);

        if (moodDef == null)
            return BadRequest(new { error = "Unknown mood" });

        // Build query
        var query = _videoDb.Videos
            .Where(v => v.Title != "");

        // Genre filter
        if (moodDef.Genres != "any")
        {
            var genreList = moodDef.Genres.Split(',', StringSplitOptions.TrimEntries);
            query = query.Where(v => genreList.Any(g => v.Genre.ToLower().Contains(g.ToLower())));
        }

        // Runtime filter
        if (moodDef.Runtime == "short")
            query = query.Where(v => v.Duration <= 5400); // 90 min
        else if (moodDef.Runtime == "long")
            query = query.Where(v => v.Duration >= 7200); // 120 min

        // Nostalgic: content 15+ years old
        if (mood == "nostalgic")
        {
            var cutoff = DateTime.Now.Year - 15;
            query = query.Where(v => v.Year != null && v.Year <= cutoff);
        }

        // Fetch pool (3x limit for randomization), prefer rated content
        var rawPool = await query
            .OrderByDescending(v => v.Rating > 0 ? v.Rating : 5.0)
            .ThenByDescending(v => v.Year ?? 2000)
            .Take(limit * 5)
            .Select(v => new
            {
                v.Id, v.Title, v.Year, v.Genre, v.Rating,
                v.MediaType, v.ThumbnailPath, v.PosterPath, v.Duration,
                v.SeriesName
            })
            .ToListAsync();

        // If no genre match, fall back to top-rated
        if (rawPool.Count == 0)
        {
            rawPool = await _videoDb.Videos
                .Where(v => v.Title != "")
                .OrderByDescending(v => v.Rating)
                .Take(limit * 3)
                .Select(v => new
                {
                    v.Id, v.Title, v.Year, v.Genre, v.Rating,
                    v.MediaType, v.ThumbnailPath, v.PosterPath, v.Duration,
                    v.SeriesName
                })
                .ToListAsync();
        }

        // Deduplicate TV shows: group episodes by SeriesName, pick the best representative
        var movies = rawPool.Where(v => v.MediaType != "tv").ToList();
        var tvShows = rawPool.Where(v => v.MediaType == "tv" && !string.IsNullOrEmpty(v.SeriesName))
            .GroupBy(v => v.SeriesName)
            .Select(g => {
                // Pick episode with best poster/rating as series representative
                var best = g.OrderByDescending(e => string.IsNullOrEmpty(e.PosterPath) ? 0 : 1)
                            .ThenByDescending(e => e.Rating > 0 ? e.Rating : 5.0)
                            .First();
                return new {
                    best.Id, Title = g.Key, best.Year, best.Genre, best.Rating,
                    MediaType = "tv", best.ThumbnailPath, best.PosterPath, best.Duration,
                    best.SeriesName
                };
            })
            .ToList();

        var pool = movies.Concat(tvShows).ToList();

        // Shuffle and take limit
        var rng = new Random();
        var recommendations = pool.OrderBy(_ => rng.Next()).Take(limit).ToList();

        return Ok(new { recommendations });
    }

    // ─── System Power ────────────────────────────────────────────────

    [HttpPost("system/shutdown")]
    [Authorize(Roles = "admin")]
    public IActionResult ShutdownHost()
    {
        _logger.LogWarning("System shutdown requested by user: {User}", CurrentUsername);

        Task.Run(async () =>
        {
            await Task.Delay(2000); // Give response time to reach client
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("shutdown", "/s /t 5 /c \"NexusM: Shutdown requested from Web UI\"") { CreateNoWindow = true, UseShellExecute = false });
            else
                Process.Start(new ProcessStartInfo("shutdown", "-h now") { CreateNoWindow = true, UseShellExecute = false });
        });

        return Ok(new { success = true, message = "Shutdown initiated. The system will shut down in a few seconds." });
    }

    [HttpPost("system/reboot")]
    [Authorize(Roles = "admin")]
    public IActionResult RebootHost()
    {
        _logger.LogWarning("System reboot requested by user: {User}", CurrentUsername);

        Task.Run(async () =>
        {
            await Task.Delay(2000); // Give response time to reach client
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("shutdown", "/r /t 5 /c \"NexusM: Reboot requested from Web UI\"") { CreateNoWindow = true, UseShellExecute = false });
            else
                Process.Start(new ProcessStartInfo("shutdown", "-r now") { CreateNoWindow = true, UseShellExecute = false });
        });

        return Ok(new { success = true, message = "Reboot initiated. The system will restart in a few seconds." });
    }

    [HttpPost("system/restart-service")]
    [Authorize(Roles = "admin")]
    public IActionResult RestartService()
    {
        _logger.LogWarning("NexusM service restart requested by user: {User}", CurrentUsername);

        // Fire-and-forget so the HTTP response reaches the client before the
        // process exits. Reuses the auto-update relaunch (systemd / nohup / Windows .bat).
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            try { await _autoUpdate.RestartAppAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Service restart failed"); }
        });

        return Ok(new { success = true, message = "Restart initiated. NexusM will be back in a few seconds." });
    }

    // ─── Actors ──────────────────────────────────────────────────────

    [HttpGet("actors")]
    public async Task<IActionResult> GetActors([FromQuery] int page = 1, [FromQuery] int limit = 60, [FromQuery] string? search = null, [FromQuery] string? sort = "popularity")
    {
        // Exclude rows that exist only to back a Director/Writer page.
        var query = _actorsDb.Actors.Where(a => !a.HiddenFromBrowse);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim().ToLowerInvariant().Replace(" ", "");
            query = query.Where(a => a.NormalizedName.Contains(normalized) || a.Name.Contains(search.Trim()));
        }

        var total = await query.CountAsync();
        // gender sort: females (1) then males (2) then unknown (0/3), alphabetical within each.
        var orderedQuery = sort == "name"
            ? query.OrderBy(a => a.Name)
            : sort == "gender"
                ? query.OrderBy(a => a.Gender == 1 ? 0 : a.Gender == 2 ? 1 : 2).ThenBy(a => a.Name)
                : query.OrderByDescending(a => a.Popularity ?? 0);
        var actorPage = await orderedQuery
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(a => new { a.Id, a.Name, a.TmdbId, a.ImageCached, a.KnownForDepartment, a.Popularity, a.Gender })
            .ToListAsync();

        // Compute grouped library count (movies + distinct TV series) via cross-DB lookup
        var actorIds = actorPage.Select(a => a.Id).ToList();
        var allMovieActors = await _actorsDb.MovieActors
            .Where(ma => actorIds.Contains(ma.ActorId))
            .Select(ma => new { ma.ActorId, ma.VideoId })
            .ToListAsync();
        var allVideoIds = allMovieActors.Select(ma => ma.VideoId).Distinct().ToList();
        var videoInfoList = allVideoIds.Count > 0
            ? await _videoDb.Videos.Where(v => allVideoIds.Contains(v.Id))
                .Select(v => new { v.Id, v.MediaType, v.SeriesName }).ToListAsync()
            : [];
        var videoInfoById = videoInfoList.ToDictionary(v => v.Id);

        var actors = actorPage.Select(a =>
        {
            var vids = allMovieActors.Where(ma => ma.ActorId == a.Id).Select(ma => ma.VideoId).ToList();
            var actorVideos = vids.Where(vid => videoInfoById.ContainsKey(vid)).Select(vid => videoInfoById[vid]).ToList();
            var nonTv = actorVideos.Count(v => v.MediaType != "tv");
            var tvSeries = actorVideos.Where(v => v.MediaType == "tv" && !string.IsNullOrEmpty(v.SeriesName))
                .Select(v => v.SeriesName!).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            return new { a.Id, a.Name, a.TmdbId, a.ImageCached, a.KnownForDepartment, a.Popularity, a.Gender, movieCount = nonTv + tvSeries };
        }).ToList();

        return Ok(new { total, page, limit, actors });
    }

    [HttpGet("actors/stats")]
    public async Task<IActionResult> GetActorStats()
    {
        var total = await _actorsDb.Actors.CountAsync(a => !a.HiddenFromBrowse);
        return Ok(new { total });
    }

    [HttpPost("actors/populate")]
    public async Task<IActionResult> PopulateActors()
    {
        if (_metadata.IsFetching)
            return Ok(new { success = false, message = "Metadata fetch already in progress" });

        var processed = await _metadata.PopulateActorsAsync();
        var total = await _actorsDb.Actors.CountAsync();
        return Ok(new { success = true, processed, total });
    }

    [HttpGet("actors/{id:int}")]
    public async Task<IActionResult> GetActorDetail(int id)
    {
        var actor = await _actorsDb.Actors.FindAsync(id);
        if (actor == null) return NotFound();

        // Lazy-fetch bio from TMDB when missing, OR when it was fetched in a different language than
        // the server is now set to (so changing Settings → Metadata Language refreshes actor bios too).
        var wantBioLang = string.IsNullOrWhiteSpace(_config.Config.Metadata.ScrapeLanguage)
            ? "en-US" : _config.Config.Metadata.ScrapeLanguage;
        if (actor.TmdbId.HasValue &&
            (string.IsNullOrEmpty(actor.Biography) ||
             !string.Equals(actor.BiographyLanguage, wantBioLang, StringComparison.OrdinalIgnoreCase)))
        {
            var key = _config.Config.Metadata.EffectiveTmdbApiKey;
            if (!string.IsNullOrWhiteSpace(key))
            {
                try
                {
                    using var http = Http(10);
                    // Fetch in the server's configured metadata language (Settings → Library →
                    // Metadata Language) so actor bios honour the same setting as movie/TV overviews.
                    // TMDB does NOT fall back to English for a person biography - it returns an empty
                    // string when there's no translation - so keep the localized bio only when
                    // non-empty and fetch an English one as a fallback otherwise.
                    var url = $"https://api.themoviedb.org/3/person/{actor.TmdbId}?api_key={key}&language={wantBioLang}";
                    var resp = await http.GetAsync(url);
                    if (resp.IsSuccessStatusCode)
                    {
                        var json = await resp.Content.ReadAsStringAsync();
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        string? localizedBio = root.TryGetProperty("biography", out var bio) && bio.ValueKind == System.Text.Json.JsonValueKind.String
                            ? bio.GetString() : null;
                        // Birthday / deathday / place_of_birth are not localized.
                        if (root.TryGetProperty("birthday", out var bd) && bd.ValueKind == System.Text.Json.JsonValueKind.String)
                            actor.Birthday = bd.GetString();
                        if (root.TryGetProperty("deathday", out var dd) && dd.ValueKind == System.Text.Json.JsonValueKind.String)
                            actor.Deathday = dd.GetString();
                        if (root.TryGetProperty("place_of_birth", out var pob) && pob.ValueKind == System.Text.Json.JsonValueKind.String)
                            actor.PlaceOfBirth = pob.GetString();

                        // English fallback when the localized bio came back empty (TMDB gives "" for
                        // an untranslated person). Skip the extra call when already English.
                        if (string.IsNullOrWhiteSpace(localizedBio) &&
                            !wantBioLang.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var enResp = await http.GetAsync($"https://api.themoviedb.org/3/person/{actor.TmdbId}?api_key={key}&language=en-US");
                                if (enResp.IsSuccessStatusCode)
                                {
                                    using var enDoc = System.Text.Json.JsonDocument.Parse(await enResp.Content.ReadAsStringAsync());
                                    if (enDoc.RootElement.TryGetProperty("biography", out var enBio) && enBio.ValueKind == System.Text.Json.JsonValueKind.String)
                                        localizedBio = enBio.GetString();
                                }
                            }
                            catch { /* keep whatever we have */ }
                        }
                        actor.Biography = localizedBio;
                        actor.BiographyLanguage = wantBioLang;   // remember so a language change re-fetches

                        actor.LastUpdated = DateTime.UtcNow;
                        await _actorsDb.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch TMDB bio for actor {Id}", actor.TmdbId);
                }
            }
        }

        // Compute grouped library count (movies + distinct TV series) via cross-DB lookup
        var detailVideoIds = await _actorsDb.MovieActors
            .Where(ma => ma.ActorId == id)
            .Select(ma => ma.VideoId)
            .Distinct()
            .ToListAsync();
        int movieCount;
        if (detailVideoIds.Count == 0)
        {
            movieCount = 0;
        }
        else
        {
            var detailVideos = await _videoDb.Videos
                .Where(v => detailVideoIds.Contains(v.Id))
                .Select(v => new { v.MediaType, v.SeriesName })
                .ToListAsync();
            var nonTv = detailVideos.Count(v => v.MediaType != "tv");
            var tvSeries = detailVideos.Where(v => v.MediaType == "tv" && !string.IsNullOrEmpty(v.SeriesName))
                .Select(v => v.SeriesName!).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            movieCount = nonTv + tvSeries;
        }

        return Ok(new
        {
            actor.Id,
            actor.Name,
            actor.TmdbId,
            actor.ImageCached,
            actor.KnownForDepartment,
            actor.Popularity,
            actor.Birthday,
            actor.Deathday,
            actor.PlaceOfBirth,
            actor.Biography,
            movieCount
        });
    }

    [HttpGet("actors/{id:int}/movies")]
    public async Task<IActionResult> GetActorMovies(int id)
    {
        // Get video IDs from actors DB
        var movieActors = await _actorsDb.MovieActors
            .Where(ma => ma.ActorId == id)
            .OrderBy(ma => ma.BillingOrder)
            .ToListAsync();

        var videoIds = movieActors.Select(ma => ma.VideoId).Distinct().ToList();

        // Query videos from videos DB (cross-DB), then join character names in memory
        var videos = await _videoDb.Videos
            .Where(v => videoIds.Contains(v.Id))
            .Select(v => new
            {
                v.Id,
                v.Title,
                v.SeriesName,
                v.Year,
                v.MediaType,
                v.PosterPath,
                v.Rating,
                v.Genre
            })
            .ToListAsync();

        // Group TV episodes by series name, keep movies individual
        var grouped = new List<object>();
        var tvSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Movies first, then TV grouped
        foreach (var v in videos.Where(v => v.MediaType != "tv"))
        {
            grouped.Add(new
            {
                v.Id, v.Title, v.SeriesName, v.Year, v.MediaType, v.PosterPath, v.Rating, v.Genre,
                episodeCount = 0,
                characterName = movieActors.Where(ma => ma.VideoId == v.Id).Select(ma => ma.CharacterName).FirstOrDefault()
            });
        }

        foreach (var grp in videos.Where(v => v.MediaType == "tv" && !string.IsNullOrEmpty(v.SeriesName))
            .GroupBy(v => v.SeriesName!))
        {
            var first = grp.First();
            grouped.Add(new
            {
                first.Id, Title = first.SeriesName ?? first.Title, first.SeriesName, first.Year,
                first.MediaType, first.PosterPath, first.Rating, first.Genre,
                episodeCount = grp.Count(),
                characterName = movieActors.Where(ma => ma.VideoId == first.Id).Select(ma => ma.CharacterName).FirstOrDefault()
            });
        }

        return Ok(grouped);
    }

    [HttpGet("actors/{id:int}/knownfor")]
    public async Task<IActionResult> GetActorKnownFor(int id)
    {
        var actor = await _actorsDb.Actors.FindAsync(id);
        if (actor == null || !actor.TmdbId.HasValue) return Ok(new List<object>());

        var key = _config.Config.Metadata.EffectiveTmdbApiKey;
        if (string.IsNullOrWhiteSpace(key)) return Ok(new List<object>());

        var actorsDir = Path.Combine(AppContext.BaseDirectory, "assets", "actors");
        Directory.CreateDirectory(actorsDir);
        var cacheFile = Path.Combine(actorsDir, $"knownfor_{actor.TmdbId}.json");

        // Determine staleness window: 30 days for deceased actors, 7 days for living
        bool isDeceased = !string.IsNullOrEmpty(actor.Deathday);
        var staleness = isDeceased ? TimeSpan.FromDays(30) : TimeSpan.FromDays(7);

        // Try to serve from cache
        if (System.IO.File.Exists(cacheFile))
        {
            try
            {
                var cacheJson = await System.IO.File.ReadAllTextAsync(cacheFile);
                using var cacheDoc = System.Text.Json.JsonDocument.Parse(cacheJson);
                var cacheRoot = cacheDoc.RootElement;
                if (cacheRoot.TryGetProperty("cachedAt", out var cachedAtEl) &&
                    DateTime.TryParse(cachedAtEl.GetString(), out var cachedAt) &&
                    DateTime.UtcNow - cachedAt < staleness &&
                    cacheRoot.TryGetProperty("items", out var cachedItems))
                {
                    // Cache is fresh - deserialise and return
                    var cached = System.Text.Json.JsonSerializer.Deserialize<List<System.Text.Json.JsonElement>>(cachedItems.GetRawText());
                    return Ok(cached ?? new List<System.Text.Json.JsonElement>());
                }
            }
            catch { /* fall through to refresh */ }
        }

        try
        {
            using var http = Http(15);
            // Localise film/show titles in the filmography to the server's metadata language.
            var lang = string.IsNullOrWhiteSpace(_config.Config.Metadata.ScrapeLanguage) ? "en-US" : _config.Config.Metadata.ScrapeLanguage;
            var url = $"https://api.themoviedb.org/3/person/{actor.TmdbId}/combined_credits?api_key={key}&language={lang}";
            var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return Ok(new List<object>());

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var credits = new List<object>();
            // Exclude talk shows (10767) and news (10763); reality TV (10764) is allowed
            var excludedGenreIds = new HashSet<int> { 10767, 10763 };

            if (root.TryGetProperty("cast", out var castArr) && castArr.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var seen = new HashSet<string>(); // Deduplicate by mediaType+tmdbId

                // Parse all items first, then deduplicate, sort by date, take 30
                var parsed = new List<(string mediaType, string title, string? year, string? posterPath, int tmdbId, string character)>();

                foreach (var item in castArr.EnumerateArray())
                {
                    // Exclude talk shows and news (but allow reality TV)
                    if (item.TryGetProperty("genre_ids", out var gIds) && gIds.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        if (gIds.EnumerateArray().Any(g => g.ValueKind == System.Text.Json.JsonValueKind.Number && excludedGenreIds.Contains(g.GetInt32())))
                            continue;
                    }

                    var mediaType = item.TryGetProperty("media_type", out var mt) ? mt.GetString() ?? "movie" : "movie";
                    var tmdbId = item.TryGetProperty("id", out var idEl) && idEl.ValueKind == System.Text.Json.JsonValueKind.Number
                        ? idEl.GetInt32() : 0;

                    // Deduplicate (same show can appear multiple times for different episodes/roles)
                    var dedupeKey = $"{mediaType}_{tmdbId}";
                    if (!seen.Add(dedupeKey)) continue;

                    var title = mediaType == "tv"
                        ? (item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")
                        : (item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "");
                    var year = mediaType == "tv"
                        ? (item.TryGetProperty("first_air_date", out var fad) ? fad.GetString()?.Split('-').FirstOrDefault() : null)
                        : (item.TryGetProperty("release_date", out var rd) ? rd.GetString()?.Split('-').FirstOrDefault() : null);
                    var posterPath = item.TryGetProperty("poster_path", out var pp) && pp.ValueKind == System.Text.Json.JsonValueKind.String
                        ? pp.GetString() : null;
                    var character = item.TryGetProperty("character", out var ch) ? ch.GetString() ?? "" : "";

                    parsed.Add((mediaType, title, year, posterPath, tmdbId, character));
                }

                // Sort by year descending (newest first), then take 30
                var sorted = parsed
                    .OrderByDescending(p => int.TryParse(p.year, out var y) ? y : 0)
                    .Take(30);

                foreach (var p in sorted)
                {
                    // Cache poster to assets/actors/knownfor_{type}_{id}.jpg
                    string? cachedPoster = null;
                    if (!string.IsNullOrEmpty(p.posterPath))
                    {
                        var posterFile = $"knownfor_{p.mediaType}_{p.tmdbId}.jpg";
                        var posterFullPath = Path.Combine(actorsDir, posterFile);
                        if (System.IO.File.Exists(posterFullPath))
                        {
                            cachedPoster = posterFile;
                        }
                        else
                        {
                            try
                            {
                                var posterUrl = $"https://image.tmdb.org/t/p/w200{p.posterPath}";
                                var imgResp = await http.GetAsync(posterUrl);
                                if (imgResp.IsSuccessStatusCode)
                                {
                                    var bytes = await imgResp.Content.ReadAsByteArrayAsync();
                                    if (bytes.Length > 500)
                                    {
                                        await System.IO.File.WriteAllBytesAsync(posterFullPath, bytes);
                                        cachedPoster = posterFile;
                                    }
                                }
                            }
                            catch { /* ignore poster download failures */ }
                        }
                    }

                    credits.Add(new
                    {
                        tmdbId = p.tmdbId,
                        title = p.title,
                        year = p.year,
                        mediaType = p.mediaType,
                        character = p.character,
                        poster = cachedPoster
                    });
                }
            }

            // Persist cache file
            try
            {
                var cachePayload = new
                {
                    cachedAt = DateTime.UtcNow.ToString("O"),
                    actorDeceased = isDeceased,
                    items = credits
                };
                var cacheText = System.Text.Json.JsonSerializer.Serialize(cachePayload, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
                await System.IO.File.WriteAllTextAsync(cacheFile, cacheText);
            }
            catch { /* ignore cache write failures */ }

            return Ok(credits);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch known-for credits for actor {Id}", actor.TmdbId);

            // On failure, return stale cache if available rather than an empty list
            if (System.IO.File.Exists(cacheFile))
            {
                try
                {
                    var cacheJson = await System.IO.File.ReadAllTextAsync(cacheFile);
                    using var cacheDoc = System.Text.Json.JsonDocument.Parse(cacheJson);
                    if (cacheDoc.RootElement.TryGetProperty("items", out var staleItems))
                    {
                        var stale = System.Text.Json.JsonSerializer.Deserialize<List<System.Text.Json.JsonElement>>(staleItems.GetRawText());
                        return Ok(stale ?? new List<System.Text.Json.JsonElement>());
                    }
                }
                catch { }
            }

            return Ok(new List<object>());
        }
    }

    // ─── Actor Social IDs ─────────────────────────────────────────────────────
    // Lazily fetched from TMDB on first page view; refreshed every 30 days on view.

    [HttpGet("actors/{id:int}/social")]
    public async Task<IActionResult> GetActorSocial(int id)
    {
        var actor = await _actorsDb.Actors.FindAsync(id);
        if (actor == null) return Ok(new { facebook = (string?)null, instagram = (string?)null, twitter = (string?)null });

        bool needsRefresh = actor.TmdbId.HasValue &&
            (actor.SocialFetchedAt == null || DateTime.UtcNow - actor.SocialFetchedAt.Value > TimeSpan.FromDays(30));

        if (needsRefresh)
        {
            var key = _config.Config.Metadata.EffectiveTmdbApiKey;
            if (!string.IsNullOrWhiteSpace(key))
            {
                try
                {
                    using var http = Http(10);
                    var url = $"https://api.themoviedb.org/3/person/{actor.TmdbId}/external_ids?api_key={key}";
                    var resp = await http.GetAsync(url);
                    if (resp.IsSuccessStatusCode)
                    {
                        var json = await resp.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        string? fb = root.TryGetProperty("facebook_id",  out var fbEl) && fbEl.ValueKind == JsonValueKind.String ? fbEl.GetString() : null;
                        string? ig = root.TryGetProperty("instagram_id", out var igEl) && igEl.ValueKind == JsonValueKind.String ? igEl.GetString() : null;
                        string? tw = root.TryGetProperty("twitter_id",   out var twEl) && twEl.ValueKind == JsonValueKind.String ? twEl.GetString() : null;

                        actor.FacebookId      = string.IsNullOrWhiteSpace(fb) ? null : fb;
                        actor.InstagramId     = string.IsNullOrWhiteSpace(ig) ? null : ig;
                        actor.TwitterId       = string.IsNullOrWhiteSpace(tw) ? null : tw;
                        actor.SocialFetchedAt = DateTime.UtcNow;
                        await _actorsDb.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch social IDs for actor {TmdbId}", actor.TmdbId);
                }
            }
        }

        return Ok(new { facebook = actor.FacebookId, instagram = actor.InstagramId, twitter = actor.TwitterId });
    }

    // ─── Actor Filmography ────────────────────────────────────────────────────
    // Lazily fetched on first view; refreshed every 7 days (30 for deceased) on view.

    [HttpGet("actors/{id:int}/filmography")]
    public async Task<IActionResult> GetActorFilmography(int id)
    {
        var actor = await _actorsDb.Actors.FindAsync(id);
        if (actor == null)
            return Ok(new { actor = Array.Empty<object>(), appearances = Array.Empty<object>(), producer = Array.Empty<object>() });

        bool isDeceased = !string.IsNullOrEmpty(actor.Deathday);
        var staleness   = isDeceased ? TimeSpan.FromDays(30) : TimeSpan.FromDays(7);

        bool needsRefresh = actor.TmdbId.HasValue &&
            (actor.FilmographyFetchedAt == null || DateTime.UtcNow - actor.FilmographyFetchedAt.Value > staleness);

        if (needsRefresh)
        {
            var key = _config.Config.Metadata.EffectiveTmdbApiKey;
            if (!string.IsNullOrWhiteSpace(key))
            {
                try
                {
                    using var http = Http(15);
                    var lang = string.IsNullOrWhiteSpace(_config.Config.Metadata.ScrapeLanguage) ? "en-US" : _config.Config.Metadata.ScrapeLanguage;
                    var url = $"https://api.themoviedb.org/3/person/{actor.TmdbId}/combined_credits?api_key={key}&language={lang}";
                    var resp = await http.GetAsync(url);
                    if (resp.IsSuccessStatusCode)
                    {
                        var json = await resp.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        var excludedGenres = new HashSet<int> { 10767, 10763 }; // talk shows, news
                        var newCredits     = new List<ActorFilmographyCredit>();

                        // ── Cast credits ──────────────────────────────────────
                        if (root.TryGetProperty("cast", out var castArr) && castArr.ValueKind == JsonValueKind.Array)
                        {
                            var seen = new HashSet<string>();
                            foreach (var item in castArr.EnumerateArray())
                            {
                                var mediaType = item.TryGetProperty("media_type", out var mt) ? mt.GetString() ?? "" : "";
                                if (mediaType != "movie" && mediaType != "tv") continue;

                                if (item.TryGetProperty("genre_ids", out var gIds) && gIds.ValueKind == JsonValueKind.Array)
                                    if (gIds.EnumerateArray().Any(g => g.ValueKind == JsonValueKind.Number && excludedGenres.Contains(g.GetInt32()))) continue;

                                var tmdbItemId = item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt32() : 0;
                                if (!seen.Add($"{mediaType}_{tmdbItemId}")) continue;

                                var title = mediaType == "tv"
                                    ? (item.TryGetProperty("name",  out var n) ? n.GetString() ?? "" : "")
                                    : (item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "");
                                var dateStr = mediaType == "tv"
                                    ? (item.TryGetProperty("first_air_date", out var fad) ? fad.GetString() ?? "" : "")
                                    : (item.TryGetProperty("release_date",   out var rd)  ? rd.GetString()  ?? "" : "");
                                var year      = dateStr.Length >= 4 ? dateStr[..4] : "";
                                var character = item.TryGetProperty("character", out var ch) ? ch.GetString() ?? "" : "";

                                newCredits.Add(new ActorFilmographyCredit
                                {
                                    ActorId    = id,
                                    CreditType = mediaType == "movie" ? "actor" : "appearances",
                                    Title      = title,
                                    Year       = year,
                                    Character  = string.IsNullOrEmpty(character) ? null : character,
                                    TmdbItemId = tmdbItemId,
                                    MediaType  = mediaType
                                });
                            }
                        }

                        // ── Production / directing / writing crew ─────────────
                        if (root.TryGetProperty("crew", out var crewArr) && crewArr.ValueKind == JsonValueKind.Array)
                        {
                            var seenCrew = new HashSet<string>();
                            foreach (var item in crewArr.EnumerateArray())
                            {
                                var mediaType  = item.TryGetProperty("media_type",  out var mt)  ? mt.GetString()  ?? "" : "";
                                var department = item.TryGetProperty("department",  out var dep) ? dep.GetString() ?? "" : "";
                                var job        = item.TryGetProperty("job",         out var j)   ? j.GetString()   ?? "" : "";

                                if (mediaType != "movie" && mediaType != "tv") continue;
                                if (!string.Equals(department, "Production", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(department, "Directing",  StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(department, "Writing",    StringComparison.OrdinalIgnoreCase)) continue;

                                if (item.TryGetProperty("genre_ids", out var gIds) && gIds.ValueKind == JsonValueKind.Array)
                                    if (gIds.EnumerateArray().Any(g => g.ValueKind == JsonValueKind.Number && excludedGenres.Contains(g.GetInt32()))) continue;

                                var tmdbItemId = item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt32() : 0;
                                if (!seenCrew.Add($"{mediaType}_{tmdbItemId}_{job}")) continue;

                                var title = mediaType == "tv"
                                    ? (item.TryGetProperty("name",  out var n) ? n.GetString() ?? "" : "")
                                    : (item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "");
                                var dateStr = mediaType == "tv"
                                    ? (item.TryGetProperty("first_air_date", out var fad) ? fad.GetString() ?? "" : "")
                                    : (item.TryGetProperty("release_date",   out var rd)  ? rd.GetString()  ?? "" : "");
                                var year = dateStr.Length >= 4 ? dateStr[..4] : "";

                                newCredits.Add(new ActorFilmographyCredit
                                {
                                    ActorId    = id,
                                    CreditType = "producer",
                                    Title      = title,
                                    Year       = year,
                                    Job        = string.IsNullOrEmpty(job) ? null : job,
                                    TmdbItemId = tmdbItemId,
                                    MediaType  = mediaType
                                });
                            }
                        }

                        // Replace all existing credits for this actor
                        await _actorsDb.Database.ExecuteSqlRawAsync(
                            "DELETE FROM \"ActorFilmographyCredits\" WHERE \"ActorId\" = {0}", id);
                        _actorsDb.ActorFilmographyCredits.AddRange(newCredits);
                        actor.FilmographyFetchedAt = DateTime.UtcNow;
                        await _actorsDb.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch filmography for actor {TmdbId}", actor.TmdbId);
                }
            }
        }

        // Serve from DB (sorted year desc; empty year sorts last)
        var credits = await _actorsDb.ActorFilmographyCredits
            .Where(c => c.ActorId == id)
            .OrderByDescending(c => c.Year)
            .ToListAsync();

        return Ok(new
        {
            actor       = credits.Where(c => c.CreditType == "actor")
                                 .Select(c => new { c.Title, c.Year, c.Character, c.TmdbItemId }).ToList(),
            appearances = credits.Where(c => c.CreditType == "appearances")
                                 .Select(c => new { c.Title, c.Year, c.Character, c.TmdbItemId }).ToList(),
            producer    = credits.Where(c => c.CreditType == "producer")
                                 .Select(c => new { c.Title, c.Year, c.Job, c.TmdbItemId }).ToList()
        });
    }

    // ─── TMDB item detail (overview, rating, votes) ──────────────────────────

    [HttpGet("tmdb/details")]
    public async Task<IActionResult> GetTmdbDetails(
        [FromQuery] string type = "movie",
        [FromQuery] int    id   = 0)
    {
        if (id <= 0) return Ok(new { overview = "", rating = 0.0, votes = 0 });
        var key = _config.Config.Metadata.EffectiveTmdbApiKey;
        if (string.IsNullOrWhiteSpace(key)) return Ok(new { overview = "", rating = 0.0, votes = 0 });

        var safetype  = type == "tv" ? "tv" : "movie";
        var actorsDir = Path.Combine(AppContext.BaseDirectory, "assets", "actors");
        Directory.CreateDirectory(actorsDir);
        var cacheFile = Path.Combine(actorsDir, $"tmdbdetail_{safetype}_{id}.json");

        // Serve from cache if present (ratings drift slowly - 14-day window is fine)
        if (System.IO.File.Exists(cacheFile))
        {
            try
            {
                var cacheJson = await System.IO.File.ReadAllTextAsync(cacheFile);
                using var cd = System.Text.Json.JsonDocument.Parse(cacheJson);
                var cr = cd.RootElement;
                if (cr.TryGetProperty("cachedAt", out var cat) &&
                    DateTime.TryParse(cat.GetString(), out var cachedAt) &&
                    DateTime.UtcNow - cachedAt < TimeSpan.FromDays(14))
                {
                    var ov2 = cr.TryGetProperty("overview", out var o) ? o.GetString() ?? "" : "";
                    var ra2 = cr.TryGetProperty("rating",   out var r) ? r.GetDouble()        : 0.0;
                    var vo2 = cr.TryGetProperty("votes",    out var v) ? v.GetInt32()          : 0;
                    return Ok(new { overview = ov2, rating = ra2, votes = vo2 });
                }
            }
            catch { /* fall through to live fetch */ }
        }

        try
        {
            using var http = Http(10);
            var url  = $"https://api.themoviedb.org/3/{safetype}/{id}?api_key={key}";
            var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return Ok(new { overview = "", rating = 0.0, votes = 0 });

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var overview = root.TryGetProperty("overview",     out var ov) ? ov.GetString() ?? "" : "";
            var rating   = root.TryGetProperty("vote_average", out var va) ? Math.Round(va.GetDouble(), 1) : 0.0;
            var votes    = root.TryGetProperty("vote_count",   out var vc) ? vc.GetInt32() : 0;

            // Persist to cache
            try
            {
                var payload  = new { cachedAt = DateTime.UtcNow.ToString("O"), overview, rating, votes };
                var text     = System.Text.Json.JsonSerializer.Serialize(payload);
                await System.IO.File.WriteAllTextAsync(cacheFile, text);
            }
            catch { /* ignore write failures */ }

            return Ok(new { overview, rating, votes });
        }
        catch
        {
            // On network failure return stale cache if available
            if (System.IO.File.Exists(cacheFile))
            {
                try
                {
                    var cacheJson = await System.IO.File.ReadAllTextAsync(cacheFile);
                    using var cd = System.Text.Json.JsonDocument.Parse(cacheJson);
                    var cr = cd.RootElement;
                    var ov2 = cr.TryGetProperty("overview", out var o) ? o.GetString() ?? "" : "";
                    var ra2 = cr.TryGetProperty("rating",   out var r) ? r.GetDouble()        : 0.0;
                    var vo2 = cr.TryGetProperty("votes",    out var v) ? v.GetInt32()          : 0;
                    return Ok(new { overview = ov2, rating = ra2, votes = vo2 });
                }
                catch { }
            }
            return Ok(new { overview = "", rating = 0.0, votes = 0 });
        }
    }

    // ─── TMDB Search (Browse Similar Names) ─────────────────────────

    [HttpGet("tmdb/search")]
    public async Task<IActionResult> SearchTmdb(
        [FromQuery] string query = "",
        [FromQuery] string type  = "multi")
    {
        if (string.IsNullOrWhiteSpace(query))
            return Ok(new { results = Array.Empty<object>() });

        var key = _config.Config.Metadata.EffectiveTmdbApiKey;
        if (string.IsNullOrWhiteSpace(key))
            return Ok(new { results = Array.Empty<object>(), noKey = true });

        var endpoint = type switch { "movie" => "search/movie", "tv" => "search/tv", _ => "search/multi" };
        var url = $"https://api.themoviedb.org/3/{endpoint}?api_key={key}&query={Uri.EscapeDataString(query)}&include_adult=false";

        try
        {
            using var http = Http(10);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("NexusM/1.0 (self-hosted media server)");
            var json = await http.GetStringAsync(url);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var arr = doc.RootElement.GetProperty("results");

            var items = new List<object>();
            foreach (var r in arr.EnumerateArray().Take(15))
            {
                var mType = r.TryGetProperty("media_type", out var mt) ? mt.GetString() : type;
                if (mType == "person") continue;

                var tmdbId  = r.TryGetProperty("id", out var idEl) && idEl.ValueKind == System.Text.Json.JsonValueKind.Number ? idEl.GetInt32() : 0;
                var title   = mType == "tv"
                    ? (r.TryGetProperty("name", out var n) ? n.GetString() : null)
                    : (r.TryGetProperty("title", out var t) ? t.GetString() : null);
                var dateStr = mType == "tv"
                    ? (r.TryGetProperty("first_air_date", out var fad) ? fad.GetString() : null)
                    : (r.TryGetProperty("release_date",   out var rd)  ? rd.GetString()  : null);
                int? year = dateStr?.Length >= 4 && int.TryParse(dateStr[..4], out var y) ? y : null;
                var overview  = r.TryGetProperty("overview",     out var ov) ? ov.GetString() : null;
                var poster    = r.TryGetProperty("poster_path",  out var pp) && pp.ValueKind != System.Text.Json.JsonValueKind.Null
                    ? $"https://image.tmdb.org/t/p/w185{pp.GetString()}" : null;
                var rating    = r.TryGetProperty("vote_average", out var ra) && ra.ValueKind == System.Text.Json.JsonValueKind.Number ? ra.GetDouble() : 0.0;
                var voteCount = r.TryGetProperty("vote_count",   out var vc) && vc.ValueKind == System.Text.Json.JsonValueKind.Number ? vc.GetInt32()  : 0;

                items.Add(new { tmdbId, title, year, overview, posterUrl = poster, mediaType = mType, rating, voteCount });
            }
            return Ok(new { results = items });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TMDB search failed for '{Query}'", query);
            return StatusCode(502, new { error = "TMDB search unavailable" });
        }
    }

    [HttpPost("videos/{id}/apply-tmdb")]
    public async Task<IActionResult> ApplyTmdbToVideo(int id, [FromBody] System.Text.Json.JsonElement body)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();

        if (!body.TryGetProperty("tmdbId", out var tmdbIdEl) || tmdbIdEl.ValueKind != System.Text.Json.JsonValueKind.Number)
            return BadRequest(new { error = "tmdbId required" });

        var tmdbId = tmdbIdEl.GetInt32();
        var type   = body.TryGetProperty("type",  out var te) ? te.GetString() ?? "movie" : "movie";
        var title  = body.TryGetProperty("title", out var tl) ? tl.GetString() : null;
        var year   = body.TryGetProperty("year",  out var ye) && ye.ValueKind == System.Text.Json.JsonValueKind.Number ? ye.GetInt32() : (int?)null;

        // TV series: apply the chosen TMDB entry across EVERY episode. The corrected series
        // name + year + fetched metadata must land on all episodes, otherwise the show
        // fragments into two cards (episodes under the old filename-derived name vs the new
        // TMDB name). ForceApplyTmdbToTvSeriesAsync matches siblings by the OLD series name.
        if (type == "tv" && !string.IsNullOrWhiteSpace(video.SeriesName))
        {
            var okTv = await _metadata.ForceApplyTmdbToTvSeriesAsync(id, tmdbId, title, year);
            if (!okTv) return StatusCode(502, new { error = "Could not fetch TMDB metadata for that entry" });
            return Ok(new { success = true });
        }

        // Movie / standalone (or a TV item with no series name): single-row apply.
        // Update title and year from the user's selection before fetching full metadata.
        if (!string.IsNullOrEmpty(title))
        {
            if (type == "tv") video.SeriesName = title;
            else              video.Title       = title;
        }
        if (year.HasValue && year > 0) video.Year = year.Value;

        var ok = await _metadata.ForceApplyFromTmdbIdAsync(video, tmdbId, type);
        if (!ok) return StatusCode(502, new { error = "Could not fetch TMDB metadata for that entry" });

        await _videoDb.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ─── Anime "Browse Similar" (Tenrai / MyAnimeList) ──────────────
    // The anime library is sourced from Tenrai, not TMDB, so its Browse-Similar picker searches
    // MyAnimeList entries and applies the chosen one across the series.

    [HttpGet("anime/search")]
    public async Task<IActionResult> SearchAnime([FromQuery] string query = "")
    {
        if (string.IsNullOrWhiteSpace(query))
            return Ok(new { results = Array.Empty<object>() });

        var candidates = await _animeScanner.SearchAnimeCandidatesAsync(query);
        var items = candidates.Select(c => new
        {
            malId    = c.MalId,
            title    = c.Title,
            year     = c.Year,
            overview = c.Synopsis,
            posterUrl = c.PosterUrl,
            rating   = c.Score ?? 0.0,
            type     = c.Type,
            episodes = c.Episodes
        });
        return Ok(new { results = items });
    }

    [HttpPost("videos/{id}/apply-anime")]
    public async Task<IActionResult> ApplyAnimeToVideo(int id, [FromBody] System.Text.Json.JsonElement body)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();
        if (video.MediaType != "anime")
            return BadRequest(new { error = "Video is not classified as anime" });

        if (!body.TryGetProperty("malId", out var malEl) || malEl.ValueKind != System.Text.Json.JsonValueKind.Number)
            return BadRequest(new { error = "malId required" });

        var ok = await _animeScanner.ApplyChosenAnimeAsync(id, malEl.GetInt32());
        if (!ok) return StatusCode(502, new { error = "Could not fetch anime metadata for that entry" });
        return Ok(new { success = true });
    }

    // Apply TMDB metadata to an anime that isn't listed on Tenrai. Keeps MediaType="anime"
    // and applies across the whole series (see MetadataService.ForceApplyTmdbToAnimeAsync).
    [HttpPost("videos/{id}/apply-tmdb-anime")]
    public async Task<IActionResult> ApplyTmdbToAnime(int id, [FromBody] System.Text.Json.JsonElement body)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();
        if (video.MediaType != "anime")
            return BadRequest(new { error = "Video is not classified as anime" });

        if (!body.TryGetProperty("tmdbId", out var tmdbIdEl) || tmdbIdEl.ValueKind != System.Text.Json.JsonValueKind.Number)
            return BadRequest(new { error = "tmdbId required" });

        var tmdbId = tmdbIdEl.GetInt32();
        var type   = body.TryGetProperty("type",  out var te) ? te.GetString() ?? "tv" : "tv";
        var title  = body.TryGetProperty("title", out var tl) ? tl.GetString() : null;
        var year   = body.TryGetProperty("year",  out var ye) && ye.ValueKind == System.Text.Json.JsonValueKind.Number ? ye.GetInt32() : (int?)null;

        var ok = await _metadata.ForceApplyTmdbToAnimeAsync(id, tmdbId, type, title, year);
        if (!ok) return StatusCode(502, new { error = "Could not fetch TMDB metadata for that entry" });
        return Ok(new { success = true });
    }

    // ─── Podcasts ────────────────────────────────────────────────────

    [HttpGet("podcasts/stats")]
    public async Task<IActionResult> GetPodcastStats()
    {
        var totalFeeds = await _podcastDb.Feeds.CountAsync();
        var totalEpisodes = await _podcastDb.Episodes.CountAsync();
        var unplayedEpisodes = await _podcastDb.Episodes.CountAsync(e => !e.IsPlayed);
        return Ok(new { totalFeeds, totalEpisodes, unplayedEpisodes });
    }

    [HttpGet("podcasts")]
    public async Task<IActionResult> GetPodcasts()
    {
        var favIds = _userFavs.GetFavouriteIds(CurrentUsername, "podcast").ToHashSet();
        var feeds = await _podcastDb.Feeds.OrderByDescending(f => f.DateAdded).ToListAsync();
        var unplayedCounts = await _podcastDb.Episodes
            .Where(e => !e.IsPlayed)
            .GroupBy(e => e.FeedId)
            .Select(g => new { FeedId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FeedId, x => x.Count);

        var result = feeds.Select(f => new
        {
            f.Id, f.Title, f.Description, f.Author, f.RssUrl,
            f.ArtworkUrl, f.ArtworkFile, f.Category, f.Language,
            f.EpisodeCount, f.LastRefreshed, f.DateAdded,
            isFavourite = favIds.Contains(f.Id),
            unplayedCount = unplayedCounts.GetValueOrDefault(f.Id, 0)
        });
        return Ok(result);
    }

    [HttpGet("podcasts/preview")]
    public async Task<IActionResult> PreviewPodcastFeed([FromQuery] string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
            return BadRequest(new { message = "A valid RSS URL is required." });
        try
        {
            var (feed, episodes) = await _podcastSvc.ParseRssFeedAsync(url);
            return Ok(new {
                title       = feed.Title,
                author      = feed.Author,
                description = feed.Description,
                artworkUrl  = feed.ArtworkUrl,
                category    = feed.Category,
                episodes    = episodes.Take(15).Select(e => new {
                    title       = e.Title,
                    duration    = e.DurationSeconds,
                    publishDate = e.PublishDate,
                    mediaType   = e.MediaType,
                    mediaUrl    = e.MediaUrl
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to preview RSS feed: {Url}", url);
            return BadRequest(new { message = $"Could not load feed: {ex.Message}" });
        }
    }

    [HttpPost("podcasts")]
    public async Task<IActionResult> AddPodcast([FromBody] System.Text.Json.JsonElement body)
    {
        var url = body.TryGetProperty("url", out var u) ? u.GetString()?.Trim() ?? "" : "";
        if (string.IsNullOrEmpty(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
            return BadRequest(new { message = "A valid RSS URL is required." });

        var existing = await _podcastDb.Feeds.FirstOrDefaultAsync(f => f.RssUrl == url);
        if (existing != null)
            return Ok(new { message = "Already subscribed.", feedId = existing.Id });

        try
        {
            var feed = await _podcastSvc.AddOrRefreshFeedAsync(_podcastDb, url);
            return Ok(new { message = $"Subscribed to \"{feed.Title}\".", feedId = feed.Id });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to subscribe to RSS feed: {Url}", url);
            return BadRequest(new { message = $"Could not load feed: {ex.Message}" });
        }
    }

    [HttpDelete("podcasts/{id}")]
    public async Task<IActionResult> DeletePodcast(int id)
    {
        var feed = await _podcastDb.Feeds.FindAsync(id);
        if (feed == null) return NotFound();
        _podcastDb.Feeds.Remove(feed);
        await _podcastDb.SaveChangesAsync();
        return Ok(new { message = "Unsubscribed." });
    }

    [HttpPost("podcasts/import-opml")]
    public async Task<IActionResult> ImportOpml(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file uploaded." });

        List<string> urls;
        using (var stream = file.OpenReadStream())
            urls = _podcastSvc.ParseOpml(stream);

        if (urls.Count == 0)
            return BadRequest(new { message = "No RSS feeds found in the OPML file." });

        int imported = 0, skipped = 0, failed = 0;
        foreach (var url in urls)
        {
            try
            {
                var existing = await _podcastDb.Feeds.FirstOrDefaultAsync(f => f.RssUrl == url);
                if (existing != null) { skipped++; continue; }
                await _podcastSvc.AddOrRefreshFeedAsync(_podcastDb, url);
                imported++;
                await Task.Delay(300);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OPML import: failed to load feed {Url}", url);
                failed++;
            }
        }

        return Ok(new { message = "Import complete.", imported, skipped, failed, total = urls.Count });
    }

    [HttpPost("podcasts/refresh")]
    public IActionResult RefreshPodcasts()
    {
        _ = Task.Run(() => _podcastSvc.RefreshAllFeedsAsync(_scopeFactory));
        return Ok(new { message = "Podcast refresh started in background." });
    }

    [HttpGet("podcasts/{id}/episodes")]
    public async Task<IActionResult> GetPodcastEpisodes(int id, [FromQuery] bool? played, [FromQuery] string? search)
    {
        var feed = await _podcastDb.Feeds.FindAsync(id);
        if (feed == null) return NotFound();

        var query = _podcastDb.Episodes.Where(e => e.FeedId == id);
        if (played.HasValue) query = query.Where(e => e.IsPlayed == played.Value);
        if (!string.IsNullOrEmpty(search))
            query = query.Where(e => e.Title.Contains(search) || e.Description.Contains(search));

        var episodes = await query.OrderByDescending(e => e.PublishDate).ToListAsync();
        return Ok(episodes.Select(e => new
        {
            e.Id, e.FeedId, e.Title, e.Description, e.MediaUrl, e.MediaType,
            e.DurationSeconds, e.PublishDate, e.Guid, e.IsPlayed, e.PlayPositionSeconds, e.DateFetched
        }));
    }

    [HttpPost("podcasts/{feedId}/ep/{epId}/played")]
    public async Task<IActionResult> ToggleEpisodePlayed(int feedId, int epId)
    {
        var ep = await _podcastDb.Episodes.FirstOrDefaultAsync(e => e.Id == epId && e.FeedId == feedId);
        if (ep == null) return NotFound();
        ep.IsPlayed = !ep.IsPlayed;
        await _podcastDb.SaveChangesAsync();
        return Ok(new { isPlayed = ep.IsPlayed });
    }

    [HttpPost("podcasts/{feedId}/ep/{epId}/progress")]
    public async Task<IActionResult> SaveEpisodeProgress(int feedId, int epId, [FromBody] System.Text.Json.JsonElement body)
    {
        var ep = await _podcastDb.Episodes.FirstOrDefaultAsync(e => e.Id == epId && e.FeedId == feedId);
        if (ep == null) return NotFound();
        if (body.TryGetProperty("position", out var pos))
            ep.PlayPositionSeconds = pos.GetInt32();
        await _podcastDb.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("podcasts/{id}/favourite")]
    public IActionResult TogglePodcastFavourite(int id)
    {
        var isFav = _userFavs.ToggleFavourite(CurrentUsername, "podcast", id);
        return Ok(new { isFavourite = isFav });
    }

    [HttpGet("podcasts/favourites")]
    public async Task<IActionResult> GetPodcastFavourites()
    {
        var favIds = _userFavs.GetFavouriteIds(CurrentUsername, "podcast");
        if (favIds.Count == 0) return Ok(new { podcasts = Array.Empty<object>() });

        var feeds = await _podcastDb.Feeds
            .Where(f => favIds.Contains(f.Id))
            .OrderByDescending(f => f.DateAdded)
            .ToListAsync();

        var unplayedCounts = await _podcastDb.Episodes
            .Where(e => favIds.Contains(e.FeedId) && !e.IsPlayed)
            .GroupBy(e => e.FeedId)
            .Select(g => new { FeedId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FeedId, x => x.Count);

        var result = feeds.Select(f => new
        {
            f.Id, f.Title, f.Description, f.Author, f.RssUrl,
            f.ArtworkUrl, f.ArtworkFile, f.Category, f.Language,
            f.EpisodeCount, f.LastRefreshed, f.DateAdded,
            isFavourite = true,
            unplayedCount = unplayedCounts.GetValueOrDefault(f.Id, 0)
        });
        return Ok(new { podcasts = result });
    }

    [HttpGet("podcasts/proxy")]
    public async Task ProxyPodcastAudio([FromQuery] string url)
    {
        var ct = HttpContext.RequestAborted;

        // SSRF guard: only public http/https targets. NetGuard's connect callback re-checks
        // every address actually dialled, so redirects to internal hosts are refused too.
        if (await NetGuard.ValidateOutboundUrlAsync(url, ct) is { } urlError)
        {
            Response.StatusCode = 400;
            await Response.WriteAsync(urlError, ct);
            return;
        }

        // Timeout covers headers only; the body copy is bounded by the client disconnect (ct).
        using var http = NetGuard.CreateOutboundClient(_httpFactory, Timeout.InfiniteTimeSpan);

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (Request.Headers.TryGetValue("Range", out var rangeVal))
            req.Headers.TryAddWithoutValidation("Range", rangeVal.ToArray());

        HttpResponseMessage resp;
        try
        {
            using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            headerCts.CancelAfter(TimeSpan.FromSeconds(30));
            resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, headerCts.Token);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Podcast proxy refused/failed for {Url}: {Msg}", url, ex.Message);
            Response.StatusCode = 502;
            return;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Response.StatusCode = 504;
            return;
        }
        using var _ = resp;
        Response.StatusCode = (int)resp.StatusCode;
        Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "audio/mpeg";
        if (resp.Content.Headers.ContentLength.HasValue)
            Response.ContentLength = resp.Content.Headers.ContentLength;
        if (resp.Content.Headers.ContentRange != null)
            Response.Headers["Content-Range"] = resp.Content.Headers.ContentRange.ToString();
        if (resp.Headers.AcceptRanges.Count > 0)
            Response.Headers["Accept-Ranges"] = string.Join(", ", resp.Headers.AcceptRanges);

        try { await resp.Content.CopyToAsync(Response.Body, ct); }
        catch (OperationCanceledException) { /* client went away */ }
    }

    // ─── Go Big Remote Control ────────────────────────────────────────

    private static readonly ConcurrentDictionary<string, GoBigSession> _gobigSessions = new();
    private static readonly ConcurrentDictionary<string, bool> _gobigRequests = new();

    // Long-poll tuning. With ?wait=1 the status / pending / poll endpoints hold the request
    // until something changes (checked every step) or the hold expires, instead of the SPA
    // hammering them every second from every open tab. Hold stays well under the relay's
    // 60 s upstream timeout and Cloudflare's 100 s.
    private const int GoBigHoldMs     = 25_000;
    private const int GoBigCmdHoldMs  = 8_000;   // desktop command poll: shorter so LastSeen stays fresh
    private const int GoBigStepMs     = 250;

    private static bool GoBigDesktopActive(string user) =>
        _gobigSessions.TryGetValue(user, out var s) && (DateTime.UtcNow - s.LastSeen).TotalSeconds < 10;

    /// <summary>Waits (step by step) until <paramref name="done"/> is true, the hold expires, or the client disconnects.</summary>
    private async Task GoBigHoldAsync(Func<bool> done, int holdMs, Action? eachStep = null)
    {
        // Cancel the hold on client disconnect (RequestAborted) OR server shutdown
        // (ApplicationStopping). Without the latter, an in-flight long-poll keeps a
        // request alive across graceful shutdown, so Kestrel waits out the full hold
        // (up to 25 s) before the process can exit.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            HttpContext.RequestAborted, _appLifetime.ApplicationStopping);
        var ct = linked.Token;
        var deadline = DateTime.UtcNow.AddMilliseconds(holdMs);
        while (!done() && DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try { await Task.Delay(GoBigStepMs, ct); } catch (OperationCanceledException) { break; }
            eachStep?.Invoke();
        }
    }

    /// <summary>Desktop announces it entered Go Big mode (acts as heartbeat).</summary>
    [HttpPost("gobig/announce")]
    public IActionResult GoBigAnnounce()
    {
        var isNew = !_gobigSessions.ContainsKey(CurrentUsername);
        var session = _gobigSessions.GetOrAdd(CurrentUsername, _ => new GoBigSession());
        session.LastSeen = DateTime.UtcNow;
        if (isNew) _logger.LogInformation("Go Big mode started for user '{User}'", CurrentUsername);
        return Ok(new { ok = true });
    }

    /// <summary>
    /// Mobile checks whether a desktop session is active for this user.
    /// ?wait=1&amp;known=0|1 long-polls: returns as soon as the active state differs from
    /// what the client already knows, or after the hold with the unchanged state.
    /// </summary>
    [HttpGet("gobig/status")]
    public async Task<IActionResult> GoBigStatus([FromQuery] int wait = 0, [FromQuery] int known = -1)
    {
        var user = CurrentUsername;
        if (wait == 1 && known is 0 or 1)
            await GoBigHoldAsync(() => GoBigDesktopActive(user) != (known == 1), GoBigHoldMs);
        return Ok(new { active = GoBigDesktopActive(user) });
    }

    /// <summary>Mobile enqueues a command for the desktop to execute.</summary>
    [HttpPost("gobig/command")]
    public async Task<IActionResult> GoBigCommand()
    {
        using var sr = new System.IO.StreamReader(Request.Body);
        var body = await sr.ReadToEndAsync();
        if (_gobigSessions.TryGetValue(CurrentUsername, out var s))
            s.Commands.Enqueue(body);
        return Ok(new { ok = true });
    }

    /// <summary>
    /// Desktop polls for pending commands; also acts as heartbeat.
    /// ?wait=1 long-polls until a command arrives (LastSeen is refreshed on every step so the
    /// mobile side keeps seeing the session as active).
    /// </summary>
    [HttpGet("gobig/poll")]
    public async Task<IActionResult> GoBigPoll([FromQuery] int wait = 0)
    {
        var session = _gobigSessions.GetOrAdd(CurrentUsername, _ => new GoBigSession());
        session.LastSeen = DateTime.UtcNow;
        if (wait == 1)
            await GoBigHoldAsync(() => !session.Commands.IsEmpty, GoBigCmdHoldMs,
                                 () => session.LastSeen = DateTime.UtcNow);
        var cmds = new List<string>();
        while (session.Commands.TryDequeue(out var cmd))
            cmds.Add(cmd);
        return Ok(new { commands = cmds });
    }

    /// <summary>Desktop signals it has left Go Big mode.</summary>
    [HttpDelete("gobig/session")]
    public IActionResult GoBigSessionEnd()
    {
        _gobigSessions.TryRemove(CurrentUsername, out _);
        _logger.LogInformation("Go Big mode ended for user '{User}'", CurrentUsername);
        return Ok(new { ok = true });
    }

    /// <summary>Mobile requests the desktop to enter Go Big mode for this user (per-user, session-only).</summary>
    [HttpPost("gobig/request")]
    public IActionResult GoBigRequest()
    {
        _gobigRequests[CurrentUsername] = true;
        return Ok(new { queued = true });
    }

    /// <summary>
    /// Desktop polls whether mobile requested Go Big; clears the flag on read.
    /// ?wait=1 long-polls until a request appears or the hold expires.
    /// </summary>
    [HttpGet("gobig/pending")]
    public async Task<IActionResult> GoBigPending([FromQuery] int wait = 0)
    {
        var user = CurrentUsername;
        if (wait == 1)
            await GoBigHoldAsync(() => _gobigRequests.ContainsKey(user), GoBigHoldMs);
        var pending = _gobigRequests.TryRemove(user, out _);
        if (pending) _logger.LogInformation("Mobile triggered Go Big for user '{User}'", user);
        return Ok(new { pending });
    }

    /// <summary>
    /// Injects a real OS-level 'F' keypress so the desktop browser can call requestFullscreen().
    /// The Fullscreen API requires a trusted user gesture; the poll callback is not one.
    /// An OS keystroke IS trusted - the browser receives it exactly as if the user pressed F.
    /// Requires NexusM to run on the same machine as the Go Big browser (typical home setup).
    /// Windows: user32 keybd_event. Linux/X11: xdotool. Linux/Wayland: ydotool.
    /// </summary>
    [HttpPost("gobig/keypress")]
    public IActionResult GoBigKeypress()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                GoBigKeypressHelper.SendFWindows();
            else
                GoBigKeypressHelper.SendFLinux();
            return Ok(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning("gobig/keypress: {Msg}", ex.Message);
            return Ok(new { ok = false, error = ex.Message });
        }
    }

    // ─── New Releases ───────────────────────────────────────────────────────

    /// <summary>
    /// Fetch new or upcoming releases from TMDB.
    /// category: movies | tv | anime | documentary | cartoon
    /// country:  ISO 3166-1 alpha-2 code (US, GB, FR …) - empty = global
    /// genre:    TMDB genre id (only for movies/tv categories)
    /// mode:     recent | upcoming
    /// </summary>
    [HttpGet("new-releases")]
    public async Task<IActionResult> GetNewReleases(
        [FromQuery] string category = "movies",
        [FromQuery] string country  = "US",
        [FromQuery] string genre    = "",
        [FromQuery] int    page     = 1,
        [FromQuery] string mode     = "recent")
    {
        try
        {
            var result = await _newReleases.GetReleasesAsync(
                category.ToLowerInvariant(),
                country.ToUpperInvariant(),
                genre,
                Math.Max(1, page),
                mode == "upcoming" ? "upcoming" : "recent");
            return Ok(result);
        }
        catch
        {
            return StatusCode(503, new { error = "fetch_failed" });
        }
    }

    [HttpGet("new-releases/genres")]
    public IActionResult GetNewReleasesGenres([FromQuery] string category = "movies")
    {
        return Ok(_newReleases.GetGenres(category.ToLowerInvariant()));
    }

    [HttpGet("new-releases/trailer")]
    public async Task<IActionResult> GetNewReleasesTrailer(
        [FromQuery] string type = "movie",
        [FromQuery] int    id   = 0)
    {
        return Ok(await _newReleases.GetTrailerAsync(type, id));
    }

    // ── Watchmode - "Where to Watch?" streaming search ───────────────────────

    [HttpGet("watchmode/search")]
    public async Task<IActionResult> WatchmodeSearch(
        [FromQuery] string q      = "",
        [FromQuery] string type   = "",
        [FromQuery] string region = "US")
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(new { results = Array.Empty<object>() });
        return Ok(await _watchmode.SearchAsync(q.Trim(), type, region));
    }

    [HttpGet("watchmode/sources")]
    public async Task<IActionResult> WatchmodeSources(
        [FromQuery] int    id     = 0,
        [FromQuery] string region = "US")
    {
        if (id <= 0) return Ok(new { sources = Array.Empty<object>() });
        return Ok(await _watchmode.GetSourcesAsync(id, region));
    }

    // ── Server Resource Metrics ──────────────────────────────────────────────
    // Returns the last 3 hours of CPU/RAM samples collected by MetricsCollectorService.
    // The JSON file is written atomically every 15 seconds; we read it directly.
    [HttpGet("metrics")]
    public IActionResult GetMetrics()
    {
        var path = Path.Combine("assets", "metrics", "metrics.json");
        if (!System.IO.File.Exists(path))
            return Ok(Array.Empty<object>());
        try
        {
            var json = System.IO.File.ReadAllText(path);
            return Content(json, "application/json");
        }
        catch
        {
            return Ok(Array.Empty<object>());
        }
    }

    // ─── Trakt.tv ────────────────────────────────────────────────────────

    [HttpPost("trakt/connect")]
    public async Task<IActionResult> TraktConnect()
    {
        var result = await _trakt.StartDeviceAuthAsync(CurrentUsername);
        if (result == null)
            return BadRequest(new { error = "Trakt connection failed. Check Client ID in settings." });
        return Ok(new
        {
            userCode        = result.Value.UserCode,
            verificationUrl = result.Value.VerificationUrl,
            expiresIn       = result.Value.ExpiresIn,
            interval        = result.Value.Interval
        });
    }

    [HttpGet("trakt/poll")]
    public async Task<IActionResult> TraktPoll()
    {
        var status = await _trakt.PollDeviceAuthAsync(CurrentUsername);
        var traktUser = status == "authorized" ? _trakt.GetTraktUsername(CurrentUsername) : null;
        return Ok(new { status, traktUsername = traktUser ?? "" });
    }

    [HttpDelete("trakt/connect")]
    public IActionResult TraktDisconnect()
    {
        _trakt.Disconnect(CurrentUsername);
        return Ok(new { success = true });
    }

    [HttpPost("trakt/pin")]
    public async Task<IActionResult> TraktPin([FromBody] System.Text.Json.JsonElement body)
    {
        if (!body.TryGetProperty("pin", out var pinEl) || string.IsNullOrWhiteSpace(pinEl.GetString()))
            return BadRequest(new { error = "PIN is required." });
        var (ok, traktUser) = await _trakt.ExchangePinAsync(CurrentUsername, pinEl.GetString()!);
        if (!ok) return BadRequest(new { error = "PIN exchange failed. Check the code and try again." });
        return Ok(new { success = true, traktUsername = traktUser ?? "" });
    }

    [HttpGet("trakt/status")]
    public IActionResult TraktStatus()
    {
        return Ok(new
        {
            connected    = _trakt.IsConnected(CurrentUsername),
            traktUsername = _trakt.GetTraktUsername(CurrentUsername) ?? ""
        });
    }

    [HttpPost("trakt/scrobble")]
    public async Task<IActionResult> TraktScrobble([FromBody] TraktScrobbleDto dto)
    {
        var video = await _videoDb.Videos.FindAsync(dto.VideoId);
        if (video == null) return NotFound();
        var ok = await _trakt.ScrobbleAsync(CurrentUsername, dto.Action, video, dto.Progress);
        return Ok(new { success = ok });
    }

    // ─── Last.fm ──────────────────────────────────────────────────────────

    [HttpGet("lastfm/auth-url")]
    public IActionResult LastFmAuthUrl()
    {
        if (string.IsNullOrEmpty(_config.Config.LastFm.ApiKey))
            return BadRequest(new { error = "Last.fm API key not configured. Add it to Settings → Scrobbling." });
        var callback = $"{Request.Scheme}://{Request.Host}/api/lastfm/callback";
        return Ok(new { url = _scrobbling.LfmGetAuthUrl(callback) });
    }

    // Called by the browser after the user approves on Last.fm (redirect target).
    [HttpGet("lastfm/callback")]
    public async Task<IActionResult> LastFmCallback([FromQuery] string token)
    {
        if (string.IsNullOrEmpty(token))
            return BadRequest("Missing token");
        var (ok, error) = await _scrobbling.LfmConnectAsync(CurrentUsername, token);
        // Redirect back to the SPA; JS will poll status to confirm connection.
        var status = ok ? "ok" : Uri.EscapeDataString(error);
        return Redirect($"/?lastfm={status}");
    }

    [HttpGet("lastfm/status")]
    public IActionResult LastFmStatus()
    {
        var connected   = _scrobbling.LfmIsConnected(CurrentUsername);
        var lfmUsername = _scrobbling.LfmGetUsername(CurrentUsername);
        var hasKey      = !string.IsNullOrEmpty(_config.Config.LastFm.ApiKey);
        return Ok(new { connected, lfmUsername, hasKey });
    }

    [HttpPost("lastfm/disconnect")]
    public IActionResult LastFmDisconnect()
    {
        _scrobbling.LfmDisconnect(CurrentUsername);
        return Ok(new { message = "Disconnected from Last.fm" });
    }

    [HttpGet("lastfm/similar-artists")]
    public async Task<IActionResult> GetSimilarArtists([FromQuery] string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return BadRequest();

        var similar = await _scrobbling.GetSimilarArtistsAsync(artist.Trim(), 30);
        if (similar.Count == 0) return Ok(new { similar = Array.Empty<object>() });

        // Match on the Unicode-folded ArtistKey (SQL lower() is ASCII-only - see MusicKey).
        var lowerNames = similar.Select(a => MusicKey.Of(a.Name)).ToList();
        var libraryMap = (await _db.Tracks
            .Where(t => lowerNames.Contains(t.ArtistKey))
            .GroupBy(t => t.ArtistKey)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync())
            .ToDictionary(x => x.Name, x => x.Count);

        var result = similar.Select(a => new
        {
            a.Name,
            a.Url,
            Match      = (int)Math.Round(a.Match * 100),
            InLibrary  = libraryMap.ContainsKey(MusicKey.Of(a.Name)),
            TrackCount = libraryMap.GetValueOrDefault(MusicKey.Of(a.Name), 0)
        });

        return Ok(new { similar = result });
    }

    [HttpGet("lastfm/artist-info")]
    public async Task<IActionResult> GetLastFmArtistInfo([FromQuery] string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return BadRequest();
        var info = await _scrobbling.GetArtistInfoAsync(artist.Trim());
        if (info == null) return Ok(new { bio = (string?)null, tags = Array.Empty<string>(), listeners = 0L, playCount = 0L });

        // Cache bio to Artist row so subsequent page loads are instant
        if (!string.IsNullOrEmpty(info.Bio))
        {
            var dbArtist = await _db.Artists
                .FirstOrDefaultAsync(a => a.Name == artist.Trim());
            if (dbArtist != null && string.IsNullOrEmpty(dbArtist.Bio))
            {
                dbArtist.Bio = info.Bio;
                await _db.SaveChangesAsync();
            }
        }

        return Ok(new { bio = info.Bio, tags = info.Tags, listeners = info.Listeners, playCount = info.PlayCount, url = info.Url });
    }

    [HttpGet("radio/artist")]
    public async Task<IActionResult> ArtistRadio([FromQuery] string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return BadRequest();
        artist = artist.Trim();

        var similar   = await _scrobbling.GetSimilarArtistsAsync(artist, 30);
        // Match on the Unicode-folded ArtistKey (SQL lower() is ASCII-only - see MusicKey).
        var nameSet   = similar.Select(a => MusicKey.Of(a.Name)).ToHashSet();
        nameSet.Add(MusicKey.Of(artist));

        var tracks = await _db.Tracks
            .Where(t => nameSet.Contains(t.ArtistKey))
            .AsNoTracking()
            .ToListAsync();

        if (tracks.Count == 0)
        {
            var artistKey = MusicKey.Of(artist);
            tracks = await _db.Tracks
                .Where(t => t.ArtistKey == artistKey)
                .AsNoTracking()
                .ToListAsync();
        }

        var favIds = _userFavs.GetFavouriteIds(CurrentUsername, "track");
        foreach (var t in tracks) t.IsFavourite = favIds.Contains(t.Id);

        var shuffled = tracks.OrderBy(_ => Random.Shared.Next()).Take(200).ToList();
        return Ok(new { tracks = shuffled, total = shuffled.Count });
    }

    // ─── ListenBrainz ────────────────────────────────────────────────────

    [HttpPost("listenbrainz/connect")]
    public async Task<IActionResult> ListenBrainzConnect([FromBody] ListenBrainzConnectDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Token))
            return BadRequest(new { error = "Token is required" });
        var (ok, error) = await _scrobbling.LbConnectAsync(CurrentUsername, dto.Token.Trim());
        if (!ok) return BadRequest(new { error });
        return Ok(new { message = "ListenBrainz connected", lbUsername = _scrobbling.LbGetUsername(CurrentUsername) });
    }

    [HttpGet("listenbrainz/status")]
    public IActionResult ListenBrainzStatus()
    {
        var connected  = _scrobbling.LbIsConnected(CurrentUsername);
        var lbUsername = _scrobbling.LbGetUsername(CurrentUsername);
        return Ok(new { connected, lbUsername });
    }

    [HttpPost("listenbrainz/disconnect")]
    public IActionResult ListenBrainzDisconnect()
    {
        _scrobbling.LbDisconnect(CurrentUsername);
        return Ok(new { message = "Disconnected from ListenBrainz" });
    }

    // ─── Music Scrobble ───────────────────────────────────────────────────

    [HttpPost("scrobble")]
    public async Task<IActionResult> Scrobble([FromBody] ScrobbleDto dto)
    {
        // Server-side already scrobbled this track - skip to avoid duplicates from web clients
        if (_streaming.WasRecentlyScrobbled(CurrentUsername, dto.TrackId))
            return Ok(new { message = "Already scrobbled server-side" });

        var track = await _db.Tracks.FindAsync(dto.TrackId);
        if (track == null) return NotFound();

        await _scrobbling.ScrobbleAsync(
            CurrentUsername,
            track.Artist ?? "",
            track.Title  ?? "",
            track.Album  ?? "",
            dto.Timestamp,
            dto.Duration);

        return Ok(new { message = "Scrobbled" });
    }

    [HttpPost("scrobble/now-playing")]
    public async Task<IActionResult> ScrobbleNowPlaying([FromBody] ScrobbleDto dto)
    {
        var track = await _db.Tracks.FindAsync(dto.TrackId);
        if (track == null) return NotFound();

        _scrobbling.LfmNowPlayingFireAndForget(
            CurrentUsername,
            track.Artist ?? "",
            track.Title  ?? "",
            track.Album  ?? "",
            dto.Duration);

        return Ok(new { message = "Now playing sent" });
    }

    // ─── Video Thumbnail Sprite Endpoints ──────────────────────────────────

    [HttpPost("video-thumbnails/{type}/{id}/generate")]
    public async Task<IActionResult> GenerateVideoThumbnails(string type, int id)
    {
        if (!_thumbnails.IsEnabled)
            return BadRequest(new { error = "Video thumbnails disabled" });
        if (!_ffmpeg.IsAvailable)
            return BadRequest(new { error = "FFmpeg not available" });

        string? filePath = null;
        double  duration = 0;

        if (type == "video")
        {
            var v = await _videoDb.Videos.FindAsync(id);
            if (v == null) return NotFound();
            filePath = v.FilePath;
            duration = v.Duration;
        }
        else if (type == "musicvideo")
        {
            var mv = await _mvDb.MusicVideos.FindAsync(id);
            if (mv == null) return NotFound();
            filePath = mv.FilePath;
            duration = mv.Duration;
        }
        else return BadRequest(new { error = "Unknown type" });

        if (!System.IO.File.Exists(filePath)) return NotFound();

        _thumbnails.TriggerGeneration(type, id, filePath, duration);
        return Ok(new { started = true });
    }

    [HttpGet("video-thumbnails/{type}/{id}/status")]
    public IActionResult GetVideoThumbnailStatus(string type, int id)
    {
        var ready      = _thumbnails.IsReady(type, id);
        var generating = _thumbnails.IsGenerating(type, id);

        if (ready)
        {
            var meta = _thumbnails.GetMetadata(type, id);
            if (meta == null) return Ok(new { ready = false, generating });
            return Ok(new
            {
                ready      = true,
                generating = false,
                meta.Cols,
                meta.Rows,
                meta.Interval,
                meta.FrameW,
                meta.FrameH,
                meta.TotalFrames
            });
        }
        return Ok(new { ready = false, generating });
    }

    [HttpGet("video-thumbnails/{type}/{id}/sprite.jpg")]
    public IActionResult GetVideoThumbnailSprite(string type, int id)
    {
        var path = _thumbnails.SpritePath(type, id);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "image/jpeg");
    }

    [HttpGet("video-thumbnails/{type}/{id}/thumbnails.vtt")]
    public IActionResult GetVideoThumbnailVtt(string type, int id)
    {
        var path = _thumbnails.VttPath(type, id);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "text/vtt");
    }

    // ─── Video Preview Clip Endpoints ─────────────────────────────────────────

    [HttpGet("vpreviews/status")]
    public IActionResult GetVideoPreviewStatus()
    {
        return Ok(new
        {
            enabled       = _vpreviews.IsEnabled,
            scanning      = _vpreviews.IsScanning,
            total         = _vpreviews.TotalVideos,
            generated     = _vpreviews.Generated,
            ratePerMinute = _vpreviews.RatePerMinute,
            lastError     = _vpreviews.LastError,
            currentItem   = _vpreviews.CurrentItem,
            ffmpegAvailable = _ffmpeg.IsAvailable
        });
    }

    [HttpPost("vpreviews/start")]
    public IActionResult StartVideoPreviewScan()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin") return Forbid();
        if (!_ffmpeg.IsAvailable) return BadRequest(new { error = "FFmpeg not available - cannot generate previews" });
        _vpreviews.StartScan();
        return Ok(new { started = true });
    }

    [HttpPost("vpreviews/stop")]
    public IActionResult StopVideoPreviewScan()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin") return Forbid();
        _vpreviews.StopScan();
        return Ok(new { stopped = true });
    }

    [HttpDelete("vpreviews/all")]
    public IActionResult ClearAllVideoPreviews()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin") return Forbid();
        var deleted = _vpreviews.ClearAll();
        return Ok(new { deleted });
    }

    [HttpGet("vpreviews/{type}/{id}")]
    public IActionResult GetVideoPreview(string type, int id)
    {
        if (type != "video" && type != "musicvideo")
            return BadRequest(new { error = "Invalid type" });

        var path = _vpreviews.GetPreviewPath(type, id);
        if (!System.IO.File.Exists(path)) return NotFound();

        Response.Headers["Accept-Ranges"] = "bytes";
        return PhysicalFile(path, "video/mp4", enableRangeProcessing: true);
    }

    // ─── Access Gate ─────────────────────────────────────────────────────────

    [HttpGet("gate/status")]
    public IActionResult GateStatus()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin") return Forbid();
        var g = _config.Config.Gate;
        var now = DateTime.UtcNow;
        return Ok(new
        {
            enabled    = g.Enabled,
            cookieDays = g.CookieDays,
            codes      = g.Codes.Select((c, i) => new
            {
                index     = i,
                code      = c.Code,
                label     = c.Label,
                expires   = c.Expires?.ToString("yyyy-MM-dd"),
                isExpired = c.Expires.HasValue && c.Expires.Value < now
            })
        });
    }

    [HttpPost("gate/enable")]
    public IActionResult GateEnable()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin") return Forbid();
        var g = _config.Config.Gate;
        if (g.Codes.Count == 0)
            g.Codes.Add(new GateCodeEntry { Code = Program.GenerateGateCode(), Label = "" });
        g.Enabled = true;
        _config.SaveConfig();
        return Ok(new { enabled = true });
    }

    [HttpPost("gate/disable")]
    public IActionResult GateDisable()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin") return Forbid();
        _config.Config.Gate.Enabled = false;
        _config.SaveConfig();
        return Ok(new { enabled = false });
    }

    [HttpPost("gate/codes/add")]
    public IActionResult GateAddCode([FromBody] GateAddCodeDto? dto)
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin") return Forbid();
        var codes = _config.Config.Gate.Codes;
        if (codes.Count >= 10) return BadRequest(new { error = "Maximum 10 invite codes allowed." });
        DateTime? expires = null;
        if (!string.IsNullOrEmpty(dto?.Expires))
        {
            if (!DateTime.TryParse(dto.Expires, out var dt))
                return BadRequest(new { error = "Invalid expiry date." });
            var utcExpires = dt.ToUniversalTime();
            if (utcExpires <= DateTime.UtcNow)
                return BadRequest(new { error = "Expiry date must be in the future." });
            if ((utcExpires - DateTime.UtcNow).TotalDays > 360)
                return BadRequest(new { error = "Expiry date cannot be more than 360 days from now." });
            expires = utcExpires;
        }
        // Managing codes does not by itself turn the gate on - the Enabled toggle is
        // authoritative (the gate is opt-in and OFF by default).
        codes.Add(new GateCodeEntry { Code = Program.GenerateGateCode(), Label = dto?.Label ?? "", Expires = expires });
        _config.SaveConfig();
        return Ok(new { added = true });
    }

    [HttpDelete("gate/codes/{index:int}")]
    public IActionResult GateDeleteCode(int index)
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin") return Forbid();
        var codes = _config.Config.Gate.Codes;
        if (index < 0 || index >= codes.Count) return NotFound();
        codes.RemoveAt(index);
        _config.SaveConfig();
        return Ok(new { deleted = true });
    }

    [HttpPost("gate/codes/{index:int}/regenerate")]
    public IActionResult GateRegenerateCode(int index)
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin") return Forbid();
        var codes = _config.Config.Gate.Codes;
        if (index < 0 || index >= codes.Count) return NotFound();
        codes[index].Code = Program.GenerateGateCode();
        _config.SaveConfig();
        return Ok(new { code = codes[index].Code });
    }

    [HttpPost("gate/codes/{index:int}/update")]
    public IActionResult GateUpdateCode(int index, [FromBody] GateCodeUpdateDto dto)
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin") return Forbid();
        var codes = _config.Config.Gate.Codes;
        if (index < 0 || index >= codes.Count) return NotFound();
        codes[index].Label = dto.Label ?? "";
        codes[index].Expires = string.IsNullOrEmpty(dto.Expires)
            ? null
            : DateTime.TryParse(dto.Expires, out var dt) ? dt.ToUniversalTime() : (DateTime?)null;
        _config.SaveConfig();
        return Ok(new { updated = true });
    }

    [HttpGet("gate/qr.png")]
    public IActionResult GateQrCode([FromQuery] string? code = null)
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin") return Forbid();
        var g = _config.Config.Gate;
        GateCodeEntry? entry = !string.IsNullOrEmpty(code)
            ? g.Codes.FirstOrDefault(c => c.Code == code)
            : g.Codes.FirstOrDefault(c => !string.IsNullOrEmpty(c.Code));
        if (entry == null || string.IsNullOrEmpty(entry.Code)) return NotFound();
        var scheme = Request.Headers["X-Forwarded-Proto"].FirstOrDefault()
                     ?? (Request.IsHttps ? "https" : "http");
        var host = Request.Headers["X-Forwarded-Host"].FirstOrDefault()
                   ?? Request.Host.Value;
        var accessUrl = $"{scheme}://{host}/?access={Program.GateCookieValue(entry.Code, g.HmacSecret)}";
        var png = CloudflareTunnelService.MakeUrlQrPng(accessUrl);
        if (png == null) return NotFound();
        return File(png, "image/png");
    }

    // ─── Cloudflare Quick Tunnel ─────────────────────────────────────────────

    [HttpGet("tunnel/status")]
    public IActionResult TunnelStatus()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
            return Forbid();
        return Ok(new CloudflareTunnelStatusDto
        {
            Status = _cfTunnel.Status,
            TunnelUrl = _cfTunnel.TunnelUrl,
            ErrorMessage = _cfTunnel.ErrorMessage,
            CloudflaredPresent = _cfTunnel.IsCloudflaredPresent()
        });
    }

    [HttpPost("tunnel/start")]
    public IActionResult TunnelStart()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
            return Forbid();
        var cfg = _config.Config;
        // Always use the plain HTTP port - cloudflared rejects self-signed certs on the origin.
        // Cloudflare handles public HTTPS; the local cloudflared→NexusM leg is plain HTTP.
        var port = cfg.Server.ServerPort;
        // CancellationToken.None - must NOT be tied to this HTTP request's lifetime
        _ = _cfTunnel.StartAsync(port, CancellationToken.None);
        return Ok(new { started = true });
    }

    [HttpPost("tunnel/stop")]
    public IActionResult TunnelStop()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
            return Forbid();
        _cfTunnel.Stop();
        return Ok(new { stopped = true });
    }

    [HttpGet("tunnel/qr.png")]
    public IActionResult TunnelQrCode()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
            return Forbid();
        var url = _cfTunnel.TunnelUrl;
        if (string.IsNullOrEmpty(url)) return NotFound();
        var png = CloudflareTunnelService.MakeUrlQrPng(url);
        if (png == null) return NotFound();
        return File(png, "image/png");
    }

    // ─── NexusM Relay (nexusm.net permanent tunnel) ──────────────────────────

    [HttpGet("relay/status")]
    public IActionResult RelayStatus()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
            return Forbid();
        return Ok(new
        {
            enabled       = _config.Config.RemoteAccess.TunnelEnabled,
            status        = _relay.Status,
            connected     = _relay.IsConnected,
            directCapable = _relay.IsDirectCapable,
            directBaseUrl = _relay.DirectBaseUrl,
            tunnelUrl     = _relay.TunnelUrl,
            subdomain     = _relay.Subdomain,
            fingerprint   = _relay.Fingerprint,
            errorMessage  = _relay.ErrorMessage
        });
    }

    [HttpPost("relay/enable")]
    public IActionResult RelayEnable()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
            return Forbid();
        _relay.Enable();
        return Ok(new { enabled = true });
    }

    [HttpPost("relay/disable")]
    public IActionResult RelayDisable()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
            return Forbid();
        _relay.Disable();
        return Ok(new { enabled = false });
    }

    [HttpGet("relay/qr.png")]
    public IActionResult RelayQrCode()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
            return Forbid();
        var png = CloudflareTunnelService.MakeUrlQrPng(_relay.TunnelUrl);
        if (png == null) return NotFound();
        return File(png, "image/png");
    }

    // ─── Semantic (natural-language) search ───────────────────────────
    // Local, in-process embedding search. Opt-in (Config.SemanticSearch.Enabled). Mutating
    // endpoints are admin-gated; the query endpoint is self-scoped and falls back to keyword.

    private bool IsAdmin() => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "admin";

    [HttpGet("semantic/status")]
    public async Task<IActionResult> SemanticStatus()
    {
        if (!IsAdmin()) return Forbid();
        // After a server restart the model files + index persist on disk but the in-memory
        // session is gone. Reload it (fast - no re-download) so the panel shows Ready again.
        if (_semantic.Enabled && !_semantic.ModelReady) await _semantic.EnsureReadyAsync();
        return Ok(_semantic.GetStatus());
    }

    // Tail of logs/semantic.log - shows exactly what each index pass embedded (metadata) and which
    // videos had subtitles read / had none / failed (dialogue), for transparency into "what did it find?".
    [HttpGet("semantic/log")]
    public IActionResult SemanticLogTail([FromQuery] int lines = 400)
    {
        if (!IsAdmin()) return Forbid();
        return Ok(new { text = SemanticLog.ReadTail(Math.Clamp(lines, 20, 5000)) });
    }

    [HttpPost("semantic/enable")]
    public async Task<IActionResult> SemanticEnable([FromBody] JsonElement body)
    {
        if (!IsAdmin()) return Forbid();
        var enabled = body.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True;
        _config.Config.SemanticSearch.Enabled = enabled;
        _config.SaveConfig();
        if (enabled)
        {
            // Download (first run) + load the model, then index in the background.
            _ = Task.Run(async () =>
            {
                try
                {
                    if (await _semantic.EnsureReadyAsync())
                        await IndexSemanticAsync();
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Semantic enable/index failed"); }
            });
        }
        else _semantic.Unload();
        return Ok(_semantic.GetStatus());
    }

    [HttpPost("semantic/rebuild")]
    public IActionResult SemanticRebuild()
    {
        if (!IsAdmin()) return Forbid();
        if (!_semantic.Enabled) return BadRequest(new { error = "Semantic search is disabled." });
        _ = Task.Run(async () =>
        {
            try { _semantic.ClearIndex(); await IndexSemanticAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Semantic rebuild failed"); }
        });
        return Ok(new { started = true });
    }

    [HttpPost("semantic/clear")]
    public IActionResult SemanticClear()
    {
        if (!IsAdmin()) return Forbid();
        _semantic.ClearIndex();
        return Ok(_semantic.GetStatus());
    }

    // Incremental: embed only library items not already in the index (fast for a few new files).
    [HttpPost("semantic/index-new")]
    public IActionResult SemanticIndexNew()
    {
        if (!IsAdmin()) return Forbid();
        if (!_semantic.Enabled) return BadRequest(new { error = "Semantic search is disabled." });
        _ = Task.Run(async () =>
        {
            try { await IndexSemanticAsync(skipExisting: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Semantic index-new failed"); }
        });
        return Ok(new { started = true });
    }

    [HttpPost("semantic/settings")]
    public IActionResult SemanticSettings([FromBody] JsonElement body)
    {
        if (!IsAdmin()) return Forbid();
        var s = _config.Config.SemanticSearch;
        if (body.TryGetProperty("cpuThreads", out var ct) && ct.TryGetInt32(out var threads))
            s.CpuThreads = Math.Clamp(threads, 0, 32);
        if (body.TryGetProperty("indexOnlyWhenIdle", out var idle))
            s.IndexOnlyWhenIdle = idle.ValueKind == JsonValueKind.True;
        if (body.TryGetProperty("maxResults", out var mr) && mr.TryGetInt32(out var max))
            s.MaxResults = Math.Clamp(max, 5, 200);
        if (body.TryGetProperty("autoIndexOnScan", out var ai))
            s.AutoIndexOnScan = ai.ValueKind == JsonValueKind.True;
        _config.SaveConfig();
        // Thread cap is applied when the session is (re)loaded - reload if already running.
        if (_semantic.ModelReady) { _semantic.Unload(); _ = _semantic.EnsureReadyAsync(); }
        return Ok(_semantic.GetStatus());
    }

    /// <summary>Semantic query. Returns { available:false } when off/not-ready so the client
    /// transparently falls back to the normal keyword search.</summary>
    [HttpGet("search/semantic")]
    public async Task<IActionResult> SemanticSearch([FromQuery] string q, [FromQuery] string? type = null, [FromQuery] int limit = 40)
    {
        if (!_semantic.Enabled)
            return Ok(new { available = false, results = Array.Empty<object>() });
        // Lazy-load the model on the first query after a restart (session is not kept warm).
        if (!_semantic.ModelReady) await _semantic.EnsureReadyAsync();
        if (!_semantic.ModelReady)
            return Ok(new { available = false, results = Array.Empty<object>() });
        if (string.IsNullOrWhiteSpace(q))
            return Ok(new { available = true, results = Array.Empty<object>() });

        var hits = _semantic.Search(q, type, Math.Clamp(limit, 1, 100));
        var results = await ResolveSemanticHitsAsync(hits);
        return Ok(new { available = true, results });
    }

    // Gather searchable text from every media DB and (re)index it. The gather + index now lives in
    // SemanticSearchService.IndexAllAsync (shared with the auto-index-on-scan trigger); it resolves
    // its own DbContexts from a fresh DI scope, safe to call from a background task.
    private Task IndexSemanticAsync(bool skipExisting = false)
        => _semantic.IndexAllAsync(skipExisting);

    // Resolve raw (type,id,score) hits into light display objects. Orphans (media deleted after
    // indexing) simply fail to resolve and are skipped.
    private async Task<List<object>> ResolveSemanticHitsAsync(List<SemanticSearchService.SemanticHit> hits)
    {
        // One batched query per media kind (at most five) instead of one query per hit; the
        // hit order (best score first) is preserved by the final loop.
        int[] IdsOf(params string[] types) =>
            hits.Where(h => types.Contains(h.Type)).Select(h => (int)h.Id).Distinct().ToArray();
        var videoIds = IdsOf("movie", "tv", "anime", "documentary");
        var trackIds = IdsOf("music");
        var mvIds    = IdsOf("musicvideo");
        var bookIds  = IdsOf("ebook");
        var abIds    = IdsOf("audiobook");

        var videos = videoIds.Length == 0 ? new Dictionary<int, Video>()
            : await _videoDb.Videos.AsNoTracking().Where(x => videoIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var tracks = trackIds.Length == 0 ? new Dictionary<int, Track>()
            : await _db.Tracks.AsNoTracking().Where(x => trackIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var mvs = mvIds.Length == 0 ? new Dictionary<int, MusicVideo>()
            : await _mvDb.MusicVideos.AsNoTracking().Where(x => mvIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var books = bookIds.Length == 0 ? new Dictionary<int, EBook>()
            : await _ebookDb.EBooks.AsNoTracking().Where(x => bookIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var abooks = abIds.Length == 0 ? new Dictionary<int, AudioBook>()
            : await _audioBooksDb.AudioBooks.AsNoTracking().Where(x => abIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);

        var results = new List<object>();
        foreach (var h in hits)
        {
            var id = (int)h.Id;
            object? card = null;
            switch (h.Type)
            {
                case "movie": case "tv": case "anime": case "documentary":
                    if (videos.TryGetValue(id, out var v)) card = new { type = h.Type, id = v.Id, title = v.Title,
                        subtitle = v.SeriesName, year = v.Year, posterPath = v.PosterPath,
                        thumbnailPath = v.ThumbnailPath, mediaType = v.MediaType, score = h.Score };
                    break;
                case "music":
                    if (tracks.TryGetValue(id, out var t)) card = new { type = "music", id = t.Id, title = t.Title,
                        subtitle = t.Artist, album = t.Album, score = h.Score };
                    break;
                case "musicvideo":
                    if (mvs.TryGetValue(id, out var m)) card = new { type = "musicvideo", id = m.Id, title = m.Title,
                        subtitle = m.Artist, thumbnailPath = m.ThumbnailPath, score = h.Score };
                    break;
                case "ebook":
                    if (books.TryGetValue(id, out var b)) card = new { type = "ebook", id = b.Id, title = b.Title,
                        subtitle = b.Author, coverImage = b.CoverImage, score = h.Score };
                    break;
                case "audiobook":
                    if (abooks.TryGetValue(id, out var a)) card = new { type = "audiobook", id = a.Id, title = a.Title,
                        subtitle = a.Author, coverImage = a.CoverImage, score = h.Score };
                    break;
            }
            if (card != null) results.Add(card);
        }
        return results;
    }

    // ─── Dialogue (subtitle) search - the "cheap" exact-quote tier ─────
    // Independent sub-feature: extracts subtitle text into an FTS5 index so an exact spoken line
    // ("i'll be back") resolves to a video + timestamp the player can jump to. No embedding model.

    [HttpPost("semantic/dialogue/enable")]
    public IActionResult DialogueEnable([FromBody] JsonElement body)
    {
        if (!IsAdmin()) return Forbid();
        var enabled = body.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True;
        _config.Config.SemanticSearch.IndexDialogue = enabled;
        _config.SaveConfig();
        if (enabled)
            _ = Task.Run(async () =>
            {
                try { await IndexDialogueSemanticAsync(skipExisting: true); }
                catch (Exception ex) { _logger.LogWarning(ex, "Dialogue enable/index failed"); }
            });
        return Ok(_semantic.GetStatus());
    }

    [HttpPost("semantic/dialogue/rebuild")]
    public IActionResult DialogueRebuild()
    {
        if (!IsAdmin()) return Forbid();
        if (!_semantic.DialogueEnabled) return BadRequest(new { error = "Dialogue search is disabled." });
        _ = Task.Run(async () =>
        {
            try { _semantic.ClearDialogueIndex(); await IndexDialogueSemanticAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Dialogue rebuild failed"); }
        });
        return Ok(new { started = true });
    }

    [HttpPost("semantic/dialogue/index-new")]
    public IActionResult DialogueIndexNew()
    {
        if (!IsAdmin()) return Forbid();
        if (!_semantic.DialogueEnabled) return BadRequest(new { error = "Dialogue search is disabled." });
        _ = Task.Run(async () =>
        {
            try { await IndexDialogueSemanticAsync(skipExisting: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Dialogue index-new failed"); }
        });
        return Ok(new { started = true });
    }

    [HttpPost("semantic/dialogue/clear")]
    public IActionResult DialogueClear()
    {
        if (!IsAdmin()) return Forbid();
        _semantic.ClearDialogueIndex();
        return Ok(_semantic.GetStatus());
    }

    // Resource limits for the subtitle-extraction worker. Take effect on the next/ongoing run.
    [HttpPost("semantic/dialogue/settings")]
    public IActionResult DialogueSettings([FromBody] JsonElement body)
    {
        if (!IsAdmin()) return Forbid();
        var s = _config.Config.SemanticSearch;
        if (body.TryGetProperty("cpuThreads", out var ct) && ct.TryGetInt32(out var threads))
            s.DialogueCpuThreads = Math.Clamp(threads, 0, 32);
        if (body.TryGetProperty("priority", out var pr) && pr.ValueKind == JsonValueKind.String)
        {
            var p = (pr.GetString() ?? "").ToLowerInvariant();
            if (p is "idle" or "belownormal" or "normal") s.DialoguePriority = p;
        }
        if (body.TryGetProperty("throttleMs", out var th) && th.TryGetInt32(out var ms))
            s.DialogueThrottleMs = Math.Clamp(ms, 0, 60000);
        if (body.TryGetProperty("pauseWhileStreaming", out var pw))
            s.DialoguePauseWhileStreaming = pw.ValueKind == JsonValueKind.True;
        _config.SaveConfig();
        return Ok(_semantic.GetStatus());
    }

    /// <summary>Exact-quote dialogue search. Returns { available:false } when the feature is off.</summary>
    [HttpGet("search/dialogue")]
    public async Task<IActionResult> DialogueSearch([FromQuery] string q, [FromQuery] int limit = 40)
    {
        if (!_semantic.DialogueEnabled)
            return Ok(new { available = false, results = Array.Empty<object>() });
        if (string.IsNullOrWhiteSpace(q))
            return Ok(new { available = true, results = Array.Empty<object>() });
        var hits = _semantic.SearchDialogue(q, Math.Clamp(limit, 1, 100));
        var results = await ResolveDialogueHitsAsync(hits);
        return Ok(new { available = true, results });
    }

    private sealed record DialogueOccurrence(double seekTo, long startMs, string line);

    // Gather every video with a file path and (re)index its subtitles. Delegates to
    // SemanticSearchService.IndexAllDialogueAsync (shared with the auto-index-on-scan trigger),
    // which resolves its own DI scope - safe to call from a background task.
    private Task IndexDialogueSemanticAsync(bool skipExisting = false)
        => _semantic.IndexAllDialogueAsync(skipExisting);

    // Resolve dialogue hits → one card PER VIDEO carrying every matched line (occurrences) with its
    // own seek time, so all repetitions of a quote are shown. Hits arrive grouped-by-video already,
    // ordered best-video-first then chronological within a video.
    private async Task<List<object>> ResolveDialogueHitsAsync(List<SemanticSearchService.DialogueHit> hits)
    {
        var order = new List<long>();
        var byVideo = new Dictionary<long, List<SemanticSearchService.DialogueHit>>();
        foreach (var h in hits)
        {
            if (!byVideo.TryGetValue(h.Id, out var list)) { list = new(); byVideo[h.Id] = list; order.Add(h.Id); }
            list.Add(h);
        }

        // One batched query for every hit video instead of one per video.
        var videoIds = order.Select(x => (int)x).ToArray();
        var videos = videoIds.Length == 0 ? new Dictionary<int, Video>()
            : await _videoDb.Videos.AsNoTracking().Where(x => videoIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);

        var results = new List<object>();
        foreach (var id in order)
        {
            if (!videos.TryGetValue((int)id, out var v)) continue;
            var lines = byVideo[id];
            var occ = lines.Select(h => new DialogueOccurrence(Math.Max(0, h.StartMs / 1000.0), h.StartMs, h.Line)).ToList();
            results.Add(new
            {
                type = lines[0].Type, id = v.Id, title = v.Title, subtitle = v.SeriesName,
                year = v.Year, posterPath = v.PosterPath, thumbnailPath = v.ThumbnailPath,
                mediaType = v.MediaType, occurrences = occ, count = occ.Count,
                // first occurrence kept flat for any simple consumer.
                line = occ[0].line, seekTo = occ[0].seekTo, startMs = occ[0].startMs
            });
        }
        return results;
    }

    // ─── Scene (concept) search - the semantic half of the hybrid ───────
    // Chunked-subtitle embeddings: "the scene where he vows to return" → jump to the moment.
    // Reuses the dialogue resolver (SearchScenes returns DialogueHit) and jump-to-moment plumbing.

    [HttpPost("semantic/scenes/enable")]
    public IActionResult ScenesEnable([FromBody] JsonElement body)
    {
        if (!IsAdmin()) return Forbid();
        var enabled = body.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True;
        _config.Config.SemanticSearch.IndexScenes = enabled;
        _config.SaveConfig();
        if (enabled)
            _ = Task.Run(async () =>
            {
                try { await _semantic.IndexAllScenesAsync(skipExisting: true); }
                catch (Exception ex) { _logger.LogWarning(ex, "Scene enable/index failed"); }
            });
        return Ok(_semantic.GetStatus());
    }

    [HttpPost("semantic/scenes/rebuild")]
    public IActionResult ScenesRebuild()
    {
        if (!IsAdmin()) return Forbid();
        if (!_semantic.ScenesEnabled) return BadRequest(new { error = "Scene search is disabled." });
        _ = Task.Run(async () =>
        {
            try { _semantic.ClearSceneIndex(); await _semantic.IndexAllScenesAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Scene rebuild failed"); }
        });
        return Ok(new { started = true });
    }

    [HttpPost("semantic/scenes/index-new")]
    public IActionResult ScenesIndexNew()
    {
        if (!IsAdmin()) return Forbid();
        if (!_semantic.ScenesEnabled) return BadRequest(new { error = "Scene search is disabled." });
        _ = Task.Run(async () =>
        {
            try { await _semantic.IndexAllScenesAsync(skipExisting: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Scene index-new failed"); }
        });
        return Ok(new { started = true });
    }

    [HttpPost("semantic/scenes/clear")]
    public IActionResult ScenesClear()
    {
        if (!IsAdmin()) return Forbid();
        _semantic.ClearSceneIndex();
        return Ok(_semantic.GetStatus());
    }

    /// <summary>Concept scene search. Returns { available:false } when the feature is off.</summary>
    [HttpGet("search/scenes")]
    public async Task<IActionResult> SceneSearch([FromQuery] string q, [FromQuery] int limit = 40)
    {
        if (!_semantic.ScenesEnabled)
            return Ok(new { available = false, results = Array.Empty<object>() });
        if (string.IsNullOrWhiteSpace(q))
            return Ok(new { available = true, results = Array.Empty<object>() });
        var hits = _semantic.SearchScenes(q, Math.Clamp(limit, 1, 100));
        var results = await ResolveDialogueHitsAsync(hits);
        return Ok(new { available = true, results });
    }

    // ─── eBook (EPUB) in-book text search ───────────────────────────────
    // Passage FTS over EPUB text: "the part where the ring is destroyed" → open the reader on that
    // chapter. One card per book carrying every matched passage with its chapter jump target.

    private sealed record BookOccurrence(string href, string locator, string text);

    private async Task<List<object>> ResolveBookHitsAsync(List<SemanticSearchService.BookHit> hits)
    {
        var order = new List<long>();
        var byBook = new Dictionary<long, List<SemanticSearchService.BookHit>>();
        foreach (var h in hits)
        {
            if (!byBook.TryGetValue(h.Id, out var list)) { list = new(); byBook[h.Id] = list; order.Add(h.Id); }
            list.Add(h);
        }

        // One batched query for every hit book instead of one per book.
        var bookIds = order.Select(x => (int)x).ToArray();
        var books = bookIds.Length == 0 ? new Dictionary<int, EBook>()
            : await _ebookDb.EBooks.AsNoTracking().Where(x => bookIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);

        var results = new List<object>();
        foreach (var id in order)
        {
            if (!books.TryGetValue((int)id, out var b)) continue;
            var rows = byBook[id];
            var occ = rows.Select(h => new BookOccurrence(h.Href, h.Locator, h.Text)).ToList();
            results.Add(new
            {
                id = b.Id, title = b.Title, author = b.Author, coverImage = b.CoverImage,
                format = b.Format, occurrences = occ, count = occ.Count,
                href = occ[0].href, locator = occ[0].locator, snippet = occ[0].text
            });
        }
        return results;
    }

    [HttpPost("semantic/books/enable")]
    public IActionResult BooksEnable([FromBody] JsonElement body)
    {
        if (!IsAdmin()) return Forbid();
        var enabled = body.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True;
        _config.Config.SemanticSearch.IndexBooks = enabled;
        _config.SaveConfig();
        if (enabled)
            _ = Task.Run(async () =>
            {
                try { await _semantic.IndexAllBooksAsync(skipExisting: true); }
                catch (Exception ex) { _logger.LogWarning(ex, "Book enable/index failed"); }
            });
        return Ok(_semantic.GetStatus());
    }

    [HttpPost("semantic/books/rebuild")]
    public IActionResult BooksRebuild()
    {
        if (!IsAdmin()) return Forbid();
        if (!_semantic.BooksEnabled) return BadRequest(new { error = "Book search is disabled." });
        _ = Task.Run(async () =>
        {
            try { _semantic.ClearBookIndex(); await _semantic.IndexAllBooksAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Book rebuild failed"); }
        });
        return Ok(new { started = true });
    }

    [HttpPost("semantic/books/index-new")]
    public IActionResult BooksIndexNew()
    {
        if (!IsAdmin()) return Forbid();
        if (!_semantic.BooksEnabled) return BadRequest(new { error = "Book search is disabled." });
        _ = Task.Run(async () =>
        {
            try { await _semantic.IndexAllBooksAsync(skipExisting: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Book index-new failed"); }
        });
        return Ok(new { started = true });
    }

    [HttpPost("semantic/books/clear")]
    public IActionResult BooksClear()
    {
        if (!IsAdmin()) return Forbid();
        _semantic.ClearBookIndex();
        return Ok(_semantic.GetStatus());
    }

    /// <summary>In-book passage search. Returns { available:false } when the feature is off.</summary>
    [HttpGet("search/books")]
    public async Task<IActionResult> BookSearch([FromQuery] string q, [FromQuery] int limit = 40)
    {
        if (!_semantic.BooksEnabled)
            return Ok(new { available = false, results = Array.Empty<object>() });
        if (string.IsNullOrWhiteSpace(q))
            return Ok(new { available = true, results = Array.Empty<object>() });
        var hits = _semantic.SearchBooks(q, Math.Clamp(limit, 1, 100));
        var results = await ResolveBookHitsAsync(hits);
        return Ok(new { available = true, results });
    }

    // ─── EPG Guide ────────────────────────────────────────────────────

    [HttpGet("epg/status")]
    public async Task<IActionResult> EpgStatus() => Ok(await _epgService.GetStatusAsync());

    [HttpGet("epg/channel-ids")]
    public async Task<IActionResult> EpgChannelIds()
        => Ok(await _epgService.GetChannelIdsWithDataAsync());

    [HttpGet("epg/name-map")]
    public async Task<IActionResult> EpgNameMap()
        => Ok(await _epgService.GetNameMapAsync());

    [HttpGet("epg/now")]
    public async Task<IActionResult> EpgNow() => Ok(await _epgService.GetNowNextAsync());

    [HttpGet("epg/schedule")]
    public async Task<IActionResult> EpgSchedule([FromQuery] string tvgId, [FromQuery] string? date)
    {
        if (string.IsNullOrWhiteSpace(tvgId)) return BadRequest(new { error = "tvgId required" });
        var day = DateTime.TryParse(date, out var d) ? d : DateTime.Now;
        var programmes = await _epgService.GetScheduleAsync(tvgId, day);
        return Ok(programmes.Select(p => new
        {
            p.Title, p.Description, p.Category,
            start = p.Start.ToString("o"),
            stop  = p.Stop.ToString("o")
        }));
    }

    [HttpGet("epg/grid")]
    public async Task<IActionResult> EpgGrid([FromQuery] string? date = null)
    {
        var day = DateTime.TryParse(date, out var d) ? d : DateTime.UtcNow;
        return Ok(await _epgService.GetGridAsync(day));
    }

    [HttpGet("epg/sources")]
    public async Task<IActionResult> EpgGetSources()
    {
        var status = await _epgService.GetStatusAsync();
        return Ok(status);
    }

    [HttpPost("epg/sources")]
    public async Task<IActionResult> EpgAddSource([FromBody] EpgAddSourceDto dto)
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin") return Forbid();
        if (string.IsNullOrWhiteSpace(dto?.Url)) return BadRequest(new { error = "URL required" });
        var added = await _epgService.RegisterSourceAsync(dto.Url, dto.Name ?? "", isAuto: false);
        return Ok(new { added });
    }

    [HttpDelete("epg/sources/{id:int}")]
    public async Task<IActionResult> EpgDeleteSource(int id)
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin") return Forbid();
        await _epgService.DeleteSourceAsync(id);
        return Ok(new { success = true });
    }

    [HttpPost("epg/refresh")]
    public async Task<IActionResult> EpgRefresh()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin") return Forbid();
        _ = _epgService.RefreshAllAsync();
        return Ok(new { started = true });
    }

    // ─── Auto-Update ──────────────────────────────────────────────────

    [HttpGet("autoupdate/status")]
    public IActionResult AutoUpdateStatus() => Ok(_autoUpdate.GetStatus());

    [HttpPost("autoupdate/check")]
    public async Task<IActionResult> AutoUpdateCheck()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin") return Forbid();
        var ok = await _autoUpdate.CheckAsync();
        return Ok(new { success = ok, status = _autoUpdate.GetStatus() });
    }

    [HttpPost("autoupdate/download")]
    public async Task<IActionResult> AutoUpdateDownload()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin") return Forbid();
        var started = await _autoUpdate.StartDownloadAsync();
        return Ok(new { started });
    }

    [HttpGet("autoupdate/download-progress")]
    public IActionResult AutoUpdateDownloadProgress() => Ok(_autoUpdate.GetStatus());

    [HttpPost("autoupdate/apply")]
    public async Task<IActionResult> AutoUpdateApply()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin") return Forbid();
        var ok = await _autoUpdate.ApplyAsync();
        if (!ok) return BadRequest(new { error = _autoUpdate.GetStatus().ErrorMessage });
        return Ok(new { restarting = true });
    }

    [HttpPost("autoupdate/rollback")]
    public async Task<IActionResult> AutoUpdateRollback()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin") return Forbid();
        var ok = await _autoUpdate.RollbackAsync();
        if (!ok) return BadRequest(new { error = _autoUpdate.GetStatus().ErrorMessage });
        return Ok(new { restarting = true });
    }

    [HttpDelete("autoupdate/staging")]
    public IActionResult AutoUpdateDiscardStaging()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin") return Forbid();
        _autoUpdate.DiscardStaging();
        return Ok(new { success = true });
    }

    // ─── Server-side Google Cast ────────────────────────────────────────
    // NexusM discovers + controls Cast devices directly via the CASTV2 protocol. The device
    // fetches the stream from this server over plain HTTP with a short-lived ?token=. No browser
    // secure context / HTTPS needed - works like the Android client and DLNA casting.

    [HttpGet("cast/devices")]
    public async Task<IActionResult> CastDevices()
    {
        var devices = await _cast.DiscoverAsync();
        return Ok(new { devices });
    }

    [HttpPost("cast/play")]
    public async Task<IActionResult> CastPlay([FromBody] CastPlayDto dto)
    {
        if (dto == null || string.IsNullOrEmpty(dto.DeviceId))
            return BadRequest(new { error = "deviceId required" });

        // The Cast device pulls the stream over plain HTTP from this server's LAN address,
        // authenticated by a short-lived token (it cannot send cookies).
        var ip    = DlnaService.GetLocalIpAddress();
        var port  = _config.Config.Server.ServerPort;
        var role  = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "guest";
        // The token travels in the URL (the device cannot send headers), so it is registered
        // below with a scope limited to exactly the media + artwork paths built here.
        var token = ApiTokenStore.NewToken();

        string url, contentType;
        switch (dto.Kind)
        {
            case "video":
                (url, contentType) = await BuildCastVideoUrlAsync(await _videoDb.Videos.FindAsync(dto.Id), ip, port, token);
                break;
            case "mv":
                (url, contentType) = await BuildCastMusicVideoUrlAsync(await _mvDb.MusicVideos.FindAsync(dto.Id), ip, port, token);
                break;
            default: // "track"
                url = $"http://{ip}:{port}/api/stream/{dto.Id}?format=mp3&maxBitRate=320&token={token}";
                contentType = "audio/mpeg";
                break;
        }

        // Album art / poster: the client sends a relative path (e.g. /api/cover/track/5 or
        // /videometa/poster.jpg). Authenticated /api/ paths get the same token appended so the
        // Cast device can fetch them; static paths (videometa) are LAN-served without a token.
        string imageUrl = "";
        if (!string.IsNullOrEmpty(dto.Image))
            imageUrl = dto.Image.StartsWith("/api/")
                ? $"http://{ip}:{port}{dto.Image}?token={token}"
                : $"http://{ip}:{port}{dto.Image}";

        // Scope = the paths of the URLs handed to the device (query stripped), nothing else.
        var scopes = new List<string>();
        foreach (var u in new[] { url, imageUrl })
            if (Uri.TryCreate(u, UriKind.Absolute, out var pu) && pu.AbsolutePath.StartsWith("/api/"))
                scopes.Add(Uri.UnescapeDataString(pu.AbsolutePath));
        ApiTokenStore.Register(token, CurrentUsername, role, TimeSpan.FromHours(12), "cast", scopes.ToArray());

        var ok = await _cast.PlayAsync(dto.DeviceId, url, contentType, dto.Title ?? "NexusM", dto.Subtitle ?? "", imageUrl);
        return ok ? Ok(new { success = true }) : StatusCode(502, new { error = "Could not start casting" });
    }

    [HttpPost("cast/control")]
    public async Task<IActionResult> CastControl([FromBody] CastControlDto dto)
    {
        if (dto == null || string.IsNullOrEmpty(dto.DeviceId) || string.IsNullOrEmpty(dto.Action))
            return BadRequest(new { error = "deviceId and action required" });
        var ok = await _cast.ControlAsync(dto.DeviceId, dto.Action);
        return ok ? Ok(new { success = true }) : StatusCode(502, new { error = "Control failed" });
    }

    [HttpPost("cast/stop")]
    public async Task<IActionResult> CastStop([FromBody] CastControlDto dto)
    {
        if (dto == null || string.IsNullOrEmpty(dto.DeviceId))
            return BadRequest(new { error = "deviceId required" });
        await _cast.StopAsync(dto.DeviceId);
        return Ok(new { success = true });
    }

    // Polled by the web UI while audio-casting so it can auto-advance the playlist - a casting
    // device never fires the browser's local 'ended' event. Returns the device's player state.
    [HttpGet("cast/status")]
    public async Task<IActionResult> CastStatus([FromQuery] string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return BadRequest(new { error = "deviceId required" });
        var status = await _cast.GetStatusAsync(deviceId);
        return Ok(status);   // null → 200 with empty body; the client treats that as "no session"
    }

    // Build the media URL for a Cast video. The Cast Default Media Receiver reliably plays only
    // H.264 in MP4 - anything else (HEVC, MKV, VP9, MPEG-2…) gives sound + a black screen - so we
    // transcode/remux to H.264 HLS and point the device at LAN-gated cast/hls/* proxy endpoints
    // (their segment URLs are relative, so a token on the playlist wouldn't reach segments).
    private async Task<(string url, string contentType)> BuildCastVideoUrlAsync(Video? v, string ip, int port, string token)
    {
        if (v == null) return ($"http://{ip}:{port}/api/stream-video/0?token={token}", "video/mp4");
        var direct = $"http://{ip}:{port}/api/stream-video/{v.Id}?token={token}";
        return await BuildCastMediaUrlAsync(v.Id, "", direct, v.Codec, v.Format, v.Mp4Compliant, v.FilePath, v.Duration, ip, port);
    }

    // Music videos use the SAME rule as movies/TV: "MP4 compliant" only means a valid container,
    // not that the codec is H.264 - an HEVC-in-MP4 music video casts as sound with a black screen.
    private async Task<(string url, string contentType)> BuildCastMusicVideoUrlAsync(MusicVideo? mv, string ip, int port, string token)
    {
        if (mv == null) return ($"http://{ip}:{port}/api/stream-musicvideo/0?token={token}", "video/mp4");
        var direct = $"http://{ip}:{port}/api/stream-musicvideo/{mv.Id}?token={token}";
        return await BuildCastMediaUrlAsync(mv.Id, "mv", direct, mv.Codec, mv.Format, mv.Mp4Compliant, mv.FilePath, mv.Duration, ip, port);
    }

    // Shared Cast media-URL logic for movies/TV and music videos: H.264-in-MP4 casts direct
    // (already Cast-friendly); everything else (HEVC, MKV, VP9, MPEG-2, H.264-in-MKV…) is served
    // as MPEG-TS HLS of H.264 + AAC - the only HLS form the Cast Default Media Receiver renders
    // reliably (fMP4 gives sound with a black screen).
    private async Task<(string url, string contentType)> BuildCastMediaUrlAsync(
        int id, string idPrefix, string direct, string? codec, string? format, bool mp4Compliant,
        string filePath, double duration, string ip, int port)
    {
        bool isH264 = codec != null && (
            codec.StartsWith("h264", StringComparison.OrdinalIgnoreCase) ||
            codec.StartsWith("avc",  StringComparison.OrdinalIgnoreCase) ||
            codec.Equals("h.264",    StringComparison.OrdinalIgnoreCase));
        bool isMp4 = mp4Compliant && (format?.Equals("MP4", StringComparison.OrdinalIgnoreCase) == true);

        if (isH264 && isMp4) return (direct, "video/mp4");   // already Cast-friendly

        if (_ffmpeg.IsAvailable && _transcoding != null)
        {
            var transcodeId = await _transcoding.GetCastHlsAsync(id, filePath, codec, duration, idPrefix);
            if (!string.IsNullOrEmpty(transcodeId))
                return ($"http://{ip}:{port}/api/cast/hls/{transcodeId}/playlist.m3u8", "application/x-mpegurl");
        }
        return (direct, "video/mp4");   // last-resort fallback
    }

    // ---- External-player handoff (MPV and any generic HTTP-stream player) --------------------
    // NexusM hands the user's OWN player a playable, token-authed URL - there is no bundled or
    // custom player. Three shapes: a .m3u playlist file, a one-line .strm URL file, and a JSON
    // URL (for "copy" + the mpv:// deep link). Default is DIRECT-PLAY of the original file:
    // MPV/VLC/mpc handle HEVC, MKV, VP9, AV1 and HDR natively, so transcoding would only lose
    // quality and burn CPU. ?transcode=1 opts into the SAME H.264 HLS path Cast uses - for the
    // odd player/build that can't decode the source, or to cap bandwidth (LAN-only, like the
    // Cast HLS proxy). The URL mirrors Cast: plain HTTP on the configured port + LAN IP, which
    // sidesteps the self-signed HTTPS cert MPV would reject on the TLS port.

    private static string BuildVideoDisplayTitle(Video v)
    {
        if (!string.IsNullOrWhiteSpace(v.SeriesName) && v.Season.HasValue && v.Episode.HasValue)
            return $"{v.SeriesName} S{v.Season.Value:D2}E{v.Episode.Value:D2} - {v.Title}";
        return string.IsNullOrWhiteSpace(v.Title) ? $"Video {v.Id}" : v.Title;
    }

    private static string SafeDownloadName(string name)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim();
        return string.IsNullOrEmpty(name) ? "video" : name;
    }

    private async Task<(string url, string title, bool hls, string? dvdDevice)?> BuildExternalPlayerUrlAsync(int id, bool transcode)
    {
        var v = await _videoDb.Videos.FindAsync(id);
        if (v == null) return null;

        // DVD-Video disc: hand the player the real disc source (ISO / VIDEO_TS folder), not an HTTP
        // stream - libdvdnav needs the device to reproduce the interactive menus, and the MPV client
        // must be able to reach that path locally or via a mounted share. `url` carries the device
        // path so the Copy / .strm actions surface it too.
        if (v.VideoKind == "dvd")
        {
            var device = string.IsNullOrWhiteSpace(v.DvdDevicePath) ? v.FilePath : v.DvdDevicePath;
            return (device, BuildVideoDisplayTitle(v), false, device);
        }

        var ip    = DlnaService.GetLocalIpAddress();
        var port  = _config.Config.Server.ServerPort;
        var role  = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "guest";
        // URL-borne token for MPV/VLC: scoped to this one file's download path (the trailing
        // title segment is covered by the segment-aware prefix match).
        var token = ApiTokenStore.NewToken();
        ApiTokenStore.Register(token, CurrentUsername, role, TimeSpan.FromHours(12), "external",
            new[] { $"/api/download/video/{v.Id}" });

        if (transcode && _ffmpeg.IsAvailable && _transcoding != null)
        {
            var transcodeId = await _transcoding.GetCastHlsAsync(v.Id, v.FilePath, v.Codec, v.Duration, "");
            if (!string.IsNullOrEmpty(transcodeId))
                return ($"http://{ip}:{port}/api/cast/hls/{transcodeId}/playlist.m3u8", BuildVideoDisplayTitle(v), true, null);
        }
        // IMPORTANT: use download/video (raw file + HTTP range support), NOT stream-video. The
        // stream-video endpoint is browser-smart and 302-redirects HDR / browser-incompatible
        // content into the web player's HLS transcode - which MPV follows and then crashes on
        // (and the redirect drops the ?token= too). External players decode HDR/HEVC/MKV natively,
        // so we serve them the original bytes directly. The trailing filename segment (ignored by
        // the DownloadVideo handler) lets MPV/VLC read a title from the URL's last path segment;
        // slashes are stripped so it stays one segment.
        var title = BuildVideoDisplayTitle(v);
        var ext = System.IO.Path.GetExtension(v.FilePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".mkv";
        var nameSeg = Uri.EscapeDataString(title.Replace('/', '-').Replace('\\', '-') + ext);
        return ($"http://{ip}:{port}/api/download/video/{v.Id}/{nameSeg}?token={token}", title, false, null);
    }

    [HttpGet("videos/{id}/external.m3u")]
    public async Task<IActionResult> VideoExternalM3u(int id, [FromQuery] bool transcode = false)
    {
        var built = await BuildExternalPlayerUrlAsync(id, transcode);
        if (built == null) return NotFound();
        var (url, title, _, _) = built.Value;
        var m3u = $"#EXTM3U\n#EXTINF:-1,{title}\n{url}\n";
        return File(System.Text.Encoding.UTF8.GetBytes(m3u), "audio/x-mpegurl", SafeDownloadName(title) + ".m3u");
    }

    [HttpGet("videos/{id}/external.strm")]
    public async Task<IActionResult> VideoExternalStrm(int id, [FromQuery] bool transcode = false)
    {
        var built = await BuildExternalPlayerUrlAsync(id, transcode);
        if (built == null) return NotFound();
        var (url, title, _, _) = built.Value;
        return File(System.Text.Encoding.UTF8.GetBytes(url + "\n"), "application/octet-stream", SafeDownloadName(title) + ".strm");
    }

    [HttpGet("videos/{id}/external-url")]
    public async Task<IActionResult> VideoExternalUrl(int id, [FromQuery] bool transcode = false)
    {
        var built = await BuildExternalPlayerUrlAsync(id, transcode);
        if (built == null) return NotFound();
        var (url, title, hls, dvdDevice) = built.Value;
        if (dvdDevice != null)
        {
            // Menu playback needs the --dvd-device argument, which a bare mpv://<url> deep link
            // cannot carry, so we return an explicit command the UI shows for copy/paste alongside
            // the device path. `dvd://menu` starts MPV at the disc's root menu via libdvdnav.
            var command = $"mpv dvd://menu --dvd-device=\"{dvdDevice}\"";
            return Ok(new { kind = "dvd", dvdDevice, title, command, url = dvdDevice });
        }
        return Ok(new { kind = "file", url, mpvUrl = "mpv://" + url, title, transcode = hls });
    }

    // Cast devices fetch HLS over the LAN without a token (segment URLs in the playlist are
    // relative, so a token can't be carried). These mirror the GetHLS* endpoints but are
    // [AllowAnonymous] + LAN-gated, so only devices on the local network can reach them.
    [HttpGet("cast/hls/{transcodeId}/playlist.m3u8")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> CastHlsPlaylist(string transcodeId)
    {
        if (!IsCastLanRequest()) return Forbid();
        const int maxWaitMs = 30_000, poll = 500;
        int waited = 0; string? content = null;
        while (waited < maxWaitMs)
        {
            if (_transcoding.IsTranscodeFailed(transcodeId))
                return StatusCode(500, "Transcode failed");
            content = _transcoding.GetPlaylistContent(transcodeId);
            // MPEG-TS HLS has no init.mp4 (that's an fMP4-only artefact). The playlist listing a
            // segment (#EXTINF:) means at least the first .ts is on disk and ready to serve.
            if (content != null && content.Contains("#EXTINF:")) break;
            await Task.Delay(poll);
            waited += poll;
        }
        if (content == null) return NotFound();
        return Content(content, "application/vnd.apple.mpegurl");
    }

    [HttpGet("cast/hls/{transcodeId}/{segment}")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public IActionResult CastHlsSegment(string transcodeId, string segment)
    {
        if (!IsCastLanRequest()) return Forbid();
        if (segment.Contains("..") || segment.Contains('/') || segment.Contains('\\')) return BadRequest();
        var path = _transcoding.GetSegmentPath(transcodeId, segment);
        if (path == null) return NotFound();
        var mime = segment.EndsWith(".m4s") ? "video/iso.segment"
            : segment.EndsWith(".mp4") ? "video/mp4"
            : segment.EndsWith(".ts") ? "video/mp2t"
            : segment.EndsWith(".m3u8") ? "application/vnd.apple.mpegurl"
            : "application/octet-stream";
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, mime, enableRangeProcessing: true);
    }

    // LAN-only guard for the [AllowAnonymous] cast HLS proxy endpoints. Uses the real origin
    // (Cloudflare / relay aware) so tunnelled internet traffic is not mistaken for loopback.
    private bool IsCastLanRequest() => NetGuard.IsLanRequest(HttpContext);
}

public record CastPlayDto(string DeviceId, string Kind, int Id, string? Title, string? Subtitle, string? Image);
// Action is nullable so the same DTO works for cast/stop (which sends only deviceId) - a
// non-nullable Action would be treated as required and make cast/stop fail model validation (400).
public record CastControlDto(string DeviceId, string? Action);
public record EpgAddSourceDto(string Url, string? Name);
public record TraktScrobbleDto(int VideoId, string Action, double Progress);
public record GateCodeUpdateDto(string? Label, string? Expires);
public record GateAddCodeDto(string? Label, string? Expires);
public record ScrobbleDto(int TrackId, long Timestamp, int Duration);
public record ListenBrainzConnectDto(string Token);
public record ImportUrlRequest(string? Url);

// ─── Go Big OS keypress helper ───────────────────────────────────────
// Injects a real OS-level 'F' keystroke into the NexusM browser window so that
// the browser receives a trusted keydown event → _gbHandleKey → requestFullscreen().
// On Windows we locate the browser window by title and bring it to the foreground
// before sending the key so keybd_event lands in the right process even when the
// browser is not the currently active window.

internal static class GoBigKeypressHelper
{
    // ── Windows P/Invoke ─────────────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern void     keybd_event(byte vk, byte scan, uint flags, uint extra);
    [DllImport("user32.dll")] private static extern bool     EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
                               private static extern int      GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] private static extern bool     IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint     GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern uint     GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool     AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] private static extern bool     SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool     ShowWindow(IntPtr hWnd, int nCmdShow);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const byte VK_F            = 0x46;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const int  SW_RESTORE      = 9;

    internal static void SendFWindows()
    {
        // Find the visible browser window that has "NexusM" in its title bar
        // (e.g. "NexusM – Google Chrome" or "NexusM – Microsoft Edge").
        var browserWnd = FindWindowByTitle("NexusM");
        if (browserWnd != IntPtr.Zero)
        {
            // Bring the window to the foreground so keybd_event targets it.
            // AttachThreadInput lets us call SetForegroundWindow from a
            // background process without triggering the Windows focus-lock.
            var targetTid = GetWindowThreadProcessId(browserWnd, out _);
            var thisTid   = GetCurrentThreadId();
            AttachThreadInput(thisTid, targetTid, true);
            ShowWindow(browserWnd, SW_RESTORE);      // un-minimise if needed
            SetForegroundWindow(browserWnd);
            AttachThreadInput(thisTid, targetTid, false);
            Thread.Sleep(80);                        // let the focus change settle
        }
        keybd_event(VK_F, 0, 0, 0);
        keybd_event(VK_F, 0, KEYEVENTF_KEYUP, 0);
    }

    private static IntPtr FindWindowByTitle(string fragment)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindowsProc proc = (hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, 256);
            if (sb.ToString().Contains(fragment, StringComparison.OrdinalIgnoreCase))
            { result = hWnd; return false; }   // stop enumeration
            return true;
        };
        EnumWindows(proc, IntPtr.Zero);
        return result;
    }

    internal static void SendFLinux()
    {
        // NexusM may run as a systemd service where DISPLAY / WAYLAND_DISPLAY are not
        // inherited. Fall back to socket-file discovery so the keypress works without
        // any extra service-unit configuration.

        // ── Resolve X11 display ───────────────────────────────────────────────────
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        if (string.IsNullOrEmpty(display) && Directory.Exists("/tmp/.X11-unix"))
        {
            var socket = Directory.GetFiles("/tmp/.X11-unix", "X*")
                                  .OrderBy(f => f)
                                  .FirstOrDefault();
            if (socket != null)
                display = ":" + Path.GetFileName(socket).TrimStart('X');
        }

        // ── Resolve Wayland display ───────────────────────────────────────────────
        var wayland    = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        string? xdgRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");

        if (string.IsNullOrEmpty(wayland) && Directory.Exists("/run/user"))
        {
            foreach (var uidDir in Directory.GetDirectories("/run/user").OrderBy(d => d))
            {
                var sock = Directory.GetFiles(uidDir, "wayland-*").OrderBy(f => f).FirstOrDefault();
                if (sock == null) continue;
                wayland    = Path.GetFileName(sock);
                xdgRuntime = uidDir;
                break;
            }
        }

        // ── Build the process ─────────────────────────────────────────────────────
        if (string.IsNullOrEmpty(display) && string.IsNullOrEmpty(wayland))
            throw new InvalidOperationException(
                "No display server found (no X11 socket in /tmp/.X11-unix, no Wayland socket " +
                "in /run/user/*/). Real fullscreen requires NexusM to run on the same machine " +
                "as the Go Big browser.");

        ProcessStartInfo psi;
        if (!string.IsNullOrEmpty(display))
        {
            // X11: use xdotool to search for the NexusM window, focus it, then press F.
            // This works even when the browser is not the currently active window.
            psi = new ProcessStartInfo(
                      "xdotool",
                      "search --name NexusM windowfocus --sync key --clearmodifiers f")
                  { UseShellExecute = false, RedirectStandardError = true };
            psi.Environment["DISPLAY"] = display;
            var xauth = Environment.GetEnvironmentVariable("XAUTHORITY");
            if (!string.IsNullOrEmpty(xauth)) psi.Environment["XAUTHORITY"] = xauth;
        }
        else
        {
            // Wayland: ydotool key - requires ydotoold daemon
            psi = new ProcessStartInfo("ydotool", "key f")
                  { UseShellExecute = false, RedirectStandardError = true };
            psi.Environment["WAYLAND_DISPLAY"] = wayland!;
            if (!string.IsNullOrEmpty(xdgRuntime))
                psi.Environment["XDG_RUNTIME_DIR"] = xdgRuntime;
        }

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start xdotool/ydotool process.");
        p.WaitForExit(2000);
        if (p.ExitCode != 0)
        {
            var stderr = p.StandardError.ReadToEnd().Trim();
            var hint   = !string.IsNullOrEmpty(display)
                ? "Ensure xdotool is installed: sudo apt install xdotool"
                : "Ensure ydotool is installed and ydotoold daemon is running.";
            throw new InvalidOperationException(
                $"xdotool/ydotool exited {p.ExitCode}" +
                (string.IsNullOrEmpty(stderr) ? "" : $": {stderr}") +
                $". {hint}");
        }
    }
}

// ─── DTOs ────────────────────────────────────────────────────────────

public class UpdateEBookRequest
{
    public string? Title       { get; set; }
    public string? Author      { get; set; }
    public string? Genre       { get; set; }
    public string? Description { get; set; }
    public string? Series      { get; set; }
    public double? SeriesIndex { get; set; }
    public string? CoverUrl    { get; set; }   // optional Open Library cover to download
}

public class BatchEBookRequest
{
    public List<int> Ids       { get; set; } = new();
    public string?   Series    { get; set; }   // null = leave, "" = clear, name = set collection
    public bool      AutoNumber { get; set; }  // number SeriesIndex sequentially by title
    public double?   StartIndex { get; set; }  // first number when AutoNumber (default 1)
}

public class UpdateAudioBookRequest
{
    public string? Title       { get; set; }
    public string? Author      { get; set; }
    public string? Narrator    { get; set; }
    public string? Category    { get; set; }
    public string? Series      { get; set; }
    public double? SeriesIndex { get; set; }
    public string? Description { get; set; }
    public int?    Year        { get; set; }
    public string? CoverUrl    { get; set; }   // optional Open Library cover to download
}

public class BatchUpdateDto
{
    public List<int> Ids { get; set; } = new();
    public System.Text.Json.JsonElement Fields { get; set; }
}

public class CreateAlbumDto
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}

public class AlbumItemDto
{
    public int MediaId { get; set; }
    public string MediaType { get; set; } = "picture"; // "picture" or "video"
}

public class AgpConfigDto
{
    public string[] ActiveTypes { get; set; } = Array.Empty<string>();
    public int[] ExcludedDecades { get; set; } = Array.Empty<int>();
    public Dictionary<string, int[]> ExcludedTracks { get; set; } = new();
    public Dictionary<string, string> Covers { get; set; } = new();
}

public class MusicCategorySettings
{
    /// <summary>Folders excluded from All Tracks / shuffle / AGP, but still directly browsable.</summary>
    public List<string> ExcludedFromLibrary { get; set; } = new();
    /// <summary>Folders completely hidden for this user (admin-managed).</summary>
    public List<string> Hidden { get; set; } = new();
}

public class VideoCategorySettings
{
    /// <summary>Folders completely hidden for this user (admin-managed).</summary>
    public List<string> Hidden { get; set; } = new();
}

public class CategorySettingsDto
{
    public MusicCategorySettings Music { get; set; } = new();
    public VideoCategorySettings Video { get; set; } = new();
    public List<string> HiddenCustomGenres { get; set; } = new();
}
public record PlaylistCreateDto(string Name, string? Description, string? CoverImagePath = null);
public record PlaylistAddTrackDto(int TrackId);
public record PlaylistAddTracksDto(int[] TrackIds);
public record PlaylistImportDto(string Name, PlaylistImportEntry[] Entries);
public record PlaylistImportEntry(string Path, string? Title, string? Artist);
public record SmartPlaylistSaveDto(
    string Name, string? Description,
    string? MatchMode, object[]? Rules,
    string? SortField, string? SortDirection, int Limit);
public record RateDto(int Rating);
public record MoodDef(string Key, string Name, string Icon, string Color, string Description, string Genres, string Runtime);

public class CustomGenreRule
{
    public string Type { get; set; } = "genre"; // "genre" or "folder"
    public string Value { get; set; } = "";
}

public class CreateCustomGenreDto
{
    public string Name { get; set; } = "";
    public string Domain { get; set; } = "music"; // music | movie | tv | anime
    public List<CustomGenreRule> Rules { get; set; } = new();
}

public class SetVideoCustomGenresDto
{
    public List<string> GenreIds { get; set; } = new();
}

public class GoBigSession
{
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public ConcurrentQueue<string> Commands { get; } = new();
}

