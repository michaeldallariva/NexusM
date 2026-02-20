using Microsoft.EntityFrameworkCore;
using NexusM.Models;

namespace NexusM.Data;

/// <summary>
/// EF Core database context for Internet TV channels.
/// Uses a separate SQLite database (tvchannels.db).
/// </summary>
public class TvChannelsDbContext : DbContext
{
    public TvChannelsDbContext(DbContextOptions<TvChannelsDbContext> options) : base(options) { }

    public DbSet<TvChannel> TvChannels => Set<TvChannel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TvChannel>(entity =>
        {
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.Country);
            entity.HasIndex(e => e.Genre);
            entity.HasIndex(e => e.StreamUrl).IsUnique();
            entity.HasIndex(e => e.DateAdded);
        });
    }
}
