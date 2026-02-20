using Microsoft.EntityFrameworkCore;
using NexusM.Models;

namespace NexusM.Data;

/// <summary>
/// EF Core database context for community ratings.
/// Uses a separate SQLite database (ratings.db), shared across all users.
/// </summary>
public class RatingsDbContext : DbContext
{
    public RatingsDbContext(DbContextOptions<RatingsDbContext> options) : base(options) { }

    public DbSet<Rating> Ratings => Set<Rating>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Rating>(entity =>
        {
            // One vote per user per item
            entity.HasIndex(r => new { r.MediaType, r.MediaId, r.Username }).IsUnique();
            entity.HasIndex(r => r.MediaType);
            entity.HasIndex(r => r.MediaId);
            entity.HasIndex(r => r.Username);
            entity.HasIndex(r => r.DateRated);
        });
    }
}
