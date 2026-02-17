using Microsoft.EntityFrameworkCore;
using NexusM.Models;

namespace NexusM.Data;

/// <summary>
/// EF Core database context for the pictures library.
/// Uses a separate SQLite database (pictures.db) from the music database.
/// </summary>
public class PicturesDbContext : DbContext
{
    public PicturesDbContext(DbContextOptions<PicturesDbContext> options) : base(options) { }

    public DbSet<Picture> Pictures => Set<Picture>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Picture>(entity =>
        {
            entity.HasIndex(p => p.FilePath).IsUnique();
            entity.HasIndex(p => p.Category);
            entity.HasIndex(p => p.DateTaken);
            entity.HasIndex(p => p.DateAdded);
            entity.HasIndex(p => p.Format);
        });
    }
}
