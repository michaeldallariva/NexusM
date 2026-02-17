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
}

/// <summary>
/// Tracks progress of an ongoing pictures scan.
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
    internal int _errorCount;

    public int ProcessedFiles => _processedFiles;
    public int NewPictures => _newPictures;
    public int UpdatedPictures => _updatedPictures;
    public int ErrorCount => _errorCount;

    public double PercentComplete => TotalFiles > 0
        ? Math.Round((double)_processedFiles / TotalFiles * 100, 1) : 0;
}
