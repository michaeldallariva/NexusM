using Microsoft.EntityFrameworkCore;
using NexusM.Models;

namespace NexusM.Data;

/// <summary>
/// EF Core database context for the music videos library.
/// Uses a separate SQLite database (musicvideos.db).
/// </summary>
public class MusicVideosDbContext : DbContext
{
    public MusicVideosDbContext(DbContextOptions<MusicVideosDbContext> options) : base(options) { }

    public DbSet<MusicVideo> MusicVideos => Set<MusicVideo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MusicVideo>(entity =>
        {
            entity.HasIndex(e => e.FilePath).IsUnique();
            entity.HasIndex(e => e.Title);
            entity.HasIndex(e => e.Artist);
            entity.HasIndex(e => e.Year);
            entity.HasIndex(e => e.Format);
            entity.HasIndex(e => e.DateAdded);
            entity.HasIndex(e => e.NeedsOptimization);
            entity.HasIndex(e => e.Mp4Compliant);
        });
    }
}
