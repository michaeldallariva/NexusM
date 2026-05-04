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
    public DbSet<PictureVideo> PictureVideos => Set<PictureVideo>();
    public DbSet<PictureAlbum> PictureAlbums => Set<PictureAlbum>();
    public DbSet<PictureAlbumItem> PictureAlbumItems => Set<PictureAlbumItem>();

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

        modelBuilder.Entity<PictureVideo>(entity =>
        {
            entity.HasIndex(v => v.FilePath).IsUnique();
            entity.HasIndex(v => v.Category);
            entity.HasIndex(v => v.DateAdded);
        });

        modelBuilder.Entity<PictureAlbum>(entity =>
        {
            entity.HasIndex(a => a.Username);
        });

        modelBuilder.Entity<PictureAlbumItem>(entity =>
        {
            entity.HasIndex(i => i.AlbumId);
        });
    }
}
