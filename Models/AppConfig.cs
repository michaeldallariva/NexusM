namespace NexusM.Models;

/// <summary>
/// Application configuration loaded from NexusM.conf
/// Mirrors the NexusM .conf approach with INI-style sections.
/// </summary>
public class AppConfig
{
    public ServerConfig Server { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();
    public LibraryConfig Library { get; set; } = new();
    public PlaybackConfig Playback { get; set; } = new();
    public TranscodingConfig Transcoding { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
    public UIConfig UI { get; set; } = new();
    public MetadataConfig Metadata { get; set; } = new();
}

public class ServerConfig
{
    public int ServerPort { get; set; } = 8182;
    public string ServerHost { get; set; } = "0.0.0.0";
    public int WorkerThreads { get; set; } = 0;
    public int RequestTimeout { get; set; } = 300;
    public int SessionTimeout { get; set; } = 28800;
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
    public string PicturesFolders { get; set; } = "";
    public string MusicVideosFolders { get; set; } = "";
    public string EBooksFolders { get; set; } = "";
    public string AudioExtensions { get; set; } = ".mp3,.flac,.wav,.m4a,.aac,.ogg,.wma,.opus,.ape,.alac,.aiff,.dsf,.dff";
    public string ImageExtensions { get; set; } = ".jpg,.jpeg,.png,.gif,.bmp,.webp,.tiff";
    public string EBookExtensions { get; set; } = ".pdf,.epub";
    public string MusicVideoExtensions { get; set; } = ".mp4,.mkv,.avi,.mov,.flv,.webm,.m4v";
    public string VideoExtensions { get; set; } = ".mp4,.mkv,.avi,.mov,.flv,.webm,.m4v,.wmv,.ts";
    public bool AutoScanOnStartup { get; set; } = false;
    public int AutoScanInterval { get; set; } = 0;
    public int ScanThreads { get; set; } = 4;

    public List<string> GetMusicFolderList() =>
        MusicFolders.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public List<string> GetMoviesTVFolderList() =>
        MoviesTVFolders.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public List<string> GetPicturesFolderList() =>
        PicturesFolders.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public List<string> GetMusicVideosFolderList() =>
        MusicVideosFolders.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public List<string> GetEBooksFolderList() =>
        EBooksFolders.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public List<string> GetAudioExtensionList() =>
        AudioExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public List<string> GetImageExtensionList() =>
        ImageExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public List<string> GetEBookExtensionList() =>
        EBookExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

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
}

public class TranscodingConfig
{
    /// <summary>
    /// Hardware acceleration preference: auto, nvenc, qsv, amf, software
    /// "auto" detects the best GPU encoder at startup.
    /// </summary>
    public string PreferredEncoder { get; set; } = "auto";

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

    // Formats that require full transcoding (not just remux)
    public string TranscodeFormats { get; set; } = ".mkv,.avi,.wmv,.flv,.mov,.mpg,.mpeg,.vob,.ts,.webm,.divx,.3gp";

    public List<string> GetTranscodeFormatList() =>
        TranscodeFormats.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
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
    public bool ShowMoviesTV { get; set; } = true;
    public bool ShowMusicVideos { get; set; } = true;
    public bool ShowRadio { get; set; } = true;
    public bool ShowInternetTV { get; set; } = true;
    public bool ShowEBooks { get; set; } = true;
    public bool ShowActors { get; set; } = true;
    public bool ShowPodcasts { get; set; } = true;
}

public class MetadataConfig
{
    /// <summary>Metadata provider: tvmaze (free, TV only), tmdb (API key, movies+TV), none</summary>
    public string Provider { get; set; } = "tvmaze";
    /// <summary>TMDB API key (user provides their own from themoviedb.org)</summary>
    public string TmdbApiKey { get; set; } = "";
    /// <summary>Auto-fetch metadata during library scan</summary>
    public bool FetchOnScan { get; set; } = true;
    /// <summary>Download cast member photos</summary>
    public bool FetchCastPhotos { get; set; } = true;
}
