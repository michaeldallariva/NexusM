using System.ComponentModel.DataAnnotations;

namespace NexusM.Models;

public class PodcastFeed
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Title { get; set; } = "";

    public string Description { get; set; } = "";
    public string Author { get; set; } = "";

    [Required]
    public string RssUrl { get; set; } = "";

    public string ArtworkUrl { get; set; } = "";
    public string ArtworkFile { get; set; } = "";
    public string Category { get; set; } = "";
    public string Language { get; set; } = "";
    public int EpisodeCount { get; set; } = 0;
    public DateTime? LastRefreshed { get; set; }
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    public ICollection<PodcastEpisode> Episodes { get; set; } = new List<PodcastEpisode>();
}

public class PodcastEpisode
{
    [Key]
    public int Id { get; set; }

    public int FeedId { get; set; }
    public PodcastFeed? Feed { get; set; }

    [Required]
    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    [Required]
    public string MediaUrl { get; set; } = "";

    /// <summary>"audio" or "video"</summary>
    public string MediaType { get; set; } = "audio";

    public int DurationSeconds { get; set; } = 0;
    public DateTime? PublishDate { get; set; }

    /// <summary>RSS item GUID — used for deduplication.</summary>
    public string Guid { get; set; } = "";

    public bool IsPlayed { get; set; } = false;
    public int PlayPositionSeconds { get; set; } = 0;
    public DateTime DateFetched { get; set; } = DateTime.UtcNow;
}
