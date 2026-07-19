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
    public class ExclusionsController : ControllerBase
    {
        private readonly StorarrDbContext _dbContext;
        private readonly ISonarrService _sonarrService;
        private readonly IRadarrService _radarrService;
        private readonly IExclusionService _exclusionService;
        private readonly ILogger<ExclusionsController> _logger;

        public ExclusionsController(
            StorarrDbContext dbContext,
            ISonarrService sonarrService,
            IRadarrService radarrService,
            IExclusionService exclusionService,
            ILogger<ExclusionsController> logger)
        {
            _dbContext = dbContext;
            _sonarrService = sonarrService;
            _radarrService = radarrService;
            _exclusionService = exclusionService;
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
                int removedCount = await _exclusionService.CountMatchingMediaItemsAsync(item);

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
        /// If Sonarr/Radarr APIs are configured, will automatically look up IDs by title.
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

                // Look up IDs from Sonarr/Radarr if not provided
                int? sonarrId = dto.SonarrId;
                int? radarrId = dto.RadarrId;
                int? tmdbId = dto.TmdbId;
                int? tvdbId = dto.TvdbId;

                if (dto.Type == MediaType.Series || dto.Type == MediaType.Anime)
                {
                    // Try to look up in Sonarr
                    if (!sonarrId.HasValue || !tvdbId.HasValue)
                    {
                        try
                        {
                            var series = await _sonarrService.LookupSeriesByTitle(dto.Title);
                            if (series != null)
                            {
                                if (!sonarrId.HasValue)
                                {
                                    sonarrId = series.Id;
                                    _logger.LogInformation("[ExclusionsController] Found Sonarr ID {Id} for '{Title}'", sonarrId, dto.Title);
                                }
                                if (!tvdbId.HasValue && series.TvdbId > 0)
                                {
                                    tvdbId = series.TvdbId;
                                    _logger.LogInformation("[ExclusionsController] Found TVDB ID {Id} for '{Title}'", tvdbId, dto.Title);
                                }
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // Sonarr not configured, skip
                            _logger.LogDebug("[ExclusionsController] Sonarr not configured, skipping ID lookup");
                        }
                    }
                }
                else if (dto.Type == MediaType.Movie)
                {
                    // Try to look up in Radarr
                    if (!radarrId.HasValue || !tmdbId.HasValue)
                    {
                        try
                        {
                            var movie = await _radarrService.LookupMovieByTitle(dto.Title);
                            if (movie != null)
                            {
                                if (!radarrId.HasValue)
                                {
                                    radarrId = movie.Id;
                                    _logger.LogInformation("[ExclusionsController] Found Radarr ID {Id} for '{Title}'", radarrId, dto.Title);
                                }
                                if (!tmdbId.HasValue && movie.TmdbId > 0)
                                {
                                    tmdbId = movie.TmdbId;
                                    _logger.LogInformation("[ExclusionsController] Found TMDB ID {Id} for '{Title}'", tmdbId, dto.Title);
                                }
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // Radarr not configured, skip
                            _logger.LogDebug("[ExclusionsController] Radarr not configured, skipping ID lookup");
                        }
                    }
                }

                var exclusion = new ExcludedItem
                {
                    Title = dto.Title,
                    Type = dto.Type,
                    TmdbId = tmdbId,
                    TvdbId = tvdbId,
                    SonarrId = sonarrId,
                    RadarrId = radarrId,
                    Reason = dto.Reason,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.ExcludedItems.Add(exclusion);

                // Soft-pause any existing media items that match this exclusion (rows kept; auto-transitions disabled)
                int pausedCount = await _exclusionService.PauseMatchingMediaItemsAsync(exclusion);

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("[ExclusionsController] Created exclusion for {Title} and paused {Count} media items",
                    exclusion.Title, pausedCount);

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
                    RemovedMediaCount = pausedCount
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
                int resumedCount = await _exclusionService.UnpauseMatchingMediaItemsAsync(item);
                _dbContext.ExcludedItems.Remove(item);
                await _dbContext.SaveChangesAsync();

                return Ok(new
                {
                    message = "Exclusion removed. Matching items will resume auto-transitions.",
                    title = item.Title,
                    resumedMediaCount = resumedCount
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
                var result = await _exclusionService.ExcludeByArrIdAsync(dto);

                if (result.Error != null)
                    return BadRequest(new { error = result.Error });

                if (result.AlreadyExcluded)
                    return BadRequest(new { error = "This item is already excluded", exclusionId = result.ExistingExclusionId });

                await _dbContext.SaveChangesAsync();

                var exclusion = result.Exclusion!;
                _logger.LogInformation("[ExclusionsController] Excluded {Title} and removed {Count} media items",
                    exclusion.Title, result.RemovedMediaCount);

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
                    RemovedMediaCount = result.RemovedMediaCount
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

    }
}
