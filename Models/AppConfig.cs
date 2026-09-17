using System.Linq;

namespace NexusM.Models;

/// <summary>
/// Application configuration loaded from NexusM.conf
/// Mirrors the NexusM .conf approach with INI-style sections.
/// </summary>
public class AppConfig
{
    public ServerConfig Server { get; set; } = new();
    public HttpsConfig Https { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();
    public LibraryConfig Library { get; set; } = new();
    public PlaybackConfig Playback { get; set; } = new();
    public TranscodingConfig Transcoding { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
    public UIConfig UI { get; set; } = new();
    public MetadataConfig Metadata { get; set; } = new();
    public SubtitlesConfig Subtitles { get; set; } = new();
    public DlnaConfig Dlna { get; set; } = new();
    public TraktConfig Trakt { get; set; } = new();
    public LastFmConfig LastFm { get; set; } = new();
    public AnalysisConfig Analysis { get; set; } = new();
    public VideoThumbnailsConfig VideoThumbnails { get; set; } = new();
    public VideoPreviewsConfig VideoPreviews { get; set; } = new();
    public RemoteAccessConfig RemoteAccess { get; set; } = new();
    public GateConfig Gate { get; set; } = new();
    public AutoUpdateConfig AutoUpdate { get; set; } = new();
    public SemanticSearchConfig SemanticSearch { get; set; } = new();

    /// <summary>
    /// Demo mode settings - populated only if [DEMOMOD] section exists in NexusM.conf.
    /// Never written back to the conf file; never auto-created.
    /// </summary>
    public DemoModeConfig DemoMode { get; set; } = new();
}

/// <summary>
/// Optional demo mode: add [DEMOMOD] section to NexusM.conf to activate.
/// Any key set to FALSE hides the corresponding menu from all users.
/// Keys absent or set to TRUE are shown normally.
/// This section is never written by the server - only read at startup.
/// </summary>
public class DemoModeConfig
{
    public bool Enabled { get; set; } = false;
    public bool ShowMusic { get; set; } = true;
    public bool ShowPicture { get; set; } = true;
    public bool ShowMusicVideo { get; set; } = true;
    public bool ShowRadio { get; set; } = true;
    public bool ShowTV { get; set; } = true;
    public bool ShowPodcast { get; set; } = true;
    public bool ShowEBooks { get; set; } = true;
    public bool ShowAudioBooks { get; set; } = true;
    public bool ShowSettings { get; set; } = true;
}

public class HttpsConfig
{
    public bool Enabled { get; set; } = false;
    public int HttpsPort { get; set; } = 8183;
    public bool RedirectHttpToHttps { get; set; } = false;
    /// <summary>Path to a custom PFX certificate. Empty = use auto-generated cert.</summary>
    public string CertPath { get; set; } = "";
    /// <summary>Password for a custom PFX certificate. Empty = use internal default for auto-generated cert.</summary>
    public string CertPassword { get; set; } = "";
    /// <summary>
    /// Extra IP addresses to include in the auto-generated cert SANs. Comma-separated.
    /// Needed in Docker where the container cannot detect the host's real LAN IP.
    /// </summary>
    public string DockerHostIPs { get; set; } = "";
}

public class ServerConfig
{
    public int ServerPort { get; set; } = 8182;
    public string ServerHost { get; set; } = "0.0.0.0";
    public int WorkerThreads { get; set; } = 0;
    public int RequestTimeout { get; set; } = 300;
    public int SessionTimeout { get; set; } = 12;
    public bool ShowConsole { get; set; } = false;
    public bool OpenBrowser { get; set; } = true;
    public bool RunOnStartup { get; set; } = false;
}

public class SecurityConfig
{
    public bool SecurityByPin { get; set; } = true;
    public string DefaultAdminUser { get; set; } = "admin";
    public string IPWhitelist { get; set; } = "";

    public List<string> GetIPWhitelistList() =>
        IPWhitelist.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}

public class LibraryConfig
{
    public string MusicFolders { get; set; } = "";
    public string MoviesTVFolders { get; set; } = "";
    public string MoviesFolders { get; set; } = "";
    public string TvShowsFolders { get; set; } = "";
    public string PicturesFolders { get; set; } = "";
    public string MusicVideosFolders { get; set; } = "";
    public string EBooksFolders { get; set; } = "";
    public string AudioBooksFolders { get; set; } = "";
    public string AnimeFolders { get; set; } = "";
    public string AudioExtensions { get; set; } = ".mp3,.flac,.wav,.m4a,.aac,.ogg,.wma,.opus,.ape,.alac,.aiff,.dsf,.dff";
    public string ImageExtensions { get; set; } = ".jpg,.jpeg,.png,.gif,.bmp,.webp,.tiff,.heic,.heif,.cr2,.cr3,.crw,.nef,.nrw,.arw,.srf,.sr2,.dng,.orf,.rw2,.raf,.srw,.pef,.x3f,.3fr,.mef,.mrw,.dcr,.kdc,.raw";
    public string EBookExtensions { get; set; } = ".pdf,.epub,.cbz,.cbr";
    public string AudioBooksExtensions { get; set; } = ".mp3,.m4b,.m4a,.aac,.ogg,.opus,.flac";
    public string MusicVideoExtensions { get; set; } = ".mp4,.mkv,.avi,.mov,.flv,.webm,.m4v";
    public string VideoExtensions { get; set; } = ".mp4,.mkv,.avi,.mov,.flv,.webm,.m4v,.wmv,.ts,.m2ts";
    public bool AutoScanOnStartup { get; set; } = false;
    public int AutoScanInterval { get; set; } = 0;
    public int ScanThreads { get; set; } = 4;
    public bool DynamicCleanEnabled { get; set; } = false;
    public int DynamicCleanIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Which tag drives the Music → Artists grouping:
    ///   "artist"              → the per-track ARTIST tag (default, original behaviour)
    ///   "albumartist"         → the ALBUMARTIST tag, applied live at query time (Option B - no rescan)
    ///   "albumartist-indexed" → same grouping, but the scanner also materialises Artist rows
    ///                            from the album-artist name (Option A - needs a full rescan)
    /// </summary>
    public string ArtistGrouping { get; set; } = "artist";

    /// <summary>
    /// Splits a comma-separated path string into a trimmed list.
    /// e.g. "C:\Movies, D:\Films" → ["C:\Movies", "D:\Films"]
    /// </summary>
    private static List<string> MultiplePaths(string? value) =>
        (value ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

    public List<string> GetMusicFolderList() => MultiplePaths(MusicFolders);

    public List<string> GetMoviesTVFolderList() => MultiplePaths(MoviesTVFolders);

    public List<string> GetMoviesFolderList() => MultiplePaths(MoviesFolders);

    public List<string> GetTvShowsFolderList() => MultiplePaths(TvShowsFolders);

    /// <summary>Returns all video folders (Movies + TV Shows). Falls back to legacy MoviesTVFolders if both new fields are empty.</summary>
    public List<string> GetAllVideoFolderList()
    {
        var all = new List<string>();
        all.AddRange(GetMoviesFolderList());
        all.AddRange(GetTvShowsFolderList());
        if (all.Count == 0) all.AddRange(GetMoviesTVFolderList());
        return all.Distinct().ToList();
    }

    public List<string> GetPicturesFolderList() => MultiplePaths(PicturesFolders);

    public List<string> GetMusicVideosFolderList() => MultiplePaths(MusicVideosFolders);

    public List<string> GetEBooksFolderList() => MultiplePaths(EBooksFolders);

    public List<string> GetAudioBooksFolderList() => MultiplePaths(AudioBooksFolders);

    public List<string> GetAnimeFolderList() => MultiplePaths(AnimeFolders);

    public List<string> GetAudioExtensionList() =>
        AudioExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public List<string> GetImageExtensionList() =>
        ImageExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public List<string> GetEBookExtensionList() =>
        EBookExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public List<string> GetAudioBooksExtensionList() =>
        AudioBooksExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public List<string> GetMusicVideoExtensionList() =>
        MusicVideoExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public List<string> GetVideoExtensionList() =>
        VideoExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}

public class PlaybackConfig
{
    public bool TranscodingEnabled { get; set; } = false;
    public string TranscodeFormat { get; set; } = "mp3";
    public string TranscodeBitrate { get; set; } = "192k";
    public string FFmpegPath { get; set; } = "";
    /// <summary>Show "Skip Intro" button when chapter-based intro timestamps are detected.</summary>
    public bool IntroSkipperEnabled { get; set; } = false;
    /// <summary>Enable "Watch Together" (WatchTogether): synchronised group playback of the same title across users.</summary>
    public bool WatchTogetherEnabled { get; set; } = true;
}

public class TranscodingConfig
{
    /// <summary>
    /// Hardware acceleration preference: auto, nvenc, qsv, amf, software
    /// "auto" detects the best GPU encoder at startup.
    /// </summary>
    public string PreferredEncoder { get; set; } = "auto";

    /// <summary>
    /// Output video codec: "h264" (default, universal browser support) or
    /// "av1" (better compression; requires Chrome/Firefox/Edge/Safari 17+).
    /// When "av1" is selected and the active GPU supports hardware AV1 encoding
    /// (av1_nvenc on RTX 4000+, av1_qsv on Intel Arc/12th gen+, av1_amf on RX 7000+),
    /// the hardware encoder is used. If the GPU doesn't support hardware AV1, falls back
    /// to H.264 on the same GPU. Without a GPU, uses SVT-AV1 software encoding (CPU).
    /// </summary>
    public string PreferredVideoCodec { get; set; } = "h264";

    // Video quality
    public string VideoCodec { get; set; } = "h264";
    public string VideoPreset { get; set; } = "veryfast";
    public int VideoCRF { get; set; } = 23;
    public string VideoMaxrate { get; set; } = "5M";
    public string VideoBufsize { get; set; } = "10M";

    // Audio
    public string AudioCodec { get; set; } = "aac";
    public string AudioBitrate { get; set; } = "192k";
    public int AudioChannels { get; set; } = 2;

    // Performance
    public int MaxConcurrentTranscodes { get; set; } = 2;

    /// <summary>
    /// Maximum number of concurrent HEVC passthrough remuxes (video is stream-copied,
    /// audio transcoded to AAC). Separate from MaxConcurrentTranscodes because passthrough
    /// is a different cost profile: near-zero CPU for video, but each ffmpeg races the whole
    /// file to disk UNTHROTTLED (no -re), so N simultaneous cold-starts contend for disk I/O
    /// and mux throughput - the cause of the multi-client "segment took 15-20s" stall
    /// (diagnosed 2026-08-06). This bounds that burst. When the cap is hit a new stream waits
    /// up to 30s for a slot, then falls back to a full transcode. Raise it on fast storage
    /// (NVMe/SSD); lower it to 1 on a single mechanical drive. 0 or negative snaps to 1.
    /// </summary>
    public int MaxConcurrentPassthrough { get; set; } = 3;

    /// <summary>
    /// CPU usage limit for FFmpeg software transcoding (0-100%, 0 = unlimited).
    /// Controls thread count: threads = CPUCount * limit / 100.
    /// Also sets process priority (BelowNormal if &lt;= 50%, Normal if > 50%).
    /// </summary>
    public int FFmpegCPULimit { get; set; } = 0;

    /// <summary>
    /// Process priority for remux operations: normal, abovenormal, high.
    /// Higher priority makes remux complete faster for better UX.
    /// </summary>
    public string RemuxPriority { get; set; } = "abovenormal";

    /// <summary>
    /// Number of threads for remux I/O operations (0 = auto).
    /// More threads can speed up container conversion.
    /// </summary>
    public int RemuxThreads { get; set; } = 0;

    // Resolution limiting (0 = no limit, or 1080, 720)
    public int TranscodeMaxHeight { get; set; } = 0;

    // HLS streaming settings
    public int HLSSegmentDuration { get; set; } = 4;

    // HLS cache settings
    public bool HLSCacheEnabled { get; set; } = true;
    public int HLSCacheMaxSizeGB { get; set; } = 100;
    public int HLSCacheRetentionDays { get; set; } = 90;

    /// <summary>
    /// When the last viewer leaves, let FFmpeg keep running to the end of the file so the
    /// cache completes and nobody ever has to transcode this title again.
    ///
    /// OFF by default and deliberately so: it burns CPU for a title nobody is watching,
    /// which defeats FFmpegCPULimit from the user's point of view. With resume-on-restart
    /// (see TryPlanResume) the partial cache is picked up where it stopped instead, so the
    /// work is never lost - it is just deferred to the next viewer, who pays for it while
    /// actually watching. Turn this on only on a machine that has CPU to spare.
    /// </summary>
    public bool KeepTranscodingAfterLastViewer { get; set; } = false;

    /// <summary>
    /// Allowed client bitrate caps in kbps. A request is snapped DOWN to the nearest rung.
    ///
    /// The cache key includes the bitrate cap, so an arbitrary value would mint a whole
    /// separate transcode + cache folder of the same film. Snapping to a ladder bounds the
    /// number of variants per title, so two clients asking for 5000 and 4800 share one
    /// cached transcode instead of encoding the film twice.
    /// </summary>
    public string BitrateLadderKbps { get; set; } = "2000,5000,10000,20000";

    public List<int> GetBitrateLadder() =>
        BitrateLadderKbps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : 0)
            .Where(v => v > 0).OrderBy(v => v).ToList();

    // Formats that require full transcoding (not just remux)
    public string TranscodeFormats { get; set; } = ".mkv,.avi,.wmv,.flv,.mov,.mpg,.mpeg,.vob,.ts,.webm,.divx,.3gp";

    public List<string> GetTranscodeFormatList() =>
        TranscodeFormats.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>
    /// HDR playback mode: "auto" copies any HEVC file directly to HLS fMP4 (no re-encode) -
    /// HDR metadata is preserved and the display handles tonemapping. "sdr" forces a full
    /// H.264/SDR transcode (slow CPU zscale tonemap). Default "auto".
    /// </summary>
    public string HdrPlaybackMode { get; set; } = "auto";
}

public class DatabaseConfig
{
    public string DatabasePath { get; set; } = "data/music.db";
    public string PicturesDatabasePath { get; set; } = "data/pictures.db";
    public string EBooksDatabasePath { get; set; } = "data/ebooks.db";
    public string MusicVideosDatabasePath { get; set; } = "data/musicvideos.db";
    public string VideosDatabasePath { get; set; } = "data/videos.db";
    public string UsersDatabasePath { get; set; } = "data/users.db";
    public string TvChannelsDatabasePath { get; set; } = "data/tvchannels.db";
    public string SharesDatabasePath { get; set; } = "data/shares.db";
    public string ActorsDatabasePath { get; set; } = "data/actors.db";
    public string RatingsDatabasePath { get; set; } = "data/ratings.db";
    public string PodcastsDatabasePath { get; set; } = "data/podcasts.db";
    public string AudioBooksDatabasePath { get; set; } = "data/audiobooks.db";
    public string EpgDatabasePath { get; set; } = "data/epguidetv.db";
}

public class LoggingConfig
{
    public string LogLevel { get; set; } = "Information";
    public string LogFile { get; set; } = "logs/nexusm.log";
    public int MaxLogSizeMB { get; set; } = 50;
}

public class UIConfig
{
    public string DefaultView { get; set; } = "grid";
    public string Theme { get; set; } = "dark";
    public string Language { get; set; } = "en";
    /// <summary>How calendar dates are displayed across the UI. One of:
    /// "auto" (follow the browser/UI language, default), "dmy" (dd/mm/yyyy),
    /// "mdy" (mm/dd/yyyy) or "iso" (yyyy-mm-dd). Global - applies to all users.</summary>
    public string DateFormat { get; set; } = "auto";
    public bool ShowMusic { get; set; } = true;
    public bool ShowPictures { get; set; } = true;
    public bool ShowMoviesTV { get; set; } = true;
    public bool ShowMovies { get; set; } = true;
    public bool ShowTvShows { get; set; } = true;
    public bool ShowMusicVideos { get; set; } = true;
    public bool ShowRadio { get; set; } = true;
    public bool ShowInternetTV { get; set; } = true;
    public bool ShowEBooks { get; set; } = true;
    public bool ShowAudioBooks { get; set; } = true;
    public bool ShowActors { get; set; } = true;
    public bool ShowPodcasts { get; set; } = true;
    public bool ShowAnime { get; set; } = true;
    /// <summary>Launch Go Big / TV Mode automatically on startup.</summary>
    public bool GoBigDefault { get; set; } = false;
    /// <summary>Show the welcome popup on startup. Set to False once the user dismisses it.</summary>
    public bool ShowWelcome { get; set; } = true;
    /// <summary>Active UI template id. Empty string = built-in default.</summary>
    public string Template { get; set; } = "";
}

public class MetadataConfig
{
    /// <summary>Metadata provider: tvmaze (free, TV only), tmdb (movies+TV, bundled key or user's own), none</summary>
    public string Provider { get; set; } = "tmdb";
    /// <summary>TMDB API key - the USER's own key from themoviedb.org, if they set one.
    /// Empty by default; when empty the bundled developer key is used instead (see
    /// <see cref="EffectiveTmdbApiKey"/>). This field is the only value ever written to NexusM.conf.</summary>
    public string TmdbApiKey { get; set; } = "";

    /// <summary>
    /// Bundled NexusM TMDB developer key (v3, non-commercial). Used for all TMDB
    /// requests when the user has NOT configured their own key. Kept ONLY in source -
    /// it is never written to NexusM.conf and never sent to the frontend. When a user
    /// enters their own key it is persisted to NexusM.conf and takes precedence.
    /// </summary>
    public const string BundledTmdbApiKey = "0072c7ddad551ec12f3e28a89a394e7d";

    /// <summary>The key actually used for TMDB calls: the user's own key when set, else the bundled default.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveTmdbApiKey =>
        string.IsNullOrWhiteSpace(TmdbApiKey) ? BundledTmdbApiKey : TmdbApiKey;

    /// <summary>True when TMDB can be used (always true now that a key is bundled).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasTmdbKey => !string.IsNullOrWhiteSpace(EffectiveTmdbApiKey);
    /// <summary>Watchmode API key (free tier at api.watchmode.com) - used for "Where to Watch?" streaming search</summary>
    public string WatchmodeApiKey { get; set; } = "";
    /// <summary>fanart.tv API key (user provides their own from fanart.tv/get-an-api-key) - alternative posters/backdrops</summary>
    public string FanartApiKey { get; set; } = "";
    /// <summary>
    /// TMDB language for scraped text (overviews, titles), as an ISO 639-1 code
    /// optionally with a region, e.g. "en-US", "fr-FR", "pt-BR". Genre names are
    /// deliberately NOT affected - they stay English so grouping/filtering keeps
    /// working across items scraped in different languages.
    /// </summary>
    public string ScrapeLanguage { get; set; } = "en-US";
    /// <summary>Auto-fetch metadata during library scan</summary>
    public bool FetchOnScan { get; set; } = true;
    /// <summary>Download cast member photos</summary>
    public bool FetchCastPhotos { get; set; } = true;

    /// <summary>
    /// How local Kodi/Jellyfin .nfo sidecar files are used during a scan:
    /// "off" (ignore), "online-then-nfo" (scrape TMDB first, NFO fills the blanks - default),
    /// "nfo-then-online" (read NFO first, TMDB fills gaps), "nfo-only" (never scrape online).
    /// </summary>
    public string NfoMode { get; set; } = "online-then-nfo";

    /// <summary>Automatically write/update a sidecar .nfo whenever an admin edits a video's metadata.</summary>
    public bool NfoAutoExport { get; set; } = false;
}

public class SubtitlesConfig
{
    /// <summary>OpenSubtitles.com consumer API key - register free at https://www.opensubtitles.com/consumers</summary>
    public string OpenSubtitlesApiKey { get; set; } = "";
    /// <summary>OpenSubtitles.com account username</summary>
    public string OpenSubtitlesUsername { get; set; } = "";
    /// <summary>OpenSubtitles.com account password</summary>
    public string OpenSubtitlesPassword { get; set; } = "";
    /// <summary>SubDL.com API key - register free at https://subdl.com</summary>
    public string SubDlApiKey { get; set; } = "";
}

public class TraktConfig
{
    /// <summary>Trakt.tv application Client ID (from trakt.tv/apps).</summary>
    public string ClientId { get; set; } = "";
    /// <summary>Trakt.tv application Client Secret (from trakt.tv/apps).</summary>
    public string ClientSecret { get; set; } = "";
    /// <summary>Automatically scrobble playback events to Trakt.tv.</summary>
    public bool ScrobbleEnabled { get; set; } = false;
}

public class LastFmConfig
{
    /// <summary>Last.fm API key - register your app at https://www.last.fm/api/account/create</summary>
    public string ApiKey { get; set; } = "";
    /// <summary>Last.fm API shared secret for signing requests.</summary>
    public string ApiSecret { get; set; } = "";
}

public class DlnaConfig
{
    /// <summary>Enable the DLNA/UPnP media server (SSDP discovery + ContentDirectory).
    /// Exposes the library to DLNA clients (Roku, smart TVs, etc.) on the LAN.
    /// No authentication - LAN-only access enforced by private-IP guard.
    /// Restart required after changing this value.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Friendly name shown in DLNA client device lists.</summary>
    public string FriendlyName { get; set; } = "NexusM Media Server";
}

public class AnalysisConfig
{
    /// <summary>Enable background deep media analysis (ffprobe per video file for multi-track details).</summary>
    public bool DeepScanEnabled { get; set; } = true;
    /// <summary>Minutes to wait between scan batches (1-1440).</summary>
    public int DeepScanIntervalMinutes { get; set; } = 60;
    /// <summary>Milliseconds to pause between each file analysis - keeps CPU/disk pressure low.</summary>
    public int DeepScanDelayMs { get; set; } = 1500;
    /// <summary>Max video files to analyze per batch run.</summary>
    public int DeepScanBatchSize { get; set; } = 20;
}

public class VideoThumbnailsConfig
{
    /// <summary>Generate sprite thumbnail sheets for video seek-bar preview on hover.</summary>
    public bool Enabled { get; set; } = false;
    /// <summary>One preview frame every N seconds (5–60).</summary>
    public int IntervalSeconds { get; set; } = 10;
}

public class VideoPreviewsConfig
{
    /// <summary>Generate 30-second preview clips for the Netflux hero banner.</summary>
    public bool Enabled { get; set; } = false;
    /// <summary>Maximum video clips to generate per minute (1–20). Keeps CPU/disk load low.</summary>
    public int RatePerMinute { get; set; } = 5;
}

/// <summary>
/// Natural-language (semantic) search - local, in-process embedding search over the whole
/// library. Opt-in and OFF by default: nothing is downloaded, indexed, or loaded until Enabled.
/// The embedding model lives in assets/semantic-search/; the vector index in data/semantic.db.
/// </summary>
public class SemanticSearchConfig
{
    /// <summary>Master switch. OFF = no model load, no indexing, search falls back to keyword.</summary>
    public bool Enabled { get; set; } = false;
    /// <summary>Embedding model id (folder + download source). Default all-MiniLM-L6-v2 (Apache-2.0).</summary>
    public string ModelId { get; set; } = "all-MiniLM-L6-v2";
    /// <summary>ONNX Runtime CPU thread cap (intra/inter-op). 1 keeps it gentle; 0 = library default.</summary>
    public int CpuThreads { get; set; } = 1;
    /// <summary>Pause background indexing while anything is transcoding/streaming.</summary>
    public bool IndexOnlyWhenIdle { get; set; } = true;
    /// <summary>Maximum results returned by a semantic query.</summary>
    public int MaxResults { get; set; } = 40;
    /// <summary>Dialogue (subtitle) full-text index. Separate, heavier opt-in: extracts embedded/sidecar
    /// subtitles into an FTS5 table for exact-quote "find the line" search. OFF by default.</summary>
    public bool IndexDialogue { get; set; } = false;
    /// <summary>FFmpeg thread cap for the subtitle-extraction worker. 0 = FFmpeg default.</summary>
    public int DialogueCpuThreads { get; set; } = 1;
    /// <summary>OS priority of the extraction worker: normal | belownormal | idle. Lower keeps playback smooth.</summary>
    public string DialoguePriority { get; set; } = "belownormal";
    /// <summary>Pause between each video during extraction, in milliseconds. 0 = no pause.</summary>
    public int DialogueThrottleMs { get; set; } = 0;
    /// <summary>Pause extraction while anything is transcoding, then resume. Protects live playback.</summary>
    public bool DialoguePauseWhileStreaming { get; set; } = true;
    /// <summary>Scene (concept) index. Chunks subtitles into ~45s windows and embeds each so you can
    /// search a scene by MEANING ("the scene where he vows to return") and jump to it. Needs the
    /// master model; shares the dialogue worker resource controls. Heavier opt-in, OFF by default.</summary>
    public bool IndexScenes { get; set; } = false;
    /// <summary>eBook (EPUB) full-text index. Parses each EPUB's text into passages so you can search
    /// INSIDE your books and jump to the passage. Pure FTS5 (no model needed); shares the dialogue
    /// worker throttle/pause controls. Opt-in, OFF by default.</summary>
    public bool IndexBooks { get; set; } = false;
    /// <summary>Automatically pick up newly-scanned media: after a library scan finishes, run an
    /// incremental index pass (metadata, and dialogue if enabled) so new titles are searchable
    /// without a manual "Index new items". ON by default. Only acts when the feature is enabled.</summary>
    public bool AutoIndexOnScan { get; set; } = true;
}

public class RemoteAccessConfig
{
    /// <summary>Whether remote access is currently enabled. Persisted in NexusM.conf.</summary>
    public bool Enabled { get; set; } = false;
    /// <summary>External TCP port exposed to the internet (same as HTTPS port by default).</summary>
    public int ExternalPort { get; set; } = 8183;
    /// <summary>Attempt UPnP IGD port mapping automatically when enabling remote access.</summary>
    public bool UPnPEnabled { get; set; } = true;
    /// <summary>Whether the NexusM Relay tunnel (nexusm.net) is enabled.</summary>
    public bool TunnelEnabled { get; set; } = false;
}

/// <summary>
/// Pre-authentication access gate - blocks external visitors with an invite code
/// before NexusM's login page is shown. LAN requests always bypass the gate.
/// </summary>
public class GateConfig
{
    public bool Enabled    { get; set; } = false;
    /// <summary>How long the gate cookie remains valid (days). Options: 10, 30, 90, 180, 360.</summary>
    public int  CookieDays { get; set; } = 30;
    /// <summary>Server-side HMAC secret for signing gate cookies. Auto-generated on first run.</summary>
    public string HmacSecret { get; set; } = "";
    /// <summary>Up to 10 invite codes, each with an optional label and expiry date.</summary>
    public List<GateCodeEntry> Codes { get; set; } = new();
}

public class AutoUpdateConfig
{
    public bool Enabled { get; set; } = false;
    public int CheckIntervalHours { get; set; } = 24;
    public string Channel { get; set; } = "stable";
    public string LastChecked { get; set; } = "";
    public string AvailableVersion { get; set; } = "";
}

/// <summary>One invite-code slot in the Access Gate.</summary>
public class GateCodeEntry
{
    public string    Code    { get; set; } = "";
    public string    Label   { get; set; } = "";
    public DateTime? Expires { get; set; }
}
