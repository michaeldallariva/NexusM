using System.ComponentModel.DataAnnotations;

namespace NexusM.Models;

/// <summary>
/// Represents an Internet TV channel imported from an M3U playlist.
/// </summary>
public class TvChannel
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = "";

    public string Country { get; set; } = "";
    public string Genre { get; set; } = "";

    [Required]
    public string StreamUrl { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>Logo filename stored in assets/tvlogos/</summary>
    public string Logo { get; set; } = "";

    /// <summary>tvg-id from M3U file, used for EPG matching</summary>
    public string TvgId { get; set; } = "";

    /// <summary>Resolution info extracted from channel name, e.g. "1080p", "720p"</summary>
    public string Resolution { get; set; } = "";

    /// <summary>Source playlist filename this channel was imported from</summary>
    public string SourcePlaylist { get; set; } = "";

    public bool IsFavourite { get; set; }
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Tracks progress of an ongoing TV logo fetch.
/// </summary>
public class TvLogoFetchProgress
{
    public bool IsFetching { get; set; }
    public int Progress { get; set; }
    public int Total { get; set; }
    public int Success { get; set; }
    public int Failed { get; set; }
    public string Status { get; set; } = "";
}
