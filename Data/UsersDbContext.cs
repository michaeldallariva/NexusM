using Microsoft.EntityFrameworkCore;
using NexusM.Models;

namespace NexusM.Data;

/// <summary>
/// Entity Framework Core database context for user authentication.
/// Uses a separate SQLite database (users.db) for user accounts and PIN security.
/// </summary>
public class UsersDbContext : DbContext
{
    public UsersDbContext(DbContextOptions<UsersDbContext> options) : base(options) { }

    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasIndex(u => u.Username).IsUnique();
        });
    }
}
