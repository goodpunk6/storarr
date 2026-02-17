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
    public class SonarrService : ISonarrService
    {
        private readonly HttpClient _httpClient;
        private readonly StorarrDbContext _dbContext;
        private readonly ILogger<SonarrService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public SonarrService(
            HttpClient httpClient,
            StorarrDbContext dbContext,
            ILogger<SonarrService> logger)
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
            if (string.IsNullOrEmpty(config.SonarrUrl) || string.IsNullOrEmpty(config.SonarrApiKey))
            {
                throw new InvalidOperationException("Sonarr URL or API key not configured");
            }

            var baseUrl = config.SonarrUrl.TrimEnd('/');
            var request = new HttpRequestMessage(method, $"{baseUrl}/{path}");
            request.Headers.Add("X-Api-Key", config.SonarrApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return request;
        }

        public async Task TestConnection()
        {
            var request = await CreateRequest(HttpMethod.Get, "api/v3/system/status");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        public async Task<IEnumerable<Series>> GetSeries()
        {
            try
            {
                var request = await CreateRequest(HttpMethod.Get, "api/v3/series");
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<List<SonarrSeries>>(content, _jsonOptions);

                return data?.Select(s => new Series
                {
                    Id = s.Id,
                    Title = s.Title,
                    TvdbId = s.TvdbId,
                    Path = s.Path,
                    QualityProfileId = s.QualityProfileId
                }) ?? Enumerable.Empty<Series>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get series from Sonarr");
                return Enumerable.Empty<Series>();
            }
        }

        public async Task<Series?> GetSeries(int sonarrId)
        {
            try
            {
                var request = await CreateRequest(HttpMethod.Get, $"api/v3/series/{sonarrId}");
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var s = JsonSerializer.Deserialize<SonarrSeries>(content, _jsonOptions);

                if (s == null) return null;

                return new Series
                {
                    Id = s.Id,
                    Title = s.Title,
                    TvdbId = s.TvdbId,
                    Path = s.Path,
                    QualityProfileId = s.QualityProfileId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get series {SeriesId}", sonarrId);
                return null;
            }
        }

        public async Task<IEnumerable<SonarrEpisodeFile>> GetEpisodeFiles(int seriesId)
        {
            try
            {
                var request = await CreateRequest(HttpMethod.Get, $"api/v3/episodefile?seriesId={seriesId}");
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<List<SonarrEpisodeFileResponse>>(content, _jsonOptions);

                return data?.Select(e => new SonarrEpisodeFile
                {
                    Id = e.Id,
                    SeriesId = e.SeriesId,
                    SeasonNumber = e.SeasonNumber,
                    EpisodeNumber = e.Episodes?.FirstOrDefault()?.EpisodeNumber ?? 0,
                    Path = e.Path,
                    Size = e.Size,
                    Quality = e.Quality?.Quality?.Name ?? string.Empty
                }) ?? Enumerable.Empty<SonarrEpisodeFile>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get episode files for series {SeriesId}", seriesId);
                return Enumerable.Empty<SonarrEpisodeFile>();
            }
        }

        public async Task<SonarrEpisodeFile?> FindEpisodeFileByPath(int seriesId, string filePath)
        {
            try
            {
                var episodeFiles = await GetEpisodeFiles(seriesId);

                // Normalize paths for comparison
                var normalizedFilePath = filePath.Replace('\\', '/').ToLowerInvariant();
                var fileName = Path.GetFileName(normalizedFilePath);

                // Try exact path match first
                var match = episodeFiles.FirstOrDefault(f =>
                    f.Path.Replace('\\', '/').ToLowerInvariant() == normalizedFilePath);

                if (match != null)
                {
                    _logger.LogDebug("Found episode file by exact path match: {FileId}", match.Id);
                    return match;
                }

                // Try filename match
                match = episodeFiles.FirstOrDefault(f =>
                {
                    var episodeFileName = Path.GetFileName(f.Path.Replace('\\', '/').ToLowerInvariant());
                    return episodeFileName == fileName;
                });

                if (match != null)
                {
                    _logger.LogDebug("Found episode file by filename match: {FileId}", match.Id);
                    return match;
                }

                _logger.LogDebug("No episode file found for path: {FilePath}", filePath);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to find episode file by path: {FilePath}", filePath);
                return null;
            }
        }

        public async Task TriggerSearch(int seriesId, int[]? episodeIds = null)
        {
            try
            {
                object requestBody;

                if (episodeIds != null && episodeIds.Length > 0)
                {
                    requestBody = new { name = "EpisodeSearch", episodeIds };
                }
                else
                {
                    requestBody = new { name = "SeriesSearch", seriesId };
                }

                var json = JsonSerializer.Serialize(requestBody);
                var request = await CreateRequest(HttpMethod.Post, "api/v3/command");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                _logger.LogInformation("Triggered search for series {SeriesId}", seriesId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to trigger search for series {SeriesId}", seriesId);
                throw;
            }
        }

        public async Task<SonarrEpisodeFile?> GetEpisodeFile(int episodeFileId)
        {
            try
            {
                var request = await CreateRequest(HttpMethod.Get, $"api/v3/episodefile/{episodeFileId}");
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var e = JsonSerializer.Deserialize<SonarrEpisodeFileResponse>(content, _jsonOptions);

                if (e == null) return null;

                return new SonarrEpisodeFile
                {
                    Id = e.Id,
                    SeriesId = e.SeriesId,
                    SeasonNumber = e.SeasonNumber,
                    EpisodeNumber = e.Episodes?.FirstOrDefault()?.EpisodeNumber ?? 0,
                    Path = e.Path,
                    Size = e.Size,
                    Quality = e.Quality?.Quality?.Name ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get episode file {EpisodeFileId}", episodeFileId);
                return null;
            }
        }

        public async Task DeleteEpisodeFile(int episodeFileId)
        {
            try
            {
                var request = await CreateRequest(HttpMethod.Delete, $"api/v3/episodefile/{episodeFileId}");
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                _logger.LogInformation("Deleted episode file {EpisodeFileId}", episodeFileId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete episode file {EpisodeFileId}", episodeFileId);
                throw;
            }
        }

        public async Task<bool> DeleteEpisodeFileByPath(int seriesId, string filePath)
        {
            try
            {
                var episodeFile = await FindEpisodeFileByPath(seriesId, filePath);
                if (episodeFile != null)
                {
                    await DeleteEpisodeFile(episodeFile.Id);
                    _logger.LogInformation("Deleted episode file via API: {FilePath} (ID: {FileId})", filePath, episodeFile.Id);
                    return true;
                }

                _logger.LogWarning("Could not find episode file in Sonarr for deletion: {FilePath}", filePath);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete episode file by path: {FilePath}", filePath);
                return false;
            }
        }

        public async Task<List<SonarrQueueItem>> GetQueue()
        {
            try
            {
                var request = await CreateRequest(HttpMethod.Get, "api/v3/queue");
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<SonarrQueueResponse>(content, _jsonOptions);

                return data?.Records?.Select(q => new SonarrQueueItem
                {
                    DownloadId = q.DownloadId,
                    SeriesId = q.SeriesId,
                    EpisodeId = q.EpisodeId,
                    Title = q.Title,
                    Status = q.Status,
                    Size = q.Size,
                    SizeLeft = q.SizeLeft,
                    ErrorMessage = q.ErrorMessage
                }).ToList() ?? new List<SonarrQueueItem>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Sonarr queue");
                return new List<SonarrQueueItem>();
            }
        }
    }

    // JSON response models
    internal class SonarrSeries
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int TvdbId { get; set; }
        public string? Path { get; set; }
        public int QualityProfileId { get; set; }
    }

    internal class SonarrEpisodeFileResponse
    {
        public int Id { get; set; }
        public int SeriesId { get; set; }
        public int SeasonNumber { get; set; }
        public List<SonarrEpisode>? Episodes { get; set; }
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public SonarrQualityInfo? Quality { get; set; }
    }

    internal class SonarrEpisode
    {
        public int EpisodeNumber { get; set; }
    }

    internal class SonarrQualityInfo
    {
        public SonarrQuality? Quality { get; set; }
    }

    internal class SonarrQuality
    {
        public string Name { get; set; } = string.Empty;
    }

    internal class SonarrQueueResponse
    {
        public List<SonarrQueueItemResponse>? Records { get; set; }
    }

    internal class SonarrQueueItemResponse
    {
        public string DownloadId { get; set; } = string.Empty;
        public int SeriesId { get; set; }
        public int EpisodeId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public long Size { get; set; }
        public long SizeLeft { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
