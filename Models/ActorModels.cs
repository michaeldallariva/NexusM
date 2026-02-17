using System.ComponentModel.DataAnnotations;

namespace NexusM.Models;

/// <summary>
/// Represents an actor in the actors database.
/// </summary>
public class Actor
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = "";

    [Required]
    public string NormalizedName { get; set; } = "";

    public int? TmdbId { get; set; }

    public string? ProfilePath { get; set; }

    public string? ImageCached { get; set; }

    public string KnownForDepartment { get; set; } = "Acting";

    public double? Popularity { get; set; }

    public string? Birthday { get; set; }

    public string? Deathday { get; set; }

    public string? PlaceOfBirth { get; set; }

    public string? Biography { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Join table linking actors to videos (movies/TV episodes).
/// </summary>
public class MovieActor
{
    [Key]
    public int Id { get; set; }

    public int VideoId { get; set; }

    public int ActorId { get; set; }

    public string? CharacterName { get; set; }

    public int BillingOrder { get; set; } = 999;
}
