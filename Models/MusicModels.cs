using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NexusM.Models;

/// <summary>
/// Represents a music track in the library database.
/// </summary>
public class Track
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string FilePath { get; set; } = "";

    public string FileName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string AlbumArtist { get; set; } = "";
    public string Album { get; set; } = "";
    public int? Year { get; set; }
    public int? TrackNumber { get; set; }
    public int? DiscNumber { get; set; }
    public string Genre { get; set; } = "";
    public string Composer { get; set; } = "";

    /// <summary>Duration in seconds</summary>
    public double Duration { get; set; }

    /// <summary>Bitrate in kbps</summary>
    public int Bitrate { get; set; }

    /// <summary>Sample rate in Hz (e.g., 44100)</summary>
    public int SampleRate { get; set; }

    /// <summary>Number of audio channels</summary>
    public int Channels { get; set; }

    /// <summary>Audio codec (MP3, FLAC, AAC, etc.)</summary>
    public string Codec { get; set; } = "";

    /// <summary>File size in bytes</summary>
    public long FileSize { get; set; }

    /// <summary>MIME type (audio/mpeg, audio/flac, etc.)</summary>
    public string MimeType { get; set; } = "";

    /// <summary>Whether album art is embedded in the file</summary>
    public bool HasAlbumArt { get; set; }

    /// <summary>Cached album art filename (e.g., albumart_123.jpg) stored in assets/albumart/</summary>
    public string? AlbumArtCached { get; set; }

    /// <summary>Lyrics if embedded in tags</summary>
    public string? Lyrics { get; set; }

    /// <summary>MusicBrainz track ID if available</summary>
    public string? MusicBrainzId { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public DateTime FileModified { get; set; }
    public DateTime LastScanned { get; set; } = DateTime.UtcNow;

    /// <summary>Play count for this track</summary>
    public int PlayCount { get; set; }

    /// <summary>Last played timestamp</summary>
    public DateTime? LastPlayed { get; set; }

    /// <summary>User rating 0-5</summary>
    public int Rating { get; set; }

    /// <summary>Favourite flag</summary>
    public bool IsFavourite { get; set; }

    // Navigation properties
    public int? AlbumId { get; set; }
    [ForeignKey("AlbumId")]
    public Album? AlbumEntity { get; set; }
}

/// <summary>
/// Represents an album (grouping of tracks).
/// </summary>
public class Album
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = "";

    public string Artist { get; set; } = "";
    public int? Year { get; set; }
    public string Genre { get; set; } = "";

    /// <summary>Path to extracted/cached album art image</summary>
    public string? CoverArtPath { get; set; }

    public int TrackCount { get; set; }
    public double TotalDuration { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public bool IsFavourite { get; set; }
    public int Rating { get; set; }

    // Navigation
    public ICollection<Track> Tracks { get; set; } = new List<Track>();
}

/// <summary>
/// Represents an artist.
/// </summary>
public class Artist
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = "";

    public string? Bio { get; set; }
    public string? ImagePath { get; set; }
    public int AlbumCount { get; set; }
    public int TrackCount { get; set; }
    public bool IsFavourite { get; set; }
}

/// <summary>
/// Represents a user-created playlist.
/// </summary>
public class Playlist
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = "";

    public string? Description { get; set; }
    public string? CoverImagePath { get; set; }
    public bool IsPublic { get; set; } = true;

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    public DateTime DateModified { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<PlaylistTrack> PlaylistTracks { get; set; } = new List<PlaylistTrack>();
}

/// <summary>
/// Join table for Playlist <-> Track (many-to-many with ordering).
/// </summary>
public class PlaylistTrack
{
    [Key]
    public int Id { get; set; }

    public int PlaylistId { get; set; }
    [ForeignKey("PlaylistId")]
    public Playlist? Playlist { get; set; }

    public int TrackId { get; set; }
    [ForeignKey("TrackId")]
    public Track? Track { get; set; }

    /// <summary>Position/order within the playlist</summary>
    public int Position { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Application user (for multi-user support with PIN authentication).
/// </summary>
public class AppUser
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Username { get; set; } = "";

    public string? DisplayName { get; set; }

    /// <summary>PBKDF2-HMAC-SHA256 hashed PIN</summary>
    public string PinHash { get; set; } = "";

    /// <summary>Random salt used for PIN hashing (Base64)</summary>
    public string PinSalt { get; set; } = "";

    /// <summary>User role: admin or guest</summary>
    public string Role { get; set; } = "guest";

    public bool IsActive { get; set; } = true;

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    public DateTime? LastLogin { get; set; }
}

/// <summary>
/// Scan status tracking.
/// </summary>
public class ScanStatus
{
    [Key]
    public int Id { get; set; }

    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string Status { get; set; } = "running"; // running, completed, failed
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public int NewTracks { get; set; }
    public int UpdatedTracks { get; set; }
    public int ErrorCount { get; set; }
    public string? ErrorMessage { get; set; }
}
