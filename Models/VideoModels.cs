using System.ComponentModel.DataAnnotations;

namespace NexusM.Models;

/// <summary>
/// Represents a movie or TV show episode in the videos library database.
/// </summary>
public class Video
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string FilePath { get; set; } = "";

    [Required]
    public string FileName { get; set; } = "";

    public string Title { get; set; } = "";
    public int? Year { get; set; }

    /// <summary>Duration in seconds</summary>
    public double Duration { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Container format: MP4, MKV, AVI, etc.</summary>
    public string Format { get; set; } = "";

    /// <summary>Resolution string, e.g. "1920x1080"</summary>
    public string Resolution { get; set; } = "";

    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Video codec, e.g. h264, hevc</summary>
    public string Codec { get; set; } = "";

    /// <summary>Video bitrate in kbps</summary>
    public int VideoBitrate { get; set; }

    /// <summary>Audio codec, e.g. aac, ac3, dts</summary>
    public string AudioCodec { get; set; } = "";

    /// <summary>Number of audio channels (2=stereo, 6=5.1, 8=7.1)</summary>
    public int AudioChannels { get; set; } = 2;

    /// <summary>Comma-separated audio languages, e.g. "eng,fre,ger"</summary>
    public string AudioLanguages { get; set; } = "";

    /// <summary>Comma-separated subtitle languages, e.g. "eng,fre"</summary>
    public string SubtitleLanguages { get; set; } = "";

    public string Genre { get; set; } = "";
    public string Director { get; set; } = "";

    /// <summary>Comma-separated cast names</summary>
    public string Cast { get; set; } = "";

    /// <summary>Plot overview / description</summary>
    public string Overview { get; set; } = "";

    /// <summary>Rating 0-10 (e.g. TMDB score, to be filled later)</summary>
    public double Rating { get; set; }

    /// <summary>Content/age rating, e.g. PG-13, R, TV-MA</summary>
    public string ContentRating { get; set; } = "";

    /// <summary>"movie" or "tv"</summary>
    public string MediaType { get; set; } = "movie";

    /// <summary>Series/show name for TV episodes</summary>
    public string SeriesName { get; set; } = "";

    /// <summary>Season number for TV episodes</summary>
    public int? Season { get; set; }

    /// <summary>Episode number for TV episodes</summary>
    public int? Episode { get; set; }

    /// <summary>Thumbnail filename stored in assets/videothumbs/</summary>
    public string? ThumbnailPath { get; set; }

    // ── External metadata provider IDs ──
    public string? TmdbId { get; set; }
    public string? TvMazeId { get; set; }
    public string? ImdbId { get; set; }

    /// <summary>Poster image filename in assets/videometa/</summary>
    public string? PosterPath { get; set; }

    /// <summary>Backdrop/fanart image filename in assets/videometa/</summary>
    public string? BackdropPath { get; set; }

    /// <summary>JSON array of cast: [{name, character, photo}, ...]</summary>
    public string? CastJson { get; set; }

    /// <summary>Whether external metadata has been fetched for this video</summary>
    public bool MetadataFetched { get; set; }

    /// <summary>Whether the video is a compliant MP4 for direct browser streaming</summary>
    public bool Mp4Compliant { get; set; } = true;

    /// <summary>Whether the video needs optimization (non-MP4, surround audio, etc.)</summary>
    public bool NeedsOptimization { get; set; }

    /// <summary>Whether metadata was manually edited (protects from auto-overwrite)</summary>
    public bool ManuallyEdited { get; set; }

    public bool IsFavourite { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public DateTime LastModified { get; set; }
    public DateTime? LastPlayed { get; set; }
    public int PlayCount { get; set; }
}

/// <summary>
/// Tracks progress of an ongoing videos scan.
/// </summary>
public class VideoScanProgress
{
    public string Status { get; set; } = "idle";
    public string Message { get; set; } = "";
    public DateTime? StartTime { get; set; }
    public int TotalFiles { get; set; }

    internal int _processedFiles;
    internal int _newVideos;
    internal int _updatedVideos;
    internal int _errorCount;

    public int ProcessedFiles => _processedFiles;
    public int NewVideos => _newVideos;
    public int UpdatedVideos => _updatedVideos;
    public int ErrorCount => _errorCount;

    public double PercentComplete => TotalFiles > 0
        ? Math.Round((double)_processedFiles / TotalFiles * 100, 1) : 0;
}
