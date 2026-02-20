using System.Xml.Linq;
using NexusM.Data;
using NexusM.Models;
using Microsoft.EntityFrameworkCore;

namespace NexusM.Services;

/// <summary>
/// Handles RSS feed parsing, OPML import, artwork downloading, and episode refresh for Podcasts.
/// </summary>
public class PodcastService
{
    private readonly ILogger<PodcastService> _logger;
    private readonly HttpClient _http;

    private static readonly XNamespace Itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
    private static readonly XNamespace Content = "http://purl.org/rss/1.0/modules/content/";

    public PodcastService(ILogger<PodcastService> logger)
    {
        _logger = logger;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NexusM/1.0");
    }

    // ─── RSS Feed Parsing ──────────────────────────────────────────────

    /// <summary>
    /// Fetches and parses an RSS feed URL.
    /// Returns (feed metadata, list of episodes) or throws on failure.
    /// </summary>
    public async Task<(PodcastFeed Feed, List<PodcastEpisode> Episodes)> ParseRssFeedAsync(string url)
    {
        _logger.LogInformation("Fetching RSS feed: {Url}", url);

        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync();

        var doc = XDocument.Parse(xml);
        var channel = doc.Root?.Element("channel")
            ?? throw new InvalidDataException("RSS feed missing <channel> element.");

        var feed = new PodcastFeed
        {
            RssUrl = url,
            Title = channel.Element("title")?.Value.Trim() ?? "Untitled Podcast",
            Description = StripHtml(channel.Element("description")?.Value ?? channel.Element(Itunes + "summary")?.Value ?? ""),
            Author = channel.Element(Itunes + "author")?.Value.Trim()
                  ?? channel.Element("managingEditor")?.Value.Trim() ?? "",
            ArtworkUrl = channel.Element(Itunes + "image")?.Element("url")?.Value.Trim()
                      ?? channel.Element("image")?.Element("url")?.Value.Trim() ?? "",
            Category = channel.Element(Itunes + "category")?.Attribute("text")?.Value.Trim() ?? "",
            Language = channel.Element("language")?.Value.Trim() ?? "",
            LastRefreshed = DateTime.UtcNow,
            DateAdded = DateTime.UtcNow
        };

        var episodes = new List<PodcastEpisode>();
        foreach (var item in channel.Elements("item"))
        {
            var enclosure = item.Element("enclosure");
            var mediaUrl = enclosure?.Attribute("url")?.Value.Trim() ?? "";
            if (string.IsNullOrEmpty(mediaUrl))
                continue;

            var mimeType = enclosure?.Attribute("type")?.Value ?? "";
            var mediaType = mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ? "video" : "audio";

            var guid = item.Element("guid")?.Value.Trim()
                    ?? item.Element("link")?.Value.Trim()
                    ?? mediaUrl;

            var durationStr = item.Element(Itunes + "duration")?.Value.Trim() ?? "";
            var durationSecs = ParseDuration(durationStr);

            DateTime? pubDate = null;
            var pubDateStr = item.Element("pubDate")?.Value.Trim();
            if (!string.IsNullOrEmpty(pubDateStr) &&
                DateTime.TryParse(pubDateStr, out var parsedDate))
                pubDate = parsedDate.ToUniversalTime();

            episodes.Add(new PodcastEpisode
            {
                Title = item.Element("title")?.Value.Trim() ?? "Untitled Episode",
                Description = StripHtml(
                    item.Element(Content + "encoded")?.Value
                    ?? item.Element(Itunes + "summary")?.Value
                    ?? item.Element("description")?.Value ?? ""),
                MediaUrl = mediaUrl,
                MediaType = mediaType,
                DurationSeconds = durationSecs,
                PublishDate = pubDate,
                Guid = guid,
                DateFetched = DateTime.UtcNow
            });
        }

        feed.EpisodeCount = episodes.Count;
        return (feed, episodes);
    }

    // ─── OPML Parsing ──────────────────────────────────────────────────

    /// <summary>
    /// Parses an OPML file stream and returns all RSS feed URLs found.
    /// </summary>
    public List<string> ParseOpml(Stream stream)
    {
        var urls = new List<string>();
        try
        {
            var doc = XDocument.Load(stream);
            foreach (var outline in doc.Descendants("outline"))
            {
                var xmlUrl = outline.Attribute("xmlUrl")?.Value.Trim()
                          ?? outline.Attribute("xmlurl")?.Value.Trim();
                if (!string.IsNullOrEmpty(xmlUrl) && Uri.IsWellFormedUriString(xmlUrl, UriKind.Absolute))
                    urls.Add(xmlUrl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse OPML file");
        }
        return urls;
    }

    // ─── Feed Management ───────────────────────────────────────────────

    /// <summary>
    /// Subscribes to a new feed or refreshes an existing one.
    /// Upserts feed metadata and inserts new episodes (skips existing GUIDs).
    /// </summary>
    public async Task<PodcastFeed> AddOrRefreshFeedAsync(PodcastsDbContext db, string url)
    {
        url = url.Trim();
        var (parsedFeed, episodes) = await ParseRssFeedAsync(url);

        var existing = await db.Feeds.FirstOrDefaultAsync(f => f.RssUrl == url);
        PodcastFeed feed;

        if (existing != null)
        {
            existing.Title = parsedFeed.Title;
            existing.Description = parsedFeed.Description;
            existing.Author = parsedFeed.Author;
            existing.ArtworkUrl = parsedFeed.ArtworkUrl;
            existing.Category = parsedFeed.Category;
            existing.Language = parsedFeed.Language;
            existing.LastRefreshed = DateTime.UtcNow;
            feed = existing;
        }
        else
        {
            db.Feeds.Add(parsedFeed);
            await db.SaveChangesAsync();
            feed = parsedFeed;
        }

        // Download artwork if we have a URL and no local file yet
        if (!string.IsNullOrEmpty(feed.ArtworkUrl) && string.IsNullOrEmpty(feed.ArtworkFile))
        {
            var artFile = await DownloadArtworkAsync(feed.ArtworkUrl, feed.Id);
            if (!string.IsNullOrEmpty(artFile))
                feed.ArtworkFile = artFile;
        }

        // Upsert episodes — skip any whose GUID already exists for this feed
        var existingGuidsList = await db.Episodes
            .Where(e => e.FeedId == feed.Id)
            .Select(e => e.Guid)
            .ToListAsync();
        var existingGuids = existingGuidsList.ToHashSet();

        var newEpisodes = episodes
            .Where(e => !existingGuids.Contains(e.Guid))
            .ToList();

        foreach (var ep in newEpisodes)
            ep.FeedId = feed.Id;

        if (newEpisodes.Count > 0)
            db.Episodes.AddRange(newEpisodes);

        feed.EpisodeCount = existingGuids.Count + newEpisodes.Count;
        await db.SaveChangesAsync();

        _logger.LogInformation("Feed '{Title}': {New} new episodes added", feed.Title, newEpisodes.Count);
        return feed;
    }

    /// <summary>
    /// Refreshes all subscribed feeds. Called by the startup hosted service.
    /// Creates its own DI scope to get a fresh DbContext.
    /// </summary>
    public async Task RefreshAllFeedsAsync(IServiceScopeFactory scopeFactory)
    {
        _logger.LogInformation("Starting podcast feed refresh...");
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PodcastsDbContext>();
            var feeds = await db.Feeds.ToListAsync();

            if (feeds.Count == 0)
            {
                _logger.LogInformation("No podcast feeds subscribed; skipping refresh.");
                return;
            }

            foreach (var feed in feeds)
            {
                try
                {
                    await AddOrRefreshFeedAsync(db, feed.RssUrl);
                    await Task.Delay(500); // Polite delay between requests
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refresh podcast feed: {Title} ({Url})", feed.Title, feed.RssUrl);
                }
            }

            _logger.LogInformation("Podcast feed refresh complete ({Count} feeds).", feeds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Podcast refresh failed.");
        }
    }

    // ─── Artwork ───────────────────────────────────────────────────────

    private async Task<string> DownloadArtworkAsync(string url, int feedId)
    {
        try
        {
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return "";

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length < 100) return "";

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var ext = contentType switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/webp" => ".webp",
                _ => Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant() is { Length: > 0 } e ? e : ".jpg"
            };

            var dir = Path.Combine(AppContext.BaseDirectory, "assets", "podcastart");
            Directory.CreateDirectory(dir);
            var filename = $"podcast_{feedId}{ext}";
            var fullPath = Path.Combine(dir, filename);

            await File.WriteAllBytesAsync(fullPath, bytes);
            return filename;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not download podcast artwork from {Url}", url);
            return "";
        }
    }

    // ─── Helpers ───────────────────────────────────────────────────────

    private static int ParseDuration(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        // Format can be: "3600", "01:00:00", or "60:00"
        if (int.TryParse(value, out var seconds)) return seconds;
        var parts = value.Split(':');
        return parts.Length switch
        {
            3 when int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m) && int.TryParse(parts[2], out var s)
                => h * 3600 + m * 60 + s,
            2 when int.TryParse(parts[0], out var m2) && int.TryParse(parts[1], out var s2)
                => m2 * 60 + s2,
            _ => 0
        };
    }

    private static string StripHtml(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return System.Text.RegularExpressions.Regex.Replace(input, "<[^>]+>", " ")
            .Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<")
            .Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&#39;", "'")
            .Trim();
    }
}
