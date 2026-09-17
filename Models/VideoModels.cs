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

    /// <summary>
    /// Edition / cut label distinguishing multiple owned copies of the same movie
    /// (e.g. "Extended Edition", "Theatrical Cut", "Director's Cut"). Auto-detected from
    /// the filename during scan and freely editable. Null/empty = unspecified.
    /// </summary>
    public string? Edition { get; set; }

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

    /// <summary>HDR format detected by ffprobe: "HDR10", "HDR10+", "Dolby Vision", "HLG", or empty (SDR)</summary>
    public string HdrFormat { get; set; } = "";

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

    public string? ImdbRating { get; set; }
    public string? RottenTomatoesRating { get; set; }
    public string? MetacriticRating { get; set; }

    /// <summary>"movie" or "tv"</summary>
    public string MediaType { get; set; } = "movie";

    /// <summary>
    /// Discriminator for special video items. "" = an ordinary single-file video;
    /// "dvd" = a preserved DVD-Video disc (an .iso image or a VIDEO_TS folder) kept intact
    /// so its original interactive menus play through libdvdnav in an external player (MPV).
    /// A DVD item keeps MediaType = "movie" so it lives under Movies, filterable via a
    /// "DVDs" tile. See <see cref="DvdDevicePath"/>.
    /// </summary>
    public string VideoKind { get; set; } = "";

    /// <summary>
    /// For a DVD item (<see cref="VideoKind"/> == "dvd"), the on-disk disc source MPV opens
    /// with `dvd:// --dvd-device=&lt;path&gt;`: either the .iso image or the folder that
    /// contains VIDEO_TS. Menu navigation needs this real path (libdvdnav cannot navigate an
    /// HTTP byte stream), so the MPV client must reach it locally or via a mounted share.
    /// Null/empty for ordinary videos. Distinct from <see cref="FilePath"/>, which for a DVD
    /// points at the representative VIDEO_TS entry used for identity/dedupe.
    /// </summary>
    public string? DvdDevicePath { get; set; }

    /// <summary>First-level subfolder name under the configured video root (e.g. "Kids Movies", "Kids Shows")</summary>
    public string CustomCategory { get; set; } = "";

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

    /// <summary>TheTVDB id - used for fanart.tv TV lookups; resolved on demand from TMDB/TVMaze and cached here.</summary>
    public string? TvdbId { get; set; }

    /// <summary>MyAnimeList ID (anime only, populated by AnimeMetadataService via Jikan)</summary>
    public string? MalId { get; set; }

    /// <summary>
    /// Per-video TMDB scrape-language override (e.g. "fr-FR"), set from the language
    /// selector on the detail page. Null = follow the server-wide
    /// [Metadata] ScrapeLanguage setting.
    /// </summary>
    public string? MetadataLanguage { get; set; }

    /// <summary>
    /// Language this video's text was LAST actually scraped in. Distinct from
    /// MetadataLanguage (the request): it records the outcome, so a re-fetch can tell
    /// whether the language really changed and text should be overwritten.
    /// </summary>
    public string? MetadataLanguageApplied { get; set; }

    // ── TMDB Collection (belongs_to_collection) ──
    /// <summary>TMDB collection ID, e.g. 86311 for "The Avengers Collection"</summary>
    public int? CollectionId { get; set; }

    /// <summary>TMDB collection name, e.g. "The Avengers Collection"</summary>
    public string? CollectionName { get; set; }

    /// <summary>Collection poster filename in assets/videometa/</summary>
    public string? CollectionPosterPath { get; set; }

    /// <summary>Total number of movies in the TMDB collection (used to determine if collection is complete locally)</summary>
    public int? CollectionTotalCount { get; set; }

    /// <summary>Poster image filename in assets/videometa/</summary>
    public string? PosterPath { get; set; }

    /// <summary>Backdrop/fanart image filename in assets/videometa/</summary>
    public string? BackdropPath { get; set; }

    /// <summary>JSON array of cast: [{name, character, photo}, ...]</summary>
    public string? CastJson { get; set; }

    /// <summary>JSON array of directors: [{name, photo}, ...]</summary>
    public string? DirectorJson { get; set; }

    /// <summary>JSON array of writers: [{name, photo}, ...]</summary>
    public string? WriterJson { get; set; }

    /// <summary>JSON array of production studios: [{name, logo}, ...]</summary>
    public string? StudiosJson { get; set; }

    /// <summary>Whether external metadata has been fetched for this video</summary>
    public bool MetadataFetched { get; set; }

    /// <summary>Whether the video is a compliant MP4 for direct browser streaming</summary>
    public bool Mp4Compliant { get; set; } = true;

    /// <summary>Whether the video needs optimization (non-MP4, surround audio, etc.)</summary>
    public bool NeedsOptimization { get; set; }

    /// <summary>Whether metadata was manually edited (protects from auto-overwrite)</summary>
    public bool ManuallyEdited { get; set; }

    public bool IsFavourite { get; set; }

    /// <summary>Explicitly marked safe for child users regardless of ContentRating.</summary>
    public bool SafeForChildren { get; set; } = false;

    /// <summary>Intro start time in seconds, detected via chapter tags (null = no intro detected).</summary>
    public double? IntroStart { get; set; }

    /// <summary>Intro end time in seconds, detected via chapter tags (null = no intro detected).</summary>
    public double? IntroEnd { get; set; }

    /// <summary>JSON array of embedded chapters [{title,start,end}] from ffprobe -show_chapters.
    /// null = not yet probed; "[]" = probed, no chapters found.</summary>
    public string? ChaptersJson { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public DateTime LastModified { get; set; }
    public DateTime? LastPlayed { get; set; }
    public int PlayCount { get; set; }

    // ── Deep Analysis (populated by MediaAnalysisService via ffprobe) ──

    /// <summary>Whether background deep ffprobe analysis has been completed for this file.</summary>
    public bool DeepAnalysisDone { get; set; }

    /// <summary>JSON array of all audio tracks: [{codec,language,channels,bitrateKbps}]</summary>
    public string? AudioTracks { get; set; }

    /// <summary>JSON array of all subtitle tracks: [{codec,language,forced}]</summary>
    public string? SubtitleTracks { get; set; }

    /// <summary>Video codec profile, e.g. "High", "Main 10", "Baseline"</summary>
    public string? VideoProfile { get; set; }

    /// <summary>Color primaries / space, e.g. "bt709", "bt2020nc"</summary>
    public string? ColorSpace { get; set; }

    /// <summary>Total container bitrate in kbps (from ffprobe format.bit_rate)</summary>
    public int? TotalBitrateKbps { get; set; }
}

/// <summary>A single embedded video chapter (start/end in seconds).</summary>
public class VideoChapter
{
    public string Title { get; set; } = "";
    public double Start { get; set; }
    public double End { get; set; }
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
