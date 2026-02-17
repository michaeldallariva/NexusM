using System.ComponentModel.DataAnnotations;

namespace NexusM.Models;

/// <summary>
/// Represents a music video in the music videos library database.
/// </summary>
public class MusicVideo
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string FileName { get; set; } = "";

    [Required]
    public string FilePath { get; set; } = "";

    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
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
    public int Bitrate { get; set; }

    public string Genre { get; set; } = "";

    /// <summary>Thumbnail filename stored in assets/mvthumbs/ (e.g. mvthumb_5.jpg)</summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>Position of moov atom: "beginning" or "end"</summary>
    public string MoovPosition { get; set; } = "";

    public string StreamOrder { get; set; } = "";

    /// <summary>Whether the video needs optimization (moov at end, surround audio, etc.)</summary>
    public bool NeedsOptimization { get; set; }

    /// <summary>Whether the video is a compliant MP4 for direct browser streaming</summary>
    public bool Mp4Compliant { get; set; } = true;

    /// <summary>Number of audio channels (2=stereo, 6=5.1, etc.)</summary>
    public int AudioChannels { get; set; } = 2;

    public bool IsFavourite { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public DateTime LastModified { get; set; }
    public DateTime? LastPlayed { get; set; }
    public int PlayCount { get; set; }
}

/// <summary>
/// Tracks progress of an ongoing music videos scan.
/// </summary>
public class MusicVideoScanProgress
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
