using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Storarr.Data;
using Storarr.DTOs;
using Storarr.Models;

namespace Storarr.Services
{
    public class ExclusionService : IExclusionService
    {
        private readonly StorarrDbContext _dbContext;
        private readonly ILogger<ExclusionService> _logger;

        public ExclusionService(StorarrDbContext dbContext, ILogger<ExclusionService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<ExcludeByArrIdResult> ExcludeByArrIdAsync(ExcludeByArrIdDto dto, CancellationToken ct = default)
        {
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
                return new ExcludeByArrIdResult { Error = "Either SonarrId or RadarrId is required" };
            }

            var firstItem = await matchingItems.FirstOrDefaultAsync(ct);
            if (firstItem == null && string.IsNullOrEmpty(dto.Title))
            {
                return new ExcludeByArrIdResult { Error = "No matching media items found and no title provided" };
            }

            // Already excluded?
            var existing = await _dbContext.ExcludedItems.FirstOrDefaultAsync(e =>
                (dto.SonarrId.HasValue && e.SonarrId == dto.SonarrId.Value) ||
                (dto.RadarrId.HasValue && e.RadarrId == dto.RadarrId.Value), ct);

            if (existing != null)
            {
                return new ExcludeByArrIdResult { AlreadyExcluded = true, ExistingExclusionId = existing.Id };
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

            int pausedCount = await matchingItems.CountAsync(ct);
            var itemsToPause = await matchingItems.ToListAsync(ct);
            foreach (var m in itemsToPause)
            {
                m.IsExcluded = true;
            }

            _logger.LogInformation("[ExclusionService] Staged soft-pause exclusion for {Title}, pausing {Count} media items",
                exclusion.Title, pausedCount);

            return new ExcludeByArrIdResult { Success = true, Exclusion = exclusion, RemovedMediaCount = pausedCount };
        }

        public async Task<int> PauseMatchingMediaItemsAsync(ExcludedItem exclusion, CancellationToken ct = default)
        {
            var matchingItems = BuildMatcher(exclusion);

            int count = await matchingItems.CountAsync(ct);
            var items = await matchingItems.ToListAsync(ct);
            foreach (var m in items)
            {
                m.IsExcluded = true;
            }
            return count;
        }

        public async Task<int> UnpauseMatchingMediaItemsAsync(ExcludedItem exclusion, CancellationToken ct = default)
        {
            var matchingItems = BuildMatcher(exclusion);

            int count = await matchingItems.CountAsync(ct);
            var items = await matchingItems.ToListAsync(ct);
            foreach (var m in items)
            {
                m.IsExcluded = false;
            }
            return count;
        }

        private IQueryable<MediaItem> BuildMatcher(ExcludedItem exclusion)
        {
            if (exclusion.SonarrId.HasValue)
                return _dbContext.MediaItems.Where(m => m.SonarrId == exclusion.SonarrId);
            if (exclusion.RadarrId.HasValue)
                return _dbContext.MediaItems.Where(m => m.RadarrId == exclusion.RadarrId);
            if (exclusion.TmdbId.HasValue)
                return _dbContext.MediaItems.Where(m => m.TmdbId == exclusion.TmdbId);
            if (exclusion.TvdbId.HasValue)
                return _dbContext.MediaItems.Where(m => m.TvdbId == exclusion.TvdbId);
            return _dbContext.MediaItems.Where(m => false);
        }

        public async Task<int> CountMatchingMediaItemsAsync(ExcludedItem exclusion, CancellationToken ct = default)
        {
            if (exclusion.SonarrId.HasValue)
                return await _dbContext.MediaItems.CountAsync(m => m.SonarrId == exclusion.SonarrId, ct);
            if (exclusion.RadarrId.HasValue)
                return await _dbContext.MediaItems.CountAsync(m => m.RadarrId == exclusion.RadarrId, ct);
            if (exclusion.TmdbId.HasValue)
                return await _dbContext.MediaItems.CountAsync(m => m.TmdbId == exclusion.TmdbId, ct);
            if (exclusion.TvdbId.HasValue)
                return await _dbContext.MediaItems.CountAsync(m => m.TvdbId == exclusion.TvdbId, ct);
            return 0;
        }
    }

    public class ExcludeByArrIdResult
    {
        public bool Success { get; set; }
        public bool AlreadyExcluded { get; set; }
        public int? ExistingExclusionId { get; set; }
        public ExcludedItem? Exclusion { get; set; }
        public int RemovedMediaCount { get; set; }
        public string? Error { get; set; }
    }
}
