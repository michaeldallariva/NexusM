using System.Text.Json.Serialization;

namespace NexusM.Models;

// ─── Tenrai REST API DTOs (Jikan v4-compatible schema) ───────────────────────
// API host: https://api.tenrai.org/v1  (docs at tenrai.org; free, no API key, 4 req/s / 120 req/min).
// Drop-in successor to the decommissioned Jikan v4 API; class names kept as
// "Jikan*" since the JSON schema is identical, so these DTOs bind unchanged.

public class JikanResponse<T>
{
    [JsonPropertyName("data")] public T? Data { get; set; }
    [JsonPropertyName("pagination")] public JikanPagination? Pagination { get; set; }
}

public class JikanPagination
{
    [JsonPropertyName("last_visible_page")] public int LastVisiblePage { get; set; }
    [JsonPropertyName("has_next_page")] public bool HasNextPage { get; set; }
    [JsonPropertyName("items")] public JikanPaginationItems? Items { get; set; }
}

public class JikanPaginationItems
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("total")] public int Total { get; set; }
}

public class JikanAnimeEntry
{
    [JsonPropertyName("mal_id")] public int MalId { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("images")] public JikanImages? Images { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("title_english")] public string? TitleEnglish { get; set; }
    [JsonPropertyName("title_japanese")] public string? TitleJapanese { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("episodes")] public int? Episodes { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("airing")] public bool Airing { get; set; }
    [JsonPropertyName("duration")] public string? Duration { get; set; }
    [JsonPropertyName("rating")] public string? Rating { get; set; }
    [JsonPropertyName("score")] public double? Score { get; set; }
    [JsonPropertyName("scored_by")] public int? ScoredBy { get; set; }
    [JsonPropertyName("rank")] public int? Rank { get; set; }
    [JsonPropertyName("popularity")] public int? Popularity { get; set; }
    [JsonPropertyName("synopsis")] public string? Synopsis { get; set; }
    [JsonPropertyName("background")] public string? Background { get; set; }
    [JsonPropertyName("season")] public string? Season { get; set; }
    [JsonPropertyName("year")] public int? Year { get; set; }
    [JsonPropertyName("genres")] public List<JikanNamedEntity>? Genres { get; set; }
    [JsonPropertyName("themes")] public List<JikanNamedEntity>? Themes { get; set; }
    [JsonPropertyName("studios")] public List<JikanNamedEntity>? Studios { get; set; }
    [JsonPropertyName("producers")] public List<JikanNamedEntity>? Producers { get; set; }
}

public class JikanImages
{
    [JsonPropertyName("jpg")] public JikanImageSet? Jpg { get; set; }
    [JsonPropertyName("webp")] public JikanImageSet? Webp { get; set; }
}

public class JikanImageSet
{
    [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
    [JsonPropertyName("small_image_url")] public string? SmallImageUrl { get; set; }
    [JsonPropertyName("large_image_url")] public string? LargeImageUrl { get; set; }
}

public class JikanNamedEntity
{
    [JsonPropertyName("mal_id")] public int MalId { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
}

public class JikanEpisode
{
    [JsonPropertyName("mal_id")] public int MalId { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("title_japanese")] public string? TitleJapanese { get; set; }
    [JsonPropertyName("title_romanji")] public string? TitleRomanji { get; set; }
    [JsonPropertyName("aired")] public string? Aired { get; set; }
    [JsonPropertyName("score")] public double? Score { get; set; }
    [JsonPropertyName("filler")] public bool Filler { get; set; }
    [JsonPropertyName("recap")] public bool Recap { get; set; }
}

public class JikanCharacterEntry
{
    [JsonPropertyName("character")] public JikanCharacter? Character { get; set; }
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("voice_actors")] public List<JikanVoiceActor>? VoiceActors { get; set; }
}

public class JikanCharacter
{
    [JsonPropertyName("mal_id")] public int MalId { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("images")] public JikanImages? Images { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public class JikanVoiceActor
{
    [JsonPropertyName("person")] public JikanPerson? Person { get; set; }
    [JsonPropertyName("language")] public string? Language { get; set; }
}

public class JikanPerson
{
    [JsonPropertyName("mal_id")] public int MalId { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("images")] public JikanImages? Images { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

// ─── Internal result object passed from service to scanner ───────────────────

public class AnimeMetadataResult
{
    public int MalId { get; set; }
    public string? Title { get; set; }
    public string? TitleEnglish { get; set; }
    public string? Synopsis { get; set; }
    public double? Score { get; set; }
    public int? Year { get; set; }
    public string? Season { get; set; }
    public string? Genres { get; set; }
    public string? Studios { get; set; }
    public string? Status { get; set; }
    public string? AgeRating { get; set; }
    public string? PosterUrl { get; set; }
    public string? PosterFilename { get; set; }
    public string? BackdropUrl { get; set; }
    public string? BackdropFilename { get; set; }
    public List<AnimeCharacterEntry> Characters { get; set; } = new();
    // Per-episode titles from Jikan /anime/{id}/episodes, keyed by episode number (1-based).
    public Dictionary<int, string> EpisodeTitles { get; set; } = new();
}

public class AnimeCharacterEntry
{
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string? PhotoUrl { get; set; }
    public string? LocalPhoto { get; set; }
}

/// <summary>
/// Tracks progress of an ongoing anime scan (mirrors VideoScanProgress).
/// </summary>
public class AnimeScanProgress
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

/// <summary>
/// Live progress for the "Re-fetch all anime metadata" background job. Progress is driven by
/// SERIES processed (each series is one rate-limited Jikan lookup - that's where the time goes),
/// with episode counters exposed as secondary detail for the UI.
/// </summary>
public class AnimeRefetchProgress
{
    public string Status { get; set; } = "idle"; // idle | running | completed | error
    public string Message { get; set; } = "";
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int TotalSeries { get; set; }
    public int TotalEpisodes { get; set; }
    public string CurrentSeries { get; set; } = "";

    internal int _processedSeries;
    internal int _matchedSeries;
    internal int _notFoundSeries;
    internal int _apiErrorSeries;
    internal int _processedEpisodes;
    internal int _updatedTitles;

    public int ProcessedSeries => _processedSeries;
    public int MatchedSeries => _matchedSeries;
    public int NotFoundSeries => _notFoundSeries;
    public int ApiErrorSeries => _apiErrorSeries;
    public int ProcessedEpisodes => _processedEpisodes;
    public int UpdatedTitles => _updatedTitles;

    public double PercentComplete => TotalSeries > 0
        ? Math.Round((double)_processedSeries / TotalSeries * 100, 1) : 0;
}
