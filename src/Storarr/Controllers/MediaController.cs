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

        public MediaController(
            StorarrDbContext dbContext,
            ITransitionService transitionService,
            IFileManagementService fileService,
            ILogger<MediaController> logger)
        {
            _dbContext = dbContext;
            _transitionService = transitionService;
            _fileService = fileService;
            _logger = logger;
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
                        IsOverdue = IsTransitionOverdue(m, config, now)
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
                    TransitionType = GetTransitionType(item, config)
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

            if (item.CurrentState != FileState.Mkv && item.CurrentState != FileState.Downloading)
            {
                _logger.LogWarning("[MediaController] Invalid state for force symlink. CurrentState: {State}", item.CurrentState);
                return BadRequest("Can only force symlink for items in MKV or downloading state");
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
                _ => null
            };
        }
    }
}
