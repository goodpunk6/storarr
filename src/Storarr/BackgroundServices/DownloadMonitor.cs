using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Storarr.Data;
using Storarr.Models;
using Storarr.Hubs;
using Storarr.Services;

namespace Storarr.BackgroundServices
{
    public class DownloadMonitor : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DownloadMonitor> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

        public DownloadMonitor(IServiceProvider serviceProvider, ILogger<DownloadMonitor> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("DownloadMonitor started");

            while (!stoppingToken.IsCancellationRequested)
            {
                await BackgroundServiceLock.GlobalLock.WaitAsync(stoppingToken);
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<StorarrDbContext>();
                    var sonarrService = scope.ServiceProvider.GetRequiredService<ISonarrService>();
                    var radarrService = scope.ServiceProvider.GetRequiredService<IRadarrService>();
                    var fileService = scope.ServiceProvider.GetRequiredService<IFileManagementService>();
                    var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<NotificationHub>>();

                    await MonitorDownloads(dbContext, sonarrService, radarrService, fileService, hubContext);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in DownloadMonitor");
                }
                finally
                {
                    BackgroundServiceLock.GlobalLock.Release();
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }

        private async Task MonitorDownloads(
            StorarrDbContext dbContext,
            ISonarrService sonarrService,
            IRadarrService radarrService,
            IFileManagementService fileService,
            IHubContext<NotificationHub> hubContext)
        {
            var downloadingItems = await dbContext.MediaItems
                .Where(m => m.CurrentState == FileState.Downloading)
                .ToListAsync();

            if (!downloadingItems.Any())
                return;

            // Get queues from both services
            var sonarrQueue = await sonarrService.GetQueue();
            var radarrQueue = await radarrService.GetQueue();

            foreach (var item in downloadingItems)
            {
                // Determine download status — null means inconclusive (no Arr service linked)
                bool? stillDownloading = null;

                if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                {
                    stillDownloading = sonarrQueue.Any(q => q.SeriesId == item.SonarrId.Value);
                }
                else if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                {
                    stillDownloading = radarrQueue.Any(q => q.MovieId == item.RadarrId.Value);
                }
                else
                {
                    // No Arr service linked — skip rather than incorrectly assuming download completed
                    _logger.LogWarning("[DownloadMonitor] Item '{Title}' has no Arr service linked, skipping completion check", item.Title);
                    continue;
                }

                // Check if file exists now that we know the download is not active
                if (!stillDownloading.Value && await fileService.FileExists(item.FilePath))
                {
                    var isSymlink = await fileService.IsSymlink(item.FilePath);

                    var previousState = item.CurrentState;
                    item.CurrentState = isSymlink ? FileState.Symlink : FileState.Mkv;
                    item.StateChangedAt = DateTime.UtcNow;
                    item.FileSize = await fileService.GetFileSize(item.FilePath);

                    // Log activity
                    dbContext.ActivityLogs.Add(new ActivityLog
                    {
                        MediaItemId = item.Id,
                        Action = "DownloadComplete",
                        FromState = previousState.ToString(),
                        ToState = item.CurrentState.ToString(),
                        Timestamp = DateTime.UtcNow
                    });

                    _logger.LogInformation("Download complete for {Title}, state: {State}", item.Title, item.CurrentState);

                    // Notify clients
                    await hubContext.Clients.All.SendAsync("MediaUpdated", item.Id, item.CurrentState.ToString());
                }
            }

            await dbContext.SaveChangesAsync();
        }
    }
}
