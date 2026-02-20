using System.ComponentModel.DataAnnotations;

namespace NexusM.Models;

/// <summary>
/// Represents an eBook (PDF or EPUB) in the eBooks library database.
/// </summary>
public class EBook
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string FileName { get; set; } = "";

    [Required]
    public string FilePath { get; set; } = "";

    /// <summary>Title extracted from metadata, or filename if unavailable</summary>
    public string Title { get; set; } = "";

    /// <summary>Author(s) extracted from metadata</summary>
    public string Author { get; set; } = "";

    /// <summary>File format: PDF, EPUB</summary>
    public string Format { get; set; } = "";

    public long FileSize { get; set; }

    /// <summary>Number of pages (PDF only, 0 for EPUB)</summary>
    public int PageCount { get; set; }

    /// <summary>First-level subfolder name under the configured eBooks root</summary>
    public string Category { get; set; } = "";

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public DateTime LastModified { get; set; }

    // Optional metadata fields
    public string? Publisher { get; set; }
    public string? Language { get; set; }
    public string? ISBN { get; set; }
    public string? Description { get; set; }
    public string? Subject { get; set; }

    /// <summary>Cover image filename stored in assets/ebookcovers/ (e.g., epub_cover_5.jpg)</summary>
    public string? CoverImage { get; set; }
}

/// <summary>
/// Tracks progress of an ongoing eBooks scan.
/// </summary>
public class EBookScanProgress
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
