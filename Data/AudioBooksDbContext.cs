using Microsoft.EntityFrameworkCore;
using NexusM.Models;

namespace NexusM.Data;

/// <summary>
/// EF Core database context for the audio books library.
/// Uses a separate SQLite database (audiobooks.db).
/// </summary>
public class AudioBooksDbContext : DbContext
{
    public AudioBooksDbContext(DbContextOptions<AudioBooksDbContext> options) : base(options) { }

    public DbSet<AudioBook> AudioBooks => Set<AudioBook>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AudioBook>(entity =>
        {
            entity.HasIndex(e => e.FilePath).IsUnique();
            entity.HasIndex(e => e.Title);
            entity.HasIndex(e => e.Author);
            entity.HasIndex(e => e.Category);
            entity.HasIndex(e => e.Format);
            entity.HasIndex(e => e.DateAdded);
        });
    }
}
