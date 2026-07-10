using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
                    MovieFileId = m.MovieFileId,
                    Images = m.Images
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
                    MovieFileId = m.MovieFileId,
                    Images = m.Images
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get movie {MovieId}", radarrId);
                return null;
            }
        }

        public async Task<Movie?> LookupMovieByTitle(string title)
        {
            try
            {
                var allMovies = await GetMovies();
                var normalizedTitle = title.ToLowerInvariant().Trim();

                // Try exact match first
                var match = allMovies.FirstOrDefault(m =>
                    m.Title.ToLowerInvariant().Trim() == normalizedTitle);

                if (match != null)
                {
                    _logger.LogDebug("Found movie by exact title match: {Title} -> RadarrId {Id}", title, match.Id);
                    return match;
                }

                // Try contains match
                match = allMovies.FirstOrDefault(m =>
                    m.Title.ToLowerInvariant().Contains(normalizedTitle) ||
                    normalizedTitle.Contains(m.Title.ToLowerInvariant()));

                if (match != null)
                {
                    _logger.LogDebug("Found movie by partial title match: {Title} -> RadarrId {Id}", title, match.Id);
                    return match;
                }

                _logger.LogDebug("No movie found matching title: {Title}", title);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to lookup movie by title: {Title}", title);
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
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("Movie file {MovieFileId} already deleted (404)", movieFileId);
                    return;
                }
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

        public async Task DeleteMovie(int movieId, bool deleteFiles = false)
        {
            var request = await CreateRequest(HttpMethod.Delete, $"api/v3/movie/{movieId}?deleteFiles={deleteFiles.ToString().ToLowerInvariant()}");
            var response = await _httpClient.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return;
            response.EnsureSuccessStatusCode();
        }

        public async Task SetMovieMonitorState(int movieId, bool monitored)
        {
            var getRequest = await CreateRequest(HttpMethod.Get, $"api/v3/movie/{movieId}");
            var getResponse = await _httpClient.SendAsync(getRequest);
            getResponse.EnsureSuccessStatusCode();
            var content = await getResponse.Content.ReadAsStringAsync();
            var movie = JsonSerializer.Deserialize<Movie>(content, _jsonOptions);
            if (movie == null) throw new InvalidOperationException($"Failed to deserialize movie {movieId}");

            movie.Monitored = monitored;
            var putRequest = await CreateRequest(HttpMethod.Put, $"api/v3/movie/{movieId}");
            putRequest.Content = new StringContent(JsonSerializer.Serialize(movie, _jsonOptions), Encoding.UTF8, "application/json");
            var putResponse = await _httpClient.SendAsync(putRequest);
            putResponse.EnsureSuccessStatusCode();
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

        public async Task<IEnumerable<ReleaseResult>> SearchReleases(int movieId)
        {
            try
            {
                var request = await CreateRequest(HttpMethod.Get, $"api/v3/release?movieId={movieId}");
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Radarr release search for movie {MovieId} returned {StatusCode}", movieId, response.StatusCode);
                    return Enumerable.Empty<ReleaseResult>();
                }

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<List<RadarrReleaseResponse>>(content, _jsonOptions);

                // Capture each release's raw JSON (keyed by Radarr's cache key indexerId_guid) so a later
                // "override and add to download queue" grab can echo quality/languages back to Radarr.
                var rawByCacheKey = new Dictionary<string, string>();
                using (var doc = JsonDocument.Parse(content))
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        var guid = el.TryGetProperty("guid", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() : null;
                        var indexerId = el.TryGetProperty("indexerId", out var i) && i.TryGetInt32(out var iv) ? iv : 0;
                        if (guid != null)
                            rawByCacheKey[$"{indexerId}_{guid}"] = el.GetRawText();
                    }
                }

                return data?.Select(r => new ReleaseResult
                {
                    Guid = r.Guid ?? string.Empty,
                    Title = r.Title ?? string.Empty,
                    Size = r.Size,
                    IndexerId = r.IndexerId,
                    DownloadAllowed = r.DownloadAllowed,
                    Protocol = r.Protocol,
                    QualityWeight = r.QualityWeight,
                    CustomFormatScore = r.CustomFormatScore,
                    Seeders = r.Seeders,
                    Age = r.Age,
                    RawJson = rawByCacheKey.TryGetValue($"{r.IndexerId}_{r.Guid}", out var raw) ? raw : null
                }) ?? Enumerable.Empty<ReleaseResult>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search releases for movie {MovieId}", movieId);
                return Enumerable.Empty<ReleaseResult>();
            }
        }

        public async Task<GrabResult> GrabRelease(string guid, int indexerId, int? downloadClientId = null, int? movieId = null)
        {
            try
            {
                var body = new Dictionary<string, object>
                {
                    ["guid"] = guid,
                    ["indexerId"] = indexerId
                };
                if (downloadClientId.HasValue)
                {
                    body["downloadClientId"] = downloadClientId.Value;
                }
                if (movieId.HasValue)
                {
                    body["movieId"] = movieId.Value;
                }

                var json = JsonSerializer.Serialize(body);
                var request = await CreateRequest(HttpMethod.Post, "api/v3/release");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    return new GrabResult { Success = true };
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to grab release: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return new GrabResult { Success = false, ErrorMessage = errorContent };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to grab release {Guid}", guid);
                return new GrabResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<GrabResult> GrabReleaseOverride(string rawJson, int downloadClientId, int movieId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawJson))
                    return new GrabResult { Success = false, ErrorMessage = "Release has no cached data; search again" };

                var body = JsonNode.Parse(rawJson)?.AsObject();
                if (body == null)
                    return new GrabResult { Success = false, ErrorMessage = "Invalid release data" };

                // "Override and add to download queue": force the release into the specified download
                // client, bypassing quality / existing-file rejections. Quality + Languages are echoed
                // from the cached release so Radarr accepts the override.
                body["downloadClientId"] = downloadClientId;
                body["shouldOverride"] = true;
                body["movieId"] = movieId;

                var json = body.ToJsonString();
                var request = await CreateRequest(HttpMethod.Post, "api/v3/release");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Override-grabbed release for movie {MovieId} via download client {ClientId}", movieId, downloadClientId);
                    return new GrabResult { Success = true };
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Override grab failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return new GrabResult { Success = false, ErrorMessage = $"HTTP {response.StatusCode}: {errorContent}" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed override grab for movie {MovieId}", movieId);
                return new GrabResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<IEnumerable<DownloadClientInfo>> GetDownloadClients()
        {
            try
            {
                var request = await CreateRequest(HttpMethod.Get, "api/v3/downloadclient");
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<List<RadarrDownloadClientResponse>>(content, _jsonOptions);

                return data?.Select(d => new DownloadClientInfo
                {
                    Id = d.Id,
                    Name = d.Name ?? string.Empty,
                    Implementation = d.Implementation ?? string.Empty,
                    Enable = d.Enable
                }) ?? Enumerable.Empty<DownloadClientInfo>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get download clients from Radarr");
                return Enumerable.Empty<DownloadClientInfo>();
            }
        }

        public async Task<HashSet<string>> GetBlocklistedTitles()
        {
            try
            {
                var request = await CreateRequest(HttpMethod.Get, "api/v3/blocklist?page=1&pageSize=100");
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<RadarrBlocklistResponse>(content, _jsonOptions);

                return data?.Records?.Select(r => r.SourceTitle).ToHashSet() ?? new HashSet<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get blocklist from Radarr");
                return new HashSet<string>();
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
        public List<SonarrImage>? Images { get; set; }
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

    internal class RadarrReleaseResponse
    {
        public string? Guid { get; set; }
        public string? Title { get; set; }
        public long Size { get; set; }
        public int IndexerId { get; set; }
        public bool DownloadAllowed { get; set; }
        public string? Protocol { get; set; }
        public int QualityWeight { get; set; }
        public int CustomFormatScore { get; set; }
        public int? Seeders { get; set; }
        public int Age { get; set; }
    }

    internal class RadarrDownloadClientResponse
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Implementation { get; set; }
        public bool Enable { get; set; }
    }

    internal class RadarrBlocklistResponse
    {
        public List<RadarrBlocklistItem>? Records { get; set; }
    }

    internal class RadarrBlocklistItem
    {
        public string SourceTitle { get; set; } = string.Empty;
    }
}
