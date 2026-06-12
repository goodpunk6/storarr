using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Storarr.Data;
using Storarr.DTOs;
using Storarr.Models;
using Storarr.Services;

namespace Storarr.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class MediaController : ControllerBase
    {
        private readonly StorarrDbContext _dbContext;
        private readonly ITransitionService _transitionService;
        private readonly IFileManagementService _fileService;
        private readonly ILogger<MediaController> _logger;
        private readonly ISonarrService _sonarrService;
        private readonly IRadarrService _radarrService;

        public MediaController(
            StorarrDbContext dbContext,
            ITransitionService transitionService,
            IFileManagementService fileService,
            ILogger<MediaController> logger,
            ISonarrService sonarrService,
            IRadarrService radarrService)
        {
            _dbContext = dbContext;
            _transitionService = transitionService;
            _fileService = fileService;
            _logger = logger;
            _sonarrService = sonarrService;
            _radarrService = radarrService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<MediaItemListDto>>> GetMedia(
            [FromQuery] FileState? state = null,
            [FromQuery] MediaType? type = null,
            [FromQuery] string? search = null,
            [FromQuery] bool? excluded = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            _logger.LogDebug("[MediaController] GetMedia called - state: {State}, type: {Type}, search: {Search}, excluded: {Excluded}, page: {Page}, pageSize: {PageSize}",
                state, type, search, excluded, page, pageSize);

            try
            {
                var query = _dbContext.MediaItems.AsNoTracking().AsQueryable();

                if (state.HasValue)
                    query = query.Where(m => m.CurrentState == state.Value);

                if (type.HasValue)
                    query = query.Where(m => m.Type == type.Value);

                if (!string.IsNullOrEmpty(search))
                    query = query.Where(m => m.Title.Contains(search));

                if (excluded.HasValue)
                    query = query.Where(m => m.IsExcluded == excluded.Value);

                var totalCount = await query.CountAsync();
                _logger.LogDebug("[MediaController] Total items matching query: {Count}", totalCount);

                var config = await _dbContext.Configs.FindAsync(Config.SingletonId);
                var now = DateTime.UtcNow;

                var items = await query
                    .OrderByDescending(m => m.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(m => new MediaItemListDto
                    {
                        Id = m.Id,
                        Title = m.Title,
                        Type = m.Type,
                        CurrentState = m.CurrentState,
                        FilePath = m.FilePath,
                        LastWatchedAt = m.LastWatchedAt,
                        SeasonNumber = m.SeasonNumber,
                        EpisodeNumber = m.EpisodeNumber,
                        FileSize = m.FileSize,
                        IsExcluded = m.IsExcluded,
                        DaysUntilTransition = CalculateDaysUntilTransition(m, config, now),
                        IsOverdue = IsTransitionOverdue(m, config, now),
                        ErrorMessage = m.ErrorMessage
                    })
                    .ToListAsync();

                _logger.LogDebug("[MediaController] Returning {Count} items", items.Count);
                return Ok(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MediaController] Error in GetMedia");
                return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<MediaItemDto>> GetMedia(int id)
        {
            _logger.LogDebug("[MediaController] GetMedia by ID called - id: {Id}", id);

            try
            {
                var item = await _dbContext.MediaItems.FindAsync(id);
                if (item == null)
                {
                    _logger.LogWarning("[MediaController] Media item not found: {Id}", id);
                    return NotFound();
                }

                var config = await _dbContext.Configs.FindAsync(Config.SingletonId);
                var now = DateTime.UtcNow;

                var dto = new MediaItemDto
                {
                    Id = item.Id,
                    Title = item.Title,
                    Type = item.Type,
                    JellyfinId = item.JellyfinId,
                    SonarrId = item.SonarrId,
                    RadarrId = item.RadarrId,
                    TmdbId = item.TmdbId,
                    TvdbId = item.TvdbId,
                    FilePath = item.FilePath,
                    CurrentState = item.CurrentState,
                    CreatedAt = item.CreatedAt,
                    LastWatchedAt = item.LastWatchedAt,
                    StateChangedAt = item.StateChangedAt,
                    SeasonNumber = item.SeasonNumber,
                    EpisodeNumber = item.EpisodeNumber,
                    FileSize = item.FileSize,
                    IsExcluded = item.IsExcluded,
                    DaysUntilTransition = CalculateDaysUntilTransition(item, config, now),
                    IsOverdue = IsTransitionOverdue(item, config, now),
                    TransitionType = GetTransitionType(item, config),
                    ErrorMessage = item.ErrorMessage,
                    ErrorAt = item.ErrorAt
                };

                return Ok(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MediaController] Error in GetMedia by ID");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("{id}/force-download")]
        public async Task<ActionResult> ForceDownload(int id)
        {
            _logger.LogDebug("[MediaController] ForceDownload called for ID: {Id}", id);

            var item = await _dbContext.MediaItems.FindAsync(id);
            if (item == null)
            {
                _logger.LogWarning("[MediaController] Media item not found for force download: {Id}", id);
                return NotFound();
            }

            if (item.CurrentState != FileState.Symlink && item.CurrentState != FileState.PendingSymlink)
            {
                _logger.LogWarning("[MediaController] Invalid state for force download. CurrentState: {State}", item.CurrentState);
                return BadRequest("Can only force download for items in symlink or pending symlink state");
            }

            try
            {
                _logger.LogInformation("[MediaController] Force downloading: {Title}", item.Title);
                await _transitionService.TransitionToMkv(item);
                return Ok(new { message = "Download triggered", item.Id, item.CurrentState });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MediaController] Error in ForceDownload");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("{id}/force-symlink")]
        public async Task<ActionResult> ForceSymlink(int id)
        {
            _logger.LogDebug("[MediaController] ForceSymlink called for ID: {Id}", id);

            var item = await _dbContext.MediaItems.FindAsync(id);
            if (item == null)
            {
                _logger.LogWarning("[MediaController] Media item not found for force symlink: {Id}", id);
                return NotFound();
            }

            if (item.CurrentState != FileState.Mkv && item.CurrentState != FileState.Downloading && item.CurrentState != FileState.PendingSymlink && item.CurrentState != FileState.Error)
            {
                _logger.LogWarning("[MediaController] Invalid state for force symlink. CurrentState: {State}", item.CurrentState);
                return BadRequest("Can only force symlink for items in MKV, downloading, pending, or error state");
            }

            try
            {
                _logger.LogInformation("[MediaController] Force symlink: {Title}", item.Title);
                await _transitionService.TransitionToSymlink(item);
                return Ok(new { message = "Symlink restoration triggered", item.Id, item.CurrentState });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MediaController] Error in ForceSymlink");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("{id}/toggle-excluded")]
        public async Task<ActionResult> ToggleExcluded(int id)
        {
            _logger.LogDebug("[MediaController] ToggleExcluded called for ID: {Id}", id);

            var item = await _dbContext.MediaItems.FindAsync(id);
            if (item == null)
            {
                _logger.LogWarning("[MediaController] Media item not found: {Id}", id);
                return NotFound();
            }

            item.IsExcluded = !item.IsExcluded;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("[MediaController] Toggled exclusion for {Title} to {IsExcluded}", item.Title, item.IsExcluded);

            return Ok(new
            {
                message = item.IsExcluded ? "Item excluded from auto-transitions" : "Item included in auto-transitions",
                id = item.Id,
                isExcluded = item.IsExcluded
            });
        }

        [HttpPut("{id}/excluded")]
        public async Task<ActionResult> SetExcluded(int id, [FromBody] SetExcludedDto dto)
        {
            _logger.LogDebug("[MediaController] SetExcluded called for ID: {Id}, Excluded: {Excluded}", id, dto.IsExcluded);

            var item = await _dbContext.MediaItems.FindAsync(id);
            if (item == null)
            {
                _logger.LogWarning("[MediaController] Media item not found: {Id}", id);
                return NotFound();
            }

            item.IsExcluded = dto.IsExcluded;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("[MediaController] Set exclusion for {Title} to {IsExcluded}", item.Title, item.IsExcluded);

            return Ok(new
            {
                message = item.IsExcluded ? "Item excluded from auto-transitions" : "Item included in auto-transitions",
                id = item.Id,
                isExcluded = item.IsExcluded
            });
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteMedia(int id)
        {
            _logger.LogDebug("[MediaController] DeleteMedia called for ID: {Id}", id);

            var item = await _dbContext.MediaItems.FindAsync(id);
            if (item == null)
            {
                _logger.LogWarning("[MediaController] Media item not found for deletion: {Id}", id);
                return NotFound();
            }

            _logger.LogInformation("[MediaController] Deleting media item: {Title}", item.Title);
            _dbContext.MediaItems.Remove(item);
            await _dbContext.SaveChangesAsync();

            return NoContent();
        }

        [HttpPost("manage")]
        public async Task<ActionResult<ManageMediaResultDto>> ManageMedia([FromBody] ManageMediaRequestDto request)
        {
            // Validation
            if (request.ItemIds == null || request.ItemIds.Count == 0)
                return BadRequest("No items selected");

            if (!request.DeleteFiles && !request.RemoveFromArr && !request.Unmonitor && !request.ReMonitor)
                return BadRequest("At least one action must be selected");

            if (request.RemoveFromArr && (request.Unmonitor || request.ReMonitor))
                return BadRequest("Cannot combine 'remove from Sonarr/Radarr' with monitor state changes");

            if (request.Unmonitor && request.ReMonitor)
                return BadRequest("Cannot both unmonitor and re-monitor");

            var results = new List<ManageMediaItemResult>();

            foreach (var itemId in request.ItemIds)
            {
                var result = new ManageMediaItemResult { ItemId = itemId };
                var item = await _dbContext.MediaItems.FindAsync(itemId);

                if (item == null)
                {
                    result.Errors.Add("Item not found in Storarr database");
                    results.Add(result);
                    continue;
                }

                result.Title = item.Title;
                result.Type = item.Type.ToString();

                try
                {
                    var isSonarr = item.Type == MediaType.Series || item.Type == MediaType.Anime;

                    // Step 1: Delete files
                    if (request.DeleteFiles)
                    {
                        try
                        {
                            var fileDeleted = false;
                            var arrFilePath = await RemapToArrPath(item.FilePath, item);
                            if (isSonarr)
                            {
                                if (item.SonarrId.HasValue)
                                {
                                    if (item.SonarrFileId.HasValue)
                                    {
                                        await _sonarrService.DeleteEpisodeFile(item.SonarrFileId.Value);
                                        fileDeleted = true;
                                    }
                                    else
                                    {
                                        fileDeleted = await _sonarrService.DeleteEpisodeFileByPath(item.SonarrId.Value, item.FilePath)
                                            || await _sonarrService.DeleteEpisodeFileByPath(item.SonarrId.Value, arrFilePath);
                                    }
                                }
                                else
                                {
                                    result.Errors.Add("No Sonarr ID — cannot delete file");
                                }
                            }
                            else
                            {
                                if (item.RadarrId.HasValue)
                                {
                                    if (item.RadarrFileId.HasValue)
                                    {
                                        await _radarrService.DeleteMovieFile(item.RadarrFileId.Value);
                                        fileDeleted = true;
                                    }
                                    else
                                    {
                                        // Try path-based deletion first, then query Radarr for the actual file ID
                                        fileDeleted = await _radarrService.DeleteMovieFileByPath(item.RadarrId.Value, item.FilePath)
                                            || await _radarrService.DeleteMovieFileByPath(item.RadarrId.Value, arrFilePath);

                                        if (!fileDeleted)
                                        {
                                            var movie = await _radarrService.GetMovie(item.RadarrId.Value);
                                            if (movie != null && movie.MovieFileId.GetValueOrDefault() > 0)
                                            {
                                                await _radarrService.DeleteMovieFile(movie.MovieFileId!.Value);
                                                fileDeleted = true;
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    result.Errors.Add("No Radarr ID — cannot delete file");
                                }
                            }

                            if (fileDeleted && !result.Errors.Any(e => e.Contains("cannot delete file")))
                            {
                                result.Actions.Add("deleteFiles");
                                _logger.LogInformation("[MediaController] Deleted file for {Title} (ID={Id})", item.Title, item.Id);
                            }
                            else if (!result.Errors.Any())
                            {
                                result.Errors.Add("File not found in Sonarr/Radarr — may already be deleted");
                                _logger.LogWarning("[MediaController] File not found in arr for {Title}: {Path}", item.Title, item.FilePath);
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add($"Failed to delete file: {ex.Message}");
                            _logger.LogWarning(ex, "[MediaController] Failed to delete file for {Title}", item.Title);
                        }
                    }

                    // Step 2: Monitor state
                    if (request.Unmonitor || request.ReMonitor)
                    {
                        var monitored = request.ReMonitor;
                        try
                        {
                            if (isSonarr && item.SonarrId.HasValue)
                            {
                                await _sonarrService.SetSeriesMonitorState(item.SonarrId.Value, monitored);
                                result.Actions.Add(monitored ? "reMonitor" : "unmonitor");
                            }
                            else if (!isSonarr && item.RadarrId.HasValue)
                            {
                                await _radarrService.SetMovieMonitorState(item.RadarrId.Value, monitored);
                                result.Actions.Add(monitored ? "reMonitor" : "unmonitor");
                            }
                            else
                            {
                                result.Errors.Add($"No {(isSonarr ? "Sonarr" : "Radarr")} ID — cannot change monitor state");
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add($"Failed to change monitor state: {ex.Message}");
                            _logger.LogWarning(ex, "[MediaController] Failed to change monitor state for {Title}", item.Title);
                        }
                    }

                    // Step 3: Remove from arr
                    if (request.RemoveFromArr)
                    {
                        try
                        {
                            // Files were already deleted in step 1, so don't double-delete
                            var shouldDeleteFiles = false;
                            if (isSonarr && item.SonarrId.HasValue)
                            {
                                await _sonarrService.DeleteSeries(item.SonarrId.Value, shouldDeleteFiles);
                                result.Actions.Add("removeFromArr");
                            }
                            else if (!isSonarr && item.RadarrId.HasValue)
                            {
                                await _radarrService.DeleteMovie(item.RadarrId.Value, shouldDeleteFiles);
                                result.Actions.Add("removeFromArr");
                            }
                            else
                            {
                                result.Errors.Add($"No {(isSonarr ? "Sonarr" : "Radarr")} ID — cannot remove from arr");
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add($"Failed to remove from arr: {ex.Message}");
                            _logger.LogWarning(ex, "[MediaController] Failed to remove {Title} from arr", item.Title);
                        }
                    }

                    // Step 4: Update Storarr DB
                    var keepInDb = request.RemoveFromArr && !request.DeleteFiles;

                    if (keepInDb)
                    {
                        item.SonarrId = null;
                        item.RadarrId = null;
                        item.SonarrFileId = null;
                        item.RadarrFileId = null;
                        item.CurrentState = FileState.Symlink;
                        item.StateChangedAt = DateTime.UtcNow;
                        _logger.LogInformation("[MediaController] Cleared arr IDs for {Title} — keeping in DB (files on disk)", item.Title);
                    }
                    else if (request.DeleteFiles || request.RemoveFromArr)
                    {
                        _dbContext.MediaItems.Remove(item);
                        _logger.LogInformation("[MediaController] Removed {Title} from Storarr DB", item.Title);
                    }

                    // Step 5: Activity log
                    foreach (var action in result.Actions)
                    {
                        _dbContext.ActivityLogs.Add(new ActivityLog
                        {
                            MediaItemId = item.Id,
                            Action = $"Manage_{action}",
                            FromState = item.CurrentState.ToString(),
                            ToState = keepInDb ? "Cleared" : "Removed",
                            Details = $"Manage action: {action}",
                            Timestamp = DateTime.UtcNow
                        });
                    }

                    result.Success = !result.Errors.Any() || result.Actions.Any();
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Unexpected error: {ex.Message}");
                    _logger.LogError(ex, "[MediaController] Unexpected error managing {Title}", item.Title);
                }

                results.Add(result);
            }

            await _dbContext.SaveChangesAsync();
            return Ok(new ManageMediaResultDto { Results = results });
        }

        [HttpPost("clear-ghost-pending")]
        public async Task<ActionResult> ClearGhostPending()
        {
            _logger.LogDebug("[MediaController] ClearGhostPending called");

            var pendingItems = await _dbContext.MediaItems
                .Where(m => m.CurrentState == FileState.PendingSymlink || m.CurrentState == FileState.Downloading || m.CurrentState == FileState.Error)
                .ToListAsync();

            _logger.LogDebug("[MediaController] Found {Count} PendingSymlink items", pendingItems.Count);

            int cleared = 0;
            foreach (var item in pendingItems)
            {
                var fileExists = await _fileService.FileExists(item.FilePath);
                FileState newState;

                if (!fileExists)
                {
                    // File gone — revert based on path extension
                    newState = item.FilePath.EndsWith(".strm", StringComparison.OrdinalIgnoreCase)
                        ? FileState.Symlink
                        : FileState.Mkv;
                }
                else if (item.FilePath.EndsWith(".strm", StringComparison.OrdinalIgnoreCase)
                    || await _fileService.IsSymlink(item.FilePath))
                {
                    // File exists but is a .strm/symlink — webhook missed it, state should be Symlink
                    newState = FileState.Symlink;
                }
                else
                {
                    // File exists and is a real file — might be a legitimate download in progress
                    _logger.LogDebug("[MediaController] Skipping {Title} — real file exists at {Path}", item.Title, item.FilePath);
                    continue;
                }

                _logger.LogInformation("[MediaController] Clearing ghost PendingSymlink for {Title} — reverting to {State}", item.Title, newState);

                var previousState = item.CurrentState;
                item.CurrentState = newState;
                item.StateChangedAt = DateTime.UtcNow;

                _dbContext.ActivityLogs.Add(new ActivityLog
                {
                    MediaItemId = item.Id,
                    Action = "ClearGhostPending",
                    FromState = previousState.ToString(),
                    ToState = newState.ToString(),
                    Details = "File not found on disk, reverting from ghost PendingSymlink",
                    Timestamp = DateTime.UtcNow
                });

                cleared++;
            }

            if (cleared > 0)
            {
                await _dbContext.SaveChangesAsync();
            }

            _logger.LogInformation("[MediaController] Cleared {Cleared}/{Total} ghost PendingSymlink items", cleared, pendingItems.Count);

            return Ok(new { cleared, total = pendingItems.Count });
        }

        [HttpPost]
        public async Task<ActionResult<MediaItemDto>> CreateMedia([FromBody] CreateMediaItemDto dto)
        {
            _logger.LogDebug("[MediaController] CreateMedia called - Title: {Title}, Type: {Type}, Path: {Path}",
                dto.Title, dto.Type, dto.FilePath);

            try
            {
                // Validate path is within allowed directory
                await _fileService.ValidatePath(dto.FilePath);

                var exists = await _dbContext.MediaItems.AnyAsync(m => m.FilePath == dto.FilePath);
                if (exists)
                {
                    _logger.LogWarning("[MediaController] Media item already exists at path: {Path}", dto.FilePath);
                    return BadRequest("Media item with this file path already exists");
                }

                var isSymlink = await _fileService.IsSymlink(dto.FilePath);
                var fileSize = await _fileService.GetFileSize(dto.FilePath);

                _logger.LogDebug("[MediaController] File check - IsSymlink: {IsSymlink}, Size: {Size}", isSymlink, fileSize);

                var item = new MediaItem
                {
                    Title = dto.Title,
                    Type = dto.Type,
                    JellyfinId = dto.JellyfinId,
                    SonarrId = dto.SonarrId,
                    RadarrId = dto.RadarrId,
                    TmdbId = dto.TmdbId,
                    TvdbId = dto.TvdbId,
                    FilePath = dto.FilePath,
                    SeasonNumber = dto.SeasonNumber,
                    EpisodeNumber = dto.EpisodeNumber,
                    CurrentState = isSymlink ? FileState.Symlink : FileState.Mkv,
                    FileSize = fileSize,
                    CreatedAt = DateTime.UtcNow,
                    StateChangedAt = DateTime.UtcNow,
                    IsExcluded = false
                };

                _dbContext.MediaItems.Add(item);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("[MediaController] Created media item: {Title} (ID: {Id})", item.Title, item.Id);

                return CreatedAtAction(nameof(GetMedia), new { id = item.Id }, item);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "[MediaController] Path traversal attempt in CreateMedia");
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MediaController] Error in CreateMedia");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Returns the number of days until transition. Negative values indicate the transition is overdue.
        /// </summary>
        private static int? CalculateDaysUntilTransition(MediaItem item, Config? config, DateTime now)
        {
            if (config == null || item.IsExcluded) return null;

            return item.CurrentState switch
            {
                FileState.Symlink => (int)(config.GetSymlinkToMkvTimeSpan() - (now - (item.LastWatchedAt ?? item.CreatedAt))).TotalDays,
                FileState.Mkv => (int)(config.GetMkvToSymlinkTimeSpan() - (now - (item.LastWatchedAt ?? item.StateChangedAt ?? item.CreatedAt))).TotalDays,
                _ => null
            };
        }

        private static bool IsTransitionOverdue(MediaItem item, Config? config, DateTime now)
        {
            var days = CalculateDaysUntilTransition(item, config, now);
            return days.HasValue && days.Value < 0;
        }

        private static string? GetTransitionType(MediaItem item, Config? config)
        {
            if (config == null) return null;
            if (item.IsExcluded) return "Excluded";

            return item.CurrentState switch
            {
                FileState.Symlink => "ToMkv",
                FileState.Mkv => "ToSymlink",
                FileState.Downloading => "Downloading",
                FileState.PendingSymlink => "AwaitingSymlink",
                FileState.Error => "Error",
                _ => null
            };
        }

        private async Task<string> RemapToArrPath(string path, MediaItem item)
        {
            try
            {
                (string storarrPrefix, string arrPrefix)? mapping = null;

                if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
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
                _logger.LogWarning(ex, "[MediaController] Failed to dynamically remap path {Path}, using original", path);
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
    }
}
