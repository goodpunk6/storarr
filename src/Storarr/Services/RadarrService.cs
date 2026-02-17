using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Storarr.Data;

namespace Storarr.Services
{
    public class RadarrService : IRadarrService
    {
        private readonly HttpClient _httpClient;
        private readonly StorarrDbContext _dbContext;
        private readonly ILogger<RadarrService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public RadarrService(
            HttpClient httpClient,
            StorarrDbContext dbContext,
            ILogger<RadarrService> logger)
        {
            _httpClient = httpClient;
            _dbContext = dbContext;
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        }

        private async Task<Models.Config> GetConfig()
        {
            return await _dbContext.Configs.FindAsync(1) ?? new Models.Config();
        }

        private async Task<HttpRequestMessage> CreateRequest(HttpMethod method, string path)
        {
            var config = await GetConfig();
            if (string.IsNullOrEmpty(config.RadarrUrl) || string.IsNullOrEmpty(config.RadarrApiKey))
            {
                throw new InvalidOperationException("Radarr URL or API key not configured");
            }

            var baseUrl = config.RadarrUrl.TrimEnd('/');
            var request = new HttpRequestMessage(method, $"{baseUrl}/{path}");
            request.Headers.Add("X-Api-Key", config.RadarrApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return request;
        }

        public async Task TestConnection()
        {
            var request = await CreateRequest(HttpMethod.Get, "api/v3/system/status");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        public async Task<IEnumerable<Movie>> GetMovies()
        {
            try
            {
                var request = await CreateRequest(HttpMethod.Get, "api/v3/movie");
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<List<RadarrMovie>>(content, _jsonOptions);

                return data?.Select(m => new Movie
                {
                    Id = m.Id,
                    Title = m.Title,
                    TmdbId = m.TmdbId,
                    Path = m.Path,
                    QualityProfileId = m.QualityProfileId,
                    MovieFileId = m.MovieFileId
                }) ?? Enumerable.Empty<Movie>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get movies from Radarr");
                return Enumerable.Empty<Movie>();
            }
        }

        public async Task<Movie?> GetMovie(int radarrId)
        {
            try
            {
                var request = await CreateRequest(HttpMethod.Get, $"api/v3/movie/{radarrId}");
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var m = JsonSerializer.Deserialize<RadarrMovie>(content, _jsonOptions);

                if (m == null) return null;

                return new Movie
                {
                    Id = m.Id,
                    Title = m.Title,
                    TmdbId = m.TmdbId,
                    Path = m.Path,
                    QualityProfileId = m.QualityProfileId,
                    MovieFileId = m.MovieFileId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get movie {MovieId}", radarrId);
                return null;
            }
        }

        public async Task<MovieFile?> FindMovieFileByPath(int movieId, string filePath)
        {
            try
            {
                // Get the movie to check its movieFileId
                var movie = await GetMovie(movieId);
                if (movie == null || !movie.MovieFileId.HasValue)
                {
                    _logger.LogDebug("Movie {MovieId} has no file attached", movieId);
                    return null;
                }

                // Get the movie file details
                var movieFile = await GetMovieFile(movie.MovieFileId.Value);
                if (movieFile == null)
                {
                    return null;
                }

                // Normalize paths for comparison
                var normalizedFilePath = filePath.Replace('\\', '/').ToLowerInvariant();
                var normalizedMoviePath = movieFile.Path.Replace('\\', '/').ToLowerInvariant();

                if (normalizedMoviePath == normalizedFilePath)
                {
                    _logger.LogDebug("Found movie file by path match: {FileId}", movieFile.Id);
                    return movieFile;
                }

                // Also try filename match
                var fileName = Path.GetFileName(normalizedFilePath);
                var movieFileName = Path.GetFileName(normalizedMoviePath);
                if (fileName == movieFileName)
                {
                    _logger.LogDebug("Found movie file by filename match: {FileId}", movieFile.Id);
                    return movieFile;
                }

                _logger.LogDebug("Movie file path does not match: {Expected} vs {Actual}", normalizedFilePath, normalizedMoviePath);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to find movie file by path: {FilePath}", filePath);
                return null;
            }
        }

        public async Task TriggerSearch(int movieId)
        {
            try
            {
                var requestBody = new { name = "MoviesSearch", movieIds = new[] { movieId } };
                var json = JsonSerializer.Serialize(requestBody);
                var request = await CreateRequest(HttpMethod.Post, "api/v3/command");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                _logger.LogInformation("Triggered search for movie {MovieId}", movieId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to trigger search for movie {MovieId}", movieId);
                throw;
            }
        }

        public async Task<MovieFile?> GetMovieFile(int movieFileId)
        {
            try
            {
                var request = await CreateRequest(HttpMethod.Get, $"api/v3/moviefile/{movieFileId}");
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var f = JsonSerializer.Deserialize<RadarrMovieFile>(content, _jsonOptions);

                if (f == null) return null;

                return new MovieFile
                {
                    Id = f.Id,
                    MovieId = f.MovieId,
                    Path = f.Path,
                    Size = f.Size,
                    Quality = f.Quality?.Quality?.Name ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get movie file {MovieFileId}", movieFileId);
                return null;
            }
        }

        public async Task DeleteMovieFile(int movieFileId)
        {
            try
            {
                var request = await CreateRequest(HttpMethod.Delete, $"api/v3/moviefile/{movieFileId}");
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                _logger.LogInformation("Deleted movie file {MovieFileId}", movieFileId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete movie file {MovieFileId}", movieFileId);
                throw;
            }
        }

        public async Task<bool> DeleteMovieFileByPath(int movieId, string filePath)
        {
            try
            {
                var movieFile = await FindMovieFileByPath(movieId, filePath);
                if (movieFile != null)
                {
                    await DeleteMovieFile(movieFile.Id);
                    _logger.LogInformation("Deleted movie file via API: {FilePath} (ID: {FileId})", filePath, movieFile.Id);
                    return true;
                }

                _logger.LogWarning("Could not find movie file in Radarr for deletion: {FilePath}", filePath);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete movie file by path: {FilePath}", filePath);
                return false;
            }
        }

        public async Task<List<RadarrQueueItem>> GetQueue()
        {
            try
            {
                var request = await CreateRequest(HttpMethod.Get, "api/v3/queue");
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<RadarrQueueResponse>(content, _jsonOptions);

                return data?.Records?.Select(q => new RadarrQueueItem
                {
                    DownloadId = q.DownloadId,
                    MovieId = q.MovieId,
                    Title = q.Title,
                    Status = q.Status,
                    Size = q.Size,
                    SizeLeft = q.SizeLeft,
                    ErrorMessage = q.ErrorMessage
                }).ToList() ?? new List<RadarrQueueItem>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Radarr queue");
                return new List<RadarrQueueItem>();
            }
        }
    }

    // JSON response models
    internal class RadarrMovie
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int TmdbId { get; set; }
        public string? Path { get; set; }
        public int QualityProfileId { get; set; }
        public int? MovieFileId { get; set; }
    }

    internal class RadarrMovieFile
    {
        public int Id { get; set; }
        public int MovieId { get; set; }
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public RadarrQualityInfo? Quality { get; set; }
    }

    internal class RadarrQualityInfo
    {
        public RadarrQuality? Quality { get; set; }
    }

    internal class RadarrQuality
    {
        public string Name { get; set; } = string.Empty;
    }

    internal class RadarrQueueResponse
    {
        public List<RadarrQueueItemResponse>? Records { get; set; }
    }

    internal class RadarrQueueItemResponse
    {
        public string DownloadId { get; set; } = string.Empty;
        public int MovieId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public long Size { get; set; }
        public long SizeLeft { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
