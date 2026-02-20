using Microsoft.EntityFrameworkCore;
using NexusM.Models;

namespace NexusM.Data;

/// <summary>
/// EF Core database context for the eBooks library.
/// Uses a separate SQLite database (ebooks.db) from music and pictures.
/// </summary>
public class EBooksDbContext : DbContext
{
    public EBooksDbContext(DbContextOptions<EBooksDbContext> options) : base(options) { }

    public DbSet<EBook> EBooks => Set<EBook>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<EBook>(entity =>
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
