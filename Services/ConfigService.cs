using IniParser;
using IniParser.Model;
using NexusM.Models;

namespace NexusM.Services;

/// <summary>
/// Loads and manages the NexusM.conf configuration file.
/// Uses INI format similar to NexusM's .conf approach.
/// </summary>
public class ConfigService
{
    private readonly string _configPath;
    private readonly ILogger<ConfigService> _logger;
    private AppConfig _config;

    public AppConfig Config => _config;

    public ConfigService(ILogger<ConfigService> logger)
    {
        _logger = logger;
        _config = new AppConfig();

        // Look for config file next to the executable, or in current directory
        var appDir = AppContext.BaseDirectory;
        _configPath = Path.Combine(appDir, "NexusM.conf");

        // Also check working directory (for development)
        if (!File.Exists(_configPath))
        {
            var workDir = Directory.GetCurrentDirectory();
            var altPath = Path.Combine(workDir, "NexusM.conf");
            if (File.Exists(altPath))
                _configPath = altPath;
        }

        LoadOrCreate();
    }

    /// <summary>
    /// Load existing config or create default one.
    /// </summary>
    private void LoadOrCreate()
    {
        if (File.Exists(_configPath))
        {
            _logger.LogInformation("Loading configuration from {Path}", _configPath);
            LoadConfig();
        }
        else
        {
            _logger.LogWarning("Configuration file not found at {Path}. Creating default...", _configPath);
            CreateDefault();
            LoadConfig();
        }
    }

    /// <summary>
    /// Parse the .conf file and populate the AppConfig object.
    /// </summary>
    private void LoadConfig()
    {
        try
        {
            var parser = new FileIniDataParser();
            // Allow comments with #
            parser.Parser.Configuration.CommentString = "#";
            parser.Parser.Configuration.AllowDuplicateKeys = false;

            IniData data = parser.ReadFile(_configPath);

            // Server section
            var server = data["Server"];
            if (server != null)
            {
                _config.Server.ServerPort = ParseInt(server["ServerPort"], 8182);
                _config.Server.ServerHost = server["ServerHost"] ?? "0.0.0.0";
                _config.Server.WorkerThreads = ParseInt(server["WorkerThreads"], 0);
                _config.Server.RequestTimeout = ParseInt(server["RequestTimeout"], 300);
                _config.Server.SessionTimeout = ParseInt(server["SessionTimeout"], 28800);
                _config.Server.ShowConsole = ParseBool(server["ShowConsole"], false);
                _config.Server.OpenBrowser = ParseBool(server["OpenBrowser"], true);
                _config.Server.RunOnStartup = ParseBool(server["RunOnStartup"], false);
            }

            // Security section
            var security = data["Security"];
            if (security != null)
            {
                _config.Security.SecurityByPin = ParseBool(security["SecurityByPin"] ?? security["AuthEnabled"], true);
                _config.Security.DefaultAdminUser = security["DefaultAdminUser"] ?? "admin";
                _config.Security.IPWhitelist = security["IPWhitelist"] ?? "";
            }

            // Library section
            var library = data["Library"];
            if (library != null)
            {
                _config.Library.MusicFolders = library["MusicFolders"] ?? "";
                _config.Library.MoviesTVFolders = library["MoviesTVFolders"] ?? "";
                _config.Library.PicturesFolders = library["PicturesFolders"] ?? "";
                _config.Library.MusicVideosFolders = library["MusicVideosFolders"] ?? "";
                _config.Library.EBooksFolders = library["EBooksFolders"] ?? "";
                _config.Library.AudioExtensions = library["AudioExtensions"] ?? ".mp3,.flac,.wav,.m4a,.aac,.ogg,.wma,.opus";
                _config.Library.ImageExtensions = library["ImageExtensions"] ?? ".jpg,.jpeg,.png,.gif,.bmp,.webp,.tiff";
                _config.Library.EBookExtensions = library["EBookExtensions"] ?? ".pdf,.epub";
                _config.Library.MusicVideoExtensions = library["MusicVideoExtensions"] ?? ".mp4,.mkv,.avi,.mov,.flv,.webm,.m4v";
                _config.Library.VideoExtensions = library["VideoExtensions"] ?? ".mp4,.mkv,.avi,.mov,.flv,.webm,.m4v,.wmv,.ts";
                _config.Library.AutoScanOnStartup = ParseBool(library["AutoScanOnStartup"], false);
                _config.Library.AutoScanInterval = ParseInt(library["AutoScanInterval"], 0);
                _config.Library.ScanThreads = ParseInt(library["ScanThreads"], 4);
            }

            // Playback section
            var playback = data["Playback"];
            if (playback != null)
            {
                _config.Playback.TranscodingEnabled = ParseBool(playback["TranscodingEnabled"], false);
                _config.Playback.TranscodeFormat = playback["TranscodeFormat"] ?? "mp3";
                _config.Playback.TranscodeBitrate = playback["TranscodeBitrate"] ?? "192k";
                _config.Playback.FFmpegPath = playback["FFmpegPath"] ?? "";
            }

            // Transcoding section (video hardware transcoding)
            var transcoding = data["Transcoding"];
            if (transcoding != null)
            {
                _config.Transcoding.PreferredEncoder = transcoding["PreferredEncoder"] ?? "auto";
                _config.Transcoding.VideoCodec = transcoding["VideoCodec"] ?? "h264";
                _config.Transcoding.VideoPreset = transcoding["VideoPreset"] ?? "veryfast";
                _config.Transcoding.VideoCRF = ParseInt(transcoding["VideoCRF"], 23);
                _config.Transcoding.VideoMaxrate = transcoding["VideoMaxrate"] ?? "5M";
                _config.Transcoding.VideoBufsize = transcoding["VideoBufsize"] ?? "10M";
                _config.Transcoding.AudioCodec = transcoding["AudioCodec"] ?? "aac";
                _config.Transcoding.AudioBitrate = transcoding["AudioBitrate"] ?? "192k";
                _config.Transcoding.AudioChannels = ParseInt(transcoding["AudioChannels"], 2);
                _config.Transcoding.MaxConcurrentTranscodes = ParseInt(transcoding["MaxConcurrentTranscodes"], 2);
                _config.Transcoding.FFmpegCPULimit = ParseInt(transcoding["FFmpegCPULimit"], 0);
                _config.Transcoding.RemuxPriority = transcoding["RemuxPriority"] ?? "abovenormal";
                _config.Transcoding.RemuxThreads = ParseInt(transcoding["RemuxThreads"], 0);
                _config.Transcoding.TranscodeMaxHeight = ParseInt(transcoding["TranscodeMaxHeight"], 0);
                _config.Transcoding.HLSSegmentDuration = ParseInt(transcoding["HLSSegmentDuration"], 4);
                _config.Transcoding.HLSCacheEnabled = ParseBool(transcoding["HLSCacheEnabled"], true);
                _config.Transcoding.HLSCacheMaxSizeGB = ParseInt(transcoding["HLSCacheMaxSizeGB"], 100);
                _config.Transcoding.HLSCacheRetentionDays = ParseInt(transcoding["HLSCacheRetentionDays"], 90);
                _config.Transcoding.TranscodeFormats = transcoding["TranscodeFormats"] ?? ".mkv,.avi,.wmv,.flv,.mov,.mpg,.mpeg,.vob,.ts,.webm,.divx,.3gp";
            }

            // Database section
            var database = data["Database"];
            if (database != null)
            {
                _config.Database.DatabasePath = database["DatabasePath"] ?? "data/music.db";
                _config.Database.PicturesDatabasePath = database["PicturesDatabasePath"] ?? "data/pictures.db";
                _config.Database.EBooksDatabasePath = database["EBooksDatabasePath"] ?? "data/ebooks.db";
                _config.Database.MusicVideosDatabasePath = database["MusicVideosDatabasePath"] ?? "data/musicvideos.db";
                _config.Database.VideosDatabasePath = database["VideosDatabasePath"] ?? "data/videos.db";
                _config.Database.UsersDatabasePath = database["UsersDatabasePath"] ?? "data/users.db";
                _config.Database.TvChannelsDatabasePath = database["TvChannelsDatabasePath"] ?? "data/tvchannels.db";
                _config.Database.SharesDatabasePath = database["SharesDatabasePath"] ?? "data/shares.db";
                _config.Database.ActorsDatabasePath = database["ActorsDatabasePath"] ?? "data/actors.db";
                _config.Database.PodcastsDatabasePath = database["PodcastsDatabasePath"] ?? "data/podcasts.db";
            }

            // Resolve database paths relative to AppContext.BaseDirectory (same as assets/ and cache/)
            var appDir = AppContext.BaseDirectory;
            if (!Path.IsPathRooted(_config.Database.DatabasePath))
                _config.Database.DatabasePath = Path.Combine(appDir, _config.Database.DatabasePath);
            if (!Path.IsPathRooted(_config.Database.PicturesDatabasePath))
                _config.Database.PicturesDatabasePath = Path.Combine(appDir, _config.Database.PicturesDatabasePath);
            if (!Path.IsPathRooted(_config.Database.EBooksDatabasePath))
                _config.Database.EBooksDatabasePath = Path.Combine(appDir, _config.Database.EBooksDatabasePath);
            if (!Path.IsPathRooted(_config.Database.MusicVideosDatabasePath))
                _config.Database.MusicVideosDatabasePath = Path.Combine(appDir, _config.Database.MusicVideosDatabasePath);
            if (!Path.IsPathRooted(_config.Database.VideosDatabasePath))
                _config.Database.VideosDatabasePath = Path.Combine(appDir, _config.Database.VideosDatabasePath);
            if (!Path.IsPathRooted(_config.Database.UsersDatabasePath))
                _config.Database.UsersDatabasePath = Path.Combine(appDir, _config.Database.UsersDatabasePath);
            if (!Path.IsPathRooted(_config.Database.TvChannelsDatabasePath))
                _config.Database.TvChannelsDatabasePath = Path.Combine(appDir, _config.Database.TvChannelsDatabasePath);
            if (!Path.IsPathRooted(_config.Database.SharesDatabasePath))
                _config.Database.SharesDatabasePath = Path.Combine(appDir, _config.Database.SharesDatabasePath);
            if (!Path.IsPathRooted(_config.Database.ActorsDatabasePath))
                _config.Database.ActorsDatabasePath = Path.Combine(appDir, _config.Database.ActorsDatabasePath);
            if (!Path.IsPathRooted(_config.Database.PodcastsDatabasePath))
                _config.Database.PodcastsDatabasePath = Path.Combine(appDir, _config.Database.PodcastsDatabasePath);

            // Logging section
            var logging = data["Logging"];
            if (logging != null)
            {
                _config.Logging.LogLevel = logging["LogLevel"] ?? "Information";
                _config.Logging.LogFile = logging["LogFile"] ?? "logs/nexusm.log";
                _config.Logging.MaxLogSizeMB = ParseInt(logging["MaxLogSizeMB"], 50);
            }

            // UI section
            var ui = data["UI"];
            if (ui != null)
            {
                _config.UI.DefaultView = ui["DefaultView"] ?? "grid";
                _config.UI.Theme = ui["Theme"] ?? "dark";
                _config.UI.Language = ui["Language"] ?? "en";
                _config.UI.ShowMoviesTV = ParseBool(ui["ShowMoviesTV"], true);
                _config.UI.ShowMusicVideos = ParseBool(ui["ShowMusicVideos"], true);
                _config.UI.ShowRadio = ParseBool(ui["ShowRadio"], true);
                _config.UI.ShowInternetTV = ParseBool(ui["ShowInternetTV"], true);
                _config.UI.ShowEBooks = ParseBool(ui["ShowEBooks"], true);
                _config.UI.ShowActors = ParseBool(ui["ShowActors"], true);
                _config.UI.ShowPodcasts = ParseBool(ui["ShowPodcasts"], true);
            }

            // Metadata section
            var metadata = data["Metadata"];
            if (metadata != null)
            {
                _config.Metadata.Provider = metadata["Provider"] ?? "tvmaze";
                _config.Metadata.TmdbApiKey = metadata["TmdbApiKey"] ?? "";
                _config.Metadata.FetchOnScan = ParseBool(metadata["FetchOnScan"], true);
                _config.Metadata.FetchCastPhotos = ParseBool(metadata["FetchCastPhotos"], true);
            }

            // Validate port range (1-65535)
            if (_config.Server.ServerPort < 1 || _config.Server.ServerPort > 65535)
            {
                _logger.LogWarning("ServerPort {Port} is outside valid range 1-65535, defaulting to 8182", _config.Server.ServerPort);
                _config.Server.ServerPort = 8182;
            }

            // Ensure all current keys are present; adds new ones with defaults if conf is from an older version
            MigrateConfig(data);

            _logger.LogInformation("Configuration loaded successfully. Port: {Port}, Host: {Host}",
                _config.Server.ServerPort, _config.Server.ServerHost);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading configuration file. Using defaults.");
            _config = new AppConfig();
        }
    }

    /// <summary>
    /// Create the default NexusM.conf file.
    /// </summary>
    private void CreateDefault()
    {
        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Copy default conf content
            var defaultContent = GetDefaultConfigContent();
            File.WriteAllText(_configPath, defaultContent);
            _logger.LogInformation("Created default configuration file at {Path}", _configPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create default configuration file");
        }
    }

    private static string GetDefaultConfigContent() => """
        # ============================================================
        # NexusM Configuration File
        # ============================================================
        # Edit this file to customize your NexusM settings.
        # Lines starting with # are comments and ignored.
        # Restart the application after making changes.
        # ============================================================

        [Server]
        ServerPort=8182
        ServerHost=0.0.0.0
        WorkerThreads=0
        RequestTimeout=300
        SessionTimeout=28800
        ShowConsole=False
        OpenBrowser=True

        [Security]
        SecurityByPin=True
        DefaultAdminUser=admin
        IPWhitelist=

        [Library]
        MusicFolders=
        MoviesTVFolders=
        PicturesFolders=
        MusicVideosFolders=
        EBooksFolders=
        AudioExtensions=.mp3,.flac,.wav,.m4a,.aac,.ogg,.wma,.opus,.ape,.alac,.aiff,.dsf,.dff
        ImageExtensions=.jpg,.jpeg,.png,.gif,.bmp,.webp,.tiff
        EBookExtensions=.pdf,.epub
        MusicVideoExtensions=.mp4,.mkv,.avi,.mov,.flv,.webm,.m4v
        VideoExtensions=.mp4,.mkv,.avi,.mov,.flv,.webm,.m4v,.wmv,.ts
        AutoScanOnStartup=False
        AutoScanInterval=0
        ScanThreads=4

        [Playback]
        TranscodingEnabled=False
        TranscodeFormat=mp3
        TranscodeBitrate=192k
        FFmpegPath=

        [Transcoding]
        # Hardware encoder: auto, nvenc, qsv, amf, software
        # "auto" detects NVIDIA/Intel/AMD GPU and tests the best encoder at startup
        PreferredEncoder=auto
        VideoCodec=h264
        VideoPreset=veryfast
        VideoCRF=23
        VideoMaxrate=5M
        VideoBufsize=10M
        AudioCodec=aac
        AudioBitrate=192k
        AudioChannels=2
        MaxConcurrentTranscodes=2
        # CPU limit for software transcoding (0-100%, 0 = unlimited)
        FFmpegCPULimit=0
        # Remux process priority: normal, abovenormal, high (higher = faster remux)
        RemuxPriority=abovenormal
        # Remux I/O threads (0 = auto)
        RemuxThreads=0
        # Max output height (0=no limit, 1080, 720)
        TranscodeMaxHeight=0
        # HLS segment duration in seconds
        HLSSegmentDuration=4
        # HLS cache (persistent across restarts)
        HLSCacheEnabled=True
        HLSCacheMaxSizeGB=100
        HLSCacheRetentionDays=90
        # File extensions that require full transcoding
        TranscodeFormats=.mkv,.avi,.wmv,.flv,.mov,.mpg,.mpeg,.vob,.ts,.webm,.divx,.3gp

        [Metadata]
        # Metadata provider: tvmaze (free, TV only), tmdb (requires API key, movies+TV), none
        Provider=tvmaze
        # TMDB API key (get yours free at https://www.themoviedb.org/settings/api)
        TmdbApiKey=
        # Fetch metadata during library scan (True/False)
        FetchOnScan=True
        # Download cast photos (True/False)
        FetchCastPhotos=True

        [Database]
        DatabasePath=data/music.db
        PicturesDatabasePath=data/pictures.db
        EBooksDatabasePath=data/ebooks.db
        MusicVideosDatabasePath=data/musicvideos.db
        VideosDatabasePath=data/videos.db
        UsersDatabasePath=data/users.db
        TvChannelsDatabasePath=data/tvchannels.db
        SharesDatabasePath=data/shares.db
        ActorsDatabasePath=data/actors.db
        PodcastsDatabasePath=data/podcasts.db

        [Logging]
        LogLevel=Information
        LogFile=logs/nexusm.log
        MaxLogSizeMB=50

        [UI]
        DefaultView=grid
        Theme=dark
        Language=en
        ShowMoviesTV=True
        ShowMusicVideos=True
        ShowRadio=True
        ShowInternetTV=True
        ShowEBooks=True
        ShowActors=True
        ShowPodcasts=True
        """;

    /// <summary>
    /// Checks the loaded IniData for any missing keys and adds them with their default values.
    /// Writes the file back only if at least one key was added.
    /// Existing user values are never modified.
    /// </summary>
    private void MigrateConfig(IniData data)
    {
        bool changed = false;

        void Ensure(string section, string key, string defaultValue)
        {
            if (!data.Sections.ContainsSection(section))
            {
                data.Sections.AddSection(section);
                changed = true;
            }
            if (!data[section].ContainsKey(key))
            {
                data[section].AddKey(key, defaultValue);
                changed = true;
                _logger.LogInformation("Config migration: added [{Section}] {Key} = {Default}", section, key, defaultValue);
            }
        }

        // Server
        Ensure("Server", "ServerPort", "8182");
        Ensure("Server", "ServerHost", "0.0.0.0");
        Ensure("Server", "WorkerThreads", "0");
        Ensure("Server", "RequestTimeout", "300");
        Ensure("Server", "SessionTimeout", "28800");
        Ensure("Server", "ShowConsole", "False");
        Ensure("Server", "OpenBrowser", "True");
        Ensure("Server", "RunOnStartup", "False");

        // Security
        Ensure("Security", "SecurityByPin", "True");
        Ensure("Security", "DefaultAdminUser", "admin");
        Ensure("Security", "IPWhitelist", "");

        // Library
        Ensure("Library", "MusicFolders", "");
        Ensure("Library", "MoviesTVFolders", "");
        Ensure("Library", "PicturesFolders", "");
        Ensure("Library", "MusicVideosFolders", "");
        Ensure("Library", "EBooksFolders", "");
        Ensure("Library", "AudioExtensions", ".mp3,.flac,.wav,.m4a,.aac,.ogg,.wma,.opus,.ape,.alac,.aiff,.dsf,.dff");
        Ensure("Library", "ImageExtensions", ".jpg,.jpeg,.png,.gif,.bmp,.webp,.tiff");
        Ensure("Library", "EBookExtensions", ".pdf,.epub");
        Ensure("Library", "MusicVideoExtensions", ".mp4,.mkv,.avi,.mov,.flv,.webm,.m4v");
        Ensure("Library", "VideoExtensions", ".mp4,.mkv,.avi,.mov,.flv,.webm,.m4v,.wmv,.ts");
        Ensure("Library", "AutoScanOnStartup", "False");
        Ensure("Library", "AutoScanInterval", "0");
        Ensure("Library", "ScanThreads", "4");

        // Playback
        Ensure("Playback", "TranscodingEnabled", "False");
        Ensure("Playback", "TranscodeFormat", "mp3");
        Ensure("Playback", "TranscodeBitrate", "192k");
        Ensure("Playback", "FFmpegPath", "");

        // Transcoding
        Ensure("Transcoding", "PreferredEncoder", "auto");
        Ensure("Transcoding", "VideoCodec", "h264");
        Ensure("Transcoding", "VideoPreset", "veryfast");
        Ensure("Transcoding", "VideoCRF", "23");
        Ensure("Transcoding", "VideoMaxrate", "5M");
        Ensure("Transcoding", "VideoBufsize", "10M");
        Ensure("Transcoding", "AudioCodec", "aac");
        Ensure("Transcoding", "AudioBitrate", "192k");
        Ensure("Transcoding", "AudioChannels", "2");
        Ensure("Transcoding", "MaxConcurrentTranscodes", "2");
        Ensure("Transcoding", "FFmpegCPULimit", "0");
        Ensure("Transcoding", "RemuxPriority", "abovenormal");
        Ensure("Transcoding", "RemuxThreads", "0");
        Ensure("Transcoding", "TranscodeMaxHeight", "0");
        Ensure("Transcoding", "HLSSegmentDuration", "4");
        Ensure("Transcoding", "HLSCacheEnabled", "True");
        Ensure("Transcoding", "HLSCacheMaxSizeGB", "100");
        Ensure("Transcoding", "HLSCacheRetentionDays", "90");
        Ensure("Transcoding", "TranscodeFormats", ".mkv,.avi,.wmv,.flv,.mov,.mpg,.mpeg,.vob,.ts,.webm,.divx,.3gp");

        // Metadata
        Ensure("Metadata", "Provider", "tvmaze");
        Ensure("Metadata", "TmdbApiKey", "");
        Ensure("Metadata", "FetchOnScan", "True");
        Ensure("Metadata", "FetchCastPhotos", "True");

        // Database
        Ensure("Database", "DatabasePath", "data/music.db");
        Ensure("Database", "PicturesDatabasePath", "data/pictures.db");
        Ensure("Database", "EBooksDatabasePath", "data/ebooks.db");
        Ensure("Database", "MusicVideosDatabasePath", "data/musicvideos.db");
        Ensure("Database", "VideosDatabasePath", "data/videos.db");
        Ensure("Database", "UsersDatabasePath", "data/users.db");
        Ensure("Database", "TvChannelsDatabasePath", "data/tvchannels.db");
        Ensure("Database", "SharesDatabasePath", "data/shares.db");
        Ensure("Database", "ActorsDatabasePath", "data/actors.db");
        Ensure("Database", "PodcastsDatabasePath", "data/podcasts.db");

        // Logging
        Ensure("Logging", "LogLevel", "Information");
        Ensure("Logging", "LogFile", "logs/nexusm.log");
        Ensure("Logging", "MaxLogSizeMB", "50");

        // UI
        Ensure("UI", "DefaultView", "grid");
        Ensure("UI", "Theme", "dark");
        Ensure("UI", "Language", "en");
        Ensure("UI", "ShowMoviesTV", "True");
        Ensure("UI", "ShowMusicVideos", "True");
        Ensure("UI", "ShowRadio", "True");
        Ensure("UI", "ShowInternetTV", "True");
        Ensure("UI", "ShowEBooks", "True");
        Ensure("UI", "ShowActors", "True");
        Ensure("UI", "ShowPodcasts", "True");

        if (!changed) return;

        try
        {
            var parser = new FileIniDataParser();
            parser.Parser.Configuration.CommentString = "#";
            parser.WriteFile(_configPath, data);
            _logger.LogInformation("Configuration migrated: missing keys added with defaults.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save migrated configuration (non-critical).");
        }
    }

    /// <summary>
    /// Save the current in-memory configuration back to NexusM.conf.
    /// Database paths are converted back to relative paths before saving.
    /// </summary>
    public void SaveConfig()
    {
        _logger.LogInformation("Saving configuration to {Path}", _configPath);

        var data = new IniData();
        var appDir = AppContext.BaseDirectory;

        // Helper to convert absolute DB path back to relative
        string RelativeDbPath(string absPath)
        {
            if (absPath.StartsWith(appDir, StringComparison.OrdinalIgnoreCase))
                return absPath[appDir.Length..].Replace('\\', '/');
            return absPath;
        }

        // Server
        data["Server"]["ServerPort"] = _config.Server.ServerPort.ToString();
        data["Server"]["ServerHost"] = _config.Server.ServerHost;
        data["Server"]["WorkerThreads"] = _config.Server.WorkerThreads.ToString();
        data["Server"]["RequestTimeout"] = _config.Server.RequestTimeout.ToString();
        data["Server"]["SessionTimeout"] = _config.Server.SessionTimeout.ToString();
        data["Server"]["ShowConsole"] = _config.Server.ShowConsole ? "True" : "False";
        data["Server"]["OpenBrowser"] = _config.Server.OpenBrowser ? "True" : "False";
        data["Server"]["RunOnStartup"] = _config.Server.RunOnStartup ? "True" : "False";

        // Security
        data["Security"]["SecurityByPin"] = _config.Security.SecurityByPin ? "True" : "False";
        data["Security"]["DefaultAdminUser"] = _config.Security.DefaultAdminUser;
        data["Security"]["IPWhitelist"] = _config.Security.IPWhitelist;

        // Library
        data["Library"]["MusicFolders"] = _config.Library.MusicFolders;
        data["Library"]["MoviesTVFolders"] = _config.Library.MoviesTVFolders;
        data["Library"]["PicturesFolders"] = _config.Library.PicturesFolders;
        data["Library"]["MusicVideosFolders"] = _config.Library.MusicVideosFolders;
        data["Library"]["EBooksFolders"] = _config.Library.EBooksFolders;
        data["Library"]["AudioExtensions"] = _config.Library.AudioExtensions;
        data["Library"]["ImageExtensions"] = _config.Library.ImageExtensions;
        data["Library"]["EBookExtensions"] = _config.Library.EBookExtensions;
        data["Library"]["MusicVideoExtensions"] = _config.Library.MusicVideoExtensions;
        data["Library"]["VideoExtensions"] = _config.Library.VideoExtensions;
        data["Library"]["AutoScanOnStartup"] = _config.Library.AutoScanOnStartup ? "True" : "False";
        data["Library"]["AutoScanInterval"] = _config.Library.AutoScanInterval.ToString();
        data["Library"]["ScanThreads"] = _config.Library.ScanThreads.ToString();

        // Playback
        data["Playback"]["TranscodingEnabled"] = _config.Playback.TranscodingEnabled ? "True" : "False";
        data["Playback"]["TranscodeFormat"] = _config.Playback.TranscodeFormat;
        data["Playback"]["TranscodeBitrate"] = _config.Playback.TranscodeBitrate;
        data["Playback"]["FFmpegPath"] = _config.Playback.FFmpegPath;

        // Transcoding (video hardware transcoding)
        data["Transcoding"]["PreferredEncoder"] = _config.Transcoding.PreferredEncoder;
        data["Transcoding"]["VideoCodec"] = _config.Transcoding.VideoCodec;
        data["Transcoding"]["VideoPreset"] = _config.Transcoding.VideoPreset;
        data["Transcoding"]["VideoCRF"] = _config.Transcoding.VideoCRF.ToString();
        data["Transcoding"]["VideoMaxrate"] = _config.Transcoding.VideoMaxrate;
        data["Transcoding"]["VideoBufsize"] = _config.Transcoding.VideoBufsize;
        data["Transcoding"]["AudioCodec"] = _config.Transcoding.AudioCodec;
        data["Transcoding"]["AudioBitrate"] = _config.Transcoding.AudioBitrate;
        data["Transcoding"]["AudioChannels"] = _config.Transcoding.AudioChannels.ToString();
        data["Transcoding"]["MaxConcurrentTranscodes"] = _config.Transcoding.MaxConcurrentTranscodes.ToString();
        data["Transcoding"]["FFmpegCPULimit"] = _config.Transcoding.FFmpegCPULimit.ToString();
        data["Transcoding"]["RemuxPriority"] = _config.Transcoding.RemuxPriority;
        data["Transcoding"]["RemuxThreads"] = _config.Transcoding.RemuxThreads.ToString();
        data["Transcoding"]["TranscodeMaxHeight"] = _config.Transcoding.TranscodeMaxHeight.ToString();
        data["Transcoding"]["HLSSegmentDuration"] = _config.Transcoding.HLSSegmentDuration.ToString();
        data["Transcoding"]["HLSCacheEnabled"] = _config.Transcoding.HLSCacheEnabled ? "True" : "False";
        data["Transcoding"]["HLSCacheMaxSizeGB"] = _config.Transcoding.HLSCacheMaxSizeGB.ToString();
        data["Transcoding"]["HLSCacheRetentionDays"] = _config.Transcoding.HLSCacheRetentionDays.ToString();
        data["Transcoding"]["TranscodeFormats"] = _config.Transcoding.TranscodeFormats;

        // Database (convert back to relative)
        data["Database"]["DatabasePath"] = RelativeDbPath(_config.Database.DatabasePath);
        data["Database"]["PicturesDatabasePath"] = RelativeDbPath(_config.Database.PicturesDatabasePath);
        data["Database"]["EBooksDatabasePath"] = RelativeDbPath(_config.Database.EBooksDatabasePath);
        data["Database"]["MusicVideosDatabasePath"] = RelativeDbPath(_config.Database.MusicVideosDatabasePath);
        data["Database"]["VideosDatabasePath"] = RelativeDbPath(_config.Database.VideosDatabasePath);
        data["Database"]["UsersDatabasePath"] = RelativeDbPath(_config.Database.UsersDatabasePath);
        data["Database"]["TvChannelsDatabasePath"] = RelativeDbPath(_config.Database.TvChannelsDatabasePath);
        data["Database"]["SharesDatabasePath"] = RelativeDbPath(_config.Database.SharesDatabasePath);
        data["Database"]["ActorsDatabasePath"] = RelativeDbPath(_config.Database.ActorsDatabasePath);
        data["Database"]["PodcastsDatabasePath"] = RelativeDbPath(_config.Database.PodcastsDatabasePath);

        // Logging
        data["Logging"]["LogLevel"] = _config.Logging.LogLevel;
        data["Logging"]["LogFile"] = _config.Logging.LogFile;
        data["Logging"]["MaxLogSizeMB"] = _config.Logging.MaxLogSizeMB.ToString();

        // Metadata
        data["Metadata"]["Provider"] = _config.Metadata.Provider;
        data["Metadata"]["TmdbApiKey"] = _config.Metadata.TmdbApiKey;
        data["Metadata"]["FetchOnScan"] = _config.Metadata.FetchOnScan ? "True" : "False";
        data["Metadata"]["FetchCastPhotos"] = _config.Metadata.FetchCastPhotos ? "True" : "False";

        // UI
        data["UI"]["DefaultView"] = _config.UI.DefaultView;
        data["UI"]["Theme"] = _config.UI.Theme;
        data["UI"]["Language"] = _config.UI.Language;
        data["UI"]["ShowMoviesTV"] = _config.UI.ShowMoviesTV ? "True" : "False";
        data["UI"]["ShowMusicVideos"] = _config.UI.ShowMusicVideos ? "True" : "False";
        data["UI"]["ShowRadio"] = _config.UI.ShowRadio ? "True" : "False";
        data["UI"]["ShowInternetTV"] = _config.UI.ShowInternetTV ? "True" : "False";
        data["UI"]["ShowEBooks"] = _config.UI.ShowEBooks ? "True" : "False";
        data["UI"]["ShowActors"] = _config.UI.ShowActors ? "True" : "False";
        data["UI"]["ShowPodcasts"] = _config.UI.ShowPodcasts ? "True" : "False";

        var parser = new FileIniDataParser();
        parser.WriteFile(_configPath, data);

        _logger.LogInformation("Configuration saved successfully");

        // Reload to ensure in-memory state matches disk
        Reload();
    }

    /// <summary>
    /// Reload configuration from disk.
    /// </summary>
    public void Reload()
    {
        _logger.LogInformation("Reloading configuration...");
        _config = new AppConfig();
        LoadConfig();
    }

    public string ConfigFilePath => _configPath;

    private static int ParseInt(string? value, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        return int.TryParse(value.Trim(), out var result) ? result : defaultValue;
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        var v = value.Trim().ToLowerInvariant();
        return v switch
        {
            "true" or "yes" or "1" or "y" => true,
            "false" or "no" or "0" or "n" => false,
            _ => defaultValue
        };
    }
}
