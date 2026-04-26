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
                var arrFilePath = RemapToArrPath(item.FilePath);

                if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                {
                    _logger.LogDebug("[TransitionService] Deleting movie file via Radarr API: {Path} (arr: {ArrPath})", item.FilePath, arrFilePath);
                    apiDeleted = await _radarrService.DeleteMovieFileByPath(item.RadarrId.Value, item.FilePath)
                        || await _radarrService.DeleteMovieFileByPath(item.RadarrId.Value, arrFilePath);
                }
                else if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                {
                    _logger.LogDebug("[TransitionService] Deleting episode file via Sonarr API: {Path} (arr: {ArrPath})", item.FilePath, arrFilePath);
                    apiDeleted = await _sonarrService.DeleteEpisodeFileByPath(item.SonarrId.Value, item.FilePath)
                        || await _sonarrService.DeleteEpisodeFileByPath(item.SonarrId.Value, arrFilePath);
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
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (item.TmdbId == null) throw new InvalidOperationException("Item must have a TMDb ID");

            try
            {
                // Check if usenet releases are available before doing anything
                var hasUsenet = await CheckUsenetAvailable(item);
                if (!hasUsenet)
                {
                    _logger.LogInformation("[TransitionService] No usenet releases available for {Title}, skipping symlink transition", item.Title);
                    return;
                }

                // Delete the file via Arr API and/or disk
                bool apiDeleted = false;
                var arrFilePath = RemapToArrPath(item.FilePath);

                if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                {
                    _logger.LogDebug("[TransitionService] Deleting movie file via Radarr API: {Path} (arr: {ArrPath})", item.FilePath, arrFilePath);
                    apiDeleted = await _radarrService.DeleteMovieFileByPath(item.RadarrId.Value, item.FilePath)
                        || await _radarrService.DeleteMovieFileByPath(item.RadarrId.Value, arrFilePath);
                }
                else if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                {
                    _logger.LogDebug("[TransitionService] Deleting episode file via Sonarr API: {Path} (arr: {ArrPath})", item.FilePath, arrFilePath);
                    apiDeleted = await _sonarrService.DeleteEpisodeFileByPath(item.SonarrId.Value, item.FilePath)
                        || await _sonarrService.DeleteEpisodeFileByPath(item.SonarrId.Value, arrFilePath);
                }

                if (await _fileService.FileExists(item.FilePath))
                {
                    _logger.LogDebug("[TransitionService] Deleting file from disk: {Path}", item.FilePath);
                    await _fileService.DeleteFile(item.FilePath);
                }

                // Trigger Sonarr/Radarr search — they will pick NZBdav for usenet releases
                if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                {
                    await _radarrService.TriggerSearch(item.RadarrId.Value);
                }
                else if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                {
                    if (item.SeasonNumber.HasValue && item.EpisodeNumber.HasValue)
                    {
                        var episodeId = await _sonarrService.GetEpisodeId(item.SonarrId.Value, item.SeasonNumber.Value, item.EpisodeNumber.Value);
                        if (episodeId.HasValue)
                        {
                            await _sonarrService.TriggerSearch(item.SonarrId.Value, new[] { episodeId.Value });
                        }
                        else
                        {
                            _logger.LogWarning("[TransitionService] Could not resolve episode ID for {Title}, triggering series search", item.Title);
                            await _sonarrService.TriggerSearch(item.SonarrId.Value);
                        }
                    }
                    else
                    {
                        await _sonarrService.TriggerSearch(item.SonarrId.Value);
                    }
                }

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
                    catch (Exception ex) { _logger.LogError(ex, "[TransitionService] Failed to auto-transition to MKV"); break; }
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

                    // Stop after first failure to avoid flooding Jellyseerr
                    break;
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

        private async Task<bool> TryDirectReleaseGrab(MediaItem item, Config config)
        {
            int? downloadClientId = null;
            string? requiredProtocol = null;

            if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
            {
                downloadClientId = config.SonarrSymlinkDownloadClientId;
                if (!downloadClientId.HasValue) return false;

                // Determine the download client's protocol to filter compatible releases
                try
                {
                    var clients = await _sonarrService.GetDownloadClients();
                    var targetClient = clients.FirstOrDefault(c => c.Id == downloadClientId.Value);
                    if (targetClient != null)
                    {
                        requiredProtocol = targetClient.Implementation.Equals("Sabnzbd", StringComparison.OrdinalIgnoreCase)
                            ? "usenet" : "torrent";
                    }
                }
                catch { /* ignore, will try without protocol filter */ }

                // Get episode ID first
                if (!item.SeasonNumber.HasValue || !item.EpisodeNumber.HasValue)
                {
                    _logger.LogWarning("[TransitionService] Cannot direct grab: missing season/episode for {Title}", item.Title);
                    return false;
                }

                var episodeId = await _sonarrService.GetEpisodeId(item.SonarrId.Value, item.SeasonNumber.Value, item.EpisodeNumber.Value);
                if (!episodeId.HasValue)
                {
                    _logger.LogWarning("[TransitionService] Cannot direct grab: episode not found for {Title}", item.Title);
                    return false;
                }

                var releases = await _sonarrService.SearchReleases(item.SonarrId.Value, new[] { episodeId.Value });
                var release = FilterRelease(releases, requiredProtocol);
                if (release == null)
                {
                    _logger.LogWarning("[TransitionService] No compatible release found for {Title} (protocol: {Protocol})", item.Title, requiredProtocol ?? "any");
                    return false;
                }

                var result = await _sonarrService.GrabRelease(release.Guid, release.IndexerId, downloadClientId);
                if (!result.Success)
                {
                    _logger.LogWarning("[TransitionService] Grab failed for {Title}: {Error}", item.Title, result.ErrorMessage);
                    return false;
                }

                _logger.LogInformation("[TransitionService] Grabbed release '{ReleaseTitle}' via download client {ClientId} for {Title}",
                    release.Title, downloadClientId, item.Title);
                return true;
            }
            else if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
            {
                downloadClientId = config.RadarrSymlinkDownloadClientId;
                if (!downloadClientId.HasValue) return false;

                // Determine the download client's protocol
                try
                {
                    var clients = await _radarrService.GetDownloadClients();
                    var targetClient = clients.FirstOrDefault(c => c.Id == downloadClientId.Value);
                    if (targetClient != null)
                    {
                        requiredProtocol = targetClient.Implementation.Equals("Sabnzbd", StringComparison.OrdinalIgnoreCase)
                            ? "usenet" : "torrent";
                    }
                }
                catch { /* ignore */ }

                var releases = await _radarrService.SearchReleases(item.RadarrId.Value);
                var release = FilterRelease(releases, requiredProtocol);
                if (release == null)
                {
                    _logger.LogWarning("[TransitionService] No compatible release found for {Title} (protocol: {Protocol})", item.Title, requiredProtocol ?? "any");
                    return false;
                }

                var result = await _radarrService.GrabRelease(release.Guid, release.IndexerId, downloadClientId);
                if (!result.Success)
                {
                    _logger.LogWarning("[TransitionService] Grab failed for {Title}: {Error}", item.Title, result.ErrorMessage);
                    return false;
                }

                _logger.LogInformation("[TransitionService] Grabbed release '{ReleaseTitle}' via download client {ClientId} for {Title}",
                    release.Title, downloadClientId, item.Title);
                return true;
            }

            return false;
        }

        private static ReleaseResult? FilterRelease(IEnumerable<ReleaseResult> releases, string? requiredProtocol)
        {
            var candidates = releases.Where(r => r.DownloadAllowed);
            if (requiredProtocol != null)
            {
                var filtered = candidates.FirstOrDefault(r =>
                    r.Protocol?.Equals(requiredProtocol, StringComparison.OrdinalIgnoreCase) == true);
                if (filtered != null) return filtered;
            }
            // Fallback: try any download-allowed release
            return candidates.FirstOrDefault();
        }

        private async Task<bool> CheckUsenetAvailable(MediaItem item)
        {
            try
            {
                if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                {
                    if (!item.SeasonNumber.HasValue || !item.EpisodeNumber.HasValue)
                    {
                        _logger.LogDebug("[TransitionService] Cannot check usenet: missing season/episode for {Title}", item.Title);
                        return false;
                    }

                    var episodeId = await _sonarrService.GetEpisodeId(item.SonarrId.Value, item.SeasonNumber.Value, item.EpisodeNumber.Value);
                    if (!episodeId.HasValue)
                    {
                        _logger.LogDebug("[TransitionService] Cannot check usenet: episode not found for {Title}", item.Title);
                        return false;
                    }

                    var releases = await _sonarrService.SearchReleases(item.SonarrId.Value, new[] { episodeId.Value });
                    return releases.Any(r => r.DownloadAllowed && r.Protocol?.Equals("usenet", StringComparison.OrdinalIgnoreCase) == true);
                }
                else if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                {
                    var releases = await _radarrService.SearchReleases(item.RadarrId.Value);
                    return releases.Any(r => r.DownloadAllowed && r.Protocol?.Equals("usenet", StringComparison.OrdinalIgnoreCase) == true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TransitionService] Failed to check usenet availability for {Title}", item.Title);
            }

            return false;
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

        private static string RemapToArrPath(string path)
        {
            // storarr mounts at /media, /tv, /movies but Arr stacks use /data/media, /data/tv, /data/movies
            string[][] mappings = {
                new[] { "/media/", "/data/media/" },
                new[] { "/tv/", "/data/tv/" },
                new[] { "/movies/", "/data/movies/" },
            };

            foreach (var mapping in mappings)
            {
                if (path.StartsWith(mapping[0], StringComparison.OrdinalIgnoreCase))
                    return mapping[1] + path.Substring(mapping[0].Length);
            }
            return path;
        }
    }
}
