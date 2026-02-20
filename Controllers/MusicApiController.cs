using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusM.Data;
using NexusM.Models;
using NexusM.Services;

namespace NexusM.Controllers;

/// <summary>
/// REST API for music library operations.
/// All endpoints prefixed with /api/
/// Requires authentication when SecurityByPin is enabled.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public class MusicApiController : ControllerBase
{
    private readonly MusicDbContext _db;
    private readonly PicturesDbContext _picDb;
    private readonly EBooksDbContext _ebookDb;
    private readonly MusicVideosDbContext _mvDb;
    private readonly VideosDbContext _videoDb;
    private readonly ActorsDbContext _actorsDb;
    private readonly LibraryScannerService _scanner;
    private readonly PictureScannerService _picScanner;
    private readonly EBookScannerService _ebookScanner;
    private readonly MusicVideoScannerService _mvScanner;
    private readonly VideoScannerService _videoScanner;
    private readonly FFmpegService _ffmpeg;
    private readonly TranscodingService _transcoding;
    private readonly GpuDetectionService _gpuDetection;
    private readonly RadioService _radio;
    private readonly TvChannelService _tvService;
    private readonly TvChannelsDbContext _tvDb;
    private readonly UserFavouritesService _userFavs;
    private readonly MetadataService _metadata;
    private readonly ShareCredentialService _shareService;
    private readonly ConfigService _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MusicApiController> _logger;

    public MusicApiController(
        MusicDbContext db,
        PicturesDbContext picDb,
        EBooksDbContext ebookDb,
        MusicVideosDbContext mvDb,
        VideosDbContext videoDb,
        ActorsDbContext actorsDb,
        TvChannelsDbContext tvDb,
        LibraryScannerService scanner,
        PictureScannerService picScanner,
        EBookScannerService ebookScanner,
        MusicVideoScannerService mvScanner,
        VideoScannerService videoScanner,
        FFmpegService ffmpeg,
        TranscodingService transcoding,
        GpuDetectionService gpuDetection,
        RadioService radio,
        TvChannelService tvService,
        UserFavouritesService userFavs,
        MetadataService metadata,
        ShareCredentialService shareService,
        ConfigService config,
        IServiceScopeFactory scopeFactory,
        ILogger<MusicApiController> logger)
    {
        _db = db;
        _picDb = picDb;
        _ebookDb = ebookDb;
        _mvDb = mvDb;
        _videoDb = videoDb;
        _actorsDb = actorsDb;
        _tvDb = tvDb;
        _scanner = scanner;
        _picScanner = picScanner;
        _ebookScanner = ebookScanner;
        _mvScanner = mvScanner;
        _videoScanner = videoScanner;
        _ffmpeg = ffmpeg;
        _transcoding = transcoding;
        _gpuDetection = gpuDetection;
        _radio = radio;
        _tvService = tvService;
        _userFavs = userFavs;
        _metadata = metadata;
        _shareService = shareService;
        _config = config;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    private string CurrentUsername => User.Identity?.Name ?? "unknown";

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
            genres = await _db.Tracks.Where(t => t.Genre != "")
                .Select(t => t.Genre).Distinct().CountAsync()
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

        var musicGenres = await _db.Tracks
            .Where(t => t.Genre != "")
            .GroupBy(t => t.Genre)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count).Take(10).ToListAsync();

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
                hdRatioMv = totalMv > 0 ? Math.Round(totalHdMv * 100.0 / totalMv) : 0
            },
            music = new { formats = musicFormats, genres = musicGenres, topArtists, bitrates = musicBitrates, sampleRates = musicSampleRates },
            videos = new { resolutions = videoResolutions, formats = videoFormats, codecs = videoCodecs, genres = videoGenres },
            musicVideos = new { resolutions = mvResolutions, formats = mvFormats, topArtists = mvTopArtists },
            pictures = new { formats = picFormats, categories = picCategories, resolutions = picResolutions },
            ebooks = new { formats = ebookFormats, topAuthors = ebookTopAuthors, categories = ebookCategories }
        });
    }

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
            var s = search.ToLower();
            query = query.Where(a => a.Name.ToLower().Contains(s) || a.Artist.ToLower().Contains(s));
        }

        query = sort switch
        {
            "name" => query.OrderBy(a => a.Name),
            "artist" => query.OrderBy(a => a.Artist).ThenBy(a => a.Name),
            "year" => query.OrderByDescending(a => a.Year).ThenBy(a => a.Name),
            _ => query.OrderByDescending(a => a.DateAdded)
        };

        var total = await query.CountAsync();
        var albums = await query.Skip((page - 1) * limit).Take(limit)
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.Artist,
                a.Year,
                a.Genre,
                a.CoverArtPath,
                a.TrackCount,
                a.TotalDuration,
                a.IsFavourite,
                a.Rating,
                a.DateAdded
            }).ToListAsync();

        return Ok(new { total, page, limit, albums });
    }

    [HttpGet("albums/{id}")]
    public async Task<IActionResult> GetAlbum(int id)
    {
        var album = await _db.Albums.FirstOrDefaultAsync(a => a.Id == id);
        if (album == null) return NotFound();

        var trackFavIds = _userFavs.GetFavouriteIds(CurrentUsername, "track");
        var tracks = await _db.Tracks.Where(t => t.AlbumId == id)
            .OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber)
            .Select(t => new
            {
                t.Id, t.Title, t.Artist, t.Album, t.Genre, t.Year,
                t.TrackNumber, t.DiscNumber, t.Duration, t.Bitrate,
                t.Codec, t.FileSize, t.HasAlbumArt, t.AlbumArtCached,
                IsFavourite = trackFavIds.Contains(t.Id), t.PlayCount, t.Rating
            })
            .ToListAsync();

        return Ok(new
        {
            album.Id, album.Name, album.Artist, album.Year, album.Genre,
            album.TrackCount, totalDuration = album.TotalDuration,
            tracks
        });
    }

    // ─── Artists ────────────────────────────────────────────────────

    [HttpGet("artists")]
    public async Task<IActionResult> GetArtists(
        [FromQuery] string? search = null,
        [FromQuery] string? sort = "name",
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50)
    {
        // Compute counts dynamically from tracks
        var artistStats = await _db.Tracks
            .GroupBy(t => t.Artist)
            .Select(g => new {
                Name = g.Key,
                TrackCount = g.Count(),
                AlbumCount = g.Select(t => t.Album).Distinct().Count()
            })
            .ToListAsync();

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
        var artists = statsQuery.Skip((page - 1) * limit).Take(limit)
            .Select(a => new { name = a.Name, albumCount = a.AlbumCount, trackCount = a.TrackCount })
            .ToList();
        return Ok(new { total, page, limit, artists });
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

    [HttpGet("tracks")]
    public async Task<IActionResult> GetTracks(
        [FromQuery] string? search = null,
        [FromQuery] string? genre = null,
        [FromQuery] string? artist = null,
        [FromQuery] string? sort = "title",
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50)
    {
        var query = _db.Tracks.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(t => t.Title.ToLower().Contains(s)
                || t.Artist.ToLower().Contains(s)
                || t.Album.ToLower().Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(genre))
            query = query.Where(t => t.Genre.ToLower() == genre.ToLower());
        if (!string.IsNullOrWhiteSpace(artist))
            query = query.Where(t => t.Artist.ToLower() == artist.ToLower());

        query = sort switch
        {
            "artist" => query.OrderBy(t => t.Artist).ThenBy(t => t.Title),
            "album" => query.OrderBy(t => t.Album).ThenBy(t => t.TrackNumber),
            "recent" => query.OrderByDescending(t => t.DateAdded),
            "mostplayed" => query.OrderByDescending(t => t.PlayCount),
            "duration" => query.OrderByDescending(t => t.Duration),
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
        var count = await _db.Tracks.CountAsync();
        if (count == 0) return NotFound();
        var skip = new Random().Next(count);
        var trackFavIds = _userFavs.GetFavouriteIds(CurrentUsername, "track");
        var track = await _db.Tracks.Skip(skip)
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

    // ─── Genres ─────────────────────────────────────────────────────

    [HttpGet("genres")]
    public async Task<IActionResult> GetGenres()
    {
        var genres = await _db.Tracks
            .Where(t => t.Genre != "")
            .GroupBy(t => t.Genre)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderBy(g => g.name)
            .ToListAsync();
        return Ok(genres);
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
        var result = _userFavs.UpdatePlaylist(CurrentUsername, id, dto.Name, dto.Description);
        if (result == null) return NotFound();
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

    // ─── Playback / Streaming ──────────────────────────────────────

    [HttpGet("stream/{id}")]
    public async Task<IActionResult> StreamTrack(int id)
    {
        var track = await _db.Tracks.FindAsync(id);
        if (track == null) return NotFound();

        if (!System.IO.File.Exists(track.FilePath))
            return NotFound("File not found on disk");

        // Update global last played + per-user play count
        track.LastPlayed = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _userFavs.IncrementPlayCount(CurrentUsername, "track", id);

        var stream = new FileStream(track.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, track.MimeType, enableRangeProcessing: true);
    }

    [HttpGet("cover/{albumId}")]
    public async Task<IActionResult> GetAlbumCover(int albumId)
    {
        var album = await _db.Albums.FindAsync(albumId);
        if (album == null) return NotFound();

        // Try to get cover from first track with cached album art
        var track = await _db.Tracks.FirstOrDefaultAsync(t => t.AlbumId == albumId && t.HasAlbumArt);
        if (track == null) return NotFound();

        // Serve from cache if available
        if (!string.IsNullOrEmpty(track.AlbumArtCached))
        {
            var cachedPath = Path.Combine(AppContext.BaseDirectory, "assets", "albumart", track.AlbumArtCached);
            if (System.IO.File.Exists(cachedPath))
                return PhysicalFile(cachedPath, "image/jpeg");
        }

        // Fallback: read from audio file
        if (!System.IO.File.Exists(track.FilePath)) return NotFound();
        try
        {
            using var tagFile = TagLib.File.Create(track.FilePath);
            var picture = tagFile.Tag.Pictures?.FirstOrDefault();
            if (picture == null) return NotFound();
            return File(picture.Data.Data, picture.MimeType ?? "image/jpeg");
        }
        catch { return NotFound(); }
    }

    [HttpGet("cover/track/{trackId}")]
    public async Task<IActionResult> GetTrackCover(int trackId)
    {
        var track = await _db.Tracks.FindAsync(trackId);
        if (track == null || !track.HasAlbumArt) return NotFound();

        // Serve from cache if available
        if (!string.IsNullOrEmpty(track.AlbumArtCached))
        {
            var cachedPath = Path.Combine(AppContext.BaseDirectory, "assets", "albumart", track.AlbumArtCached);
            if (System.IO.File.Exists(cachedPath))
                return PhysicalFile(cachedPath, "image/jpeg");
        }

        // Fallback: read from audio file
        if (!System.IO.File.Exists(track.FilePath)) return NotFound();
        try
        {
            using var tagFile = TagLib.File.Create(track.FilePath);
            var picture = tagFile.Tag.Pictures?.FirstOrDefault();
            if (picture == null) return NotFound();
            return File(picture.Data.Data, picture.MimeType ?? "image/jpeg");
        }
        catch { return NotFound(); }
    }

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
                v.Resolution, v.Width, v.Height, v.MediaType, v.Rating,
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

        // ─── Music ─────────────────────────────────────────
        var tracks = await _db.Tracks
            .Where(t => t.Title.ToLower().Contains(s) || t.Artist.ToLower().Contains(s)
                || t.Album.ToLower().Contains(s))
            .Take(20)
            .Select(t => new { t.Id, t.Title, t.Artist, t.Album, t.Duration, type = "track" })
            .ToListAsync();

        var albums = await _db.Albums
            .Where(a => a.Name.ToLower().Contains(s) || a.Artist.ToLower().Contains(s))
            .Take(10)
            .Select(a => new { a.Id, title = a.Name, artist = a.Artist, a.Year, type = "album" })
            .ToListAsync();

        var artists = await _db.Artists
            .Where(a => a.Name.ToLower().Contains(s))
            .Take(10)
            .Select(a => new { a.Id, a.Name, a.AlbumCount, a.TrackCount, type = "artist" })
            .ToListAsync();

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
        var actors = await _actorsDb.Actors
            .Where(a => a.Name.ToLower().Contains(s))
            .OrderByDescending(a => a.Popularity ?? 0)
            .Take(20)
            .Select(a => new { a.Id, a.Name, a.TmdbId, a.ImageCached, a.KnownForDepartment,
                movieCount = _actorsDb.MovieActors.Count(ma => ma.ActorId == a.Id) })
            .ToListAsync();

        return Ok(new { tracks, albums, artists, pictures, ebooks, musicVideos, videos, actors });
    }

    // ─── Status / Shares ────────────────────────────────────────────

    [HttpGet("status/shares")]
    public async Task<IActionResult> GetSharesStatus()
    {
        var cfg = _config.Config;

        var shares = new List<object>();

        // Music
        var musicFolders = cfg.Library.GetMusicFolderList();
        var musicActive = musicFolders.Count > 0 && musicFolders.Any(f => System.IO.Directory.Exists(f));
        long musicSize = musicActive ? await _db.Tracks.SumAsync(t => t.FileSize) : 0;
        shares.Add(new { type = "Music", active = musicActive, configured = musicFolders.Count > 0, size = musicSize });

        // Movies/TV Shows
        var moviesFolders = cfg.Library.GetMoviesTVFolderList();
        var moviesActive = moviesFolders.Count > 0 && moviesFolders.Any(f => System.IO.Directory.Exists(f));
        long videoSize = moviesActive ? await _videoDb.Videos.SumAsync(v => v.SizeBytes) : 0;
        shares.Add(new { type = "Videos", active = moviesActive, configured = moviesFolders.Count > 0, size = videoSize });

        // Pictures
        var picFolders = cfg.Library.GetPicturesFolderList();
        var picActive = picFolders.Count > 0 && picFolders.Any(f => System.IO.Directory.Exists(f));
        long picSize = picActive ? await _picDb.Pictures.SumAsync(p => p.SizeBytes) : 0;
        shares.Add(new { type = "Pictures", active = picActive, configured = picFolders.Count > 0, size = picSize });

        // eBooks
        var ebookFolders = cfg.Library.GetEBooksFolderList();
        var ebookActive = ebookFolders.Count > 0 && ebookFolders.Any(f => System.IO.Directory.Exists(f));
        long ebookSize = ebookActive ? await _ebookDb.EBooks.SumAsync(e => e.FileSize) : 0;
        shares.Add(new { type = "eBooks", active = ebookActive, configured = ebookFolders.Count > 0, size = ebookSize });

        // Music Videos
        var mvFolders = cfg.Library.GetMusicVideosFolderList();
        var mvActive = mvFolders.Count > 0 && mvFolders.Any(f => System.IO.Directory.Exists(f));
        shares.Add(new { type = "Music Videos", active = mvActive, configured = mvFolders.Count > 0, size = 0L });

        long mvSize = mvActive ? await _mvDb.MusicVideos.SumAsync(v => v.SizeBytes) : 0;
        long totalSize = musicSize + videoSize + picSize + ebookSize + mvSize;

        return Ok(new { shares, totalSize });
    }

    // ─── Config Info ───────────────────────────────────────────────

    [HttpGet("config/info")]
    public IActionResult GetConfigInfo()
    {
        var cfg = _config.Config;
        return Ok(new
        {
            // Server
            version = "BETA 0.98 (New World Order Edition)",
            platform = Environment.OSVersion.ToString(),
            isLinux = OperatingSystem.IsLinux(),
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
            // Security
            securityByPin = cfg.Security.SecurityByPin,
            defaultAdminUser = cfg.Security.DefaultAdminUser,
            ipWhitelist = cfg.Security.IPWhitelist,
            // Library
            musicFolders = cfg.Library.MusicFolders,
            moviesTVFolders = cfg.Library.MoviesTVFolders,
            picturesFolders = cfg.Library.PicturesFolders,
            musicVideosFolders = cfg.Library.MusicVideosFolders,
            ebooksFolders = cfg.Library.EBooksFolders,
            audioExtensions = cfg.Library.AudioExtensions,
            imageExtensions = cfg.Library.ImageExtensions,
            ebookExtensions = cfg.Library.EBookExtensions,
            musicVideoExtensions = cfg.Library.MusicVideoExtensions,
            videoExtensions = cfg.Library.VideoExtensions,
            autoScanOnStartup = cfg.Library.AutoScanOnStartup,
            autoScanInterval = cfg.Library.AutoScanInterval,
            scanThreads = cfg.Library.ScanThreads,
            // Playback
            transcodingEnabled = cfg.Playback.TranscodingEnabled,
            transcodeFormat = cfg.Playback.TranscodeFormat,
            transcodeBitrate = cfg.Playback.TranscodeBitrate,
            ffmpegAvailable = _ffmpeg.IsAvailable,
            ffmpegPath = _ffmpeg.IsAvailable ? _ffmpeg.FfmpegPath : "",
            // Transcoding (video)
            preferredEncoder = cfg.Transcoding.PreferredEncoder,
            activeEncoder = _gpuDetection.Capabilities?.ActiveEncoder ?? "software",
            detectedGpus = _gpuDetection.GpuInfo?.DetectedGPUs.Select(g => new { g.Name, vendor = g.Vendor.ToString(), g.VramGB, g.EncoderType }) ?? [],
            videoPreset = cfg.Transcoding.VideoPreset,
            videoCRF = cfg.Transcoding.VideoCRF,
            videoMaxrate = cfg.Transcoding.VideoMaxrate,
            videoBufsize = cfg.Transcoding.VideoBufsize,
            transcodingAudioCodec = cfg.Transcoding.AudioCodec,
            transcodingAudioBitrate = cfg.Transcoding.AudioBitrate,
            transcodingAudioChannels = cfg.Transcoding.AudioChannels,
            maxConcurrentTranscodes = cfg.Transcoding.MaxConcurrentTranscodes,
            ffmpegCPULimit = cfg.Transcoding.FFmpegCPULimit,
            remuxPriority = cfg.Transcoding.RemuxPriority,
            remuxThreads = cfg.Transcoding.RemuxThreads,
            transcodeMaxHeight = cfg.Transcoding.TranscodeMaxHeight,
            hlsSegmentDuration = cfg.Transcoding.HLSSegmentDuration,
            hlsCacheEnabled = cfg.Transcoding.HLSCacheEnabled,
            hlsCacheMaxSizeGB = cfg.Transcoding.HLSCacheMaxSizeGB,
            hlsCacheRetentionDays = cfg.Transcoding.HLSCacheRetentionDays,
            transcodeFormats = cfg.Transcoding.TranscodeFormats,
            // Logging
            logLevel = cfg.Logging.LogLevel,
            logFile = cfg.Logging.LogFile,
            maxLogSizeMB = cfg.Logging.MaxLogSizeMB,
            // UI
            theme = cfg.UI.Theme,
            defaultView = cfg.UI.DefaultView,
            language = cfg.UI.Language,
            showMoviesTV = cfg.UI.ShowMoviesTV,
            showMusicVideos = cfg.UI.ShowMusicVideos,
            showRadio = cfg.UI.ShowRadio,
            showInternetTV = cfg.UI.ShowInternetTV,
            showEBooks = cfg.UI.ShowEBooks,
            showActors = cfg.UI.ShowActors,
            // Metadata
            metadataProvider = cfg.Metadata.Provider,
            tmdbApiKey = cfg.Metadata.TmdbApiKey,
            fetchMetadataOnScan = cfg.Metadata.FetchOnScan,
            fetchCastPhotos = cfg.Metadata.FetchCastPhotos,
            // Databases
            databases = new[] {
                new { name = "Music", path = cfg.Database.DatabasePath, exists = System.IO.File.Exists(cfg.Database.DatabasePath) },
                new { name = "Pictures", path = cfg.Database.PicturesDatabasePath, exists = System.IO.File.Exists(cfg.Database.PicturesDatabasePath) },
                new { name = "eBooks", path = cfg.Database.EBooksDatabasePath, exists = System.IO.File.Exists(cfg.Database.EBooksDatabasePath) },
                new { name = "Music Videos", path = cfg.Database.MusicVideosDatabasePath, exists = System.IO.File.Exists(cfg.Database.MusicVideosDatabasePath) },
                new { name = "Movies & TV", path = cfg.Database.VideosDatabasePath, exists = System.IO.File.Exists(cfg.Database.VideosDatabasePath) },
                new { name = "Shares", path = cfg.Database.SharesDatabasePath, exists = System.IO.File.Exists(cfg.Database.SharesDatabasePath) }
            }
        });
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

            // Security
            if (body.TryGetProperty("securityByPin", out var sbp)) cfg.Security.SecurityByPin = sbp.GetBoolean();
            if (body.TryGetProperty("defaultAdminUser", out var dau)) cfg.Security.DefaultAdminUser = dau.GetString() ?? "admin";
            if (body.TryGetProperty("ipWhitelist", out var ipw)) cfg.Security.IPWhitelist = ipw.GetString() ?? "";

            // Library - folders
            if (body.TryGetProperty("musicFolders", out var mf)) cfg.Library.MusicFolders = mf.GetString() ?? "";
            if (body.TryGetProperty("moviesTVFolders", out var mtf)) cfg.Library.MoviesTVFolders = mtf.GetString() ?? "";
            if (body.TryGetProperty("picturesFolders", out var pf)) cfg.Library.PicturesFolders = pf.GetString() ?? "";
            if (body.TryGetProperty("musicVideosFolders", out var mvf)) cfg.Library.MusicVideosFolders = mvf.GetString() ?? "";
            if (body.TryGetProperty("ebooksFolders", out var ef)) cfg.Library.EBooksFolders = ef.GetString() ?? "";
            // Library - extensions
            if (body.TryGetProperty("audioExtensions", out var ae)) cfg.Library.AudioExtensions = ae.GetString() ?? "";
            if (body.TryGetProperty("imageExtensions", out var ie)) cfg.Library.ImageExtensions = ie.GetString() ?? "";
            if (body.TryGetProperty("ebookExtensions", out var ee)) cfg.Library.EBookExtensions = ee.GetString() ?? "";
            if (body.TryGetProperty("musicVideoExtensions", out var mve)) cfg.Library.MusicVideoExtensions = mve.GetString() ?? "";
            if (body.TryGetProperty("videoExtensions", out var ve)) cfg.Library.VideoExtensions = ve.GetString() ?? "";
            // Library - scan
            if (body.TryGetProperty("autoScanOnStartup", out var aso)) cfg.Library.AutoScanOnStartup = aso.GetBoolean();
            if (body.TryGetProperty("autoScanInterval", out var asi)) cfg.Library.AutoScanInterval = asi.GetInt32();
            if (body.TryGetProperty("scanThreads", out var sth)) cfg.Library.ScanThreads = sth.GetInt32();

            // Playback
            if (body.TryGetProperty("transcodingEnabled", out var te)) cfg.Playback.TranscodingEnabled = te.GetBoolean();
            if (body.TryGetProperty("transcodeFormat", out var tf)) cfg.Playback.TranscodeFormat = tf.GetString() ?? "mp3";
            if (body.TryGetProperty("transcodeBitrate", out var tb)) cfg.Playback.TranscodeBitrate = tb.GetString() ?? "192k";
            if (body.TryGetProperty("ffmpegPath", out var fp)) cfg.Playback.FFmpegPath = fp.GetString() ?? "";

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
            if (body.TryGetProperty("ffmpegCPULimit", out var fcl)) cfg.Transcoding.FFmpegCPULimit = fcl.GetInt32();
            if (body.TryGetProperty("remuxPriority", out var rp)) cfg.Transcoding.RemuxPriority = rp.GetString() ?? "abovenormal";
            if (body.TryGetProperty("remuxThreads", out var rth)) cfg.Transcoding.RemuxThreads = rth.GetInt32();
            if (body.TryGetProperty("transcodeMaxHeight", out var tmh)) cfg.Transcoding.TranscodeMaxHeight = tmh.GetInt32();
            if (body.TryGetProperty("hlsSegmentDuration", out var hsd)) cfg.Transcoding.HLSSegmentDuration = hsd.GetInt32();
            if (body.TryGetProperty("hlsCacheEnabled", out var hce)) cfg.Transcoding.HLSCacheEnabled = hce.GetBoolean();
            if (body.TryGetProperty("hlsCacheMaxSizeGB", out var hcm)) cfg.Transcoding.HLSCacheMaxSizeGB = hcm.GetInt32();
            if (body.TryGetProperty("hlsCacheRetentionDays", out var hcr)) cfg.Transcoding.HLSCacheRetentionDays = hcr.GetInt32();
            if (body.TryGetProperty("transcodeFormats", out var tfs)) cfg.Transcoding.TranscodeFormats = tfs.GetString() ?? "";

            // Logging
            if (body.TryGetProperty("logLevel", out var ll)) cfg.Logging.LogLevel = ll.GetString() ?? "Information";
            if (body.TryGetProperty("maxLogSizeMB", out var mls)) cfg.Logging.MaxLogSizeMB = mls.GetInt32();

            // UI
            if (body.TryGetProperty("theme", out var th)) cfg.UI.Theme = th.GetString() ?? "dark";
            if (body.TryGetProperty("defaultView", out var dv)) cfg.UI.DefaultView = dv.GetString() ?? "grid";
            if (body.TryGetProperty("language", out var lang)) cfg.UI.Language = lang.GetString() ?? "en";
            if (body.TryGetProperty("showMoviesTV", out var smt)) cfg.UI.ShowMoviesTV = smt.GetBoolean();
            if (body.TryGetProperty("showMusicVideos", out var smv)) cfg.UI.ShowMusicVideos = smv.GetBoolean();
            if (body.TryGetProperty("showRadio", out var sr)) cfg.UI.ShowRadio = sr.GetBoolean();
            if (body.TryGetProperty("showInternetTV", out var sit)) cfg.UI.ShowInternetTV = sit.GetBoolean();
            if (body.TryGetProperty("showEBooks", out var seb)) cfg.UI.ShowEBooks = seb.GetBoolean();
            if (body.TryGetProperty("showActors", out var sac)) cfg.UI.ShowActors = sac.GetBoolean();

            // Metadata
            if (body.TryGetProperty("metadataProvider", out var mp)) cfg.Metadata.Provider = mp.GetString() ?? "tvmaze";
            if (body.TryGetProperty("tmdbApiKey", out var tmk)) cfg.Metadata.TmdbApiKey = tmk.GetString() ?? "";
            if (body.TryGetProperty("fetchMetadataOnScan", out var fms)) cfg.Metadata.FetchOnScan = fms.GetBoolean();
            if (body.TryGetProperty("fetchCastPhotos", out var fcp)) cfg.Metadata.FetchCastPhotos = fcp.GetBoolean();

            _config.SaveConfig();

            return Ok(new { success = true, message = "Settings saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration");
            return StatusCode(500, new { success = false, message = "Failed to save settings: " + ex.Message });
        }
    }

    [HttpPost("videos/fetch-metadata")]
    public async Task<IActionResult> FetchVideoMetadata([FromQuery] bool refetchAll = false)
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
            _logger.LogInformation("Reset MetadataFetched flag for {Count} videos", allVideos.Count);
        }

        _ = Task.Run(() => _metadata.EnrichLibraryAsync(null));
        return Ok(new { message = refetchAll ? "Re-fetching all metadata in background" : "Fetching missing metadata in background" });
    }

    [HttpPost("videos/{id}/refresh-metadata")]
    public async Task<IActionResult> RefreshVideoMetadata(int id)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();

        await _metadata.FetchForVideoAsync(video, forceRefresh: true);
        return Ok(new { success = true });
    }

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

    [HttpPost("config/validate-tmdb")]
    public async Task<IActionResult> ValidateTmdbKey([FromBody] System.Text.Json.JsonElement body)
    {
        var key = body.TryGetProperty("apiKey", out var k) ? k.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(key))
            return Ok(new { valid = false, message = "No API key provided" });

        var valid = await _metadata.ValidateTmdbKeyAsync(key);
        return Ok(new { valid, message = valid ? "API key is valid" : "Invalid API key" });
    }

    [HttpPost("config/download-ffmpeg")]
    public async Task<IActionResult> DownloadFfmpeg()
    {
        if (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "admin")
            return Forbid();

        if (_ffmpeg.IsAvailable)
            return Ok(new { success = true, message = "FFmpeg is already installed", alreadyInstalled = true });

        try
        {
            await _ffmpeg.DownloadIfMissingAsync();

            if (_ffmpeg.IsAvailable)
                return Ok(new { success = true, message = "FFmpeg downloaded and installed successfully" });
            else
                return StatusCode(500, new { success = false, message = "Download completed but FFmpeg binary not found. Check server logs." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download FFmpeg");
            return StatusCode(500, new { success = false, message = "Failed to download FFmpeg: " + ex.Message });
        }
    }

    // ─── Pictures ─────────────────────────────────────────────────────

    [HttpGet("pictures")]
    public async Task<IActionResult> GetPictures(
        [FromQuery] string? category = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = "recent",
        [FromQuery] int page = 1,
        [FromQuery] int limit = 100)
    {
        var query = _picDb.Pictures.AsQueryable();

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category.ToLower() == category.ToLower());

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
                p.CameraMake, p.CameraModel, p.DateAdded
            }).ToListAsync();

        return Ok(new { total, page, limit, pictures });
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
            pic.LensModel, pic.Software, pic.DateAdded, pic.LastModified
        });
    }

    [HttpGet("image/{id}")]
    public async Task<IActionResult> GetFullImage(int id)
    {
        var pic = await _picDb.Pictures.FindAsync(id);
        if (pic == null) return NotFound();

        if (!System.IO.File.Exists(pic.FilePath))
            return NotFound("Image file not found on disk");

        var mimeType = Path.GetExtension(pic.FilePath).ToLowerInvariant() switch
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
    public IActionResult StartPictureScan()
    {
        if (_picScanner.IsScanning)
            return Conflict(new { message = "Pictures scan already in progress" });

        _ = _picScanner.StartScanAsync();
        return Ok(new { message = "Pictures scan started" });
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
            p.ErrorCount,
            p.PercentComplete,
            p.StartTime,
            isScanning = _picScanner.IsScanning
        });
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
            query = query.Where(e => e.Format.ToLower() == format.ToLower());

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(e => e.Title.ToLower().Contains(s)
                || e.Author.ToLower().Contains(s)
                || e.FileName.ToLower().Contains(s));
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
        var ebooks = await query.Skip((page - 1) * limit).Take(limit)
            .Select(e => new
            {
                e.Id, e.FileName, e.Title, e.Author, e.Format,
                e.FileSize, e.PageCount, e.Category, e.DateAdded,
                e.Publisher, e.Language, e.Subject, e.CoverImage
            }).ToListAsync();

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
            ebook.Subject, ebook.DateAdded, ebook.LastModified, ebook.CoverImage
        });
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
            "PDF" => "application/pdf",
            "EPUB" => "application/epub+zip",
            _ => "application/octet-stream"
        };

        var stream = new FileStream(ebook.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Response.Headers["Content-Disposition"] = "inline";
        return File(stream, mimeType, enableRangeProcessing: true);
    }

    // ─── eBooks Scanning ─────────────────────────────────────────────

    [HttpPost("scan/ebooks")]
    public IActionResult StartEBookScan()
    {
        if (_ebookScanner.IsScanning)
            return Conflict(new { message = "eBooks scan already in progress" });

        _ = _ebookScanner.StartScanAsync();
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

    // ─── Music Videos ─────────────────────────────────────────────────

    [HttpGet("musicvideos")]
    public async Task<IActionResult> GetMusicVideos(
        [FromQuery] string? artist = null,
        [FromQuery] string? search = null,
        [FromQuery] int? year = null,
        [FromQuery] string? sort = "recent",
        [FromQuery] int page = 1,
        [FromQuery] int limit = 100)
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

        return Ok(new { total, page, limit, videos });
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
        return Ok(artists);
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
        return Ok(new
        {
            video.Id, video.FileName, video.FilePath, video.Title, video.Artist,
            video.Album, video.Year, video.Duration, video.SizeBytes, video.Format,
            video.Resolution, video.Width, video.Height, video.Codec, video.Bitrate,
            video.Genre, video.ThumbnailPath, video.MoovPosition, video.NeedsOptimization,
            video.Mp4Compliant, video.AudioChannels, video.DateAdded, video.LastModified,
            video.LastPlayed
        });
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
        var cacheDir = Path.Combine(AppContext.BaseDirectory, "cache", "remux");
        if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
        var cacheKey = $"mv_{video.Id}_{video.LastModified.Ticks}";
        var cachePath = Path.Combine(cacheDir, $"{cacheKey}.mp4");

        if (System.IO.File.Exists(cachePath))
        {
            _logger.LogDebug("Serving cached remux for music video {Id}: {Path}", id, cachePath);
            var stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, "video/mp4", enableRangeProcessing: true);
        }

        // If FFmpeg available, use TranscodingService for enhanced remux
        if (_ffmpeg.IsAvailable && _transcoding != null)
        {
            _logger.LogInformation("Remuxing music video {Id}: {File}", id, video.FilePath);
            bool success;
            if (video.AudioChannels > 2)
                success = await _transcoding.RemuxStereoDownmixAsync(video.FilePath, cachePath);
            else
                success = await _transcoding.RemuxFaststartAsync(video.FilePath, cachePath);

            if (success && System.IO.File.Exists(cachePath))
            {
                _logger.LogInformation("Remux successful for music video {Id}, cached at {Path}", id, cachePath);
                var stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return File(stream, "video/mp4", enableRangeProcessing: true);
            }
            _logger.LogWarning("Remux failed for music video {Id}", id);
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
        var cacheDir = Path.Combine(AppContext.BaseDirectory, "cache", "remux");
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
        var cacheDir = Path.Combine(AppContext.BaseDirectory, "cache", "remux");
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
        [FromQuery] int limit = 100)
    {
        var query = _videoDb.Videos.AsQueryable();

        if (!string.IsNullOrWhiteSpace(mediaType))
            query = query.Where(v => v.MediaType == mediaType.ToLower());
        if (!string.IsNullOrWhiteSpace(genre))
            query = query.Where(v => v.Genre.ToLower().Contains(genre.ToLower()));
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

        // Grouped mode: return movies as-is + one entry per TV series
        if (grouped && string.IsNullOrWhiteSpace(series))
        {
            // Get movies
            var movieQuery = query.Where(v => v.MediaType != "tv");
            var tvQuery = query.Where(v => v.MediaType == "tv" && v.SeriesName != "");

            var vidFavIds = _userFavs.GetFavouriteIds(CurrentUsername, "video");
            var vidWatchedIds = _userFavs.GetWatchedIds(CurrentUsername);

            // Build TV series summaries
            var seriesGroups = await tvQuery
                .GroupBy(v => v.SeriesName)
                .Select(g => new
                {
                    SeriesName = g.Key,
                    EpisodeCount = g.Count(),
                    SeasonCount = g.Select(v => v.Season).Distinct().Count(),
                    TotalDuration = g.Sum(v => v.Duration),
                    TotalSize = g.Sum(v => v.SizeBytes),
                    LatestAdded = g.Max(v => v.DateAdded),
                    LatestYear = g.Max(v => v.Year),
                    // Get thumbnail from the first episode that has one
                    ThumbnailPath = g.Where(v => v.ThumbnailPath != null && v.ThumbnailPath != "")
                        .OrderBy(v => v.Season).ThenBy(v => v.Episode)
                        .Select(v => v.ThumbnailPath).FirstOrDefault(),
                    // Get poster from the first episode that has one
                    PosterPath = g.Where(v => v.PosterPath != null && v.PosterPath != "")
                        .OrderBy(v => v.Season).ThenBy(v => v.Episode)
                        .Select(v => v.PosterPath).FirstOrDefault(),
                    BackdropPath = g.Where(v => v.BackdropPath != null && v.BackdropPath != "")
                        .OrderBy(v => v.Season).ThenBy(v => v.Episode)
                        .Select(v => v.BackdropPath).FirstOrDefault(),
                    Rating = g.Max(v => v.Rating),
                    Genre = g.Where(v => v.Genre != "").Select(v => v.Genre).FirstOrDefault(),
                    Overview = g.Where(v => v.Overview != "").Select(v => v.Overview).FirstOrDefault(),
                    ContentRating = g.Where(v => v.ContentRating != "").Select(v => v.ContentRating).FirstOrDefault(),
                    FirstEpisodeId = g.OrderBy(v => v.Season).ThenBy(v => v.Episode).Select(v => v.Id).FirstOrDefault(),
                    Height = g.Max(v => v.Height)
                })
                .ToListAsync();

            // Build movies list
            var movies = await movieQuery
                .Select(v => new
                {
                    v.Id, v.FileName, v.Title, v.Year, v.Duration, v.SizeBytes,
                    v.Format, v.Resolution, v.Width, v.Height, v.Codec,
                    v.AudioCodec, v.AudioChannels, v.AudioLanguages, v.SubtitleLanguages,
                    v.Genre, v.Director, v.Cast, v.Overview, v.Rating, v.ContentRating,
                    v.MediaType, v.SeriesName, v.Season, v.Episode,
                    v.ThumbnailPath, v.PosterPath, v.BackdropPath,
                    v.Mp4Compliant, v.NeedsOptimization,
                    IsFavourite = vidFavIds.Contains(v.Id),
                    IsWatched = vidWatchedIds.Contains(v.Id), v.DateAdded
                })
                .ToListAsync();

            // Merge into a unified list of objects
            var items = new List<object>();

            foreach (var m in movies)
            {
                items.Add(new
                {
                    type = "video",
                    m.Id, m.FileName, m.Title, m.Year, m.Duration, m.SizeBytes,
                    m.Format, m.Resolution, m.Width, m.Height, m.Codec,
                    m.AudioCodec, m.AudioChannels, m.AudioLanguages, m.SubtitleLanguages,
                    m.Genre, m.Director, m.Cast, m.Overview, m.Rating, m.ContentRating,
                    m.MediaType, m.SeriesName, m.Season, m.Episode,
                    m.ThumbnailPath, m.PosterPath, m.BackdropPath,
                    m.Mp4Compliant, m.NeedsOptimization, m.IsFavourite, m.IsWatched, m.DateAdded
                });
            }

            foreach (var s2 in seriesGroups)
            {
                items.Add(new
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
                    genre = s2.Genre ?? "",
                    overview = s2.Overview ?? "",
                    contentRating = s2.ContentRating ?? "",
                    height = s2.Height,
                    year = s2.LatestYear,
                    dateAdded = s2.LatestAdded,
                    mediaType = "tv"
                });
            }

            // Sort the merged list
            items = sort switch
            {
                "title" => items.OrderBy(i => GetSortTitle(i)).ToList(),
                "year" => items.OrderByDescending(i => GetSortYear(i)).ThenBy(i => GetSortTitle(i)).ToList(),
                "size" => items.OrderByDescending(i => GetSortSize(i)).ToList(),
                "duration" => items.OrderByDescending(i => GetSortDuration(i)).ToList(),
                _ => items.OrderByDescending(i => GetSortDate(i)).ToList()
            };

            var total = items.Count;
            var paged = items.Skip((page - 1) * limit).Take(limit).ToList();
            return Ok(new { total, page, limit, videos = paged });
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
                v.Format, v.Resolution, v.Width, v.Height, v.Codec,
                v.AudioCodec, v.AudioChannels, v.AudioLanguages, v.SubtitleLanguages,
                v.Genre, v.Director, v.Cast, v.Overview, v.Rating, v.ContentRating,
                v.MediaType, v.SeriesName, v.Season, v.Episode,
                v.ThumbnailPath, v.PosterPath, v.BackdropPath, v.CastJson,
                v.Mp4Compliant, v.NeedsOptimization,
                IsFavourite = vidFavIdsU.Contains(v.Id),
                IsWatched = vidWatchedIdsU.Contains(v.Id), v.DateAdded
            }).ToListAsync();

        return Ok(new { total = totalUngrouped, page, limit, videos });
    }

    // Helper methods for sorting merged grouped results
    private static string GetSortTitle(object item)
    {
        var t = item.GetType();
        var type = (string)(t.GetProperty("type")?.GetValue(item) ?? "");
        if (type == "series") return (string)(t.GetProperty("seriesName")?.GetValue(item) ?? "");
        return (string)(t.GetProperty("Title")?.GetValue(item) ?? "");
    }
    private static int? GetSortYear(object item) => (int?)(item.GetType().GetProperty("year")?.GetValue(item) ?? item.GetType().GetProperty("Year")?.GetValue(item));
    private static long GetSortSize(object item) => (long)(item.GetType().GetProperty("sizeBytes")?.GetValue(item) ?? item.GetType().GetProperty("SizeBytes")?.GetValue(item) ?? 0L);
    private static double GetSortDuration(object item) => (double)(item.GetType().GetProperty("duration")?.GetValue(item) ?? item.GetType().GetProperty("Duration")?.GetValue(item) ?? 0.0);
    private static DateTime GetSortDate(object item) => (DateTime)(item.GetType().GetProperty("dateAdded")?.GetValue(item) ?? item.GetType().GetProperty("DateAdded")?.GetValue(item) ?? DateTime.MinValue);

    [HttpGet("videos/stats")]
    public async Task<IActionResult> GetVideoStats()
    {
        var stats = new
        {
            totalVideos = await _videoDb.Videos.CountAsync(),
            totalMovies = await _videoDb.Videos.CountAsync(v => v.MediaType == "movie"),
            totalTvEpisodes = await _videoDb.Videos.CountAsync(v => v.MediaType == "tv"),
            totalSeries = await _videoDb.Videos.Where(v => v.SeriesName != "")
                .Select(v => v.SeriesName).Distinct().CountAsync(),
            totalSize = await _videoDb.Videos.SumAsync(v => v.SizeBytes),
            totalDuration = await _videoDb.Videos.SumAsync(v => v.Duration),
            needsOptimization = await _videoDb.Videos.CountAsync(v => v.NeedsOptimization),
            withThumbnails = await _videoDb.Videos.CountAsync(v => v.ThumbnailPath != null && v.ThumbnailPath != "")
        };
        return Ok(stats);
    }

    [HttpGet("videos/{id}")]
    public async Task<IActionResult> GetVideo(int id)
    {
        var v = await _videoDb.Videos.FindAsync(id);
        if (v == null) return NotFound();
        var isWatched = _userFavs.IsWatched(CurrentUsername, id);
        return Ok(new
        {
            v.Id, v.FileName, v.FilePath, v.Title, v.Year, v.Duration, v.SizeBytes,
            v.Format, v.Resolution, v.Width, v.Height, v.Codec, v.VideoBitrate,
            v.AudioCodec, v.AudioChannels, v.AudioLanguages, v.SubtitleLanguages,
            v.Genre, v.Director, v.Cast, v.Overview, v.Rating, v.ContentRating,
            v.MediaType, v.SeriesName, v.Season, v.Episode,
            v.ThumbnailPath, v.PosterPath, v.BackdropPath, v.CastJson,
            v.TmdbId, v.TvMazeId, v.ImdbId, v.MetadataFetched,
            v.Mp4Compliant, v.NeedsOptimization,
            v.DateAdded, v.LastModified, v.LastPlayed, v.PlayCount, isWatched
        });
    }

    [HttpPut("videos/{id}")]
    public async Task<IActionResult> UpdateVideo(int id, [FromBody] System.Text.Json.JsonElement body)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();

        if (body.TryGetProperty("title", out var t)) video.Title = t.GetString() ?? "";
        if (body.TryGetProperty("year", out var y) && y.ValueKind == System.Text.Json.JsonValueKind.Number) video.Year = y.GetInt32();
        if (body.TryGetProperty("genre", out var g)) video.Genre = g.GetString() ?? "";
        if (body.TryGetProperty("director", out var d)) video.Director = d.GetString() ?? "";
        if (body.TryGetProperty("cast", out var c)) video.Cast = c.GetString() ?? "";
        if (body.TryGetProperty("overview", out var o)) video.Overview = o.GetString() ?? "";
        if (body.TryGetProperty("rating", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.Number) video.Rating = r.GetDouble();
        if (body.TryGetProperty("contentRating", out var cr)) video.ContentRating = cr.GetString() ?? "";
        if (body.TryGetProperty("mediaType", out var mt)) video.MediaType = mt.GetString() ?? "movie";
        if (body.TryGetProperty("seriesName", out var sn)) video.SeriesName = sn.GetString() ?? "";
        if (body.TryGetProperty("season", out var se) && se.ValueKind == System.Text.Json.JsonValueKind.Number) video.Season = se.GetInt32();
        if (body.TryGetProperty("episode", out var ep) && ep.ValueKind == System.Text.Json.JsonValueKind.Number) video.Episode = ep.GetInt32();

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

        return Ok(new { success = true });
    }

    [HttpGet("stream-video/{id}")]
    public async Task<IActionResult> StreamVideo(int id, [FromQuery] int audioTrack = 0)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();
        if (!System.IO.File.Exists(video.FilePath))
            return NotFound(new { message = "Video file not found on disk" });

        // Update global last played + per-user play count
        video.LastPlayed = DateTime.UtcNow;
        await _videoDb.SaveChangesAsync();
        _userFavs.IncrementPlayCount(CurrentUsername, "video", id);
        _userFavs.MarkWatched(CurrentUsername, id);

        // If MP4 compliant and using default audio track, stream directly with byte-range support
        if (video.Mp4Compliant && video.Format.Equals("MP4", StringComparison.OrdinalIgnoreCase) && audioTrack == 0)
        {
            _logger.LogDebug("Video {Id} is MP4 compliant, streaming directly", id);
            var stream = new FileStream(video.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, "video/mp4", enableRangeProcessing: true);
        }

        _logger.LogInformation("Video {Id} needs remux (Format={Format}, Mp4Compliant={Compliant}, Channels={Ch})",
            id, video.Format, video.Mp4Compliant, video.AudioChannels);

        // Check for cached remux (include audio track in cache key)
        var cacheDir = Path.Combine(AppContext.BaseDirectory, "cache", "remux");
        if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
        var cacheKey = audioTrack > 0
            ? $"vid_{video.Id}_{video.LastModified.Ticks}_a{audioTrack}"
            : $"vid_{video.Id}_{video.LastModified.Ticks}";
        var cachePath = Path.Combine(cacheDir, $"{cacheKey}.mp4");

        if (System.IO.File.Exists(cachePath))
        {
            _logger.LogDebug("Serving cached remux for video {Id}: {Path}", id, cachePath);
            var stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, "video/mp4", enableRangeProcessing: true);
        }

        // If FFmpeg available, use TranscodingService for enhanced remux (with priority/threading)
        if (_ffmpeg.IsAvailable && _transcoding != null)
        {
            _logger.LogInformation("Remuxing video {Id}: {File} (audio track {Track})", id, video.FilePath, audioTrack);
            bool success;
            if (video.AudioChannels > 2)
                success = await _transcoding.RemuxStereoDownmixAsync(video.FilePath, cachePath, audioTrack);
            else
                success = await _transcoding.RemuxFaststartAsync(video.FilePath, cachePath, audioTrack);

            if (success && System.IO.File.Exists(cachePath))
            {
                _logger.LogInformation("Remux successful for video {Id}, cached at {Path}", id, cachePath);
                var stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return File(stream, "video/mp4", enableRangeProcessing: true);
            }
            _logger.LogWarning("Remux failed for video {Id}", id);
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
        var fallbackMime = video.Format.ToUpperInvariant() switch
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
    public async Task<IActionResult> GetStreamInfo(int id, [FromQuery] int audioTrack = 0)
    {
        var video = await _videoDb.Videos.FindAsync(id);
        if (video == null) return NotFound();
        if (!System.IO.File.Exists(video.FilePath))
            return NotFound(new { message = "Video file not found on disk" });

        var result = await _transcoding.GetSmartStreamAsync(
            video.Id, video.FilePath, video.Format,
            video.Codec, video.AudioCodec, video.AudioChannels,
            video.Duration, audioTrack);

        if (result.Error != null)
            return StatusCode(503, new { error = result.Error });

        // Append audioTrack param to stream URLs so the direct-play endpoint also uses the right track
        var trackSuffix = audioTrack > 0 ? $"?audioTrack={audioTrack}" : "";

        return Ok(new
        {
            result.Type,
            streamUrl = result.StreamUrl != null ? result.StreamUrl + trackSuffix : result.StreamUrl,
            playlistUrl = result.PlaylistUrl,
            transcodeId = result.TranscodeId,
            result.Mode,
            result.Reason,
            result.Duration,
            audioTrack
        });
    }

    // ─── HLS Endpoints ──────────────────────────────────────────────

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
            if (content != null && content.Contains("#EXTINF:") && initExists)
                break;

            await Task.Delay(pollIntervalMs);
            waited += pollIntervalMs;
        }

        if (content == null)
            return NotFound(new { error = "Playlist not ready - transcode may have failed" });

        return Content(content, "application/vnd.apple.mpegurl");
    }

    [HttpGet("hls/{transcodeId}/{segment}")]
    public IActionResult GetHLSSegment(string transcodeId, string segment)
    {
        // Validate segment name (prevent path traversal)
        if (segment.Contains("..") || segment.Contains('/') || segment.Contains('\\'))
            return BadRequest();

        var path = _transcoding.GetSegmentPath(transcodeId, segment);
        if (path == null)
            return NotFound();

        var contentType = segment.EndsWith(".m4s") ? "video/iso.segment"
            : segment.EndsWith(".mp4") ? "video/mp4"
            : segment.EndsWith(".ts") ? "video/mp2t"
            : "application/octet-stream";

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, contentType, enableRangeProcessing: true);
    }

    [HttpPost("stop-transcode/{transcodeId}")]
    [HttpGet("stop-transcode/{transcodeId}")]
    public IActionResult StopTranscode(string transcodeId)
    {
        var stopped = _transcoding.StopTranscode(transcodeId);
        return Ok(new { success = stopped, transcodeId });
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
    public async Task<IActionResult> GetVideoGenres()
    {
        var genres = await _videoDb.Videos
            .Where(v => v.Genre != "")
            .GroupBy(v => v.Genre)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count)
            .ToListAsync();
        return Ok(genres);
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

    [HttpPost("scan/videos")]
    public IActionResult StartVideoScan()
    {
        if (_videoScanner.IsScanning)
            return Conflict(new { message = "Videos scan already in progress" });
        _ = _videoScanner.StartScanAsync();
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

    [HttpPost("video/generate-thumbnails")]
    public IActionResult GenerateVideoThumbnails()
    {
        if (!_ffmpeg.IsAvailable)
            return BadRequest(new { message = "FFmpeg not available" });
        _ = _videoScanner.GenerateAllThumbnailsAsync();
        return Ok(new { message = "Generating video thumbnails in background..." });
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
        [FromQuery] string? search = null)
    {
        var query = _tvDb.TvChannels.AsQueryable();

        if (!string.IsNullOrWhiteSpace(country))
            query = query.Where(c => c.Country == country);
        if (!string.IsNullOrWhiteSpace(genre))
            query = query.Where(c => c.Genre == genre);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(q) || c.Description.ToLower().Contains(q) || c.Country.ToLower().Contains(q));
        }

        var channels = await query.OrderBy(c => c.Name).ToListAsync();

        // Mark favourites
        var favIds = _userFavs.GetFavouriteIds(CurrentUsername, "tvchannel");
        foreach (var c in channels) c.IsFavourite = favIds.Contains(c.Id);

        var countries = await _tvDb.TvChannels.Where(c => c.Country != "").Select(c => c.Country).Distinct().OrderBy(c => c).ToListAsync();
        var genres = await _tvDb.TvChannels.Where(c => c.Genre != "").Select(c => c.Genre).Distinct().OrderBy(g => g).ToListAsync();

        return Ok(new { total = channels.Count, countries, genres, channels });
    }

    [HttpPost("tvchannels/import")]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> ImportM3u(IFormFile file, [FromForm] string? country, [FromForm] string? genre)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file uploaded" });

        if (!file.FileName.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) &&
            !file.FileName.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Only .m3u and .m3u8 files are supported" });

        using var stream = file.OpenReadStream();
        var channels = _tvService.ParseM3u(stream, file.FileName);

        if (channels.Count == 0)
            return BadRequest(new { message = "No channels found in the M3U file" });

        // Apply country/genre if provided
        if (!string.IsNullOrWhiteSpace(country))
            foreach (var c in channels) c.Country = country;
        if (!string.IsNullOrWhiteSpace(genre))
            foreach (var c in channels) c.Genre = genre;

        // Import: skip duplicates by stream URL
        int imported = 0, skipped = 0;
        foreach (var channel in channels)
        {
            var exists = await _tvDb.TvChannels.AnyAsync(c => c.StreamUrl == channel.StreamUrl);
            if (exists) { skipped++; continue; }
            _tvDb.TvChannels.Add(channel);
            imported++;
        }

        await _tvDb.SaveChangesAsync();
        _logger.LogInformation("Imported {Imported} TV channels from {File} ({Skipped} duplicates skipped)", imported, file.FileName, skipped);

        return Ok(new { message = $"Imported {imported} channels ({skipped} duplicates skipped)", imported, skipped, total = channels.Count });
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

        // ── Comfort content (most replayed videos) ──
        var videoPlays = _userFavs.GetMostPlayed(username, "video", 10);
        var comfortIds = videoPlays.Where(p => p.Count > 1).Select(p => p.MediaId).ToList();
        var comfortVideos = comfortIds.Count > 0
            ? await _videoDb.Videos.Where(v => comfortIds.Contains(v.Id)).ToListAsync()
            : new List<Video>();
        var comfortContent = comfortIds
            .Select(id =>
            {
                var v = comfortVideos.FirstOrDefault(x => x.Id == id);
                var plays = videoPlays.First(p => p.MediaId == id);
                return v != null ? new { v.Id, v.Title, v.MediaType, playCount = plays.Count, v.ThumbnailPath, v.PosterPath } : null;
            })
            .Where(x => x != null)
            .ToList();

        // ── Recently completed (most recent watched) ──
        // GetWatchedIds returns ordered by WatchedAt DESC, take first 10
        var recentWatchedIds = watchedIds.Take(10).ToList();
        var recentWatchedVideos = recentWatchedIds.Count > 0
            ? await _videoDb.Videos.Where(v => recentWatchedIds.Contains(v.Id)).ToListAsync()
            : new List<Video>();
        var recentlyCompleted = recentWatchedIds
            .Select(id => recentWatchedVideos.FirstOrDefault(v => v.Id == id))
            .Where(v => v != null)
            .Select(v => new { v!.Id, v.Title, v.MediaType, v.ThumbnailPath, v.PosterPath, v.Year, v.Rating })
            .ToList();

        // ── Top rated unwatched ──
        var rawUnwatched = await _videoDb.Videos
            .Where(v => v.Rating > 7 && !watchedIds.Contains(v.Id)
                     && v.PosterPath != null && v.PosterPath != "")
            .OrderByDescending(v => v.Rating)
            .ThenByDescending(v => v.Year)
            .Take(60)
            .Select(v => new { v.Id, v.Title, v.Rating, v.Genre, v.MediaType, v.PosterPath, v.ThumbnailPath, v.Year, v.SeriesName })
            .ToListAsync();

        // Deduplicate TV shows: group by SeriesName, pick best representative
        var unwatchedMovies = rawUnwatched.Where(v => v.MediaType != "tv")
            .Select(v => new { v.Id, v.Title, v.Rating, v.Genre, v.MediaType, v.PosterPath, v.ThumbnailPath, v.Year });
        var unwatchedTv = rawUnwatched.Where(v => v.MediaType == "tv" && !string.IsNullOrEmpty(v.SeriesName))
            .GroupBy(v => v.SeriesName)
            .Select(g => {
                var best = g.OrderByDescending(e => e.Rating).First();
                return new { best.Id, Title = g.Key, best.Rating, best.Genre, best.MediaType, best.PosterPath, best.ThumbnailPath, best.Year };
            });
        var topRatedUnwatched = unwatchedMovies.Concat(unwatchedTv)
            .OrderByDescending(v => v.Rating)
            .Take(20)
            .ToList();

        // ── Generate insight message templates (resolved client-side for i18n) ──
        var messages = new List<object>();

        if (totalWatched > 0)
        {
            messages.Add(new { key = "completed", movies = moviesWatched, tvEpisodes = tvEpisodesWatched, docs = docsWatched });
        }

        if (totalWatchTimeHours >= 24)
        {
            var days = Math.Round(totalWatchTimeHours / 24, 1);
            messages.Add(new { key = "watchDays", days });
        }
        else if (totalWatchTimeHours >= 1)
        {
            messages.Add(new { key = "watchHours", hours = totalWatchTimeHours });
        }

        if (favoriteGenres.Count > 0)
        {
            var topGenre = favoriteGenres[0];
            messages.Add(new { key = "topGenre", genre = topGenre.name, count = topGenre.count });
        }

        if (comfortContent.Count > 0)
        {
            var top = comfortContent[0]!;
            if (top.playCount > 2)
                messages.Add(new { key = "comfortTitle", title = top.Title, playCount = top.playCount });
        }

        if (messages.Count == 0)
            messages.Add(new { key = "startWatching" });

        // ── Total library stats for context ──
        var totalLibraryVideos = await _videoDb.Videos.CountAsync();
        var totalLibraryMovies = await _videoDb.Videos.CountAsync(v => v.MediaType == "movie");
        var totalLibraryTv = await _videoDb.Videos.CountAsync(v => v.MediaType == "tv");

        return Ok(new
        {
            totalWatched,
            moviesWatched,
            tvEpisodesWatched,
            totalWatchTimeHours,
            favoriteGenres,
            comfortContent,
            recentlyCompleted,
            topRatedUnwatched,
            insightMessages = messages,
            library = new { total = totalLibraryVideos, movies = totalLibraryMovies, tvEpisodes = totalLibraryTv }
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

    // ─── Actors ──────────────────────────────────────────────────────

    [HttpGet("actors")]
    public async Task<IActionResult> GetActors([FromQuery] int page = 1, [FromQuery] int limit = 60, [FromQuery] string? search = null)
    {
        var query = _actorsDb.Actors.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim().ToLowerInvariant().Replace(" ", "");
            query = query.Where(a => a.NormalizedName.Contains(normalized) || a.Name.Contains(search.Trim()));
        }

        var total = await query.CountAsync();
        var actors = await query
            .OrderByDescending(a => a.Popularity ?? 0)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.TmdbId,
                a.ImageCached,
                a.KnownForDepartment,
                a.Popularity,
                movieCount = _actorsDb.MovieActors.Count(ma => ma.ActorId == a.Id)
            })
            .ToListAsync();

        return Ok(new { total, page, limit, actors });
    }

    [HttpGet("actors/stats")]
    public async Task<IActionResult> GetActorStats()
    {
        var total = await _actorsDb.Actors.CountAsync();
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

        // Lazy-fetch bio from TMDB if missing and we have a TMDB ID
        if (string.IsNullOrEmpty(actor.Biography) && actor.TmdbId.HasValue)
        {
            var key = _config.Config.Metadata.TmdbApiKey;
            if (!string.IsNullOrWhiteSpace(key))
            {
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                    var url = $"https://api.themoviedb.org/3/person/{actor.TmdbId}?api_key={key}";
                    var resp = await http.GetAsync(url);
                    if (resp.IsSuccessStatusCode)
                    {
                        var json = await resp.Content.ReadAsStringAsync();
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("biography", out var bio) && bio.ValueKind == System.Text.Json.JsonValueKind.String)
                            actor.Biography = bio.GetString();
                        if (root.TryGetProperty("birthday", out var bd) && bd.ValueKind == System.Text.Json.JsonValueKind.String)
                            actor.Birthday = bd.GetString();
                        if (root.TryGetProperty("deathday", out var dd) && dd.ValueKind == System.Text.Json.JsonValueKind.String)
                            actor.Deathday = dd.GetString();
                        if (root.TryGetProperty("place_of_birth", out var pob) && pob.ValueKind == System.Text.Json.JsonValueKind.String)
                            actor.PlaceOfBirth = pob.GetString();

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

        var movieCount = await _actorsDb.MovieActors.CountAsync(ma => ma.ActorId == id);

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

        var key = _config.Config.Metadata.TmdbApiKey;
        if (string.IsNullOrWhiteSpace(key)) return Ok(new List<object>());

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var url = $"https://api.themoviedb.org/3/person/{actor.TmdbId}/combined_credits?api_key={key}";
            var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return Ok(new List<object>());

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var credits = new List<object>();
            // Exclude talk shows (10767), news (10763), reality TV (10764)
            var excludedGenreIds = new HashSet<int> { 10767, 10763, 10764 };

            if (root.TryGetProperty("cast", out var castArr) && castArr.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var actorsDir = Path.Combine(AppContext.BaseDirectory, "assets", "actors");
                var seen = new HashSet<string>(); // Deduplicate by mediaType+tmdbId

                // Parse all items first, then deduplicate, sort by date, take 30
                var parsed = new List<(string mediaType, string title, string? year, string? posterPath, int tmdbId, string character)>();

                foreach (var item in castArr.EnumerateArray())
                {
                    // Exclude talk shows, news, reality TV
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

            return Ok(credits);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch known-for credits for actor {Id}", actor.TmdbId);
            return Ok(new List<object>());
        }
    }
}

// ─── DTOs ────────────────────────────────────────────────────────────

public record PlaylistCreateDto(string Name, string? Description);
public record PlaylistAddTrackDto(int TrackId);
public record PlaylistAddTracksDto(int[] TrackIds);
public record RateDto(int Rating);
public record MoodDef(string Key, string Name, string Icon, string Color, string Description, string Genres, string Runtime);
