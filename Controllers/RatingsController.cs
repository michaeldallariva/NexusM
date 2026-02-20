using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusM.Data;
using NexusM.Models;

namespace NexusM.Controllers;

/// <summary>
/// Community rating system — one vote per user per media item, shared across all users.
/// </summary>
[ApiController]
[Route("api/ratings")]
[Authorize]
public class RatingsController : ControllerBase
{
    private readonly RatingsDbContext _ratings;
    private readonly VideosDbContext _videos;
    private readonly MusicDbContext _music;
    private readonly MusicVideosDbContext _musicVideos;
    private readonly PicturesDbContext _pictures;
    private readonly EBooksDbContext _ebooks;
    private readonly ILogger<RatingsController> _logger;

    public RatingsController(
        RatingsDbContext ratings,
        VideosDbContext videos,
        MusicDbContext music,
        MusicVideosDbContext musicVideos,
        PicturesDbContext pictures,
        EBooksDbContext ebooks,
        ILogger<RatingsController> logger)
    {
        _ratings = ratings;
        _videos = videos;
        _music = music;
        _musicVideos = musicVideos;
        _pictures = pictures;
        _ebooks = ebooks;
        _logger = logger;
    }

    private string CurrentUsername =>
        User.FindFirst(ClaimTypes.Name)?.Value ?? User.Identity?.Name ?? "unknown";

    // ─── POST /api/ratings ─────────────────────────────────────────
    // Upsert the current user's rating for a media item.

    [HttpPost]
    public async Task<IActionResult> UpsertRating([FromBody] UpsertRatingDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.MediaType))
            return BadRequest(new { error = "mediaType is required" });

        if (dto.Stars < 1 || dto.Stars > 5)
            return BadRequest(new { error = "Stars must be between 1 and 5" });

        var username = CurrentUsername;
        var existing = await _ratings.Ratings.FirstOrDefaultAsync(r =>
            r.MediaType == dto.MediaType && r.MediaId == dto.MediaId && r.Username == username);

        if (existing != null)
        {
            existing.Stars = dto.Stars;
            existing.DateModified = DateTime.UtcNow;
        }
        else
        {
            _ratings.Ratings.Add(new Rating
            {
                MediaType = dto.MediaType,
                MediaId = dto.MediaId,
                Username = username,
                Stars = dto.Stars,
                DateRated = DateTime.UtcNow
            });
        }

        await _ratings.SaveChangesAsync();

        var summary = await GetSummaryInternal(dto.MediaType, dto.MediaId, username);
        return Ok(summary);
    }

    // ─── DELETE /api/ratings/{mediaType}/{mediaId} ─────────────────

    [HttpDelete("{mediaType}/{mediaId:int}")]
    public async Task<IActionResult> DeleteRating(string mediaType, int mediaId)
    {
        var username = CurrentUsername;
        var rating = await _ratings.Ratings.FirstOrDefaultAsync(r =>
            r.MediaType == mediaType && r.MediaId == mediaId && r.Username == username);

        if (rating == null)
            return NotFound(new { error = "Rating not found" });

        _ratings.Ratings.Remove(rating);
        await _ratings.SaveChangesAsync();

        var summary = await GetSummaryInternal(mediaType, mediaId, username);
        return Ok(summary);
    }

    // ─── GET /api/ratings/summary/{mediaType}/{mediaId} ────────────

    [HttpGet("summary/{mediaType}/{mediaId:int}")]
    public async Task<IActionResult> GetSummary(string mediaType, int mediaId)
    {
        var username = CurrentUsername;
        var summary = await GetSummaryInternal(mediaType, mediaId, username);
        return Ok(summary);
    }

    private async Task<object> GetSummaryInternal(string mediaType, int mediaId, string username)
    {
        var allRatings = await _ratings.Ratings
            .Where(r => r.MediaType == mediaType && r.MediaId == mediaId)
            .ToListAsync();

        var count = allRatings.Count;
        var average = count > 0 ? allRatings.Average(r => r.Stars) : 0.0;
        var userRating = allRatings.FirstOrDefault(r => r.Username == username)?.Stars ?? 0;

        return new { average, count, userRating };
    }

    // ─── GET /api/ratings/best?limit=20 ───────────────────────────

    [HttpGet("best")]
    public async Task<IActionResult> GetBestRated([FromQuery] int limit = 20)
    {
        if (limit < 1) limit = 1;
        if (limit > 100) limit = 100;

        // Aggregate ratings per (MediaType, MediaId)
        var aggregated = await _ratings.Ratings
            .GroupBy(r => new { r.MediaType, r.MediaId })
            .Select(g => new
            {
                g.Key.MediaType,
                g.Key.MediaId,
                Avg = g.Average(r => (double)r.Stars),
                Count = g.Count()
            })
            .Where(x => x.Count >= 1 && x.Avg >= 1.0)
            .OrderByDescending(x => x.Avg)
            .ThenByDescending(x => x.Count)
            .ToListAsync();

        // Split by media type and take top N
        var videoIds = aggregated.Where(x => x.MediaType == "video").Take(limit).ToList();
        var trackIds = aggregated.Where(x => x.MediaType == "track").Take(limit).ToList();
        var albumIds = aggregated.Where(x => x.MediaType == "album").Take(limit).ToList();
        var mvIds = aggregated.Where(x => x.MediaType == "musicvideo").Take(limit).ToList();
        var pictureIds = aggregated.Where(x => x.MediaType == "picture").Take(limit).ToList();
        var ebookIds = aggregated.Where(x => x.MediaType == "ebook").Take(limit).ToList();

        // Fetch media records and merge rating data
        var videos = new List<object>();
        if (videoIds.Count > 0)
        {
            var ids = videoIds.Select(x => x.MediaId).ToList();
            var records = await _videos.Videos.Where(v => ids.Contains(v.Id)).ToListAsync();
            videos = videoIds
                .Select(r => new
                {
                    media = records.FirstOrDefault(v => v.Id == r.MediaId),
                    r.Avg,
                    r.Count
                })
                .Where(x => x.media != null)
                .Select(x => (object)new
                {
                    x.media!.Id, x.media.Title, x.media.Year, x.media.Duration,
                    x.media.MediaType, x.media.SeriesName, x.media.Season, x.media.Episode,
                    x.media.PosterPath, x.media.ThumbnailPath, x.media.Genre, x.media.Overview,
                    x.media.PlayCount,
                    avgRating = Math.Round(x.Avg, 2),
                    ratingCount = x.Count
                })
                .ToList();
        }

        var tracks = new List<object>();
        if (trackIds.Count > 0)
        {
            var ids = trackIds.Select(x => x.MediaId).ToList();
            var records = await _music.Tracks.Where(t => ids.Contains(t.Id)).ToListAsync();
            tracks = trackIds
                .Select(r => new
                {
                    media = records.FirstOrDefault(t => t.Id == r.MediaId),
                    r.Avg,
                    r.Count
                })
                .Where(x => x.media != null)
                .Select(x => (object)new
                {
                    x.media!.Id, x.media.Title, x.media.Artist, x.media.Album,
                    x.media.Duration, x.media.AlbumArtCached, x.media.PlayCount,
                    avgRating = Math.Round(x.Avg, 2),
                    ratingCount = x.Count
                })
                .ToList();
        }

        var albums = new List<object>();
        if (albumIds.Count > 0)
        {
            var ids = albumIds.Select(x => x.MediaId).ToList();
            var records = await _music.Albums.Where(a => ids.Contains(a.Id)).ToListAsync();
            albums = albumIds
                .Select(r => new
                {
                    media = records.FirstOrDefault(a => a.Id == r.MediaId),
                    r.Avg,
                    r.Count
                })
                .Where(x => x.media != null)
                .Select(x => (object)new
                {
                    x.media!.Id, x.media.Name, x.media.Artist, x.media.Year,
                    x.media.CoverArtPath, x.media.TrackCount,
                    avgRating = Math.Round(x.Avg, 2),
                    ratingCount = x.Count
                })
                .ToList();
        }

        var musicVideos = new List<object>();
        if (mvIds.Count > 0)
        {
            var ids = mvIds.Select(x => x.MediaId).ToList();
            var records = await _musicVideos.MusicVideos.Where(v => ids.Contains(v.Id)).ToListAsync();
            musicVideos = mvIds
                .Select(r => new
                {
                    media = records.FirstOrDefault(v => v.Id == r.MediaId),
                    r.Avg,
                    r.Count
                })
                .Where(x => x.media != null)
                .Select(x => (object)new
                {
                    x.media!.Id, x.media.Title, x.media.Artist, x.media.Duration,
                    x.media.ThumbnailPath, x.media.PlayCount,
                    avgRating = Math.Round(x.Avg, 2),
                    ratingCount = x.Count
                })
                .ToList();
        }

        var pictures = new List<object>();
        if (pictureIds.Count > 0)
        {
            var ids = pictureIds.Select(x => x.MediaId).ToList();
            var records = await _pictures.Pictures.Where(p => ids.Contains(p.Id)).ToListAsync();
            pictures = pictureIds
                .Select(r => new
                {
                    media = records.FirstOrDefault(p => p.Id == r.MediaId),
                    r.Avg,
                    r.Count
                })
                .Where(x => x.media != null)
                .Select(x => (object)new
                {
                    x.media!.Id, x.media.FileName, x.media.Category,
                    x.media.Width, x.media.Height, x.media.ThumbnailPath,
                    avgRating = Math.Round(x.Avg, 2),
                    ratingCount = x.Count
                })
                .ToList();
        }

        var ebooks = new List<object>();
        if (ebookIds.Count > 0)
        {
            var ids = ebookIds.Select(x => x.MediaId).ToList();
            var records = await _ebooks.EBooks.Where(e => ids.Contains(e.Id)).ToListAsync();
            ebooks = ebookIds
                .Select(r => new
                {
                    media = records.FirstOrDefault(e => e.Id == r.MediaId),
                    r.Avg,
                    r.Count
                })
                .Where(x => x.media != null)
                .Select(x => (object)new
                {
                    x.media!.Id, x.media.Title, x.media.Author, x.media.Format,
                    x.media.CoverImage, x.media.PageCount,
                    avgRating = Math.Round(x.Avg, 2),
                    ratingCount = x.Count
                })
                .ToList();
        }

        return Ok(new { videos, tracks, albums, musicVideos, pictures, ebooks });
    }
}

// ─── DTOs ─────────────────────────────────────────────────────────────

public record UpsertRatingDto(string MediaType, int MediaId, int Stars);
