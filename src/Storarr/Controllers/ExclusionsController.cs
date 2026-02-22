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

namespace Storarr.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class ExclusionsController : ControllerBase
    {
        private readonly StorarrDbContext _dbContext;
        private readonly ILogger<ExclusionsController> _logger;

        public ExclusionsController(
            StorarrDbContext dbContext,
            ILogger<ExclusionsController> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// Get all excluded items
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ExcludedItemListDto>>> GetExclusions(
            [FromQuery] MediaType? type = null,
            [FromQuery] string? search = null)
        {
            _logger.LogDebug("[ExclusionsController] GetExclusions called - type: {Type}, search: {Search}", type, search);

            try
            {
                var query = _dbContext.ExcludedItems.AsNoTracking().AsQueryable();

                if (type.HasValue)
                    query = query.Where(e => e.Type == type.Value);

                if (!string.IsNullOrEmpty(search))
                    query = query.Where(e => e.Title.Contains(search));

                var items = await query
                    .OrderByDescending(e => e.CreatedAt)
                    .Select(e => new ExcludedItemListDto
                    {
                        Id = e.Id,
                        Title = e.Title,
                        Type = e.Type,
                        TmdbId = e.TmdbId,
                        TvdbId = e.TvdbId,
                        Reason = e.Reason,
                        CreatedAt = e.CreatedAt
                    })
                    .ToListAsync();

                return Ok(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ExclusionsController] Error in GetExclusions");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Get a single excluded item by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<ExcludedItemDto>> GetExclusion(int id)
        {
            _logger.LogDebug("[ExclusionsController] GetExclusion called for ID: {Id}", id);

            try
            {
                var item = await _dbContext.ExcludedItems.FindAsync(id);
                if (item == null)
                {
                    _logger.LogWarning("[ExclusionsController] Excluded item not found: {Id}", id);
                    return NotFound();
                }

                // Count how many media items would be/were affected
                int removedCount = await CountMatchingMediaItems(item);

                var dto = new ExcludedItemDto
                {
                    Id = item.Id,
                    Title = item.Title,
                    Type = item.Type,
                    TmdbId = item.TmdbId,
                    TvdbId = item.TvdbId,
                    SonarrId = item.SonarrId,
                    RadarrId = item.RadarrId,
                    Reason = item.Reason,
                    CreatedAt = item.CreatedAt,
                    RemovedMediaCount = removedCount
                };

                return Ok(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ExclusionsController] Error in GetExclusion");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Add a new exclusion. This will also remove any existing media items matching the exclusion.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<ExcludedItemDto>> CreateExclusion([FromBody] CreateExcludedItemDto dto)
        {
            _logger.LogDebug("[ExclusionsController] CreateExclusion called - Title: {Title}, Type: {Type}", dto.Title, dto.Type);

            try
            {
                // Check if this item is already excluded
                var existingExclusion = await FindExistingExclusion(dto);
                if (existingExclusion != null)
                {
                    _logger.LogWarning("[ExclusionsController] Item already excluded: {Title}", dto.Title);
                    return BadRequest(new { error = "This item is already excluded", exclusionId = existingExclusion.Id });
                }

                var exclusion = new ExcludedItem
                {
                    Title = dto.Title,
                    Type = dto.Type,
                    TmdbId = dto.TmdbId,
                    TvdbId = dto.TvdbId,
                    SonarrId = dto.SonarrId,
                    RadarrId = dto.RadarrId,
                    Reason = dto.Reason,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.ExcludedItems.Add(exclusion);

                // Remove any existing media items that match this exclusion
                int removedCount = await RemoveMatchingMediaItems(exclusion);

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("[ExclusionsController] Created exclusion for {Title} and removed {Count} media items",
                    exclusion.Title, removedCount);

                var result = new ExcludedItemDto
                {
                    Id = exclusion.Id,
                    Title = exclusion.Title,
                    Type = exclusion.Type,
                    TmdbId = exclusion.TmdbId,
                    TvdbId = exclusion.TvdbId,
                    SonarrId = exclusion.SonarrId,
                    RadarrId = exclusion.RadarrId,
                    Reason = exclusion.Reason,
                    CreatedAt = exclusion.CreatedAt,
                    RemovedMediaCount = removedCount
                };

                return CreatedAtAction(nameof(GetExclusion), new { id = exclusion.Id }, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ExclusionsController] Error in CreateExclusion");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Remove an exclusion. This allows the item to be processed again in future library scans.
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteExclusion(int id)
        {
            _logger.LogDebug("[ExclusionsController] DeleteExclusion called for ID: {Id}", id);

            try
            {
                var item = await _dbContext.ExcludedItems.FindAsync(id);
                if (item == null)
                {
                    _logger.LogWarning("[ExclusionsController] Excluded item not found: {Id}", id);
                    return NotFound();
                }

                _logger.LogInformation("[ExclusionsController] Removing exclusion for {Title}", item.Title);
                _dbContext.ExcludedItems.Remove(item);
                await _dbContext.SaveChangesAsync();

                return Ok(new
                {
                    message = "Exclusion removed. The item will be processed in the next library scan.",
                    title = item.Title
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ExclusionsController] Error in DeleteExclusion");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Check if a specific series or movie is excluded
        /// </summary>
        [HttpGet("check")]
        public async Task<ActionResult<object>> CheckExclusion(
            [FromQuery] int? sonarrId = null,
            [FromQuery] int? radarrId = null,
            [FromQuery] int? tmdbId = null,
            [FromQuery] int? tvdbId = null)
        {
            _logger.LogDebug("[ExclusionsController] CheckExclusion called - sonarrId: {SonarrId}, radarrId: {RadarrId}, tmdbId: {TmdbId}, tvdbId: {TvdbId}",
                sonarrId, radarrId, tmdbId, tvdbId);

            try
            {
                var query = _dbContext.ExcludedItems.AsQueryable();

                if (sonarrId.HasValue)
                    query = query.Where(e => e.SonarrId == sonarrId.Value);
                else if (radarrId.HasValue)
                    query = query.Where(e => e.RadarrId == radarrId.Value);
                else if (tmdbId.HasValue)
                    query = query.Where(e => e.TmdbId == tmdbId.Value);
                else if (tvdbId.HasValue)
                    query = query.Where(e => e.TvdbId == tvdbId.Value);
                else
                    return BadRequest(new { error = "At least one ID parameter is required" });

                var exclusion = await query.FirstOrDefaultAsync();

                return Ok(new
                {
                    isExcluded = exclusion != null,
                    exclusion = exclusion != null ? new ExcludedItemListDto
                    {
                        Id = exclusion.Id,
                        Title = exclusion.Title,
                        Type = exclusion.Type,
                        TmdbId = exclusion.TmdbId,
                        TvdbId = exclusion.TvdbId,
                        Reason = exclusion.Reason,
                        CreatedAt = exclusion.CreatedAt
                    } : null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ExclusionsController] Error in CheckExclusion");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Exclude all items for a specific series (by SonarrId) or movie (by RadarrId)
        /// and remove existing tracked media items
        /// </summary>
        [HttpPost("by-arr-id")]
        public async Task<ActionResult<ExcludedItemDto>> ExcludeByArrId([FromBody] ExcludeByArrIdDto dto)
        {
            _logger.LogDebug("[ExclusionsController] ExcludeByArrId called - SonarrId: {SonarrId}, RadarrId: {RadarrId}",
                dto.SonarrId, dto.RadarrId);

            try
            {
                // Find matching media items to get the title and other IDs
                IQueryable<MediaItem> matchingItems;
                MediaType itemType;

                if (dto.SonarrId.HasValue)
                {
                    matchingItems = _dbContext.MediaItems.Where(m => m.SonarrId == dto.SonarrId.Value);
                    itemType = MediaType.Series;
                }
                else if (dto.RadarrId.HasValue)
                {
                    matchingItems = _dbContext.MediaItems.Where(m => m.RadarrId == dto.RadarrId.Value);
                    itemType = MediaType.Movie;
                }
                else
                {
                    return BadRequest(new { error = "Either SonarrId or RadarrId is required" });
                }

                var firstItem = await matchingItems.FirstOrDefaultAsync();
                if (firstItem == null && string.IsNullOrEmpty(dto.Title))
                {
                    return BadRequest(new { error = "No matching media items found and no title provided" });
                }

                // Check if already excluded
                var existingExclusion = await _dbContext.ExcludedItems
                    .FirstOrDefaultAsync(e =>
                        (dto.SonarrId.HasValue && e.SonarrId == dto.SonarrId.Value) ||
                        (dto.RadarrId.HasValue && e.RadarrId == dto.RadarrId.Value));

                if (existingExclusion != null)
                {
                    return BadRequest(new { error = "This item is already excluded", exclusionId = existingExclusion.Id });
                }

                var exclusion = new ExcludedItem
                {
                    Title = dto.Title ?? firstItem?.Title ?? "Unknown",
                    Type = dto.Type ?? firstItem?.Type ?? itemType,
                    SonarrId = dto.SonarrId,
                    RadarrId = dto.RadarrId,
                    TmdbId = dto.TmdbId ?? firstItem?.TmdbId,
                    TvdbId = dto.TvdbId ?? firstItem?.TvdbId,
                    Reason = dto.Reason,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.ExcludedItems.Add(exclusion);

                // Remove matching media items
                int removedCount = await matchingItems.CountAsync();
                _dbContext.MediaItems.RemoveRange(matchingItems);

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("[ExclusionsController] Excluded {Title} and removed {Count} media items",
                    exclusion.Title, removedCount);

                return Ok(new ExcludedItemDto
                {
                    Id = exclusion.Id,
                    Title = exclusion.Title,
                    Type = exclusion.Type,
                    TmdbId = exclusion.TmdbId,
                    TvdbId = exclusion.TvdbId,
                    SonarrId = exclusion.SonarrId,
                    RadarrId = exclusion.RadarrId,
                    Reason = exclusion.Reason,
                    CreatedAt = exclusion.CreatedAt,
                    RemovedMediaCount = removedCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ExclusionsController] Error in ExcludeByArrId");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private async Task<ExcludedItem?> FindExistingExclusion(CreateExcludedItemDto dto)
        {
            return await _dbContext.ExcludedItems
                .FirstOrDefaultAsync(e =>
                    (dto.SonarrId.HasValue && e.SonarrId == dto.SonarrId.Value) ||
                    (dto.RadarrId.HasValue && e.RadarrId == dto.RadarrId.Value) ||
                    (dto.TmdbId.HasValue && e.TmdbId == dto.TmdbId.Value) ||
                    (dto.TvdbId.HasValue && e.TvdbId == dto.TvdbId.Value));
        }

        private async Task<int> RemoveMatchingMediaItems(ExcludedItem exclusion)
        {
            IQueryable<MediaItem> matchingItems = _dbContext.MediaItems.Where(m => false);

            if (exclusion.SonarrId.HasValue)
            {
                matchingItems = _dbContext.MediaItems.Where(m => m.SonarrId == exclusion.SonarrId);
            }
            else if (exclusion.RadarrId.HasValue)
            {
                matchingItems = _dbContext.MediaItems.Where(m => m.RadarrId == exclusion.RadarrId);
            }
            else if (exclusion.TmdbId.HasValue)
            {
                matchingItems = _dbContext.MediaItems.Where(m => m.TmdbId == exclusion.TmdbId);
            }
            else if (exclusion.TvdbId.HasValue)
            {
                matchingItems = _dbContext.MediaItems.Where(m => m.TvdbId == exclusion.TvdbId);
            }

            int count = await matchingItems.CountAsync();
            _dbContext.MediaItems.RemoveRange(matchingItems);
            return count;
        }

        private async Task<int> CountMatchingMediaItems(ExcludedItem exclusion)
        {
            if (exclusion.SonarrId.HasValue)
            {
                return await _dbContext.MediaItems.CountAsync(m => m.SonarrId == exclusion.SonarrId);
            }
            if (exclusion.RadarrId.HasValue)
            {
                return await _dbContext.MediaItems.CountAsync(m => m.RadarrId == exclusion.RadarrId);
            }
            if (exclusion.TmdbId.HasValue)
            {
                return await _dbContext.MediaItems.CountAsync(m => m.TmdbId == exclusion.TmdbId);
            }
            if (exclusion.TvdbId.HasValue)
            {
                return await _dbContext.MediaItems.CountAsync(m => m.TvdbId == exclusion.TvdbId);
            }
            return 0;
        }
    }

    public class ExcludeByArrIdDto
    {
        public int? SonarrId { get; set; }
        public int? RadarrId { get; set; }
        public int? TmdbId { get; set; }
        public int? TvdbId { get; set; }
        public string? Title { get; set; }
        public MediaType? Type { get; set; }
        public string? Reason { get; set; }
    }
}
