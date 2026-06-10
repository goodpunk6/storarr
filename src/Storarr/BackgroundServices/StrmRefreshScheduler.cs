using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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
    /// <summary>
    /// Background service that refreshes STRM files on a configurable schedule.
    /// Default: Monday at 4:00 AM.
    /// STRM files contain URLs to streaming sources that can expire and need to be refreshed.
    /// </summary>
    public class StrmRefreshScheduler : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<StrmRefreshScheduler> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public StrmRefreshScheduler(
            IServiceProvider serviceProvider,
            ILogger<StrmRefreshScheduler> logger,
            IHttpClientFactory httpClientFactory)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[StrmRefreshScheduler] Service started");

            // Initial delay to let other services start
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<StorarrDbContext>();
                    var fileService = scope.ServiceProvider.GetRequiredService<IFileManagementService>();

                    var config = await dbContext.Configs.FindAsync(Config.SingletonId);
                    if (config == null)
                    {
                        _logger.LogDebug("[StrmRefreshScheduler] Config not found");
                        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                        continue;
                    }

                    // Calculate next run time
                    var nextRun = CalculateNextRun(config);
                    var now = DateTime.UtcNow;

                    // Update next run time in config if it changed
                    if (config.StrmRefreshNextRun != nextRun)
                    {
                        config.StrmRefreshNextRun = nextRun;
                        await dbContext.SaveChangesAsync();
                    }

                    // Check if it's time to refresh
                    if (!config.StrmRefreshEnabled)
                    {
                        _logger.LogDebug("[StrmRefreshScheduler] STRM refresh is disabled");
                        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                        continue;
                    }

                    if (now < nextRun)
                    {
                        var timeUntilNextRun = nextRun - now;
                        _logger.LogDebug("[StrmRefreshScheduler] Next refresh in {Time}",
                            timeUntilNextRun);

                        // Sleep for a shorter interval, but not longer than 5 minutes
                        var delay = TimeSpan.FromMinutes(1);
                        if (timeUntilNextRun.TotalMinutes > 1)
                        {
                            delay = TimeSpan.FromMinutes(Math.Min(5, timeUntilNextRun.TotalMinutes / 10));
                        }
                        await Task.Delay(delay, stoppingToken);
                        continue;
                    }

                    // Time to refresh!
                    _logger.LogInformation("[StrmRefreshScheduler] Starting scheduled STRM refresh");
                    await BackgroundServiceLock.GlobalLock.WaitAsync(stoppingToken);
                    try
                    {
                        var transitionService = scope.ServiceProvider.GetRequiredService<ITransitionService>();
                        await RefreshStrmFiles(dbContext, fileService, transitionService);

                        // Update last run time and calculate next run
                        config.StrmRefreshLastRun = now;
                        config.StrmRefreshNextRun = CalculateNextRun(config);
                        await dbContext.SaveChangesAsync();

                        _logger.LogInformation("[StrmRefreshScheduler] STRM refresh completed. Next run: {NextRun}",
                            config.StrmRefreshNextRun);
                    }
                    finally
                    {
                        BackgroundServiceLock.GlobalLock.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[StrmRefreshScheduler] Error in refresh cycle");
                }

                // Wait before checking again
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        /// <summary>
        /// Calculates the next run time based on the configuration.
        /// </summary>
        private DateTime CalculateNextRun(Config config)
        {
            var now = DateTime.UtcNow;
            var today = now.Date;
            var scheduledTime = today.AddHours(config.StrmRefreshHour).AddMinutes(config.StrmRefreshMinute);

            return config.StrmRefreshInterval switch
            {
                StrmRefreshInterval.Daily =>
                    // If today's time hasn't passed yet, use today; otherwise tomorrow
                    scheduledTime > now ? scheduledTime : scheduledTime.AddDays(1),

                StrmRefreshInterval.Weekly =>
                    FindNextOccurrenceOfDayOfWeek(scheduledTime, now, config.StrmRefreshDayOfWeek),

                StrmRefreshInterval.Monthly =>
                    FindNextMonthlyOccurrence(scheduledTime, now, config.StrmRefreshDayOfWeek),

                StrmRefreshInterval.Yearly =>
                    // January 1st of next year if we've passed this year's
                    new DateTime(now.Year + 1, 1, 1, config.StrmRefreshHour, config.StrmRefreshMinute, 0),

                _ => scheduledTime
            };
        }

        /// <summary>
        /// Finds the next occurrence of a specific day of week.
        /// </summary>
        private DateTime FindNextOccurrenceOfDayOfWeek(DateTime scheduledTime, DateTime now, DayOfWeek targetDay)
        {
            var candidate = scheduledTime;

            // If candidate is in the past, start from tomorrow
            if (candidate <= now)
            {
                candidate = candidate.AddDays(1);
            }

            // Find the next occurrence of the target day
            while (candidate.DayOfWeek != targetDay)
            {
                candidate = candidate.AddDays(1);
            }

            return candidate;
        }

        /// <summary>
        /// Finds the first occurrence of the target day of week in the next month.
        /// </summary>
        private DateTime FindNextMonthlyOccurrence(DateTime scheduledTime, DateTime now, DayOfWeek targetDay)
        {
            // Start from the first day of next month
            var nextMonth = new DateTime(now.Year, now.Month, 1, scheduledTime.Hour, scheduledTime.Minute, 0);
            nextMonth = nextMonth.AddMonths(1);

            // Find the first occurrence of the target day in that month
            while (nextMonth.DayOfWeek != targetDay)
            {
                nextMonth = nextMonth.AddDays(1);
            }

            return nextMonth;
        }

        /// <summary>
        /// Refreshes all STRM files by checking if their URLs are still valid.
        /// Invalid STRM files are flagged for re-download.
        /// </summary>
        private async Task RefreshStrmFiles(StorarrDbContext dbContext, IFileManagementService fileService, ITransitionService transitionService)
        {
            var storagePaths = await fileService.GetAllStoragePaths();
            if (storagePaths.Count == 0)
            {
                _logger.LogWarning("[StrmRefreshScheduler] No storage paths configured");
                return;
            }

            var allStrmFiles = new List<string>();
            foreach (var path in storagePaths.Where(Directory.Exists))
            {
                try
                {
                    var strmFiles = Directory.EnumerateFiles(path, "*.strm", SearchOption.AllDirectories);
                    allStrmFiles.AddRange(strmFiles);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[StrmRefreshScheduler] Error scanning {Path}", path);
                }
            }

            _logger.LogInformation("[StrmRefreshScheduler] Found {Count} STRM files to check", allStrmFiles.Count);

            if (allStrmFiles.Count == 0)
            {
                return;
            }

            var validCount = 0;
            var invalidCount = 0;
            var errorCount = 0;

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            foreach (var strmPath in allStrmFiles)
            {
                try
                {
                    var url = await ReadStrmUrl(strmPath);
                    if (string.IsNullOrEmpty(url))
                    {
                        _logger.LogWarning("[StrmRefreshScheduler] STRM file has no URL: {Path}", strmPath);
                        errorCount++;
                        continue;
                    }

                    // Check if URL is still valid
                    var isValid = await CheckUrlValid(httpClient, url);

                    if (isValid)
                    {
                        validCount++;
                        _logger.LogDebug("[StrmRefreshScheduler] STRM URL valid: {Path}", strmPath);
                    }
                    else
                    {
                        invalidCount++;
                        _logger.LogWarning("[StrmRefreshScheduler] STRM URL expired/invalid: {Path}", strmPath);

                        // Trigger Arr search to re-download the expired STRM via NZBdav
                        await MarkStrmForRefresh(dbContext, strmPath, fileService, transitionService);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[StrmRefreshScheduler] Error checking STRM file: {Path}", strmPath);
                    errorCount++;
                }
            }

            _logger.LogInformation("[StrmRefreshScheduler] STRM refresh complete. Valid: {Valid}, Invalid: {Invalid}, Errors: {Errors}",
                validCount, invalidCount, errorCount);
        }

        /// <summary>
        /// Reads the URL from a STRM file.
        /// </summary>
        private async Task<string?> ReadStrmUrl(string strmPath)
        {
            try
            {
                var content = await File.ReadAllTextAsync(strmPath);
                return content.Trim();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Checks if a URL is still valid by sending a HEAD request.
        /// </summary>
        private async Task<bool> CheckUrlValid(HttpClient httpClient, string url)
        {
            try
            {
                // Only check HTTP/HTTPS URLs
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    // For non-HTTP URLs (like local paths), assume valid
                    return true;
                }

                var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (TaskCanceledException)
            {
                // Timeout - consider it invalid
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[StrmRefreshScheduler] Error checking URL: {Url}", url);
                return false;
            }
        }

        /// <summary>
        /// Marks a STRM file for refresh by triggering a re-download via the Arr search.
        /// </summary>
        private async Task MarkStrmForRefresh(StorarrDbContext dbContext, string strmPath, IFileManagementService fileService, ITransitionService transitionService)
        {
            try
            {
                var mediaItem = await dbContext.MediaItems
                    .FirstOrDefaultAsync(m => m.FilePath == strmPath);

                if (mediaItem != null)
                {
                    _logger.LogInformation("[StrmRefreshScheduler] Triggering Arr search to refresh expired STRM: {Title}", mediaItem.Title);
                    await transitionService.TransitionToSymlink(mediaItem);
                }
                else
                {
                    _logger.LogWarning("[StrmRefreshScheduler] No tracked media item for {Path}, deleting file only", strmPath);
                    await fileService.DeleteFile(strmPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[StrmRefreshScheduler] Error refreshing STRM: {Path}", strmPath);
            }
        }
    }
}
