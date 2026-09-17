using System.ComponentModel.DataAnnotations;

namespace NexusM.Models;

/// <summary>
/// Represents an Audio Book (MP3 or M4B) in the audio books library database.
/// </summary>
public class AudioBook
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string FileName { get; set; } = "";

    [Required]
    public string FilePath { get; set; } = "";

    /// <summary>Title extracted from metadata, or filename if unavailable</summary>
    public string Title { get; set; } = "";

    /// <summary>Author extracted from AlbumArtist tag</summary>
    public string Author { get; set; } = "";

    /// <summary>Narrator extracted from Artist/Performer tag</summary>
    public string Narrator { get; set; } = "";

    /// <summary>File format: MP3, M4B</summary>
    public string Format { get; set; } = "";

    public long FileSize { get; set; }

    /// <summary>Duration in seconds</summary>
    public double Duration { get; set; }

    /// <summary>Series / collection name extracted from the Album tag, or set manually.</summary>
    public string? Series { get; set; }

    /// <summary>Position within the collection (manual); null if unknown.</summary>
    public double? SeriesIndex { get; set; }

    /// <summary>First-level subfolder name under the configured audio books root</summary>
    public string Category { get; set; } = "";

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public DateTime LastModified { get; set; }

    public int? Year { get; set; }

    // Optional metadata fields
    public string? Description { get; set; }
    public string? Publisher { get; set; }
    public string? Language { get; set; }

    /// <summary>Cover image filename stored in assets/audiobookcovers/</summary>
    public string? CoverImage { get; set; }

    /// <summary>JSON array of chapters extracted by ffprobe: [{title,start},...]. Null if no chapters or ffprobe unavailable.</summary>
    public string? ChaptersJson { get; set; }
}

/// <summary>
/// Tracks progress of an ongoing audio books scan.
/// </summary>
public class AudioBookScanProgress
{
    public string Status { get; set; } = "idle";
    public string Message { get; set; } = "";
    public DateTime? StartTime { get; set; }
    public int TotalFiles { get; set; }

    internal int _processedFiles;
    internal int _newBooks;
    internal int _updatedBooks;
    internal int _errorCount;

    public int ProcessedFiles => _processedFiles;
    public int NewBooks => _newBooks;
    public int UpdatedBooks => _updatedBooks;
    public int ErrorCount => _errorCount;

    public double PercentComplete => TotalFiles > 0
        ? Math.Round((double)_processedFiles / TotalFiles * 100, 1) : 0;
}
