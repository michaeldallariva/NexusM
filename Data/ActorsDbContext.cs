using Microsoft.EntityFrameworkCore;
using NexusM.Models;

namespace NexusM.Data;

/// <summary>
/// EF Core database context for the actors database.
/// Uses a separate SQLite database (actors.db).
/// </summary>
public class ActorsDbContext : DbContext
{
    public ActorsDbContext(DbContextOptions<ActorsDbContext> options) : base(options) { }

    public DbSet<Actor> Actors => Set<Actor>();
    public DbSet<MovieActor> MovieActors => Set<MovieActor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Actor>(entity =>
        {
            entity.HasIndex(e => e.TmdbId).IsUnique();
            entity.HasIndex(e => e.NormalizedName);
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.Popularity);
        });

        modelBuilder.Entity<MovieActor>(entity =>
        {
            entity.HasIndex(e => new { e.VideoId, e.ActorId }).IsUnique();
            entity.HasIndex(e => e.VideoId);
            entity.HasIndex(e => e.ActorId);
        });
    }
}
