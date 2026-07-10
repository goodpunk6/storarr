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
                // Search releases manually and grab via the non-NZBdav download client
                // This bypasses quality profile cutoffs (ignores DownloadAllowed flag)
                bool grabTriggered = false;

                try
                {
                    var config = await _dbContext.Configs.FindAsync(Config.SingletonId);
                    var clients = (item.Type == MediaType.Series || item.Type == MediaType.Anime)
                        ? await _sonarrService.GetDownloadClients()
                        : await _radarrService.GetDownloadClients();

                    // Use the configured MKV download client, or fall back to first non-NZBdav client
                    int? targetClientId = (item.Type == MediaType.Series || item.Type == MediaType.Anime)
                        ? config?.SonarrMkvDownloadClientId
                        : config?.RadarrMkvDownloadClientId;

                    DownloadClientInfo? targetClient = targetClientId.HasValue
                        ? clients.FirstOrDefault(c => c.Id == targetClientId.Value)
                        : null;
                    targetClient ??= clients.FirstOrDefault(c =>
                        c.Enable && !c.Name.Equals("NZBdav", StringComparison.OrdinalIgnoreCase));

                    if (targetClient != null)
                    {
                        // Search releases scoped to this specific episode/movie
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

                        // Detect protocol from the MKV download client (SABnzbd=usenet, QBittorrent=torrent)
                        var mkvProtocol = targetClient.Implementation.Equals("Sabnzbd", StringComparison.OrdinalIgnoreCase)
                            ? "usenet" : "torrent";

                        // Filter: protocol matching the download client, not blocklisted, matches item title
                        // DownloadAllowed ignored — bypasses quality profile cutoff
                        var blocklist = (item.Type == MediaType.Series || item.Type == MediaType.Anime)
                            ? await _sonarrService.GetBlocklistedTitles()
                            : await _radarrService.GetBlocklistedTitles();

                        var normalizedItemTitle = NormalizeTitle(item.Title);
                        var eligibleReleases = releases
                            .Where(r => r.Protocol?.Equals(mkvProtocol, StringComparison.OrdinalIgnoreCase) == true)
                            .Where(r => !blocklist.Contains(r.Title))
                            .Where(r => ReleaseMatchesItem(r.Title, item, normalizedItemTitle))
                            .OrderByDescending(r => r.CustomFormatScore)
                            .ThenByDescending(r => r.QualityWeight)
                            .ThenBy(r => r.Age)
                            .ToList();

                        _logger.LogInformation("[TransitionService] Found {Count} eligible {Protocol} releases for {Title} (MKV via {Client})", eligibleReleases.Count, mkvProtocol, item.Title, targetClient.Name);

                        // Delete existing file BEFORE grabbing — Sonarr/Radarr reject releases when
                        // an existing file has equal or higher Custom Format score. Deleting first
                        // removes that rejection so the configured MKV download client can grab.
                        // Applies to any show or movie.
                        if (eligibleReleases.Count > 0)
                        {
                            bool apiDeleted = false;
                            var arrFilePath = await RemapToArrPath(item.FilePath, item);

                            if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                            {
                                apiDeleted = await _radarrService.DeleteMovieFileByPath(item.RadarrId.Value, item.FilePath)
                                    || await _radarrService.DeleteMovieFileByPath(item.RadarrId.Value, arrFilePath);
                            }
                            else if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                            {
                                apiDeleted = await _sonarrService.DeleteEpisodeFileByPath(item.SonarrId.Value, item.FilePath)
                                    || await _sonarrService.DeleteEpisodeFileByPath(item.SonarrId.Value, arrFilePath);
                            }

                            if (await _fileService.FileExists(item.FilePath))
                                await _fileService.DeleteFile(item.FilePath);

                            _logger.LogInformation("[TransitionService] Deleted existing file for {Title} before grab (API: {ApiDeleted})", item.Title, apiDeleted);
                        }

                        foreach (var release in eligibleReleases)
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
                                result = await _sonarrService.GrabRelease(release.Guid, release.IndexerId, targetClient.Id, item.SonarrId, epIds);
                            }
                            else
                            {
                                result = await _radarrService.GrabRelease(release.Guid, release.IndexerId, targetClient.Id, item.RadarrId);
                            }

                            if (result.Success)
                            {
                                _logger.LogInformation("[TransitionService] Grabbed '{Title}' (CF:{CF}, QW:{QW}, Age:{Age}d) via {Client} for {ItemTitle}",
                                    release.Title, release.CustomFormatScore, release.QualityWeight, release.Age, targetClient.Name, item.Title);
                                grabTriggered = true;
                                break;
                            }

                            _logger.LogWarning("[TransitionService] Grab failed for '{Title}': {Error}, trying next",
                                release.Title, result.ErrorMessage);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("[TransitionService] No non-NZBdav download client found for {Title}", item.Title);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[TransitionService] Manual grab failed for {Title}, will fall back", item.Title);
                }

                if (!grabTriggered)
                {
                    // Check if the existing file was deleted before the grab (eligible releases existed but all grabs failed)
                    bool fileExists = await _fileService.FileExists(item.FilePath);
                    if (!fileExists)
                    {
                        // File was deleted but grab failed -> set PendingSymlink so self-heal recreates the .strm
                        _logger.LogWarning("[TransitionService] All grabs failed after file deletion for {Title}. Setting PendingSymlink for .strm recovery.", item.Title);
                        var prevState = item.CurrentState;
                        item.CurrentState = FileState.PendingSymlink;
                        item.PendingSymlinkAt = DateTime.UtcNow;
                        item.StateChangedAt = DateTime.UtcNow;
                        await _dbContext.SaveChangesAsync();
                        await LogActivity(item.Id, "TransitionToMkv", prevState, FileState.PendingSymlink,
                            "All grabs failed after deletion, pending .strm recovery");
                        await _hubContext.Clients.All.SendAsync("MediaUpdated", item.Id, item.CurrentState.ToString());
                    }
                    else
                    {
                        // No eligible releases found, file still exists -> simple revert to Symlink
                        _logger.LogWarning("[TransitionService] No eligible releases via configured MKV download client for {Title}. Reverting to Symlink.", item.Title);
                        await LogActivity(item.Id, "TransitionToMkv", FileState.Symlink, FileState.Symlink,
                            "No releases via configured MKV client, reverted");
                    }
                    return;
                }

                // Grab succeeded - set Downloading. The existing file was already deleted before the grab
                // to bypass Sonarr/Radarr's "existing file has equal or higher Custom Format score" rejection.
                var previousState = item.CurrentState;
                item.CurrentState = FileState.Downloading;
                item.StateChangedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                await LogActivity(item.Id, "TransitionToMkv", previousState, FileState.Downloading,
                    "Grabbed via configured MKV download client");
                await _hubContext.Clients.All.SendAsync("MediaUpdated", item.Id, item.CurrentState.ToString());

                _logger.LogInformation("[TransitionService] Successfully transitioned {Title} to Downloading state", item.Title);
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
                // PHASE 0: If the source file is already gone, check if a .strm/symlink already exists
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

                        if (item.SeasonNumber.HasValue && item.EpisodeNumber.HasValue)
                        {
                            var epPattern = $"S{item.SeasonNumber.Value:D2}E{item.EpisodeNumber.Value:D2}";
                            strmFiles = strmFiles
                                .Where(f => System.IO.Path.GetFileName(f.Path).Contains(epPattern, StringComparison.OrdinalIgnoreCase))
                                .ToList();
                        }

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

                // PHASE 1: Pre-validate — search for eligible releases BEFORE deleting anything
                bool grabTriggered = false;
                DownloadClientInfo? nzbdavClient = null;
                List<ReleaseResult> eligibleReleases = new List<ReleaseResult>();

                try
                {
                    var config = await _dbContext.Configs.FindAsync(Config.SingletonId);
                    var clients = (item.Type == MediaType.Series || item.Type == MediaType.Anime)
                        ? await _sonarrService.GetDownloadClients()
                        : await _radarrService.GetDownloadClients();

                    int? symlinkClientId = (item.Type == MediaType.Series || item.Type == MediaType.Anime)
                        ? config?.SonarrSymlinkDownloadClientId
                        : config?.RadarrSymlinkDownloadClientId;

                    nzbdavClient = symlinkClientId.HasValue
                        ? clients.FirstOrDefault(c => c.Id == symlinkClientId.Value)
                        : null;
                    nzbdavClient ??= clients.FirstOrDefault(c =>
                        c.Enable && c.Name.Equals("NZBdav", StringComparison.OrdinalIgnoreCase));

                    if (nzbdavClient != null)
                    {
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

                        var blocklist = (item.Type == MediaType.Series || item.Type == MediaType.Anime)
                            ? await _sonarrService.GetBlocklistedTitles()
                            : await _radarrService.GetBlocklistedTitles();

                        // Detect protocol from the symlink download client
                        // decypharr (QBittorrent) handles both torrents and usenet via Torbox - no protocol filter
                        // NZBdav (SABnzbd) only handles usenet
                        string? symlinkProtocol = nzbdavClient.Implementation.Equals("Sabnzbd", StringComparison.OrdinalIgnoreCase)
                            ? "usenet" : null; // null = accept all protocols

                        var normalizedItemTitle = NormalizeTitle(item.Title);
                        eligibleReleases = releases
                            .Where(r => symlinkProtocol == null || r.Protocol?.Equals(symlinkProtocol, StringComparison.OrdinalIgnoreCase) == true)
                            .Where(r => !blocklist.Contains(r.Title))
                            .Where(r => ReleaseMatchesItem(r.Title, item, normalizedItemTitle))
                            .OrderByDescending(r => r.CustomFormatScore)
                            .ThenByDescending(r => r.QualityWeight)
                            .ThenBy(r => r.Age)
                            .ToList();

                        _logger.LogInformation("[TransitionService] Pre-validation: found {Count} eligible {Protocol} releases for {Title} (symlink via {Client})", eligibleReleases.Count, symlinkProtocol, item.Title, nzbdavClient.Name);
                    }
                    else
                    {
                        _logger.LogWarning("[TransitionService] NZBdav client not found for {Title}", item.Title);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[TransitionService] Pre-validation release search failed for {Title}", item.Title);
                }

                // ABORT CHECK: If no NZBdav client AND no Arr IDs for TriggerSearch fallback, do not delete the file
                bool hasTriggerSearchFallback = (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                    || ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue);

                if (nzbdavClient == null && !hasTriggerSearchFallback)
                {
                    _logger.LogError("[TransitionService] ABORTING symlink transition for {Title}: no NZBdav client and no Arr ID for TriggerSearch fallback", item.Title);
                    throw new InvalidOperationException($"Cannot transition {item.Title} to symlink: no download client configured and no Arr ID for fallback search");
                }

                // PHASE 2: Delete the file via Arr API and/or disk (only after pre-validation)
                bool apiDeleted = false;
                var arrFilePath = await RemapToArrPath(item.FilePath, item);

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

                // PHASE 3: Grab the best release (releases already searched in Phase 1)
                if (nzbdavClient != null && eligibleReleases.Count > 0)
                {
                    foreach (var release in eligibleReleases)
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
                            _logger.LogInformation("[TransitionService] Grabbed '{Title}' (CF:{CF}, QW:{QW}, Age:{Age}d) via NZBdav for {ItemTitle}",
                                release.Title, release.CustomFormatScore, release.QualityWeight, release.Age, item.Title);
                            grabTriggered = true;
                            break;
                        }

                        _logger.LogWarning("[TransitionService] Grab failed for '{Title}': {Error}, trying next",
                            release.Title, result.ErrorMessage);
                    }
                }
                else if (nzbdavClient != null && eligibleReleases.Count == 0)
                {
                    _logger.LogWarning("[TransitionService] No eligible releases found for {Title}, trying TriggerSearch fallback", item.Title);
                }

                // Fallback: TriggerSearch via Arr
                if (!grabTriggered && hasTriggerSearchFallback)
                {
                    if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                    {
                        _logger.LogWarning("[TransitionService] Falling back to Radarr TriggerSearch for {Title}", item.Title);
                        await _radarrService.TriggerSearch(item.RadarrId.Value);
                        grabTriggered = true;
                    }
                    else if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                    {
                        _logger.LogWarning("[TransitionService] Falling back to Sonarr TriggerSearch for {Title}", item.Title);
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
                item.PendingSymlinkAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                if (grabTriggered)
                {
                    await LogActivity(item.Id, "TransitionToSymlink", previousState, FileState.PendingSymlink,
                        apiDeleted ? "Deleted via Arr API, grab triggered" : "Deleted from disk, grab triggered");
                    _logger.LogInformation("[TransitionService] Transitioned {Title} to PendingSymlink (grab triggered, API deleted: {ApiDeleted})",
                        item.Title, apiDeleted);
                }
                else
                {
                    await LogActivity(item.Id, "TransitionToSymlink", previousState, FileState.PendingSymlink,
                        "File deleted but NO grab triggered - will need manual resolution");
                    _logger.LogError("[TransitionService] Transitioned {Title} to PendingSymlink but NO grab was triggered! File deleted with no replacement. Manual intervention required.",
                        item.Title);
                }

                await _hubContext.Clients.All.SendAsync("MediaUpdated", item.Id, item.CurrentState.ToString());
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

            var now = DateTime.UtcNow;

            // Stale PendingSymlink reaper: runs in ALL modes (this is cleanup, not auto-transition)
            var stalePendingItems = await _dbContext.MediaItems
                .Where(m => m.CurrentState == FileState.PendingSymlink && m.PendingSymlinkAt != null)
                .ToListAsync();
            _logger.LogDebug("[TransitionService] Found {Count} PendingSymlink items to check for staleness", stalePendingItems.Count);

            foreach (var pendingItem in stalePendingItems)
            {
                var pendingAge = now - pendingItem.PendingSymlinkAt!.Value;

                // Check if a symlink/strm file has appeared on disk
                var dir = System.IO.Path.GetDirectoryName(pendingItem.FilePath);
                if (dir != null)
                {
                    try
                    {
                        if (await _fileService.FileExists(pendingItem.FilePath))
                        {
                            var isSymlink = await _fileService.IsSymlink(pendingItem.FilePath);
                            if (isSymlink)
                            {
                                _logger.LogInformation("[TransitionService] Stale PendingSymlink resolved: symlink file appeared for {Title}", pendingItem.Title);
                                pendingItem.CurrentState = FileState.Symlink;
                                pendingItem.StateChangedAt = DateTime.UtcNow;
                                pendingItem.PendingSymlinkAt = null;
                                try
                                {
                                    await _dbContext.SaveChangesAsync();
                                    await LogActivity(pendingItem.Id, "PendingSymlinkReaper", FileState.PendingSymlink, FileState.Symlink,
                                        "Symlink file appeared on disk");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "[TransitionService] Failed to save symlink resolution for {Title}", pendingItem.Title);
                                    _dbContext.Entry(pendingItem).State = Microsoft.EntityFrameworkCore.EntityState.Unchanged;
                                }
                                continue;
                            }
                        }

                        // Check for .strm files in the directory
                        var allFiles = await _fileService.ScanDirectory(dir, false);
                        var strmFiles = allFiles
                            .Where(f => f.IsSymlink || f.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (pendingItem.SeasonNumber.HasValue && pendingItem.EpisodeNumber.HasValue)
                        {
                            var epPattern = $"S{pendingItem.SeasonNumber.Value:D2}E{pendingItem.EpisodeNumber.Value:D2}";
                            strmFiles = strmFiles
                                .Where(f => System.IO.Path.GetFileName(f.Path).Contains(epPattern, StringComparison.OrdinalIgnoreCase))
                                .ToList();
                        }

                        if (strmFiles.Count > 0)
                        {
                            var targetPath = strmFiles[0].Path;
                            // Check if this path is already used by another MediaItem (UNIQUE constraint)
                            var pathAlreadyUsed = await _dbContext.MediaItems
                                .AnyAsync(m => m.Id != pendingItem.Id && m.FilePath == targetPath);
                            if (pathAlreadyUsed)
                            {
                                _logger.LogDebug("[TransitionService] Skipping strm resolution for {Title}: path {Path} already in use by another item",
                                    pendingItem.Title, targetPath);
                            }
                            else
                            {
                                _logger.LogInformation("[TransitionService] Stale PendingSymlink resolved: .strm file found for {Title}", pendingItem.Title);
                                pendingItem.FilePath = targetPath;
                                pendingItem.FileSize = strmFiles[0].Size;
                                pendingItem.CurrentState = FileState.Symlink;
                                pendingItem.StateChangedAt = DateTime.UtcNow;
                                pendingItem.PendingSymlinkAt = null;
                                try
                                {
                                    await _dbContext.SaveChangesAsync();
                                    await LogActivity(pendingItem.Id, "PendingSymlinkReaper", FileState.PendingSymlink, FileState.Symlink,
                                        "STRM file found in directory scan");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "[TransitionService] Failed to save strm resolution for {Title}", pendingItem.Title);
                                    _dbContext.Entry(pendingItem).State = Microsoft.EntityFrameworkCore.EntityState.Unchanged;
                                }
                            }
                            if (!pathAlreadyUsed)
                            {
                                // Item was resolved successfully
                                continue;
                            }
                            // strm found but path already used - fall through to retry
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[TransitionService] Error checking for symlink resolution for {Title}", pendingItem.Title);
                    }
                }

                // If pending for more than 30 minutes but less than 2 hours, retry with TriggerSearch
                // Don\'t use full TransitionToSymlink (it would re-delete files that are already gone)
                if (pendingAge.TotalMinutes > 30 && pendingAge.TotalHours < 2)
                {
                    _logger.LogWarning("[TransitionService] PendingSymlink item {Title} has been pending for {Minutes:F0} minutes - retrying via TriggerSearch",
                        pendingItem.Title, pendingAge.TotalMinutes);
                    try
                    {
                        if (pendingItem.Type == MediaType.Movie && pendingItem.RadarrId.HasValue)
                        {
                            await _radarrService.TriggerSearch(pendingItem.RadarrId.Value);
                        }
                        else if ((pendingItem.Type == MediaType.Series || pendingItem.Type == MediaType.Anime) && pendingItem.SonarrId.HasValue)
                        {
                            if (pendingItem.SeasonNumber.HasValue && pendingItem.EpisodeNumber.HasValue)
                            {
                                var epId = await _sonarrService.GetEpisodeId(pendingItem.SonarrId.Value, pendingItem.SeasonNumber.Value, pendingItem.EpisodeNumber.Value);
                                if (epId.HasValue)
                                    await _sonarrService.TriggerSearch(pendingItem.SonarrId.Value, new[] { epId.Value });
                                else
                                    await _sonarrService.TriggerSearch(pendingItem.SonarrId.Value);
                            }
                            else
                            {
                                await _sonarrService.TriggerSearch(pendingItem.SonarrId.Value);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[TransitionService] TriggerSearch retry failed for {Title}", pendingItem.Title);
                    }
                }
                else if (pendingAge.TotalHours >= 2)
                {
                    _logger.LogWarning("[TransitionService] PendingSymlink item {Title} has been pending for {Hours:F1} hours - auto-reverting to Error state",
                        pendingItem.Title, pendingAge.TotalHours);

                    pendingItem.CurrentState = FileState.Error;
                    pendingItem.ErrorMessage = $"PendingSymlink timed out after {pendingAge.TotalHours:F1} hours - no symlink appeared at {pendingItem.FilePath}";
                    pendingItem.ErrorAt = DateTime.UtcNow;
                    pendingItem.PendingSymlinkAt = null;
                    pendingItem.StateChangedAt = DateTime.UtcNow;

                    try
                    {
                        await _dbContext.SaveChangesAsync();
                        await LogActivity(pendingItem.Id, "StaleReaper", FileState.PendingSymlink, FileState.Error,
                            $"Auto-reverted: {pendingItem.ErrorMessage}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[TransitionService] Failed to save error revert for {Title}", pendingItem.Title);
                        _dbContext.Entry(pendingItem).State = Microsoft.EntityFrameworkCore.EntityState.Unchanged;
                    }
                }
            }
            await _dbContext.SaveChangesAsync();

            // Check LibraryMode - only process auto-transitions in FullAutomation mode
            if (config.LibraryMode != LibraryMode.FullAutomation)
            {
                _logger.LogDebug("[TransitionService] LibraryMode is {Mode}, skipping auto-transitions. Only FullAutomation mode processes transitions automatically.",
                    config.LibraryMode);
                return;
            }

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

        private async Task<string> RemapToArrPath(string path, MediaItem item)
        {
            try
            {
                (string storarrPrefix, string arrPrefix)? mapping = null;

                if ((item.Type == MediaType.Movie) && item.RadarrId.HasValue)
                {
                    var movie = await _radarrService.GetMovie(item.RadarrId.Value);
                    if (movie?.Path != null)
                    {
                        mapping = ExtractPathMapping(movie.Path, path);
                    }
                }
                else if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                {
                    var series = await _sonarrService.GetSeries(item.SonarrId.Value);
                    if (series?.Path != null)
                    {
                        mapping = ExtractPathMapping(series.Path, path);
                    }
                }

                if (mapping != null && path.StartsWith(mapping.Value.storarrPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return mapping.Value.arrPrefix + path.Substring(mapping.Value.storarrPrefix.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TransitionService] Failed to dynamically remap path {Path}, using original", path);
            }

            return path;
        }

        private static (string storarrPrefix, string arrPrefix)? ExtractPathMapping(string arrEntityPath, string storarrFilePath)
        {
            var arrSegments = arrEntityPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var storarrSegments = storarrFilePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            var arrLastName = arrSegments.LastOrDefault();
            if (arrLastName == null) return null;

            var storarrLastNameIdx = Array.FindIndex(storarrSegments,
                s => s.Equals(arrLastName, StringComparison.OrdinalIgnoreCase));
            if (storarrLastNameIdx < 0) return null;

            var storarrPrefix = "/" + string.Join('/', storarrSegments.Take(storarrLastNameIdx)) + "/";
            var arrPrefix = "/" + string.Join('/', arrSegments.Take(arrSegments.Length - 1)) + "/";

            if (storarrFilePath.StartsWith(storarrPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return (storarrPrefix, arrPrefix);
            }

            return null;
        }

        private static string NormalizeTitle(string title)
        {
            var normalized = title.ToLowerInvariant();
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s*\(\d{4}\)\s*", " ");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[^a-z0-9]+", " ");
            var stopWords = new HashSet<string> { "a", "an", "the", "of", "in", "and", "for", "to", "is", "us", "uk" };
            var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !stopWords.Contains(w))
                .ToList();
            return string.Join(" ", words);
        }

        private static bool ReleaseMatchesItem(string releaseTitle, MediaItem item, string normalizedItemTitle)
        {
            var normalizedRelease = System.Text.RegularExpressions.Regex.Replace(
                releaseTitle.ToLowerInvariant(), @"[^a-z0-9]+", " ");

            var itemWords = normalizedItemTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (itemWords.Length == 0) return true;

            var releaseWords = new HashSet<string>(normalizedRelease.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var titleMatchCount = itemWords.Count(w => releaseWords.Contains(w));
            var titleMatch = titleMatchCount >= Math.Max(1, (int)Math.Ceiling(itemWords.Length * 0.5));

            // For series/anime: require BOTH S##E## pattern AND title match
            if ((item.Type == MediaType.Series || item.Type == MediaType.Anime)
                && item.SeasonNumber.HasValue && item.EpisodeNumber.HasValue)
            {
                var epPattern = $"s{item.SeasonNumber.Value:d2}e{item.EpisodeNumber.Value:d2}";
                var epMatch = normalizedRelease.Contains(epPattern);
                return epMatch && titleMatch;
            }

            // For movies: title match only
            return titleMatch;
        }
    }
}
