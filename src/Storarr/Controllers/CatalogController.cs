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

                        results.Add(new CatalogGroupDto
                        {
                            Title = movie.Title,
                            Type = MediaType.Movie,
                            SonarrId = null,
                            RadarrId = movie.Id,
                            TmdbId = movie.TmdbId > 0 ? movie.TmdbId : (int?)null,
                            TvdbId = null,
                            PosterUrl = GetPosterUrl(movie.Images),
                            TotalEpisodes = movieTracked.Count,
                            TrackedEpisodes = movieTracked.Count,
                            TotalSizeBytes = movieTracked.Sum(m => m.FileSize ?? 0),
                            FormattedSize = FormatSize(movieTracked.Sum(m => m.FileSize ?? 0)),
                            StateBreakdown = movieTracked
                                .GroupBy(m => m.CurrentState.ToString())
                                .ToDictionary(g => g.Key, g => g.Count()),
                            IsExcluded = isExcluded,
                            Episodes = episodes
                        });
                    }
                }

                _logger.LogDebug("[CatalogController] Returning {Count} catalog groups", results.Count);
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CatalogController] Error in GetCatalog");
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
