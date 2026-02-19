using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Storarr.Data;
using Storarr.Models;
using Storarr.Hubs;

namespace Storarr.Services
{
    public class TransitionService : ITransitionService
    {
        private readonly StorarrDbContext _dbContext;
        private readonly IFileManagementService _fileService;
        private readonly ISonarrService _sonarrService;
        private readonly IRadarrService _radarrService;
        private readonly IJellyseerrService _jellyseerrService;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly ILogger<TransitionService> _logger;

        public TransitionService(
            StorarrDbContext dbContext,
            IFileManagementService fileService,
            ISonarrService sonarrService,
            IRadarrService radarrService,
            IJellyseerrService jellyseerrService,
            IHubContext<NotificationHub> hubContext,
            ILogger<TransitionService> logger)
        {
            _dbContext = dbContext;
            _fileService = fileService;
            _sonarrService = sonarrService;
            _radarrService = radarrService;
            _jellyseerrService = jellyseerrService;
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task TransitionToMkv(MediaItem item)
        {
            _logger.LogInformation("[TransitionService] Transitioning {Title} from symlink to MKV", item.Title);
            try
            {
                // STEP 1: Trigger search FIRST before deleting, so we can abort if search fails
                bool searchTriggered = false;

                if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                {
                    _logger.LogDebug("[TransitionService] Triggering Radarr search for movie ID: {Id}", item.RadarrId.Value);
                    try
                    {
                        await _radarrService.TriggerSearch(item.RadarrId.Value);
                        searchTriggered = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[TransitionService] Radarr search failed for {Title}, aborting deletion", item.Title);
                        throw;
                    }
                }
                else if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                {
                    _logger.LogDebug("[TransitionService] Triggering Sonarr search for series ID: {Id}", item.SonarrId.Value);
                    try
                    {
                        await _sonarrService.TriggerSearch(item.SonarrId.Value);
                        searchTriggered = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[TransitionService] Sonarr search failed for {Title}, aborting deletion", item.Title);
                        throw;
                    }
                }

                if (!searchTriggered)
                {
                    _logger.LogWarning("[TransitionService] No Arr service configured for {Title}, proceeding with deletion anyway", item.Title);
                }

                // STEP 2: Delete the file now that the search is confirmed
                bool apiDeleted = false;

                if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                {
                    _logger.LogDebug("[TransitionService] Deleting movie file via Radarr API: {Path}", item.FilePath);
                    apiDeleted = await _radarrService.DeleteMovieFileByPath(item.RadarrId.Value, item.FilePath);
                }
                else if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                {
                    _logger.LogDebug("[TransitionService] Deleting episode file via Sonarr API: {Path}", item.FilePath);
                    apiDeleted = await _sonarrService.DeleteEpisodeFileByPath(item.SonarrId.Value, item.FilePath);
                }

                // If API deletion failed or file still exists, delete from disk
                if (await _fileService.FileExists(item.FilePath))
                {
                    _logger.LogDebug("[TransitionService] Deleting file from disk: {Path}", item.FilePath);
                    await _fileService.DeleteFile(item.FilePath);
                }

                var previousState = item.CurrentState;
                item.CurrentState = FileState.Downloading;
                item.StateChangedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                await LogActivity(item.Id, "TransitionToMkv", previousState, FileState.Downloading,
                    apiDeleted ? "Deleted via Arr API" : "Deleted from disk");
                await _hubContext.Clients.All.SendAsync("MediaUpdated", item.Id, item.CurrentState.ToString());

                _logger.LogInformation("[TransitionService] Successfully transitioned {Title} to Downloading state (API deleted: {ApiDeleted})",
                    item.Title, apiDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TransitionService] Failed to transition {Title} to MKV", item.Title);
                throw;
            }
        }

        public async Task TransitionToSymlink(MediaItem item)
        {
            _logger.LogInformation("[TransitionService] Transitioning {Title} from MKV to symlink", item.Title);

            // Abort if TmdbId is null — we cannot create a Jellyseerr request without it
            if (!item.TmdbId.HasValue)
            {
                _logger.LogWarning("[TransitionService] Cannot transition {Title} to symlink: TmdbId is null. Aborting to avoid data loss.", item.Title);
                return;
            }

            try
            {
                // First, try to delete via Sonarr/Radarr API so their internal state is updated
                bool apiDeleted = false;

                if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                {
                    _logger.LogDebug("[TransitionService] Deleting movie file via Radarr API: {Path}", item.FilePath);
                    apiDeleted = await _radarrService.DeleteMovieFileByPath(item.RadarrId.Value, item.FilePath);
                }
                else if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                {
                    _logger.LogDebug("[TransitionService] Deleting episode file via Sonarr API: {Path}", item.FilePath);
                    apiDeleted = await _sonarrService.DeleteEpisodeFileByPath(item.SonarrId.Value, item.FilePath);
                }

                // If API deletion failed or file still exists, delete from disk
                if (await _fileService.FileExists(item.FilePath))
                {
                    _logger.LogDebug("[TransitionService] Deleting file from disk: {Path}", item.FilePath);
                    await _fileService.DeleteFile(item.FilePath);
                }

                // Create Jellyseerr request to re-download as symlink
                _logger.LogDebug("[TransitionService] Creating Jellyseerr request for TMDB ID: {Id}", item.TmdbId.Value);
                await _jellyseerrService.CreateRequest(item.TmdbId.Value, item.Type, item.TvdbId);

                var previousState = item.CurrentState;
                item.CurrentState = FileState.PendingSymlink;
                item.StateChangedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                await LogActivity(item.Id, "TransitionToSymlink", previousState, FileState.PendingSymlink,
                    apiDeleted ? "Deleted via Arr API" : "Deleted from disk");
                await _hubContext.Clients.All.SendAsync("MediaUpdated", item.Id, item.CurrentState.ToString());

                _logger.LogInformation("[TransitionService] Successfully transitioned {Title} to PendingSymlink state (API deleted: {ApiDeleted})",
                    item.Title, apiDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TransitionService] Failed to transition {Title} to symlink", item.Title);
                throw;
            }
        }

        public async Task CheckAndProcessTransitions()
        {
            _logger.LogDebug("[TransitionService] CheckAndProcessTransitions started");

            var config = await _dbContext.Configs.FindAsync(Config.SingletonId);
            if (config == null)
            {
                _logger.LogWarning("[TransitionService] No config found, skipping transitions");
                return;
            }

            // Check LibraryMode - only process auto-transitions in FullAutomation mode
            if (config.LibraryMode != LibraryMode.FullAutomation)
            {
                _logger.LogDebug("[TransitionService] LibraryMode is {Mode}, skipping auto-transitions. Only FullAutomation mode processes transitions automatically.",
                    config.LibraryMode);
                return;
            }

            var now = DateTime.UtcNow;
            var symlinkToMkvThreshold = config.GetSymlinkToMkvTimeSpan();
            var mkvToSymlinkThreshold = config.GetMkvToSymlinkTimeSpan();

            // Only process items that are NOT excluded
            var symlinksToConvert = await _dbContext.MediaItems
                .Where(m => m.CurrentState == FileState.Symlink && !m.IsExcluded)
                .ToListAsync();
            _logger.LogDebug("[TransitionService] Found {Count} symlinks to check (excluded items skipped)", symlinksToConvert.Count);

            foreach (var item in symlinksToConvert)
            {
                var lastWatched = item.LastWatchedAt ?? item.CreatedAt;
                var timeSinceWatch = now - lastWatched;

                if (timeSinceWatch >= symlinkToMkvThreshold)
                {
                    _logger.LogInformation("[TransitionService] Auto-transitioning symlink '{Title}' to MKV (unwatched for {Time})",
                        item.Title, timeSinceWatch);
                    try { await TransitionToMkv(item); }
                    catch (Exception ex) { _logger.LogError(ex, "[TransitionService] Failed to auto-transition to MKV"); }
                }
            }

            // Only process items that are NOT excluded
            var mkvsToConvert = await _dbContext.MediaItems
                .Where(m => m.CurrentState == FileState.Mkv && !m.IsExcluded)
                .ToListAsync();
            _logger.LogDebug("[TransitionService] Found {Count} MKVs to check (excluded items skipped)", mkvsToConvert.Count);

            foreach (var item in mkvsToConvert)
            {
                var lastWatched = item.LastWatchedAt ?? item.StateChangedAt ?? item.CreatedAt;
                var timeInactive = now - lastWatched;

                if (timeInactive >= mkvToSymlinkThreshold)
                {
                    _logger.LogInformation("[TransitionService] Auto-transitioning MKV '{Title}' to symlink (inactive for {Time})",
                        item.Title, timeInactive);
                    try { await TransitionToSymlink(item); }
                    catch (Exception ex) { _logger.LogError(ex, "[TransitionService] Failed to auto-transition to symlink"); }
                }
            }

            _logger.LogDebug("[TransitionService] CheckAndProcessTransitions completed");
        }

        public async Task<IEnumerable<TransitionCandidate>> GetUpcomingTransitions(int count = 10)
        {
            var config = await _dbContext.Configs.FindAsync(Config.SingletonId);
            if (config == null) return Enumerable.Empty<TransitionCandidate>();

            var now = DateTime.UtcNow;
            var symlinkToMkvThreshold = config.GetSymlinkToMkvTimeSpan();
            var mkvToSymlinkThreshold = config.GetMkvToSymlinkTimeSpan();
            var previewWindow = TimeSpan.FromDays(7);
            var candidates = new List<TransitionCandidate>();

            // Only include non-excluded items
            var symlinks = await _dbContext.MediaItems
                .AsNoTracking()
                .Where(m => m.CurrentState == FileState.Symlink && !m.IsExcluded)
                .OrderBy(m => m.LastWatchedAt ?? m.CreatedAt)
                .Take(count)
                .ToListAsync();

            foreach (var item in symlinks)
            {
                var lastWatched = item.LastWatchedAt ?? item.CreatedAt;
                var timeSinceWatch = now - lastWatched;
                var timeRemaining = symlinkToMkvThreshold - timeSinceWatch;
                if (timeRemaining <= previewWindow)
                {
                    candidates.Add(new TransitionCandidate
                    {
                        MediaItemId = item.Id,
                        Title = item.Title,
                        CurrentState = FileState.Symlink,
                        TargetState = FileState.Mkv,
                        DaysUntilTransition = (int)Math.Ceiling(timeRemaining.TotalDays),
                        TransitionDate = lastWatched + symlinkToMkvThreshold
                    });
                }
            }

            // Only include non-excluded items
            var mkvs = await _dbContext.MediaItems
                .AsNoTracking()
                .Where(m => m.CurrentState == FileState.Mkv && !m.IsExcluded)
                .OrderBy(m => m.LastWatchedAt ?? m.StateChangedAt ?? m.CreatedAt)
                .Take(count)
                .ToListAsync();

            foreach (var item in mkvs)
            {
                var lastActive = item.LastWatchedAt ?? item.StateChangedAt ?? item.CreatedAt;
                var timeInactive = now - lastActive;
                var timeRemaining = mkvToSymlinkThreshold - timeInactive;
                if (timeRemaining <= previewWindow)
                {
                    candidates.Add(new TransitionCandidate
                    {
                        MediaItemId = item.Id,
                        Title = item.Title,
                        CurrentState = FileState.Mkv,
                        TargetState = FileState.Symlink,
                        DaysUntilTransition = (int)Math.Ceiling(timeRemaining.TotalDays),
                        TransitionDate = lastActive + mkvToSymlinkThreshold
                    });
                }
            }

            return candidates.OrderBy(c => c.DaysUntilTransition).Take(count);
        }

        private async Task LogActivity(int mediaItemId, string action, FileState fromState, FileState toState, string? details = null)
        {
            _dbContext.ActivityLogs.Add(new ActivityLog
            {
                MediaItemId = mediaItemId,
                Action = action,
                FromState = fromState.ToString(),
                ToState = toState.ToString(),
                Details = details,
                Timestamp = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync();
        }
    }
}
