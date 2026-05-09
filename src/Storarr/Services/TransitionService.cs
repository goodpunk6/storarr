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
                // STEP 1: Search releases and grab via the matching download client
                bool grabTriggered = false;

                try
                {
                    // Fetch download clients and build protocol→clientId map (exclude NZBdav — it delivers .strm, not MKV)
                    var clients = (item.Type == MediaType.Series || item.Type == MediaType.Anime)
                        ? await _sonarrService.GetDownloadClients()
                        : await _radarrService.GetDownloadClients();

                    var protocolToClientId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var c in clients.Where(c => c.Enable && !c.Name.Equals("NZBdav", StringComparison.OrdinalIgnoreCase)))
                    {
                        var protocol = c.Implementation.Equals("Sabnzbd", StringComparison.OrdinalIgnoreCase)
                            ? "usenet" : "torrent";
                        protocolToClientId.TryAdd(protocol, c.Id);
                    }

                    // Search for available releases
                    IEnumerable<ReleaseResult> releases;
                    if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                    {
                        if (!item.SeasonNumber.HasValue || !item.EpisodeNumber.HasValue)
                        {
                            _logger.LogWarning("[TransitionService] Cannot search: missing season/episode for {Title}", item.Title);
                            releases = Enumerable.Empty<ReleaseResult>();
                        }
                        else
                        {
                            var episodeId = await _sonarrService.GetEpisodeId(item.SonarrId.Value, item.SeasonNumber.Value, item.EpisodeNumber.Value);
                            releases = episodeId.HasValue
                                ? await _sonarrService.SearchReleases(item.SonarrId.Value, new[] { episodeId.Value })
                                : Enumerable.Empty<ReleaseResult>();
                        }
                    }
                    else if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                    {
                        releases = await _radarrService.SearchReleases(item.RadarrId.Value);
                    }
                    else
                    {
                        releases = Enumerable.Empty<ReleaseResult>();
                    }

                    // Pick the best scoring release, ignoring downloadAllowed (cutoff blocks it)
                    // Skip torrent releases with 0 seeders
                    // Skip blocklisted releases (previously failed grabs)

                    var blocklist = (item.Type == MediaType.Series || item.Type == MediaType.Anime)
                        ? await _sonarrService.GetBlocklistedTitles()
                        : await _radarrService.GetBlocklistedTitles();

                    var normalizedItemTitle = NormalizeTitle(item.Title);
                    var eligibleReleases = releases
                        .Where(r => !r.Protocol?.Equals("torrent", StringComparison.OrdinalIgnoreCase) == true
                            || (r.Seeders.HasValue && r.Seeders.Value > 0))
                        .Where(r => !blocklist.Contains(r.Title))
                        .Where(r => r.CustomFormatScore >= 0)
                        .Where(r => ReleaseMatchesTitle(r.Title, normalizedItemTitle))
                        .OrderByDescending(r => r.QualityWeight)
                        .ThenByDescending(r => r.CustomFormatScore)
                        .ToList();

                    _logger.LogInformation("[TransitionService] Found {Count} eligible releases for {Title}", eligibleReleases.Count, item.Title);

                    // Try each release in order until one succeeds
                    int attempt = 0;
                    foreach (var release in eligibleReleases)
                    {
                        attempt++;
                        if (string.IsNullOrEmpty(release.Protocol))
                        {
                            _logger.LogDebug("[TransitionService] Skipping attempt {Attempt}/{Total}: no protocol for '{Title}'",
                                attempt, eligibleReleases.Count, release.Title);
                            continue;
                        }
                        if (!protocolToClientId.TryGetValue(release.Protocol, out var clientId))
                        {
                            _logger.LogDebug("[TransitionService] Skipping attempt {Attempt}/{Total}: no client for protocol {Protocol} ('{Title}')",
                                attempt, eligibleReleases.Count, release.Protocol, release.Title);
                            continue;
                        }

                        _logger.LogInformation("[TransitionService] Grab attempt {Attempt}/{Total}: '{Title}' ({Protocol}, QW:{QW}, CF:{CF}) via client {ClientId}",
                            attempt, eligibleReleases.Count, release.Title, release.Protocol, release.QualityWeight, release.CustomFormatScore, clientId);

                        GrabResult result;
                        if (item.Type == MediaType.Series || item.Type == MediaType.Anime)
                        {
                            int[]? epIds = null;
                            if (item.SonarrId.HasValue && item.SeasonNumber.HasValue && item.EpisodeNumber.HasValue)
                            {
                                var epId = await _sonarrService.GetEpisodeId(item.SonarrId.Value, item.SeasonNumber.Value, item.EpisodeNumber.Value);
                                if (epId.HasValue) epIds = new[] { epId.Value };
                            }
                            result = await _sonarrService.GrabRelease(release.Guid, release.IndexerId, clientId, item.SonarrId, epIds);
                        }
                        else
                        {
                            result = await _radarrService.GrabRelease(release.Guid, release.IndexerId, clientId, item.RadarrId);
                        }

                        if (result.Success)
                        {
                            _logger.LogInformation("[TransitionService] Successfully grabbed '{Title}' ({Protocol}) via client {ClientId} for {ItemTitle} on attempt {Attempt}",
                                release.Title, release.Protocol, clientId, item.Title, attempt);
                            grabTriggered = true;
                            break;
                        }

                        _logger.LogWarning("[TransitionService] Grab attempt {Attempt} failed for '{Title}': {Error}",
                            attempt, release.Title, result.ErrorMessage);
                    }

                    if (!grabTriggered && eligibleReleases.Count > 0)
                    {
                        _logger.LogWarning("[TransitionService] All {Count} grab attempts failed for {Title}", eligibleReleases.Count, item.Title);
                    }
                    else if (eligibleReleases.Count == 0)
                    {
                        _logger.LogWarning("[TransitionService] No eligible releases found for {Title}", item.Title);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[TransitionService] Targeted grab failed for {Title}, will fall back", item.Title);
                }

                // Fallback to blind TriggerSearch only as last resort
                if (!grabTriggered)
                {
                    if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                    {
                        _logger.LogWarning("[TransitionService] Falling back to Radarr TriggerSearch for {Title} — client selection is uncontrolled",
                            item.Title);
                        await _radarrService.TriggerSearch(item.RadarrId.Value);
                        grabTriggered = true;
                    }
                    else if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                    {
                        _logger.LogWarning("[TransitionService] Falling back to Sonarr TriggerSearch for {Title} — client selection is uncontrolled",
                            item.Title);
                        await _sonarrService.TriggerSearch(item.SonarrId.Value);
                        grabTriggered = true;
                    }
                }

                if (!grabTriggered)
                {
                    _logger.LogWarning("[TransitionService] No Arr service configured for {Title}, proceeding with deletion anyway", item.Title);
                }

                // STEP 2: Delete the file
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

            try
            {
                // If the source file is already gone, check if a .strm/symlink already exists
                var sourceExists = await _fileService.FileExists(item.FilePath);
                if (!sourceExists && !item.FilePath.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
                {
                    var dir = System.IO.Path.GetDirectoryName(item.FilePath);
                    if (dir != null)
                    {
                        var allFiles = await _fileService.ScanDirectory(dir, false);
                        var strmFiles = allFiles
                            .Where(f => f.IsSymlink || f.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (strmFiles.Count > 0)
                        {
                            _logger.LogInformation("[TransitionService] Source file gone but .strm exists for {Title}, updating state directly", item.Title);
                            var prevState = item.CurrentState;
                            item.FilePath = strmFiles[0].Path;
                            item.FileSize = strmFiles[0].Size;
                            item.CurrentState = FileState.Symlink;
                            item.StateChangedAt = DateTime.UtcNow;
                            await _dbContext.SaveChangesAsync();
                            await LogActivity(item.Id, "TransitionToSymlink", prevState, FileState.Symlink,
                                "Source file gone, .strm already exists");
                            await _hubContext.Clients.All.SendAsync("MediaUpdated", item.Id, item.CurrentState.ToString());
                            return;
                        }
                    }
                }

                // Check if usenet releases exist (regardless of cutoff status)
                var hasUsenet = await HasUsenetReleases(item);
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

                // Trigger targeted grab via NZBdav (delivers .strm files)
                // NZBdav is the download client that creates streaming pointer files
                bool grabTriggered = false;
                try
                {
                    var clients = (item.Type == MediaType.Series || item.Type == MediaType.Anime)
                        ? await _sonarrService.GetDownloadClients()
                        : await _radarrService.GetDownloadClients();

                    var nzbdavClient = clients.FirstOrDefault(c =>
                        c.Enable && c.Name.Equals("NZBdav", StringComparison.OrdinalIgnoreCase));

                    if (nzbdavClient != null)
                    {
                        // Search for usenet releases
                        IEnumerable<ReleaseResult> releases;
                        if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                        {
                            if (!item.SeasonNumber.HasValue || !item.EpisodeNumber.HasValue)
                            {
                                _logger.LogWarning("[TransitionService] Cannot search: missing season/episode for {Title}", item.Title);
                                releases = Enumerable.Empty<ReleaseResult>();
                            }
                            else
                            {
                                var episodeId = await _sonarrService.GetEpisodeId(item.SonarrId.Value, item.SeasonNumber.Value, item.EpisodeNumber.Value);
                                releases = episodeId.HasValue
                                    ? await _sonarrService.SearchReleases(item.SonarrId.Value, new[] { episodeId.Value })
                                    : Enumerable.Empty<ReleaseResult>();
                            }
                        }
                        else if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                        {
                            releases = await _radarrService.SearchReleases(item.RadarrId.Value);
                        }
                        else
                        {
                            releases = Enumerable.Empty<ReleaseResult>();
                        }

                        // Filter to usenet only (NZBdav handles usenet) and skip blocklisted
                        var blocklist = (item.Type == MediaType.Series || item.Type == MediaType.Anime)
                            ? await _sonarrService.GetBlocklistedTitles()
                            : await _radarrService.GetBlocklistedTitles();

                        var normalizedItemTitle = NormalizeTitle(item.Title);
                        var usenetReleases = releases
                            .Where(r => r.Protocol?.Equals("usenet", StringComparison.OrdinalIgnoreCase) == true)
                            .Where(r => !blocklist.Contains(r.Title))
                            .Where(r => r.CustomFormatScore >= 0)
                            .Where(r => ReleaseMatchesTitle(r.Title, normalizedItemTitle))
                            .OrderByDescending(r => r.CustomFormatScore)
                            .ThenByDescending(r => r.QualityWeight)
                            .ToList();

                        _logger.LogInformation("[TransitionService] Found {Count} usenet releases for symlink conversion of {Title}", usenetReleases.Count, item.Title);

                        foreach (var release in usenetReleases)
                        {
                            GrabResult result;
                            if (item.Type == MediaType.Series || item.Type == MediaType.Anime)
                            {
                                int[]? epIds = null;
                                if (item.SonarrId.HasValue && item.SeasonNumber.HasValue && item.EpisodeNumber.HasValue)
                                {
                                    var epId = await _sonarrService.GetEpisodeId(item.SonarrId.Value, item.SeasonNumber.Value, item.EpisodeNumber.Value);
                                    if (epId.HasValue) epIds = new[] { epId.Value };
                                }
                                result = await _sonarrService.GrabRelease(release.Guid, release.IndexerId, nzbdavClient.Id, item.SonarrId, epIds);
                            }
                            else
                            {
                                result = await _radarrService.GrabRelease(release.Guid, release.IndexerId, nzbdavClient.Id, item.RadarrId);
                            }

                            if (result.Success)
                            {
                                _logger.LogInformation("[TransitionService] Grabbed '{Title}' via NZBdav for symlink conversion of {ItemTitle}",
                                    release.Title, item.Title);
                                grabTriggered = true;
                                break;
                            }

                            _logger.LogWarning("[TransitionService] NZBdav grab failed for '{Title}': {Error}, trying next release",
                                release.Title, result.ErrorMessage);
                        }

                        if (!grabTriggered && usenetReleases.Count > 0)
                        {
                            _logger.LogWarning("[TransitionService] All NZBdav grabs failed for {Title}", item.Title);
                        }
                        else if (usenetReleases.Count == 0)
                        {
                            _logger.LogWarning("[TransitionService] No usenet releases found for {Title}", item.Title);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("[TransitionService] NZBdav client not found for {Title}, falling back to TriggerSearch", item.Title);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[TransitionService] NZBdav targeted grab failed for {Title}, will fall back", item.Title);
                }

                // Fallback to TriggerSearch only if targeted grab failed
                if (!grabTriggered)
                {
                    if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                    {
                        _logger.LogWarning("[TransitionService] Falling back to Radarr TriggerSearch for {Title} — client selection is uncontrolled", item.Title);
                        await _radarrService.TriggerSearch(item.RadarrId.Value);
                        grabTriggered = true;
                    }
                    else if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                    {
                        _logger.LogWarning("[TransitionService] Falling back to Sonarr TriggerSearch for {Title} — client selection is uncontrolled", item.Title);
                        if (item.SeasonNumber.HasValue && item.EpisodeNumber.HasValue)
                        {
                            var episodeId = await _sonarrService.GetEpisodeId(item.SonarrId.Value, item.SeasonNumber.Value, item.EpisodeNumber.Value);
                            if (episodeId.HasValue)
                            {
                                await _sonarrService.TriggerSearch(item.SonarrId.Value, new[] { episodeId.Value });
                            }
                            else
                            {
                                await _sonarrService.TriggerSearch(item.SonarrId.Value);
                            }
                        }
                        else
                        {
                            await _sonarrService.TriggerSearch(item.SonarrId.Value);
                        }
                        grabTriggered = true;
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

        private async Task<bool> TryGrabRelease(MediaItem item, int downloadClientId)
        {
            string? requiredProtocol = null;

            if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
            {
                try
                {
                    var clients = await _sonarrService.GetDownloadClients();
                    var targetClient = clients.FirstOrDefault(c => c.Id == downloadClientId);
                    if (targetClient != null)
                    {
                        requiredProtocol = targetClient.Implementation.Equals("Sabnzbd", StringComparison.OrdinalIgnoreCase)
                            ? "usenet" : "torrent";
                    }
                }
                catch { /* ignore, will try without protocol filter */ }

                if (!item.SeasonNumber.HasValue || !item.EpisodeNumber.HasValue)
                {
                    _logger.LogWarning("[TransitionService] Cannot grab: missing season/episode for {Title}", item.Title);
                    return false;
                }

                var episodeId = await _sonarrService.GetEpisodeId(item.SonarrId.Value, item.SeasonNumber.Value, item.EpisodeNumber.Value);
                if (!episodeId.HasValue)
                {
                    _logger.LogWarning("[TransitionService] Cannot grab: episode not found for {Title}", item.Title);
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
                try
                {
                    var clients = await _radarrService.GetDownloadClients();
                    var targetClient = clients.FirstOrDefault(c => c.Id == downloadClientId);
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
            var ordered = releases
                .OrderByDescending(r => r.QualityWeight)
                .ThenByDescending(r => r.CustomFormatScore);

            if (requiredProtocol != null)
            {
                var filtered = ordered.FirstOrDefault(r =>
                    r.Protocol?.Equals(requiredProtocol, StringComparison.OrdinalIgnoreCase) == true);
                if (filtered != null) return filtered;
            }
            return ordered.FirstOrDefault();
        }

        private async Task<bool> HasUsenetReleases(MediaItem item)
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
                    return releases.Any(r => r.Protocol?.Equals("usenet", StringComparison.OrdinalIgnoreCase) == true);
                }
                else if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                {
                    var releases = await _radarrService.SearchReleases(item.RadarrId.Value);
                    return releases.Any(r => r.Protocol?.Equals("usenet", StringComparison.OrdinalIgnoreCase) == true);
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

        private static string NormalizeTitle(string title)
        {
            // Remove punctuation, articles, and extra words to create a matchable form
            var normalized = title.ToLowerInvariant();
            // Remove common suffixes like "(2024)" year markers
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s*\(\d{4}\)\s*", " ");
            // Replace punctuation with spaces
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[^a-z0-9]+", " ");
            // Remove common articles/prepositions for matching
            var stopWords = new HashSet<string> { "a", "an", "the", "of", "in", "and", "for", "to", "is" };
            var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !stopWords.Contains(w))
                .ToList();
            return string.Join(" ", words);
        }

        private static bool ReleaseMatchesTitle(string releaseTitle, string normalizedItemTitle)
        {
            var normalizedRelease = System.Text.RegularExpressions.Regex.Replace(
                releaseTitle.ToLowerInvariant(), @"[^a-z0-9]+", " ");

            // Check if all significant words from the item title appear in the release title
            var itemWords = normalizedItemTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (itemWords.Length == 0) return true;

            var releaseWords = new HashSet<string>(normalizedRelease.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var matchCount = itemWords.Count(w => releaseWords.Contains(w));

            // Require at least 60% of item title words to match
            return matchCount >= Math.Max(1, (int)Math.Ceiling(itemWords.Length * 0.6));
        }
    }
}
