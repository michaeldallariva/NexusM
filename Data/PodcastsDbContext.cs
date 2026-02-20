using Microsoft.EntityFrameworkCore;
using NexusM.Models;

namespace NexusM.Data;

public class PodcastsDbContext : DbContext
{
    public PodcastsDbContext(DbContextOptions<PodcastsDbContext> options) : base(options) { }

    public DbSet<PodcastFeed> Feeds => Set<PodcastFeed>();
    public DbSet<PodcastEpisode> Episodes => Set<PodcastEpisode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PodcastFeed>(entity =>
        {
            entity.HasIndex(e => e.RssUrl).IsUnique();
            entity.HasIndex(e => e.DateAdded);
        });

        modelBuilder.Entity<PodcastEpisode>(entity =>
        {
            entity.HasIndex(e => e.FeedId);
            entity.HasIndex(e => e.PublishDate);
            entity.HasIndex(e => new { e.FeedId, e.Guid }).IsUnique();

            entity.HasOne(e => e.Feed)
                  .WithMany(f => f.Episodes)
                  .HasForeignKey(e => e.FeedId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
