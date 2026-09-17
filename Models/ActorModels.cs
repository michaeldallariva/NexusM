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

    /// <summary>TMDB gender code: 0 = unknown/unspecified, 1 = female, 2 = male, 3 = non-binary.</summary>
    public int Gender { get; set; }

    public string? Birthday { get; set; }

    public string? Deathday { get; set; }

    public string? PlaceOfBirth { get; set; }

    public string? Biography { get; set; }
    // Metadata language the cached Biography was fetched in (e.g. "fr-FR"). Lets the bio be
    // re-fetched when the server's Metadata Language setting changes, like video overviews.
    public string? BiographyLanguage { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    // Social media handles - fetched on first page view, refreshed every 30 days on view
    public string? FacebookId { get; set; }
    public string? InstagramId { get; set; }
    public string? TwitterId { get; set; }
    public DateTime? SocialFetchedAt { get; set; }

    // Filmography staleness - null means never fetched; refreshed every 7 days (30 for deceased)
    public DateTime? FilmographyFetchedAt { get; set; }

    // True for rows created only to back a Director/Writer page (resolved from crew,
    // not part of any local cast). Kept out of the Actors browse list so directors
    // and writers don't pollute the actor grid; still fully usable via their own page.
    public bool HiddenFromBrowse { get; set; }
}

/// <summary>
/// One row per filmography credit for an actor (stored locally after first TMDB fetch).
/// CreditType: 'actor' = movie cast, 'appearances' = TV cast, 'producer' = production crew.
/// </summary>
public class ActorFilmographyCredit
{
    [Key]
    public int Id { get; set; }

    public int ActorId { get; set; }

    public string CreditType { get; set; } = "";   // actor | appearances | producer

    public string Title { get; set; } = "";
    public string Year { get; set; } = "";
    public string? Character { get; set; }          // cast role
    public string? Job { get; set; }                // crew job title
    public int TmdbItemId { get; set; }
    public string MediaType { get; set; } = "";     // movie | tv
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
