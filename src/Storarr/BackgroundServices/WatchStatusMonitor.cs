using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Storarr.Data;
using Storarr.Models;
using Storarr.Services;

namespace Storarr.BackgroundServices
{
    public class WatchStatusMonitor : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<WatchStatusMonitor> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

        public WatchStatusMonitor(IServiceProvider serviceProvider, ILogger<WatchStatusMonitor> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("WatchStatusMonitor started");

            while (!stoppingToken.IsCancellationRequested)
            {
                await BackgroundServiceLock.GlobalLock.WaitAsync(stoppingToken);
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<StorarrDbContext>();
                    var jellyfinService = scope.ServiceProvider.GetRequiredService<IJellyfinService>();

                    await UpdateWatchStatus(dbContext, jellyfinService);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in WatchStatusMonitor");
                }
                finally
                {
                    BackgroundServiceLock.GlobalLock.Release();
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }

        private async Task UpdateWatchStatus(StorarrDbContext dbContext, IJellyfinService jellyfinService)
        {
            var config = await dbContext.Configs.FindAsync(Config.SingletonId);
            if (string.IsNullOrEmpty(config?.JellyfinUrl) || string.IsNullOrEmpty(config?.JellyfinApiKey))
            {
                _logger.LogDebug("Jellyfin not configured, skipping watch status update");
                return;
            }

            var mediaItems = await dbContext.MediaItems
                .Where(m => !string.IsNullOrEmpty(m.JellyfinId))
                .ToListAsync();

            foreach (var item in mediaItems)
            {
                try
                {
                    var lastPlayed = await jellyfinService.GetLastPlayedDate(item.FilePath);
                    if (lastPlayed.HasValue)
                    {
                        if (!item.LastWatchedAt.HasValue || lastPlayed > item.LastWatchedAt)
                        {
                            item.LastWatchedAt = lastPlayed;
                            _logger.LogDebug("Updated last watched date for {Title} to {Date}",
                                item.Title, lastPlayed);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get watch status for {Title}", item.Title);
                }
            }

            await dbContext.SaveChangesAsync();
            _logger.LogInformation("Updated watch status for {Count} items", mediaItems.Count);
        }
    }
}
