using System.ComponentModel.DataAnnotations;

namespace NexusM.Models;

/// <summary>
/// Represents a picture in the pictures library database.
/// </summary>
public class Picture
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string FileName { get; set; } = "";

    [Required]
    public string FilePath { get; set; } = "";

    public int Width { get; set; }
    public int Height { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>Image format (JPEG, PNG, GIF, etc.)</summary>
    public string Format { get; set; } = "";

    /// <summary>EXIF date taken</summary>
    public DateTime? DateTaken { get; set; }

    public string? CameraMake { get; set; }
    public string? CameraModel { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public DateTime LastModified { get; set; }

    /// <summary>First-level subfolder name under the configured pictures root</summary>
    public string Category { get; set; } = "";

    /// <summary>Relative thumbnail filename, e.g., "picthumb_123.jpg"</summary>
    public string? ThumbnailPath { get; set; }

    // EXIF fields
    public int? IsoSpeed { get; set; }
    public string? ExposureTime { get; set; }
    public string? FNumber { get; set; }
    public string? FocalLength { get; set; }
    public string? Flash { get; set; }
    public int? Orientation { get; set; }
    public double? DpiX { get; set; }
    public double? DpiY { get; set; }
    public string? LensModel { get; set; }
    public string? Software { get; set; }

    /// <summary>GPS latitude from EXIF (WGS84, positive = North)</summary>
    public double? GpsLat { get; set; }

    /// <summary>GPS longitude from EXIF (WGS84, positive = East)</summary>
    public double? GpsLon { get; set; }

    /// <summary>Human-readable place name resolved via Nominatim reverse geocoding</summary>
    public string? PlaceName { get; set; }

    /// <summary>True when XMP GPano:ProjectionType=equirectangular (360° photosphere)</summary>
    public bool Is360 { get; set; }

    /// <summary>True when a companion video exists (Apple LivePhoto sidecar or Google/Samsung MotionPhoto)</summary>
    public bool IsLivePhoto { get; set; }

    /// <summary>Path to the companion video file (sidecar .MOV or extracted MotionPhoto .mp4)</summary>
    public string? LivePhotoVideoPath { get; set; }
}

/// <summary>
/// Tracks progress of an ongoing pictures/videos scan.
/// </summary>
public class PictureScanProgress
{
    public string Status { get; set; } = "idle";
    public string Message { get; set; } = "";
    public DateTime? StartTime { get; set; }
    public int TotalFiles { get; set; }

    internal int _processedFiles;
    internal int _newPictures;
    internal int _updatedPictures;
    internal int _newVideos;
    internal int _updatedVideos;
    internal int _errorCount;

    public int ProcessedFiles => _processedFiles;
    public int NewPictures => _newPictures;
    public int UpdatedPictures => _updatedPictures;
    public int NewVideos => _newVideos;
    public int UpdatedVideos => _updatedVideos;
    public int ErrorCount => _errorCount;

    public double PercentComplete => TotalFiles > 0
        ? Math.Round((double)_processedFiles / TotalFiles * 100, 1) : 0;
}

/// <summary>
/// Represents a personal video in the pictures/videos library database.
/// </summary>
public class PictureVideo
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string FileName { get; set; } = "";

    [Required]
    public string FilePath { get; set; } = "";

    public long SizeBytes { get; set; }

    /// <summary>File format - MP4, MKV, MOV, etc.</summary>
    public string Format { get; set; } = "";

    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Video duration in seconds.</summary>
    public int DurationSeconds { get; set; }

    /// <summary>Relative thumbnail filename, e.g., "pvthumb_123.jpg"</summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>First-level subfolder name under the configured pictures root</summary>
    public string Category { get; set; } = "";

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public DateTime LastModified { get; set; }

    /// <summary>Creation date from file system or metadata</summary>
    public DateTime? DateTaken { get; set; }
}

/// <summary>
/// A user-created album in the pictures/videos library.
/// </summary>
public class PictureAlbum
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>Username of the album owner.</summary>
    [Required]
    public string Username { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Thumbnail filename from the cover item.</summary>
    public string? CoverThumbnail { get; set; }

    /// <summary>"picture" or "video" - which table CoverMediaId refers to.</summary>
    public string? CoverMediaType { get; set; }
    public int? CoverMediaId { get; set; }
}

/// <summary>
/// Maps a picture or video to an album.
/// </summary>
public class PictureAlbumItem
{
    [Key]
    public int Id { get; set; }

    public int AlbumId { get; set; }
    public int MediaId { get; set; }

    /// <summary>"picture" or "video"</summary>
    public string MediaType { get; set; } = "picture";

    public int Position { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
