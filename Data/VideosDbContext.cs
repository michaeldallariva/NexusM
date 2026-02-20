using Microsoft.EntityFrameworkCore;
using NexusM.Models;

namespace NexusM.Data;

/// <summary>
/// EF Core database context for the movies/TV shows library.
/// Uses a separate SQLite database (videos.db).
/// </summary>
public class VideosDbContext : DbContext
{
    public VideosDbContext(DbContextOptions<VideosDbContext> options) : base(options) { }

    public DbSet<Video> Videos => Set<Video>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Video>(entity =>
        {
            entity.HasIndex(e => e.FilePath).IsUnique();
            entity.HasIndex(e => e.Title);
            entity.HasIndex(e => e.MediaType);
            entity.HasIndex(e => e.Year);
            entity.HasIndex(e => e.Genre);
            entity.HasIndex(e => e.SeriesName);
            entity.HasIndex(e => e.DateAdded);
            entity.HasIndex(e => e.Format);
        });
    }
}
