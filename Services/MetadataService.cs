using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NexusM.Data;
using NexusM.Models;

namespace NexusM.Services;

/// <summary>
/// Fetches movie/TV metadata from external providers (TVMaze, TMDB).
/// Handles rate limiting, image caching, and series-level deduplication.
/// </summary>
public class MetadataService
{
    private readonly ILogger<MetadataService> _logger;
    private readonly ConfigService _config;
    private readonly IServiceProvider _services;
    private readonly HttpClient _http;

    // Rate limiting
    private readonly SemaphoreSlim _tvmazeLimiter = new(1, 1);
    private readonly SemaphoreSlim _tmdbLimiter = new(1, 1);
    private DateTime _lastTvmazeCall = DateTime.MinValue;
    private DateTime _lastTmdbCall = DateTime.MinValue;

    // Series-level cache to avoid duplicate lookups during a scan
    private readonly ConcurrentDictionary<string, MetadataResult?> _seriesCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string _metaDir = Path.Combine(AppContext.BaseDirectory, "assets", "videometa");
    private static readonly string _actorsDir = Path.Combine(AppContext.BaseDirectory, "assets", "actors");

    private bool _isFetching;
    public bool IsFetching => _isFetching;

    public MetadataService(ILogger<MetadataService> logger, ConfigService config, IServiceProvider services)
    {
        _logger = logger;
        _config = config;
        _services = services;
        _http = new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(15);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NexusM/1.0");
    }

    /// <summary>
    /// Enrich all un-fetched videos in the library with external metadata.
    /// Called after file scanning or manually via API.
    /// </summary>
    public async Task EnrichLibraryAsync(VideoScanProgress? progress)
    {
        if (_isFetching) return;
        _isFetching = true;
        _seriesCache.Clear();

        try
        {
            var provider = _config.Config.Metadata.Provider.ToLowerInvariant();
            if (provider == "none") return;

            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VideosDbContext>();

            var unfetched = await db.Videos
                .Where(v => !v.MetadataFetched)
                .OrderBy(v => v.MediaType) // TV first, then movies
                .ThenBy(v => v.SeriesName)
                .ThenBy(v => v.Title)
                .ToListAsync();

            if (unfetched.Count == 0)
            {
                _logger.LogInformation("All videos already have metadata");
                return;
            }

            _logger.LogInformation("Fetching metadata for {Count} videos...", unfetched.Count);

            if (progress != null)
            {
                progress.Message = $"Fetching metadata for {unfetched.Count} items...";
                progress.TotalFiles = unfetched.Count;
                progress._processedFiles = 0;
            }

            var processed = 0;
            foreach (var video in unfetched)
            {
                try
                {
                    await FetchForVideoAsync(video, db, false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch metadata for {Title}", video.Title);
                    video.MetadataFetched = true; // Mark as fetched to avoid retrying on every scan
                }

                processed++;
                if (progress != null)
                {
                    Interlocked.Exchange(ref progress._processedFiles, processed);
                    progress.Message = $"Fetching metadata... ({processed}/{unfetched.Count})";
                }
            }

            await db.SaveChangesAsync();
            _logger.LogInformation("Metadata enrichment complete. Processed {Count} videos", processed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during metadata enrichment");
        }
        finally
        {
            _isFetching = false;
        }
    }

    /// <summary>
    /// Populate actors database from existing videos that have TMDB metadata.
    /// Re-fetches cast from TMDB for each video with a TmdbId and creates actor records.
    /// </summary>
    public async Task<int> PopulateActorsAsync()
    {
        if (_isFetching) return 0;
        _isFetching = true;

        var processed = 0;
        try
        {
            var provider = _config.Config.Metadata.Provider.ToLowerInvariant();
            var hasTmdbKey = !string.IsNullOrWhiteSpace(_config.Config.Metadata.TmdbApiKey);
            if (!hasTmdbKey) return 0;

            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VideosDbContext>();

            // Get all videos that have a TMDB ID (movies and TV shows)
            var videos = await db.Videos
                .Where(v => v.TmdbId != null && v.TmdbId != "")
                .ToListAsync();

            _logger.LogInformation("Populating actors from {Count} videos with TMDB IDs...", videos.Count);

            foreach (var video in videos)
            {
                try
                {
                    var key = _config.Config.Metadata.TmdbApiKey;
                    string? detailJson = null;

                    if (video.MediaType == "movie")
                    {
                        var detailUrl = $"https://api.themoviedb.org/3/movie/{video.TmdbId}?api_key={key}&append_to_response=credits";
                        detailJson = await TmdbGetAsync(detailUrl);
                    }
                    else if (video.MediaType == "tv" && !string.IsNullOrEmpty(video.SeriesName))
                    {
                        var detailUrl = $"https://api.themoviedb.org/3/tv/{video.TmdbId}?api_key={key}&append_to_response=credits";
                        detailJson = await TmdbGetAsync(detailUrl);
                    }

                    if (detailJson == null) continue;

                    using var doc = JsonDocument.Parse(detailJson);
                    var detail = doc.RootElement;

                    if (!detail.TryGetProperty("credits", out var credits)) continue;
                    if (!credits.TryGetProperty("cast", out var castArr) || castArr.ValueKind != JsonValueKind.Array) continue;

                    var castDetails = new List<CastMember>();
                    foreach (var member in castArr.EnumerateArray().Take(10))
                    {
                        var name = GetString(member, "name");
                        var character = GetString(member, "character");
                        var profilePath = GetString(member, "profile_path");
                        string? photoUrl = null;
                        if (!string.IsNullOrEmpty(profilePath))
                            photoUrl = $"https://image.tmdb.org/t/p/w200{profilePath}";

                        var tmdbPersonId = member.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
                            ? (int?)idEl.GetInt32() : null;
                        var popularity = member.TryGetProperty("popularity", out var popEl) && popEl.ValueKind == JsonValueKind.Number
                            ? (double?)popEl.GetDouble() : null;
                        var knownFor = GetString(member, "known_for_department");
                        var order = member.TryGetProperty("order", out var ordEl) && ordEl.ValueKind == JsonValueKind.Number
                            ? ordEl.GetInt32() : 999;

                        if (!string.IsNullOrEmpty(name))
                        {
                            castDetails.Add(new CastMember
                            {
                                Name = name,
                                Character = character,
                                PhotoUrl = photoUrl,
                                TmdbId = tmdbPersonId,
                                Popularity = popularity,
                                KnownForDepartment = string.IsNullOrEmpty(knownFor) ? "Acting" : knownFor,
                                BillingOrder = order
                            });
                        }
                    }

                    if (castDetails.Count == 0) continue;

                    // Build a MetadataResult with just cast to reuse ApplyMetadataAsync logic
                    var result = new MetadataResult { CastDetails = castDetails };

                    // Always clear CastJson so ApplyMetadataAsync re-upserts actors into the DB.
                    // (handles the case where actors.db was reset but videos.db still has old actorId data)
                    video.CastJson = null;
                    await ApplyMetadataAsync(video, result);
                    await db.SaveChangesAsync();

                    processed++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to populate actors for video {Title}", video.Title);
                }
            }

            _logger.LogInformation("Actor population complete. Processed {Count} videos", processed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during actor population");
        }
        finally
        {
            _isFetching = false;
        }

        return processed;
    }

    /// <summary>
    /// Fetch metadata for a single video (public API for manual refresh).
    /// </summary>
    public async Task FetchForVideoAsync(Video video, bool forceRefresh)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VideosDbContext>();

        var dbVideo = await db.Videos.FindAsync(video.Id);
        if (dbVideo == null) return;

        if (forceRefresh)
            dbVideo.MetadataFetched = false;

        await FetchForVideoAsync(dbVideo, db, forceRefresh);
        await db.SaveChangesAsync();
    }

    private async Task FetchForVideoAsync(Video video, VideosDbContext db, bool forceRefresh)
    {
        if (video.MetadataFetched && !forceRefresh) return;
        if (video.ManuallyEdited && !forceRefresh) return;

        var provider = _config.Config.Metadata.Provider.ToLowerInvariant();
        var hasTmdbKey = !string.IsNullOrWhiteSpace(_config.Config.Metadata.TmdbApiKey);

        MetadataResult? result = null;

        if (video.MediaType == "tv" && !string.IsNullOrEmpty(video.SeriesName))
        {
            // For TV: check series-level cache first
            var cacheKey = video.SeriesName.Trim();
            if (!forceRefresh && _seriesCache.TryGetValue(cacheKey, out var cached))
            {
                result = cached;
            }
            else
            {
                if (provider == "tmdb" && hasTmdbKey)
                    result = await SearchTmdbTvAsync(video.SeriesName, video.Year);
                else if (provider == "tvmaze" || (provider == "tmdb" && !hasTmdbKey))
                    result = await SearchTvMazeAsync(video.SeriesName);

                _seriesCache[cacheKey] = result;
            }
        }
        else if (video.MediaType == "movie")
        {
            if (hasTmdbKey && (provider == "tmdb" || provider == "tvmaze"))
                result = await SearchTmdbMovieAsync(video.Title, video.Year);
            // No free provider for movies without TMDB key
        }

        if (result != null)
            await ApplyMetadataAsync(video, result);

        video.MetadataFetched = true;
    }

    private async Task ApplyMetadataAsync(Video video, MetadataResult result)
    {
        if (!string.IsNullOrEmpty(result.Overview) && string.IsNullOrEmpty(video.Overview))
            video.Overview = result.Overview;
        if (!string.IsNullOrEmpty(result.Genre) && string.IsNullOrEmpty(video.Genre))
            video.Genre = result.Genre;
        if (!string.IsNullOrEmpty(result.Director) && string.IsNullOrEmpty(video.Director))
            video.Director = result.Director;
        if (!string.IsNullOrEmpty(result.Cast) && string.IsNullOrEmpty(video.Cast))
            video.Cast = result.Cast;
        if (result.Rating > 0 && video.Rating == 0)
            video.Rating = result.Rating;
        if (!string.IsNullOrEmpty(result.ContentRating) && string.IsNullOrEmpty(video.ContentRating))
            video.ContentRating = result.ContentRating;

        video.TmdbId = result.TmdbId ?? video.TmdbId;
        video.TvMazeId = result.TvMazeId ?? video.TvMazeId;
        video.ImdbId = result.ImdbId ?? video.ImdbId;

        // Download and cache images
        if (!string.IsNullOrEmpty(result.PosterUrl) && string.IsNullOrEmpty(video.PosterPath))
        {
            var posterFile = await CacheImageAsync(result.PosterUrl, result.PosterFilename!);
            if (posterFile != null) video.PosterPath = posterFile;
        }

        if (!string.IsNullOrEmpty(result.BackdropUrl) && string.IsNullOrEmpty(video.BackdropPath))
        {
            var backdropFile = await CacheImageAsync(result.BackdropUrl, result.BackdropFilename!);
            if (backdropFile != null) video.BackdropPath = backdropFile;
        }

        // Cast with photos + actor upsert
        // Also re-process if CastJson exists but lacks actorId (pre-actors-feature data)
        bool needsCastUpdate = string.IsNullOrEmpty(video.CastJson);
        if (!needsCastUpdate && result.CastDetails.Count > 0 && !string.IsNullOrEmpty(video.CastJson))
        {
            try
            {
                using var checkDoc = JsonDocument.Parse(video.CastJson);
                var arr = checkDoc.RootElement;
                if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
                    needsCastUpdate = !arr[0].TryGetProperty("actorId", out _);
            }
            catch { needsCastUpdate = true; }
        }
        if (result.CastDetails.Count > 0 && needsCastUpdate)
        {
            var fetchPhotos = _config.Config.Metadata.FetchCastPhotos;
            var castList = new List<object>();

            // Get ActorsDbContext for upserting
            ActorsDbContext? actorsDb = null;
            try
            {
                using var actorScope = _services.CreateScope();
                actorsDb = actorScope.ServiceProvider.GetRequiredService<ActorsDbContext>();

                foreach (var c in result.CastDetails.Take(10))
                {
                    string? localPhoto = null;
                    if (fetchPhotos && !string.IsNullOrEmpty(c.PhotoUrl))
                    {
                        var hash = Md5Hash(c.PhotoUrl);
                        localPhoto = await CacheImageAsync(c.PhotoUrl, $"cast_{hash}.jpg");
                    }

                    int? actorId = null;

                    // Upsert actor if we have a TMDB person ID
                    if (c.TmdbId.HasValue)
                    {
                        var actor = await actorsDb.Actors.FirstOrDefaultAsync(a => a.TmdbId == c.TmdbId.Value);
                        if (actor == null)
                        {
                            actor = new Actor
                            {
                                Name = c.Name,
                                NormalizedName = NormalizeName(c.Name),
                                TmdbId = c.TmdbId.Value,
                                ProfilePath = c.PhotoUrl,
                                KnownForDepartment = c.KnownForDepartment,
                                Popularity = c.Popularity,
                                LastUpdated = DateTime.UtcNow
                            };
                            actorsDb.Actors.Add(actor);
                            await actorsDb.SaveChangesAsync();
                        }
                        else
                        {
                            // Update popularity if changed
                            if (c.Popularity.HasValue && c.Popularity != actor.Popularity)
                            {
                                actor.Popularity = c.Popularity;
                                actor.LastUpdated = DateTime.UtcNow;
                            }
                        }

                        actorId = actor.Id;

                        // Download actor headshot to assets/actors/
                        if (!string.IsNullOrEmpty(c.PhotoUrl))
                        {
                            var actorPhotoFile = $"actor_{c.TmdbId.Value}.jpg";
                            var actorPhotoPath = Path.Combine(_actorsDir, actorPhotoFile);
                            if (!File.Exists(actorPhotoPath))
                            {
                                await CacheActorImageAsync(c.PhotoUrl, actorPhotoFile);
                            }
                            if (string.IsNullOrEmpty(actor.ImageCached) && File.Exists(actorPhotoPath))
                            {
                                actor.ImageCached = actorPhotoFile;
                            }
                        }

                        // Link actor to video
                        if (video.Id > 0)
                        {
                            var linkExists = await actorsDb.MovieActors
                                .AnyAsync(ma => ma.VideoId == video.Id && ma.ActorId == actor.Id);
                            if (!linkExists)
                            {
                                actorsDb.MovieActors.Add(new MovieActor
                                {
                                    VideoId = video.Id,
                                    ActorId = actor.Id,
                                    CharacterName = c.Character,
                                    BillingOrder = c.BillingOrder
                                });
                            }
                        }
                    }

                    castList.Add(new { name = c.Name, character = c.Character, photo = localPhoto ?? "", actorId = actorId });
                }

                await actorsDb.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to upsert actors for video {Title}", video.Title);
                // Fallback: build castList without actorIds if actors DB failed
                if (castList.Count == 0)
                {
                    foreach (var c in result.CastDetails.Take(10))
                    {
                        string? localPhoto = null;
                        if (fetchPhotos && !string.IsNullOrEmpty(c.PhotoUrl))
                        {
                            var hash = Md5Hash(c.PhotoUrl);
                            localPhoto = await CacheImageAsync(c.PhotoUrl, $"cast_{hash}.jpg");
                        }
                        castList.Add(new { name = c.Name, character = c.Character, photo = localPhoto ?? "", actorId = (int?)null });
                    }
                }
            }

            video.CastJson = JsonSerializer.Serialize(castList);
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  TVMaze Provider
    // ──────────────────────────────────────────────────────────────

    private async Task<MetadataResult?> SearchTvMazeAsync(string seriesName)
    {
        try
        {
            // Search for the show
            var searchUrl = $"https://api.tvmaze.com/search/shows?q={Uri.EscapeDataString(seriesName)}";
            var searchJson = await TvMazeGetAsync(searchUrl);
            if (searchJson == null) return null;

            using var searchDoc = JsonDocument.Parse(searchJson);
            var results = searchDoc.RootElement;
            if (results.GetArrayLength() == 0) return null;

            // Find best match
            JsonElement? bestShow = null;
            double bestScore = -1;
            var normalizedName = NormalizeName(seriesName);

            foreach (var item in results.EnumerateArray())
            {
                var show = item.GetProperty("show");
                var showName = show.GetProperty("name").GetString() ?? "";
                var score = item.GetProperty("score").GetDouble();

                // Exact match gets priority
                if (NormalizeName(showName) == normalizedName)
                {
                    bestShow = show;
                    break;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestShow = show;
                }
            }

            if (bestShow == null) return null;
            var showElement = bestShow.Value;

            var showId = showElement.GetProperty("id").GetInt32();
            _logger.LogDebug("TVMaze matched '{Series}' -> ID {Id}", seriesName, showId);

            // Fetch full details with cast and images
            var detailUrl = $"https://api.tvmaze.com/shows/{showId}?embed[]=cast&embed[]=images";
            var detailJson = await TvMazeGetAsync(detailUrl);
            if (detailJson == null) return null;

            using var detailDoc = JsonDocument.Parse(detailJson);
            var detail = detailDoc.RootElement;

            var result = new MetadataResult
            {
                TvMazeId = showId.ToString(),
                Overview = StripHtml(GetString(detail, "summary")),
                Rating = GetNestedDouble(detail, "rating", "average"),
            };

            // Genres
            if (detail.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
            {
                var genreList = new List<string>();
                foreach (var g in genres.EnumerateArray())
                    genreList.Add(g.GetString() ?? "");
                result.Genre = string.Join(", ", genreList.Where(g => !string.IsNullOrEmpty(g)));
            }

            // Content rating from network country
            if (detail.TryGetProperty("rating", out var ratingObj) &&
                ratingObj.TryGetProperty("average", out var avg) && avg.ValueKind == JsonValueKind.Number)
            {
                result.Rating = avg.GetDouble();
            }

            // Images - find poster and background
            if (detail.TryGetProperty("_embedded", out var embedded))
            {
                // Cast
                if (embedded.TryGetProperty("cast", out var castArr) && castArr.ValueKind == JsonValueKind.Array)
                {
                    var castNames = new List<string>();
                    foreach (var member in castArr.EnumerateArray().Take(10))
                    {
                        var person = member.GetProperty("person");
                        var character = member.GetProperty("character");
                        var name = GetString(person, "name");
                        var charName = GetString(character, "name");
                        var photoUrl = GetNestedString(person, "image", "medium");

                        if (!string.IsNullOrEmpty(name))
                        {
                            castNames.Add(name);
                            result.CastDetails.Add(new CastMember
                            {
                                Name = name,
                                Character = charName,
                                PhotoUrl = photoUrl
                            });
                        }
                    }
                    result.Cast = string.Join(", ", castNames);
                }

                // Images
                if (embedded.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
                {
                    foreach (var img in images.EnumerateArray())
                    {
                        var type = GetString(img, "type");
                        var origUrl = GetNestedString(img, "resolutions", "original", "url");
                        var medUrl = GetNestedString(img, "resolutions", "medium", "url");
                        var url = origUrl ?? medUrl;

                        if (type == "poster" && result.PosterUrl == null && url != null)
                        {
                            result.PosterUrl = url;
                            result.PosterFilename = $"poster_tvmaze_{showId}.jpg";
                        }
                        else if (type == "background" && result.BackdropUrl == null && url != null)
                        {
                            result.BackdropUrl = url;
                            result.BackdropFilename = $"backdrop_tvmaze_{showId}.jpg";
                        }
                    }
                }
            }

            // Fallback: use show.image if no poster from images endpoint
            if (result.PosterUrl == null)
            {
                var imgOriginal = GetNestedString(detail, "image", "original");
                if (imgOriginal != null)
                {
                    result.PosterUrl = imgOriginal;
                    result.PosterFilename = $"poster_tvmaze_{showId}.jpg";
                }
            }

            _logger.LogInformation("TVMaze: fetched metadata for '{Series}' - {Genres}, rating {Rating}",
                seriesName, result.Genre, result.Rating);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TVMaze search failed for '{Series}'", seriesName);
            return null;
        }
    }

    private async Task<string?> TvMazeGetAsync(string url)
    {
        await _tvmazeLimiter.WaitAsync();
        try
        {
            // Rate limit: 20 requests per 10 seconds -> 500ms between requests
            var elapsed = DateTime.UtcNow - _lastTvmazeCall;
            if (elapsed.TotalMilliseconds < 500)
                await Task.Delay(500 - (int)elapsed.TotalMilliseconds);

            var response = await _http.GetAsync(url);
            _lastTvmazeCall = DateTime.UtcNow;

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("TVMaze rate limited, waiting 10s...");
                await Task.Delay(10_000);
                response = await _http.GetAsync(url);
                _lastTvmazeCall = DateTime.UtcNow;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("TVMaze returned {Status} for {Url}", response.StatusCode, url);
                return null;
            }

            return await response.Content.ReadAsStringAsync();
        }
        finally
        {
            _tvmazeLimiter.Release();
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  TMDB Provider
    // ──────────────────────────────────────────────────────────────

    private async Task<MetadataResult?> SearchTmdbMovieAsync(string title, int? year)
    {
        try
        {
            var key = _config.Config.Metadata.TmdbApiKey;
            if (string.IsNullOrWhiteSpace(key)) return null;

            // Search
            var searchUrl = $"https://api.themoviedb.org/3/search/movie?api_key={key}&query={Uri.EscapeDataString(title)}";
            if (year.HasValue) searchUrl += $"&year={year.Value}";

            var searchJson = await TmdbGetAsync(searchUrl);
            if (searchJson == null) return null;

            int movieId;
            using (var searchDoc = JsonDocument.Parse(searchJson))
            {
                var results = searchDoc.RootElement.GetProperty("results");
                if (results.GetArrayLength() == 0)
                {
                    // Retry without year
                    if (year.HasValue)
                    {
                        searchUrl = $"https://api.themoviedb.org/3/search/movie?api_key={key}&query={Uri.EscapeDataString(title)}";
                        searchJson = await TmdbGetAsync(searchUrl);
                        if (searchJson == null) return null;
                        using var retryDoc = JsonDocument.Parse(searchJson);
                        var retryResults = retryDoc.RootElement.GetProperty("results");
                        if (retryResults.GetArrayLength() == 0) return null;
                        return await FetchTmdbMovieDetails(retryResults[0].GetProperty("id").GetInt32(), key, title);
                    }
                    return null;
                }
                movieId = results[0].GetProperty("id").GetInt32();
            }
            return await FetchTmdbMovieDetails(movieId, key, title);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TMDB movie search failed for '{Title}'", title);
            return null;
        }
    }

    private async Task<MetadataResult?> FetchTmdbMovieDetails(int movieId, string key, string title)
    {
        var detailUrl = $"https://api.themoviedb.org/3/movie/{movieId}?api_key={key}&append_to_response=credits,release_dates";
        var detailJson = await TmdbGetAsync(detailUrl);
        if (detailJson == null) return null;

        using var doc = JsonDocument.Parse(detailJson);
        var detail = doc.RootElement;

        var result = new MetadataResult
        {
            TmdbId = movieId.ToString(),
            Overview = GetString(detail, "overview"),
            Rating = GetDouble(detail, "vote_average"),
        };

        // IMDb ID
        result.ImdbId = GetString(detail, "imdb_id");

        // Genres
        if (detail.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
        {
            var genreList = genres.EnumerateArray()
                .Select(g => GetString(g, "name"))
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();
            result.Genre = string.Join(", ", genreList);
        }

        // Poster & backdrop
        var posterPath = GetString(detail, "poster_path");
        if (!string.IsNullOrEmpty(posterPath))
        {
            result.PosterUrl = $"https://image.tmdb.org/t/p/w500{posterPath}";
            result.PosterFilename = $"poster_tmdb_{movieId}.jpg";
        }

        var backdropPath = GetString(detail, "backdrop_path");
        if (!string.IsNullOrEmpty(backdropPath))
        {
            result.BackdropUrl = $"https://image.tmdb.org/t/p/w780{backdropPath}";
            result.BackdropFilename = $"backdrop_tmdb_{movieId}.jpg";
        }

        // Credits
        if (detail.TryGetProperty("credits", out var credits))
        {
            // Director
            if (credits.TryGetProperty("crew", out var crew) && crew.ValueKind == JsonValueKind.Array)
            {
                var directors = crew.EnumerateArray()
                    .Where(c => GetString(c, "job") == "Director")
                    .Select(c => GetString(c, "name"))
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();
                result.Director = string.Join(", ", directors);
            }

            // Cast
            if (credits.TryGetProperty("cast", out var castArr) && castArr.ValueKind == JsonValueKind.Array)
            {
                var castNames = new List<string>();
                foreach (var member in castArr.EnumerateArray().Take(10))
                {
                    var name = GetString(member, "name");
                    var character = GetString(member, "character");
                    var profilePath = GetString(member, "profile_path");
                    string? photoUrl = null;
                    if (!string.IsNullOrEmpty(profilePath))
                        photoUrl = $"https://image.tmdb.org/t/p/w200{profilePath}";

                    var tmdbPersonId = member.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
                        ? (int?)idEl.GetInt32() : null;
                    var popularity = member.TryGetProperty("popularity", out var popEl) && popEl.ValueKind == JsonValueKind.Number
                        ? (double?)popEl.GetDouble() : null;
                    var knownFor = GetString(member, "known_for_department");
                    var order = member.TryGetProperty("order", out var ordEl) && ordEl.ValueKind == JsonValueKind.Number
                        ? ordEl.GetInt32() : 999;

                    if (!string.IsNullOrEmpty(name))
                    {
                        castNames.Add(name);
                        result.CastDetails.Add(new CastMember
                        {
                            Name = name,
                            Character = character,
                            PhotoUrl = photoUrl,
                            TmdbId = tmdbPersonId,
                            Popularity = popularity,
                            KnownForDepartment = string.IsNullOrEmpty(knownFor) ? "Acting" : knownFor,
                            BillingOrder = order
                        });
                    }
                }
                result.Cast = string.Join(", ", castNames);
            }
        }

        // Content rating from release dates (US)
        if (detail.TryGetProperty("release_dates", out var relDates) &&
            relDates.TryGetProperty("results", out var rdResults) &&
            rdResults.ValueKind == JsonValueKind.Array)
        {
            foreach (var country in rdResults.EnumerateArray())
            {
                if (GetString(country, "iso_3166_1") == "US" &&
                    country.TryGetProperty("release_dates", out var dates) &&
                    dates.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d in dates.EnumerateArray())
                    {
                        var cert = GetString(d, "certification");
                        if (!string.IsNullOrEmpty(cert))
                        {
                            result.ContentRating = cert;
                            break;
                        }
                    }
                    break;
                }
            }
        }

        _logger.LogInformation("TMDB: fetched movie metadata for '{Title}' (ID {Id})", title, movieId);
        return result;
    }

    private async Task<MetadataResult?> SearchTmdbTvAsync(string seriesName, int? year)
    {
        try
        {
            var key = _config.Config.Metadata.TmdbApiKey;
            if (string.IsNullOrWhiteSpace(key)) return null;

            // Search
            var searchUrl = $"https://api.themoviedb.org/3/search/tv?api_key={key}&query={Uri.EscapeDataString(seriesName)}";
            if (year.HasValue) searchUrl += $"&first_air_date_year={year.Value}";

            var searchJson = await TmdbGetAsync(searchUrl);
            if (searchJson == null) return null;

            int tvId;
            using (var searchDoc = JsonDocument.Parse(searchJson))
            {
                var results = searchDoc.RootElement.GetProperty("results");
                if (results.GetArrayLength() == 0)
                {
                    // Retry without year
                    if (year.HasValue)
                    {
                        searchUrl = $"https://api.themoviedb.org/3/search/tv?api_key={key}&query={Uri.EscapeDataString(seriesName)}";
                        searchJson = await TmdbGetAsync(searchUrl);
                        if (searchJson == null) return null;
                        using var retryDoc = JsonDocument.Parse(searchJson);
                        var retryResults = retryDoc.RootElement.GetProperty("results");
                        if (retryResults.GetArrayLength() == 0) return null;
                        return await FetchTmdbTvDetails(retryResults[0].GetProperty("id").GetInt32(), key, seriesName);
                    }
                    return null;
                }
                tvId = results[0].GetProperty("id").GetInt32();
            }
            return await FetchTmdbTvDetails(tvId, key, seriesName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TMDB TV search failed for '{Series}'", seriesName);
            return null;
        }
    }

    private async Task<MetadataResult?> FetchTmdbTvDetails(int tvId, string key, string seriesName)
    {
        var detailUrl = $"https://api.themoviedb.org/3/tv/{tvId}?api_key={key}&append_to_response=credits,content_ratings";
        var detailJson = await TmdbGetAsync(detailUrl);
        if (detailJson == null) return null;

        using var doc = JsonDocument.Parse(detailJson);
        var detail = doc.RootElement;

        var result = new MetadataResult
        {
            TmdbId = tvId.ToString(),
            Overview = GetString(detail, "overview"),
            Rating = GetDouble(detail, "vote_average"),
        };

        // Genres
        if (detail.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
        {
            var genreList = genres.EnumerateArray()
                .Select(g => GetString(g, "name"))
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();
            result.Genre = string.Join(", ", genreList);
        }

        // Poster & backdrop
        var posterPath = GetString(detail, "poster_path");
        if (!string.IsNullOrEmpty(posterPath))
        {
            result.PosterUrl = $"https://image.tmdb.org/t/p/w500{posterPath}";
            result.PosterFilename = $"poster_tmdb_{tvId}.jpg";
        }

        var backdropPath = GetString(detail, "backdrop_path");
        if (!string.IsNullOrEmpty(backdropPath))
        {
            result.BackdropUrl = $"https://image.tmdb.org/t/p/w780{backdropPath}";
            result.BackdropFilename = $"backdrop_tmdb_{tvId}.jpg";
        }

        // Credits
        if (detail.TryGetProperty("credits", out var credits))
        {
            // Creator/Director
            if (detail.TryGetProperty("created_by", out var creators) && creators.ValueKind == JsonValueKind.Array)
            {
                var creatorNames = creators.EnumerateArray()
                    .Select(c => GetString(c, "name"))
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();
                result.Director = string.Join(", ", creatorNames);
            }

            // Cast
            if (credits.TryGetProperty("cast", out var castArr) && castArr.ValueKind == JsonValueKind.Array)
            {
                var castNames = new List<string>();
                foreach (var member in castArr.EnumerateArray().Take(10))
                {
                    var name = GetString(member, "name");
                    var character = GetString(member, "character");
                    var profilePath = GetString(member, "profile_path");
                    string? photoUrl = null;
                    if (!string.IsNullOrEmpty(profilePath))
                        photoUrl = $"https://image.tmdb.org/t/p/w200{profilePath}";

                    var tmdbPersonId = member.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
                        ? (int?)idEl.GetInt32() : null;
                    var popularity = member.TryGetProperty("popularity", out var popEl) && popEl.ValueKind == JsonValueKind.Number
                        ? (double?)popEl.GetDouble() : null;
                    var knownFor = GetString(member, "known_for_department");
                    var order = member.TryGetProperty("order", out var ordEl) && ordEl.ValueKind == JsonValueKind.Number
                        ? ordEl.GetInt32() : 999;

                    if (!string.IsNullOrEmpty(name))
                    {
                        castNames.Add(name);
                        result.CastDetails.Add(new CastMember
                        {
                            Name = name,
                            Character = character,
                            PhotoUrl = photoUrl,
                            TmdbId = tmdbPersonId,
                            Popularity = popularity,
                            KnownForDepartment = string.IsNullOrEmpty(knownFor) ? "Acting" : knownFor,
                            BillingOrder = order
                        });
                    }
                }
                result.Cast = string.Join(", ", castNames);
            }
        }

        // Content rating (US)
        if (detail.TryGetProperty("content_ratings", out var cr) &&
            cr.TryGetProperty("results", out var crResults) &&
            crResults.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in crResults.EnumerateArray())
            {
                if (GetString(entry, "iso_3166_1") == "US")
                {
                    result.ContentRating = GetString(entry, "rating");
                    break;
                }
            }
        }

        _logger.LogInformation("TMDB: fetched TV metadata for '{Series}' (ID {Id})", seriesName, tvId);
        return result;
    }

    private async Task<string?> TmdbGetAsync(string url)
    {
        await _tmdbLimiter.WaitAsync();
        try
        {
            // Rate limit: ~40 req/s (safe margin under 50)
            var elapsed = DateTime.UtcNow - _lastTmdbCall;
            if (elapsed.TotalMilliseconds < 25)
                await Task.Delay(25 - (int)elapsed.TotalMilliseconds);

            var response = await _http.GetAsync(url);
            _lastTmdbCall = DateTime.UtcNow;

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("TMDB rate limited, waiting 2s...");
                await Task.Delay(2000);
                response = await _http.GetAsync(url);
                _lastTmdbCall = DateTime.UtcNow;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("TMDB returned {Status} for {Url}",
                    response.StatusCode, url.Replace(_config.Config.Metadata.TmdbApiKey, "***"));
                return null;
            }

            return await response.Content.ReadAsStringAsync();
        }
        finally
        {
            _tmdbLimiter.Release();
        }
    }

    /// <summary>
    /// Validate a TMDB API key by making a test request.
    /// </summary>
    public async Task<bool> ValidateTmdbKeyAsync(string apiKey)
    {
        try
        {
            var response = await _http.GetAsync(
                $"https://api.themoviedb.org/3/configuration?api_key={apiKey}");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Image caching
    // ──────────────────────────────────────────────────────────────

    private async Task<string?> CacheImageAsync(string url, string localFilename)
    {
        try
        {
            var path = Path.Combine(_metaDir, localFilename);
            if (File.Exists(path)) return localFilename; // Already cached

            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length < 500) return null; // Too small, likely an error

            await File.WriteAllBytesAsync(path, bytes);
            _logger.LogDebug("Cached image: {Filename} ({Size} bytes)", localFilename, bytes.Length);
            return localFilename;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to cache image from {Url}", url);
            return null;
        }
    }

    private async Task<string?> CacheActorImageAsync(string url, string localFilename)
    {
        try
        {
            var path = Path.Combine(_actorsDir, localFilename);
            if (File.Exists(path)) return localFilename;

            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length < 500) return null;

            await File.WriteAllBytesAsync(path, bytes);
            _logger.LogDebug("Cached actor image: {Filename} ({Size} bytes)", localFilename, bytes.Length);
            return localFilename;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to cache actor image from {Url}", url);
            return null;
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────

    private static string NormalizeName(string name) =>
        Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]", "");

    private static string StripHtml(string html) =>
        string.IsNullOrEmpty(html) ? "" : Regex.Replace(html, @"<[^>]+>", "").Trim();

    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double GetDouble(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static double GetNestedDouble(JsonElement el, string prop1, string prop2)
    {
        if (el.TryGetProperty(prop1, out var inner) && inner.ValueKind == JsonValueKind.Object)
            return GetDouble(inner, prop2);
        return 0;
    }

    private static string? GetNestedString(JsonElement el, string prop1, string prop2)
    {
        if (el.TryGetProperty(prop1, out var inner) && inner.ValueKind == JsonValueKind.Object)
        {
            var val = GetString(inner, prop2);
            return string.IsNullOrEmpty(val) ? null : val;
        }
        return null;
    }

    private static string? GetNestedString(JsonElement el, string prop1, string prop2, string prop3)
    {
        if (el.TryGetProperty(prop1, out var inner) && inner.ValueKind == JsonValueKind.Object)
            return GetNestedString(inner, prop2, prop3);
        return null;
    }

    private static string Md5Hash(string input)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

// ──────────────────────────────────────────────────────────────
//  DTOs
// ──────────────────────────────────────────────────────────────

internal class MetadataResult
{
    public string? TmdbId { get; set; }
    public string? TvMazeId { get; set; }
    public string? ImdbId { get; set; }
    public string Overview { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Director { get; set; } = "";
    public string Cast { get; set; } = "";
    public double Rating { get; set; }
    public string ContentRating { get; set; } = "";
    public string? PosterUrl { get; set; }
    public string? PosterFilename { get; set; }
    public string? BackdropUrl { get; set; }
    public string? BackdropFilename { get; set; }
    public List<CastMember> CastDetails { get; set; } = new();
}

internal class CastMember
{
    public string Name { get; set; } = "";
    public string Character { get; set; } = "";
    public string? PhotoUrl { get; set; }
    public int? TmdbId { get; set; }
    public double? Popularity { get; set; }
    public string KnownForDepartment { get; set; } = "Acting";
    public int BillingOrder { get; set; } = 999;
}
