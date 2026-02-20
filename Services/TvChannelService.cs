using System.Text.Json;
using System.Text.RegularExpressions;
using NexusM.Models;

namespace NexusM.Services;

/// <summary>
/// Manages Internet TV channels: M3U playlist import and logo fetching from IPTV-org.
/// </summary>
public class TvChannelService
{
    private readonly ILogger<TvChannelService> _logger;
    private readonly string _logosPath;
    private readonly string _cachePath;
    private readonly HttpClient _http;

    // Logo fetch progress
    public TvLogoFetchProgress FetchProgress { get; } = new();

    public TvChannelService(ILogger<TvChannelService> logger)
    {
        _logger = logger;
        _logosPath = Path.Combine(AppContext.BaseDirectory, "assets", "tvlogos");
        _cachePath = Path.Combine(AppContext.BaseDirectory, "cache", "tvlogos");
        _http = new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NexusM/1.0");

        if (!Directory.Exists(_logosPath))
            Directory.CreateDirectory(_logosPath);
        if (!Directory.Exists(_cachePath))
            Directory.CreateDirectory(_cachePath);
    }

    // ─── M3U Import ─────────────────────────────────────────────────

    /// <summary>
    /// Parse an M3U playlist file and return a list of TV channels.
    /// Supports Extended M3U format with #EXTINF and tvg-id attributes.
    /// </summary>
    public List<TvChannel> ParseM3u(Stream stream, string sourceFilename)
    {
        var channels = new List<TvChannel>();
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        var lines = content.Split('\n', StringSplitOptions.None);

        string? currentName = null;
        string? currentTvgId = null;
        string? currentResolution = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Skip M3U header
            if (line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase)) continue;

            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                // Parse: #EXTINF:-1 tvg-id="...",Channel Name (1080p)
                currentTvgId = "";
                currentResolution = "";

                // Extract tvg-id
                var tvgIdMatch = Regex.Match(line, @"tvg-id=""([^""]*)""");
                if (tvgIdMatch.Success)
                    currentTvgId = tvgIdMatch.Groups[1].Value;

                // Extract channel name (after the last comma)
                var commaIdx = line.LastIndexOf(',');
                if (commaIdx >= 0)
                {
                    currentName = line[(commaIdx + 1)..].Trim();

                    // Extract resolution from name, e.g. "(1080p)" or "(720p)"
                    var resMatch = Regex.Match(currentName, @"\((\d+p)\)");
                    if (resMatch.Success)
                    {
                        currentResolution = resMatch.Groups[1].Value;
                        // Remove resolution from display name
                        currentName = currentName.Replace(resMatch.Value, "").Trim();
                    }

                    // Remove geo-blocking info like "[Geo-blocked]"
                    currentName = Regex.Replace(currentName, @"\s*\[.*?\]\s*", " ").Trim();
                }

                continue;
            }

            // Non-comment, non-EXTINF line = stream URL
            if (!line.StartsWith('#') && currentName != null)
            {
                channels.Add(new TvChannel
                {
                    Name = currentName,
                    StreamUrl = line,
                    TvgId = currentTvgId ?? "",
                    Resolution = currentResolution ?? "",
                    SourcePlaylist = sourceFilename,
                    DateAdded = DateTime.UtcNow
                });

                currentName = null;
                currentTvgId = null;
                currentResolution = null;
            }
        }

        _logger.LogInformation("Parsed {Count} TV channels from M3U file: {File}", channels.Count, sourceFilename);
        return channels;
    }

    // ─── Logo Fetching from IPTV-org ────────────────────────────────

    /// <summary>
    /// Fetch logos for TV channels from IPTV-org database.
    /// Uses channels.json and logos.json APIs with 24h caching.
    /// Multi-method name matching: exact, normalized, base-name, fuzzy.
    /// </summary>
    public async Task FetchLogosAsync(List<TvChannel> channels, Action<TvChannel> onLogoUpdated)
    {
        if (FetchProgress.IsFetching) return;

        FetchProgress.IsFetching = true;
        FetchProgress.Progress = 0;
        FetchProgress.Success = 0;
        FetchProgress.Failed = 0;
        FetchProgress.Total = channels.Count;
        FetchProgress.Status = "Downloading IPTV-org database...";

        try
        {
            // Step 1: Fetch/cache IPTV-org data
            var (iptvChannels, iptvLogos) = await FetchIptvOrgData();
            if (iptvChannels == null || iptvChannels.Count == 0)
            {
                FetchProgress.Status = "Failed to fetch IPTV-org database";
                FetchProgress.IsFetching = false;
                return;
            }

            // Step 2: Build lookup dictionaries
            var channelByName = new Dictionary<string, IptvChannel>(StringComparer.OrdinalIgnoreCase);
            var channelByNormalized = new Dictionary<string, IptvChannel>(StringComparer.OrdinalIgnoreCase);

            foreach (var ch in iptvChannels)
            {
                if (!string.IsNullOrEmpty(ch.name))
                {
                    channelByName.TryAdd(ch.name, ch);
                    var normalized = Regex.Replace(ch.name, @"[^a-zA-Z0-9]", "").ToLower();
                    channelByNormalized.TryAdd(normalized, ch);
                }
                if (ch.alt_names != null)
                {
                    foreach (var alt in ch.alt_names)
                    {
                        channelByName.TryAdd(alt, ch);
                        var normalizedAlt = Regex.Replace(alt, @"[^a-zA-Z0-9]", "").ToLower();
                        channelByNormalized.TryAdd(normalizedAlt, ch);
                    }
                }
            }

            var logosByChannel = new Dictionary<string, List<IptvLogo>>();
            if (iptvLogos != null)
            {
                foreach (var logo in iptvLogos)
                {
                    if (!string.IsNullOrEmpty(logo.channel))
                    {
                        if (!logosByChannel.ContainsKey(logo.channel))
                            logosByChannel[logo.channel] = new List<IptvLogo>();
                        logosByChannel[logo.channel].Add(logo);
                    }
                }
            }

            // Step 3: Match and download
            foreach (var channel in channels)
            {
                FetchProgress.Progress++;
                FetchProgress.Status = $"Processing: {channel.Name}";

                try
                {
                    // Skip if logo already exists locally
                    if (!string.IsNullOrEmpty(channel.Logo))
                    {
                        var existingPath = Path.Combine(_logosPath, channel.Logo);
                        if (File.Exists(existingPath))
                        {
                            FetchProgress.Success++;
                            continue;
                        }
                    }

                    // Find matching IPTV-org channel
                    var matched = MatchChannel(channel.Name, channelByName, channelByNormalized, iptvChannels);
                    if (matched == null)
                    {
                        _logger.LogDebug("No IPTV-org match for: {Name}", channel.Name);
                        continue;
                    }

                    // Get logo URL
                    var logoUrl = GetBestLogoUrl(matched.id, logosByChannel);
                    if (string.IsNullOrEmpty(logoUrl))
                    {
                        _logger.LogDebug("No logo URL for matched channel: {Name}", channel.Name);
                        continue;
                    }

                    // Download logo
                    var savedFilename = await DownloadLogo(logoUrl, channel.Name);
                    if (!string.IsNullOrEmpty(savedFilename))
                    {
                        channel.Logo = savedFilename;
                        onLogoUpdated(channel);
                        FetchProgress.Success++;
                        _logger.LogInformation("Downloaded logo for {Name}: {File}", channel.Name, savedFilename);
                    }
                    else
                    {
                        FetchProgress.Failed++;
                    }

                    await Task.Delay(150); // Rate limiting
                }
                catch (Exception ex)
                {
                    FetchProgress.Failed++;
                    _logger.LogWarning(ex, "Error fetching logo for: {Name}", channel.Name);
                }
            }

            FetchProgress.Status = $"Done! {FetchProgress.Success} logos found, {FetchProgress.Failed} failed.";
        }
        catch (Exception ex)
        {
            FetchProgress.Status = $"Error: {ex.Message}";
            _logger.LogError(ex, "Logo fetch failed");
        }
        finally
        {
            FetchProgress.IsFetching = false;
        }
    }

    private async Task<(List<IptvChannel>?, List<IptvLogo>?)> FetchIptvOrgData()
    {
        var channelsCachePath = Path.Combine(_cachePath, "channels.json");
        var logosCachePath = Path.Combine(_cachePath, "logos.json");

        List<IptvChannel>? channels = null;
        List<IptvLogo>? logos = null;

        // Check cache (24h)
        bool useCache = File.Exists(channelsCachePath) &&
                        (DateTime.UtcNow - File.GetLastWriteTimeUtc(channelsCachePath)).TotalHours < 24;

        if (useCache)
        {
            _logger.LogInformation("Using cached IPTV-org database");
            var json = await File.ReadAllTextAsync(channelsCachePath);
            channels = JsonSerializer.Deserialize<List<IptvChannel>>(json);
            if (File.Exists(logosCachePath))
            {
                var logosJson = await File.ReadAllTextAsync(logosCachePath);
                logos = JsonSerializer.Deserialize<List<IptvLogo>>(logosJson);
            }
        }
        else
        {
            _logger.LogInformation("Downloading fresh IPTV-org database...");
            var channelsJson = await _http.GetStringAsync("https://iptv-org.github.io/api/channels.json");
            channels = JsonSerializer.Deserialize<List<IptvChannel>>(channelsJson);
            await File.WriteAllTextAsync(channelsCachePath, channelsJson);

            try
            {
                var logosJson = await _http.GetStringAsync("https://iptv-org.github.io/api/logos.json");
                logos = JsonSerializer.Deserialize<List<IptvLogo>>(logosJson);
                await File.WriteAllTextAsync(logosCachePath, logosJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch logos.json, will use channel logos only");
            }

            _logger.LogInformation("Downloaded {Count} channels from IPTV-org", channels?.Count ?? 0);
        }

        return (channels, logos);
    }

    private IptvChannel? MatchChannel(string channelName,
        Dictionary<string, IptvChannel> byName,
        Dictionary<string, IptvChannel> byNormalized,
        List<IptvChannel> allChannels)
    {
        // Method 1: Exact name match
        if (byName.TryGetValue(channelName, out var exact))
            return exact;

        // Method 2: Normalized match
        var normalized = Regex.Replace(channelName, @"[^a-zA-Z0-9]", "").ToLower();
        if (byNormalized.TryGetValue(normalized, out var norm))
            return norm;

        // Method 3: Base name pattern (strip resolution, geo-block info)
        var baseName = Regex.Replace(channelName, @"\s*\([^)]*\)\s*", " ");
        baseName = Regex.Replace(baseName, @"\s*\[[^\]]*\]\s*", " ");
        baseName = Regex.Replace(baseName, @"\s+(English|French|Spanish|German|Arabic|HD|SD|Live|FHD|UHD|4K).*$", "", RegexOptions.IgnoreCase);
        baseName = baseName.Trim();
        var baseNormalized = Regex.Replace(baseName, @"[^a-zA-Z0-9]", "").ToLower();

        if (baseNormalized.Length >= 3)
        {
            foreach (var ch in allChannels)
            {
                var iptvNorm = Regex.Replace(ch.name ?? "", @"[^a-zA-Z0-9]", "").ToLower();
                if (iptvNorm == baseNormalized || iptvNorm.StartsWith(baseNormalized))
                    return ch;
            }
        }

        // Method 4: Fuzzy word overlap
        var cleanName = Regex.Replace(channelName, @"\[.*?\]|\(.*?\)", "").Trim().ToLower();
        IptvChannel? bestMatch = null;
        int bestScore = 0;

        foreach (var ch in allChannels)
        {
            var iptvName = (ch.name ?? "").ToLower();
            int score = 0;

            if (cleanName.Length > 5 && iptvName.Contains(cleanName)) score = cleanName.Length;
            else if (iptvName.Length > 5 && cleanName.Contains(iptvName)) score = iptvName.Length;

            var localWords = cleanName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2).ToArray();
            var iptvWords = iptvName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2).ToArray();
            int matchingWords = localWords.Count(lw => iptvWords.Any(iw => iw == lw || iw.Contains(lw) || lw.Contains(iw)));

            if (matchingWords >= 2 || (localWords.Length > 0 && (double)matchingWords / localWords.Length >= 0.5))
            {
                int wordScore = matchingWords * 10;
                if (wordScore > score) score = wordScore;
            }

            if (score > bestScore) { bestScore = score; bestMatch = ch; }
        }

        return bestScore >= 10 ? bestMatch : null;
    }

    private string? GetBestLogoUrl(string? channelId, Dictionary<string, List<IptvLogo>> logosByChannel)
    {
        if (string.IsNullOrEmpty(channelId) || !logosByChannel.ContainsKey(channelId))
            return null;

        var logos = logosByChannel[channelId];

        // Prefer PNG > JPEG > WebP > GIF > others, avoid SVG
        var sorted = logos.OrderBy(l => l.format switch
        {
            "PNG" => 1, "JPEG" => 2, "WebP" => 3, "AVIF" => 4, "GIF" => 5, _ => 10
        });

        // Prefer non-SVG
        var best = sorted.FirstOrDefault(l => !string.Equals(l.format, "SVG", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(l.url));
        best ??= sorted.FirstOrDefault(l => !string.IsNullOrEmpty(l.url));

        return best?.url;
    }

    private async Task<string?> DownloadLogo(string url, string channelName)
    {
        try
        {
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var ext = contentType switch
            {
                "image/png" => ".png",
                "image/jpeg" or "image/jpg" => ".jpg",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                "image/svg+xml" => ".svg",
                "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
                _ => Path.GetExtension(new Uri(url).AbsolutePath) is { Length: > 0 } ext2 ? ext2 : ".png"
            };

            var safeFilename = SanitizeFilename(channelName) + ext;
            var filepath = Path.Combine(_logosPath, safeFilename);

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length < 100) return null;

            await File.WriteAllBytesAsync(filepath, bytes);
            return safeFilename;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to download logo from: {Url}", url);
            return null;
        }
    }

    private static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return clean.Replace(' ', '-').ToLowerInvariant();
    }

    // ─── IPTV-org API DTOs ──────────────────────────────────────────

    private class IptvChannel
    {
        public string? id { get; set; }
        public string? name { get; set; }
        public string[]? alt_names { get; set; }
        public string? country { get; set; }
        public string? logo { get; set; }
    }

    private class IptvLogo
    {
        public string? channel { get; set; }
        public string? url { get; set; }
        public string? format { get; set; }
    }
}
