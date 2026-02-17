using System.Text.Json;
using NexusM.Models;

namespace NexusM.Services;

public class RadioService
{
    private readonly ILogger<RadioService> _logger;
    private readonly string _confPath;
    private readonly string _logosPath;
    private readonly HttpClient _http;
    private List<RadioStation> _stations = new();
    private List<string> _countries = new();
    private List<string> _genres = new();
    private DateTime _lastModified = DateTime.MinValue;

    // Logo fetch progress
    public bool IsFetchingLogos { get; private set; }
    public int FetchProgress { get; private set; }
    public int FetchTotal { get; private set; }
    public string FetchStatus { get; private set; } = "";
    public int FetchSuccess { get; private set; }
    public int FetchFailed { get; private set; }

    public RadioService(ILogger<RadioService> logger)
    {
        _logger = logger;
        _confPath = Path.Combine(AppContext.BaseDirectory, "assets", "radios.conf");
        _logosPath = Path.Combine(AppContext.BaseDirectory, "assets", "radiologos");
        _http = new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(10);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NexusM/1.0");

        // Ensure directories exist
        var confDir = Path.GetDirectoryName(_confPath);
        if (!string.IsNullOrEmpty(confDir) && !Directory.Exists(confDir))
            Directory.CreateDirectory(confDir);
        if (!Directory.Exists(_logosPath))
            Directory.CreateDirectory(_logosPath);

        // Create default conf file if missing
        if (!File.Exists(_confPath))
        {
            File.WriteAllText(_confPath, GetDefaultContent());
            _logger.LogInformation("Created default radio stations file: {Path}", _confPath);
        }

        // Initial parse
        ParseStations();
    }

    public List<RadioStation> GetStations()
    {
        ReloadIfChanged();
        return _stations;
    }

    public List<string> GetCountries()
    {
        ReloadIfChanged();
        return _countries;
    }

    public List<string> GetGenres()
    {
        ReloadIfChanged();
        return _genres;
    }

    // ─── Logo Fetching ─────────────────────────────────────────────

    public async Task FetchLogosAsync()
    {
        if (IsFetchingLogos) return;

        IsFetchingLogos = true;
        FetchProgress = 0;
        FetchSuccess = 0;
        FetchFailed = 0;
        var stations = GetStations();
        FetchTotal = stations.Count;
        FetchStatus = "Starting logo fetch...";

        _logger.LogInformation("Starting radio logo fetch for {Count} stations", stations.Count);

        var updatedAny = false;

        foreach (var station in stations)
        {
            FetchProgress++;
            FetchStatus = $"Fetching logo for: {station.Name}";

            try
            {
                // Skip if logo already exists locally
                var safeFilename = SanitizeFilename(station.Name);
                var existingFiles = Directory.GetFiles(_logosPath, $"{safeFilename}.*");
                if (existingFiles.Length > 0)
                {
                    // Update Logo field to local path if not already set
                    var localName = Path.GetFileName(existingFiles[0]);
                    if (station.Logo != localName)
                    {
                        station.Logo = localName;
                        updatedAny = true;
                    }
                    FetchSuccess++;
                    _logger.LogDebug("Logo already exists for {Name}: {File}", station.Name, localName);
                    continue;
                }

                // Search Radio Browser API by name
                var faviconUrl = await SearchRadioBrowserFavicon(station.Name, station.StreamUrl);
                if (string.IsNullOrEmpty(faviconUrl))
                {
                    FetchFailed++;
                    _logger.LogWarning("No logo found for station: {Name}", station.Name);
                    continue;
                }

                // Download the favicon
                var savedFilename = await DownloadFavicon(faviconUrl, safeFilename);
                if (!string.IsNullOrEmpty(savedFilename))
                {
                    station.Logo = savedFilename;
                    updatedAny = true;
                    FetchSuccess++;
                    _logger.LogInformation("Downloaded logo for {Name}: {File}", station.Name, savedFilename);
                }
                else
                {
                    FetchFailed++;
                }

                // Small delay to be nice to the API
                await Task.Delay(200);
            }
            catch (Exception ex)
            {
                FetchFailed++;
                _logger.LogWarning(ex, "Error fetching logo for station: {Name}", station.Name);
            }
        }

        // Rewrite radios.conf with updated logo paths
        if (updatedAny)
        {
            RewriteConfFile(stations);
        }

        FetchStatus = $"Done! {FetchSuccess} logos downloaded, {FetchFailed} failed.";
        _logger.LogInformation("Logo fetch complete: {Success} success, {Failed} failed", FetchSuccess, FetchFailed);
        IsFetchingLogos = false;
    }

    private async Task<string?> SearchRadioBrowserFavicon(string stationName, string streamUrl)
    {
        // Try searching by exact name first
        var encoded = Uri.EscapeDataString(stationName);
        var apiUrl = $"https://de1.api.radio-browser.info/json/stations/byname/{encoded}?limit=10&hidebroken=true";

        try
        {
            var response = await _http.GetAsync(apiUrl);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            var results = JsonSerializer.Deserialize<List<RadioBrowserStation>>(json);

            if (results == null || results.Count == 0) return null;

            // Try to find best match - prefer matching stream URL
            var match = results.FirstOrDefault(r =>
                !string.IsNullOrEmpty(r.favicon) &&
                r.url_resolved?.Contains(new Uri(streamUrl).Host, StringComparison.OrdinalIgnoreCase) == true);

            // Fallback to first result with a favicon
            match ??= results.FirstOrDefault(r => !string.IsNullOrEmpty(r.favicon));

            return match?.favicon;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Radio Browser API search failed for: {Name}", stationName);
            return null;
        }
    }

    private async Task<string?> DownloadFavicon(string url, string safeFilename)
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

            var filename = $"{safeFilename}{ext}";
            var filepath = Path.Combine(_logosPath, filename);

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length < 100) return null; // Too small, likely not a real image

            await File.WriteAllBytesAsync(filepath, bytes);
            return filename;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to download favicon from: {Url}", url);
            return null;
        }
    }

    private void RewriteConfFile(List<RadioStation> stations)
    {
        try
        {
            // Read the original file to preserve comments and structure
            var lines = File.ReadAllLines(_confPath);
            var newLines = new List<string>();
            int stationIndex = 0;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                {
                    newLines.Add(line);
                    continue;
                }

                var parts = trimmed.Split(',', 6);
                if (parts.Length < 4)
                {
                    newLines.Add(line);
                    continue;
                }

                if (stationIndex < stations.Count)
                {
                    var s = stations[stationIndex];
                    newLines.Add($"{s.Name},{s.Country},{s.Genre},{s.StreamUrl},{s.Description},{s.Logo}");
                    stationIndex++;
                }
                else
                {
                    newLines.Add(line);
                }
            }

            File.WriteAllLines(_confPath, newLines);
            _lastModified = File.GetLastWriteTimeUtc(_confPath);
            _logger.LogInformation("Updated radios.conf with new logo paths");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rewrite radios.conf");
        }
    }

    private static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return clean.Replace(' ', '-').ToLowerInvariant();
    }

    // ─── Parsing ───────────────────────────────────────────────────

    private void ReloadIfChanged()
    {
        try
        {
            var lastWrite = File.GetLastWriteTimeUtc(_confPath);
            if (lastWrite != _lastModified)
            {
                ParseStations();
            }
        }
        catch { /* file may have been deleted */ }
    }

    private void ParseStations()
    {
        try
        {
            var lines = File.ReadAllLines(_confPath);
            var stations = new List<RadioStation>();
            int id = 1;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                    continue;

                // Format: Name,Country,Genre,URL,Description,Logo
                var parts = trimmed.Split(',', 6);
                if (parts.Length < 4) continue;

                stations.Add(new RadioStation
                {
                    Id = id++,
                    Name = parts[0].Trim(),
                    Country = parts.Length > 1 ? parts[1].Trim() : "",
                    Genre = parts.Length > 2 ? parts[2].Trim() : "",
                    StreamUrl = parts.Length > 3 ? parts[3].Trim() : "",
                    Description = parts.Length > 4 ? parts[4].Trim() : "",
                    Logo = parts.Length > 5 ? parts[5].Trim() : ""
                });
            }

            _stations = stations;
            _countries = stations.Select(s => s.Country).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList();
            _genres = stations.Select(s => s.Genre).Where(g => !string.IsNullOrEmpty(g)).Distinct().OrderBy(g => g).ToList();
            _lastModified = File.GetLastWriteTimeUtc(_confPath);

            _logger.LogInformation("Loaded {Count} radio stations from {Path}", stations.Count, _confPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse radio stations from {Path}", _confPath);
        }
    }

    // ─── Radio Browser API DTO ─────────────────────────────────────

    private class RadioBrowserStation
    {
        public string? name { get; set; }
        public string? url_resolved { get; set; }
        public string? favicon { get; set; }
    }

    // ─── Default Config ────────────────────────────────────────────

    private static string GetDefaultContent() => """
        # NexusM Internet Radio Stations Configuration
        # Format: Name,Country,Genre,URL,Description,Logo
        # Lines starting with # are comments
        #
        # You can add, remove, or modify radio stations in this file.
        # Changes will take effect after server restart.
        #
        # ========== NORTH AMERICA - USA ==========
        NPR News,USA,News,https://npr-ice.streamguys1.com/live.mp3,National Public Radio - News & Talk,
        KEXP Seattle,USA,Alternative Rock,https://kexp-mp3-128.streamguys1.com/kexp128.mp3,Independent alternative music from Seattle,
        181.FM Fusion Jazz,USA,Jazz,https://listen.181fm.com/181-fusionjazz_128k.mp3,Fusion jazz hits,
        WNYC New York,USA,News/Talk,https://fm939.wnyc.org/wnycfm,New York Public Radio,
        Radio Paradise,USA,Eclectic Rock,https://stream.radioparadise.com/aac-320,Commercial-free eclectic music,
        SomaFM Groove Salad,USA,Ambient/Downtempo,https://ice1.somafm.com/groovesalad-128-mp3,Ambient/downtempo grooves,
        SomaFM Defcon,USA,Hacker/Electronic,https://ice1.somafm.com/defcon-128-mp3,Music for hacking,
        181.FM The Rock,USA,Classic Rock,https://listen.181fm.com/181-rock_128k.mp3,Classic rock hits,
        NPR National Live,USA,News/Talk,https://npr-ice.streamguys1.com/live.mp3,National Public Radio live stream,
        181.FM Energy 93,USA,Top 40/Dance,https://listen.181fm.com/181-energy93_128k.mp3,Top 40 & dance hits,
        181.FM Classical Guitar,USA,Classical,https://listen.181fm.com/181-classicalguitar_128k.mp3,Relaxing classical guitar,
        SomaFM Secret Agent,USA,Lounge/Chill,https://ice2.somafm.com/secretagent-128-mp3,Spy-themed downtempo grooves,
        SomaFM Indie Pop Rocks,USA,Indie Pop,https://ice2.somafm.com/indiepop-128-mp3,Indie pop & alt rock,
        #
        # ========== EUROPE - UK ==========
        BBC Radio 1,UK,Pop/Dance,https://stream.live.vc.bbcmedia.co.uk/bbc_radio_one,BBC's flagship pop music station,
        BBC Radio 2,UK,Pop/Rock,https://stream.live.vc.bbcmedia.co.uk/bbc_radio_two,Popular music and culture,
        BBC Radio 3,UK,Classical,https://stream.live.vc.bbcmedia.co.uk/bbc_radio_three,Classical music and culture,
        BBC Radio 4,UK,News/Talk,https://stream.live.vc.bbcmedia.co.uk/bbc_radio_fourfm,News; drama and documentaries,
        BBC Radio 6 Music,UK,Alternative,https://stream.live.vc.bbcmedia.co.uk/bbc_6music,Alternative music,
        BBC World Service,UK,World News,https://stream.live.vc.bbcmedia.co.uk/bbc_world_service,International news and analysis,
        Heart UK,UK,Pop,https://media-ice.musicradio.com/HeartLondonMP3,Feel good music from London,
        Smooth Chill UK,UK,Chill,https://media-ice.musicradio.com/SmoothChillMP3,Relaxing smooth chill vibes,
        #
        # ========== EUROPE - FRANCE ==========
        FIP Radio,France,Eclectic,https://direct.fipradio.fr/live/fip-midfi.mp3,Eclectic French music discovery,
        France Musique,France,Classical,https://direct.francemusique.fr/live/francemusique-midfi.mp3,French classical music,
        France Info,France,News,https://direct.franceinfo.fr/live/franceinfo-midfi.mp3,24/7 French news,
        France Bleu,France,Local/Pop,https://direct.francebleu.fr/live/fb1071-midfi.mp3,French local radio,
        Europe 1,France,News/Talk,http://stream.europe1.fr/europe1.mp3,French news and information,
        RTL France,France,News/Talk,http://streaming.radio.rtl.fr/rtl-1-44-128,French news and talk,
        RTL2 France,France,Pop/Rock,http://streaming.radio.rtl2.fr/rtl2-1-44-128,French pop and rock,
        Fun Radio,France,Dance/Electronic,http://streaming.radio.funradio.fr/fun-1-44-128,French dance music,
        Skyrock,France,Hip-Hop/R&B,https://icecast.skyrock.net/s/natio_mp3_128k,French hip-hop and R&B,
        TSF Jazz,France,Jazz,http://tsfjazz.ice.infomaniak.ch/tsfjazz-high.mp3,French jazz radio,
        MFM Radio,France,Pop,http://mfm.ice.infomaniak.ch/mfm-128.mp3,French pop hits,
        Generations FM,France,Urban,http://generationfm.ice.infomaniak.ch/generationfm-high.mp3,French urban music,
        #
        # ========== EUROPE - GERMANY ==========
        1Live,Germany,Pop/Rock,https://wdr-1live-live.icecastssl.wdr.de/wdr/1live/live/mp3/128/stream.mp3,German contemporary music,
        WDR 2,Germany,Pop,https://wdr-wdr2-ruhrgebiet.icecastssl.wdr.de/wdr/wdr2/ruhrgebiet/mp3/128/stream.mp3,German popular radio,
        WDR 5,Germany,News/Culture,https://wdr-wdr5-live.icecastssl.wdr.de/wdr/wdr5/live/mp3/128/stream.mp3,German news and culture,
        Antenne Bayern,Germany,Pop/Rock,https://mp3channels.webradio.antenne.de/antenne,Bavarian hit radio,
        Radio BOB!,Germany,Rock,https://streams.radiobob.de/bob-national/mp3-192/streams.radiobob.de/,German rock station,
        FFH,Germany,Pop,https://mp3.ffh.de/radioffh/hqlivestream.mp3,Hit Radio FFH,
        JAM FM,Germany,R&B/Hip-Hop,https://stream.jam.fm/jamfm-live/mp3-128,Berlin urban music,
        Kiss FM Berlin,Germany,Dance/Electronic,https://stream.kissfm.de/kissfm/mp3-128,Berlin dance music,
        Sunshine Live,Germany,Dance/Electronic,https://stream.sunshine-live.de/live/mp3-192,Mannheim dance radio,
        SAW Magdeburg,Germany,Pop/Rock,https://stream.saw-musikwelt.de/saw/mp3-128,Saxony-Anhalt hits,
        Musik Club,Germany,Dance,https://streams.deltaradio.de/musik-club/mp3-192,German dance club hits,
        Blackbeats FM,Germany,Urban,https://stream.blackbeats.fm/live,German urban station,
        #
        # ========== EUROPE - NETHERLANDS ==========
        NPO Radio 1,Netherlands,News/Talk,https://icecast.omroep.nl/radio1-bb-mp3,Dutch news and talk,
        NPO Radio 2,Netherlands,Pop,https://icecast.omroep.nl/radio2-bb-mp3,Dutch popular music,
        Radio 538,Netherlands,Pop/Dance,https://22723.live.streamtheworld.com/RADIO538.mp3,Dutch hit music,
        100% NL,Netherlands,Dutch Pop,https://stream.100p.nl/100pctnl.mp3,Only Dutch music,
        Radio Veronica,Netherlands,Rock/Pop,https://25293.live.streamtheworld.com/VERONICA.mp3,Dutch rock and pop,
        Sky Radio,Netherlands,Love Songs,https://25293.live.streamtheworld.com/SKYRADIO.mp3,Dutch love songs,
        Radio NL,Netherlands,Dutch Hits,https://stream.radionl.fm/radionl,Dutch hits station,
        Classic FM Netherlands,Netherlands,Classical,https://icecast.omroep.nl/radio4-bb-mp3,Dutch classical music,
        Arrow Classic Rock,Netherlands,Classic Rock,https://stream.gal.io/arrow,Dutch classic rock,
        FunX,Netherlands,Hip-Hop,https://icecast.omroep.nl/funx-bb-mp3,Dutch hip-hop station,
        #
        # ========== EUROPE - ITALY ==========
        RAI Radio 2,Italy,Pop/Rock,https://icestreaming.rai.it/2.mp3,Italian contemporary music,
        Radio Kiss Kiss,Italy,Pop,https://ice07.fluidstream.net/KissKiss.mp3,Italian pop hits,
        Virgin Radio Italy,Italy,Rock,https://icy.unitedradio.it/Virgin.mp3,Italian rock station,
        Radio Monte Carlo,Italy,Pop/Rock,https://icy.unitedradio.it/RMC.mp3,Italian entertainment radio,
        Radio 105,Italy,Pop/Rock,https://icy.unitedradio.it/Radio105.mp3,Italian contemporary hits,
        #
        # ========== EUROPE - BELGIUM ==========
        VRT Radio 1,Belgium,News/Talk,http://icecast.vrtcdn.be/radio1-high.mp3,Flemish news and talk,
        Studio Brussel,Belgium,Alternative,http://icecast.vrtcdn.be/stubru-high.mp3,Belgian alternative music,
        Klara Continuo,Belgium,Classical,http://icecast.vrtcdn.be/klara-high.mp3,Flemish classical music,
        Classic 21,Belgium,Rock,https://radios.rtbf.be/classic21-128.mp3,Belgian rock station,
        Bel RTL,Belgium,News/Talk,https://belrtl.ice.infomaniak.ch/belrtl-mp3-128.mp3,Belgian news and talk,
        Joe FM,Belgium,Pop/Rock,https://playerservices.streamtheworld.com/api/livestream-redirect/JOE.mp3,Belgian popular music,
        NRJ Belgium,Belgium,Dance/Pop,https://playerservices.streamtheworld.com/api/livestream-redirect/NRJBELGIE.mp3,Belgian dance and pop,
        #
        # ========== EUROPE - IRELAND ==========
        RTÉ 2FM,Ireland,Pop/Rock,https://icecast.rte.ie/2fm,Irish contemporary music,
        RTÉ Gold,Ireland,Oldies,https://icecast.rte.ie/gold,Irish classic hits,
        Today FM,Ireland,Music/Talk,https://edge.audioxi.com/TDAAC,Irish music and talk,
        #
        # ========== EUROPE - SWITZERLAND ==========
        SRF 1,Switzerland,Pop/News,http://stream.srg-ssr.ch/m/rsp/mp3_128,Swiss German radio,
        SRF 2 Kultur,Switzerland,Culture/Classical,http://stream.srg-ssr.ch/m/rsc_de/mp3_128,Swiss culture radio,
        SRF Musikwelle,Switzerland,Folk/Oldies,http://stream.srg-ssr.ch/m/regi_ag_so/mp3_128,Swiss folk music,
        Radio Swiss Jazz,Switzerland,Jazz,http://stream.srg-ssr.ch/m/rsj/mp3_128,24/7 Swiss jazz,
        Radio Swiss Classic,Switzerland,Classical,http://stream.srg-ssr.ch/m/rsc_de/mp3_128,24/7 Swiss classical,
        Radio Swiss Pop,Switzerland,Pop,http://stream.srg-ssr.ch/m/rsp/mp3_128,24/7 Swiss pop hits,
        Energy Zürich,Switzerland,Dance/Pop,https://energyzuerich.ice.infomaniak.ch/energyzuerich-high.mp3,Zurich dance hits,
        RTS Couleur 3,Switzerland,Alternative,http://stream.srg-ssr.ch/m/couleur3/mp3_128,Swiss French alternative,
        """;
}
