using Microsoft.EntityFrameworkCore;
using NexusM.Models;

namespace NexusM.Data;

/// <summary>
/// EF Core database context for network share credentials.
/// Uses a separate SQLite database (shares.db).
/// </summary>
public class SharesDbContext : DbContext
{
    public SharesDbContext(DbContextOptions<SharesDbContext> options) : base(options) { }

    public DbSet<NetworkShare> Shares => Set<NetworkShare>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<NetworkShare>(entity =>
        {
            entity.HasIndex(e => e.SharePath).IsUnique();
        });
    }
}
