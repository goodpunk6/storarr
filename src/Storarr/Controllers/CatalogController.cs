using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Storarr.Data;
using Storarr.DTOs;
using Storarr.Models;
using Storarr.Services;

namespace Storarr.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class CatalogController : ControllerBase
    {
        private readonly ISonarrService _sonarrService;
        private readonly IRadarrService _radarrService;
        private readonly IMemoryCache _cache;
        private readonly StorarrDbContext _dbContext;
        private readonly IFileManagementService _fileService;
        private readonly ILogger<CatalogController> _logger;

        public CatalogController(
            ISonarrService sonarrService,
            IRadarrService radarrService,
            IMemoryCache cache,
            StorarrDbContext dbContext,
            IFileManagementService fileService,
            ILogger<CatalogController> logger)
        {
            _sonarrService = sonarrService;
            _radarrService = radarrService;
            _cache = cache;
            _dbContext = dbContext;
            _fileService = fileService;
            _logger = logger;
        }

        /// <summary>
        /// Get the full catalog of series and movies with tracked item aggregation.
        /// Episodes for series are lazy-loaded via a separate endpoint.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CatalogGroupDto>>> GetCatalog(
            [FromQuery] MediaType? type = null,
            [FromQuery] string? search = null)
        {
            _logger.LogDebug("[CatalogController] GetCatalog called - type: {Type}, search: {Search}", type, search);

            try
            {
                // Load all tracked items and excluded items once
                var trackedItems = await _dbContext.MediaItems.AsNoTracking().ToListAsync();
                var excludedItems = await _dbContext.ExcludedItems.AsNoTracking().ToListAsync();

                var results = new List<CatalogGroupDto>();

                // Build series catalog unless filtering to movies only
                if (!type.HasValue || type.Value == MediaType.Series || type.Value == MediaType.Anime)
                {
                    var seriesList = await GetCachedSeries();
                    foreach (var series in seriesList)
                    {
                        if (!string.IsNullOrEmpty(search) &&
                            !series.Title.Contains(search, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var seriesTracked = trackedItems
                            .Where(m => m.SonarrId == series.Id)
                            .ToList();

                        var isExcluded = excludedItems.Any(e => e.SonarrId == series.Id);

                        results.Add(new CatalogGroupDto
                        {
                            Title = series.Title,
                            Type = MediaType.Series,
                            SonarrId = series.Id,
                            RadarrId = null,
                            TmdbId = series.TmdbId,
                            TvdbId = series.TvdbId > 0 ? series.TvdbId : (int?)null,
                            PosterUrl = GetPosterUrl(series.Images),
                            TotalEpisodes = seriesTracked.Count,
                            TrackedEpisodes = seriesTracked.Count,
                            TotalSizeBytes = seriesTracked.Sum(m => m.FileSize ?? 0),
                            FormattedSize = FormatSize(seriesTracked.Sum(m => m.FileSize ?? 0)),
                            StateBreakdown = seriesTracked
                                .GroupBy(m => m.CurrentState.ToString())
                                .ToDictionary(g => g.Key, g => g.Count()),
                            IsExcluded = isExcluded,
                            Episodes = new List<CatalogEpisodeDto>() // Lazy loaded via separate endpoint
                        });
                    }
                }

                // Build movie catalog unless filtering to series only
                if (!type.HasValue || type.Value == MediaType.Movie)
                {
                    var movies = await GetCachedMovies();
                    foreach (var movie in movies)
                    {
                        if (!string.IsNullOrEmpty(search) &&
                            !movie.Title.Contains(search, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var movieTracked = trackedItems
                            .Where(m => m.RadarrId == movie.Id)
                            .ToList();

                        var isExcluded = excludedItems.Any(e => e.RadarrId == movie.Id);

                        var episodes = movieTracked.Select(m => new CatalogEpisodeDto
                        {
                            MediaItemId = m.Id,
                            SeasonNumber = null,
                            EpisodeNumber = null,
                            Title = m.Title,
                            CurrentState = m.CurrentState.ToString(),
                            FileSize = m.FileSize,
                            IsExcluded = m.IsExcluded,
                            FilePath = m.FilePath
                        }).ToList();

                        // Untracked movie -- create a synthetic episode row so it's visible and selectable
                        if (episodes.Count == 0)
                        {
                            episodes.Add(new CatalogEpisodeDto
                            {
                                MediaItemId = null,
                                SeasonNumber = null,
                                EpisodeNumber = null,
                                Title = movie.Title,
                                CurrentState = "Untracked",
                                FileSize = null,
                                IsExcluded = false,
                                FilePath = movie.Path
                            });
                        }

                        var stateBreakdown = movieTracked
                            .GroupBy(m => m.CurrentState.ToString())
                            .ToDictionary(g => g.Key, g => g.Count());
                        if (movieTracked.Count == 0)
                        {
                            stateBreakdown["Untracked"] = 1;
                        }

                        results.Add(new CatalogGroupDto
                        {
                            Title = movie.Title,
                            Type = MediaType.Movie,
                            SonarrId = null,
                            RadarrId = movie.Id,
                            TmdbId = movie.TmdbId > 0 ? movie.TmdbId : (int?)null,
                            TvdbId = null,
                            PosterUrl = GetPosterUrl(movie.Images),
                            TotalEpisodes = Math.Max(1, movieTracked.Count),
                            TrackedEpisodes = movieTracked.Count,
                            TotalSizeBytes = movieTracked.Sum(m => m.FileSize ?? 0),
                            FormattedSize = FormatSize(movieTracked.Sum(m => m.FileSize ?? 0)),
                            StateBreakdown = stateBreakdown,
                            IsExcluded = isExcluded,
                            Episodes = episodes
                        });
                    }
                }

                // Default sort: alphabetically by title
                results = results.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList();

                _logger.LogDebug("[CatalogController] Returning {Count} catalog groups", results.Count);
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CatalogController] Error in GetCatalog");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Get episodes for a specific series, combining Sonarr episode file data
        /// with tracked MediaItem state.
        /// </summary>
        [HttpGet("{sonarrId}/episodes")]
        public async Task<ActionResult<IEnumerable<CatalogEpisodeDto>>> GetSeriesEpisodes(int sonarrId)
        {
            try
            {
                var trackedItems = await _dbContext.MediaItems.AsNoTracking()
                    .Where(m => m.SonarrId == sonarrId)
                    .ToListAsync();

                var episodeFiles = await _sonarrService.GetEpisodeFiles(sonarrId);
                var series = await _sonarrService.GetSeries(sonarrId);
                var seriesTitle = series?.Title ?? $"Series {sonarrId}";

                // Track which episodes were matched to Sonarr files
                var matchedTrackedIds = new HashSet<int>();

                var episodes = episodeFiles.Select(ep =>
                {
                    // Parse season/episode from Sonarr filename first (Sonarr API often returns 0 for episode number)
                    var seasonNum = ep.SeasonNumber;
                    var episodeNum = ep.EpisodeNumber;
                    if ((episodeNum == 0 || episodeNum == null) && !string.IsNullOrEmpty(ep.Path))
                    {
                        var fileName = System.IO.Path.GetFileNameWithoutExtension(ep.Path);
                        var match = System.Text.RegularExpressions.Regex.Match(
                            fileName, @"S(\d{1,2})\s*E(\d{1,2})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            seasonNum = int.Parse(match.Groups[1].Value);
                            episodeNum = int.Parse(match.Groups[2].Value);
                        }
                    }

                    // Match tracked item by season/episode number
                    var tracked = trackedItems.FirstOrDefault(t =>
                        t.SeasonNumber == seasonNum &&
                        t.EpisodeNumber == episodeNum);

                    if (tracked != null) matchedTrackedIds.Add(tracked.Id);

                    return new CatalogEpisodeDto
                    {
                        MediaItemId = tracked?.Id,
                        SeasonNumber = seasonNum,
                        EpisodeNumber = episodeNum,
                        Title = $"{seriesTitle} S{seasonNum:D2}E{episodeNum:D2}",
                        CurrentState = tracked?.CurrentState.ToString() ?? "Untracked",
                        FileSize = tracked?.FileSize ?? ep.Size,
                        IsExcluded = tracked?.IsExcluded ?? false,
                        FilePath = ep.Path
                    };
                }).ToList();

                // Add tracked items that have no corresponding Sonarr episode file
                // (e.g. .strm files where Sonarr no longer tracks the episode file)
                var unmatched = trackedItems.Where(t => !matchedTrackedIds.Contains(t.Id)).ToList();
                foreach (var item in unmatched)
                {
                    var seasonNum = item.SeasonNumber ?? 0;
                    var episodeNum = item.EpisodeNumber ?? 0;

                    // Try parsing from filename if season/episode are 0
                    if ((seasonNum == 0 || episodeNum == 0) && !string.IsNullOrEmpty(item.FilePath))
                    {
                        var fileName = System.IO.Path.GetFileNameWithoutExtension(item.FilePath);
                        var match = System.Text.RegularExpressions.Regex.Match(
                            fileName, @"S(\d{1,2})\s*E(\d{1,2})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            seasonNum = int.Parse(match.Groups[1].Value);
                            episodeNum = int.Parse(match.Groups[2].Value);
                        }
                    }

                    episodes.Add(new CatalogEpisodeDto
                    {
                        MediaItemId = item.Id,
                        SeasonNumber = seasonNum,
                        EpisodeNumber = episodeNum,
                        Title = $"{seriesTitle} S{seasonNum:D2}E{episodeNum:D2}",
                        CurrentState = item.CurrentState.ToString(),
                        FileSize = item.FileSize,
                        IsExcluded = item.IsExcluded,
                        FilePath = item.FilePath
                    });
                }

                return Ok(episodes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting episodes for series {SonarrId}", sonarrId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Creates a MediaItem if it doesn't already exist. Idempotent.
        /// </summary>
        [HttpPost("/api/v1/media/ensure-tracked")]
        public async Task<ActionResult<EnsureTrackedResponseDto>> EnsureTracked(
            [FromBody] EnsureTrackedRequestDto dto)
        {
            try
            {
                // Validate required IDs
                if (!dto.SonarrId.HasValue && !dto.RadarrId.HasValue)
                {
                    return BadRequest(new { error = "Either SonarrId or RadarrId is required." });
                }

                if (string.IsNullOrWhiteSpace(dto.FilePath))
                {
                    return BadRequest(new { error = "FilePath is required." });
                }

                // Idempotency: check if already tracked
                MediaItem? existing = null;
                if (dto.SonarrId.HasValue && dto.SeasonNumber.HasValue && dto.EpisodeNumber.HasValue)
                {
                    existing = await _dbContext.MediaItems.FirstOrDefaultAsync(m =>
                        m.SonarrId == dto.SonarrId &&
                        m.SeasonNumber == dto.SeasonNumber &&
                        m.EpisodeNumber == dto.EpisodeNumber);
                }
                else if (dto.RadarrId.HasValue)
                {
                    existing = await _dbContext.MediaItems.FirstOrDefaultAsync(m =>
                        m.RadarrId == dto.RadarrId);
                }

                if (existing != null)
                {
                    return Ok(new EnsureTrackedResponseDto
                    {
                        MediaItemId = existing.Id,
                        Created = false
                    });
                }

                // TmdbId required for TransitionToSymlink
                if (!dto.TmdbId.HasValue)
                {
                    return BadRequest(new { error = "TmdbId is required for tracking." });
                }

                // Validate file path
                try
                {
                    await _fileService.ValidatePath(dto.FilePath);
                }
                catch (UnauthorizedAccessException ex)
                {
                    return BadRequest(new { error = ex.Message });
                }

                var item = new MediaItem
                {
                    Title = dto.Title,
                    Type = dto.Type,
                    SonarrId = dto.SonarrId,
                    RadarrId = dto.RadarrId,
                    TmdbId = dto.TmdbId,
                    FilePath = dto.FilePath,
                    SeasonNumber = dto.SeasonNumber,
                    EpisodeNumber = dto.EpisodeNumber,
                    CurrentState = FileState.Symlink,
                    CreatedAt = DateTime.UtcNow,
                    StateChangedAt = DateTime.UtcNow,
                    IsExcluded = false
                };

                _dbContext.MediaItems.Add(item);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Created tracked media item: {Title} (ID: {Id})", item.Title, item.Id);

                return Ok(new EnsureTrackedResponseDto
                {
                    MediaItemId = item.Id,
                    Created = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in EnsureTracked");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private async Task<List<Series>> GetCachedSeries()
        {
            if (!_cache.TryGetValue("sonarr_series", out List<Series>? seriesList))
            {
                _logger.LogDebug("[CatalogController] Cache miss for sonarr_series, fetching from API");
                seriesList = (await _sonarrService.GetSeries()).ToList();
                _cache.Set("sonarr_series", seriesList, TimeSpan.FromMinutes(5));
            }
            return seriesList!;
        }

        private async Task<List<Movie>> GetCachedMovies()
        {
            if (!_cache.TryGetValue("radarr_movies", out List<Movie>? movieList))
            {
                _logger.LogDebug("[CatalogController] Cache miss for radarr_movies, fetching from API");
                movieList = (await _radarrService.GetMovies()).ToList();
                _cache.Set("radarr_movies", movieList, TimeSpan.FromMinutes(5));
            }
            return movieList!;
        }

        private static string? GetPosterUrl(List<SonarrImage>? images)
        {
            if (images == null || images.Count == 0) return null;
            var poster = images.FirstOrDefault(i =>
                i.CoverType?.Equals("poster", StringComparison.OrdinalIgnoreCase) == true);
            return poster?.RemoteUrl ?? poster?.Url;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes == 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = (int)Math.Floor(Math.Log(bytes, 1024));
            return $"{bytes / Math.Pow(1024, order):F1} {sizes[order]}";
        }
    }
}
