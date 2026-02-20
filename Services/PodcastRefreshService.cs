using NexusM.Data;

namespace NexusM.Services;

/// <summary>
/// Background service that refreshes all podcast feeds once at application startup.
/// Waits 10 seconds after startup to allow the database to fully initialize first.
/// </summary>
public class PodcastRefreshService : IHostedService
{
    private readonly PodcastService _podcastService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PodcastRefreshService> _logger;

    public PodcastRefreshService(
        PodcastService podcastService,
        IServiceScopeFactory scopeFactory,
        ILogger<PodcastRefreshService> logger)
    {
        _podcastService = podcastService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Wait for DB init and other startup tasks to complete
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;
                await _podcastService.RefreshAllFeedsAsync(_scopeFactory);
            }
            catch (OperationCanceledException) { /* normal shutdown */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Podcast startup refresh encountered an error.");
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
