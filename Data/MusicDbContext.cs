using Microsoft.EntityFrameworkCore;
using NexusM.Models;

namespace NexusM.Data;

/// <summary>
/// Entity Framework Core database context for NexusM.
/// Uses SQLite for portability (same as NexusM approach).
/// </summary>
public class MusicDbContext : DbContext
{
    public MusicDbContext(DbContextOptions<MusicDbContext> options) : base(options) { }

    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<ScanStatus> ScanStatuses => Set<ScanStatus>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Track indexes for fast querying
        modelBuilder.Entity<Track>(entity =>
        {
            entity.HasIndex(t => t.FilePath).IsUnique();
            entity.HasIndex(t => t.Title);
            entity.HasIndex(t => t.Artist);
            entity.HasIndex(t => t.Album);
            entity.HasIndex(t => t.Genre);
            entity.HasIndex(t => t.Year);
            entity.HasIndex(t => t.DateAdded);
            entity.HasIndex(t => t.PlayCount);
            entity.HasIndex(t => t.IsFavourite);
        });

        // Album indexes
        modelBuilder.Entity<Album>(entity =>
        {
            entity.HasIndex(a => a.Name);
            entity.HasIndex(a => a.Artist);
            entity.HasIndex(a => new { a.Name, a.Artist }).IsUnique();
        });

        // Artist indexes
        modelBuilder.Entity<Artist>(entity =>
        {
            entity.HasIndex(a => a.Name).IsUnique();
        });

    }
}
