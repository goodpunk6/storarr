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
                    QualityProfileId = s.QualityProfileId,
                    TmdbId = s.TmdbId,
                    Images = s.Images
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
                    QualityProfileId = s.QualityProfileId,
                    TmdbId = s.TmdbId,
                    Images = s.Images
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get series {SeriesId}", sonarrId);
                return null;
            }
        }

        public async Task<Series?> LookupSeriesByTitle(string title)
        {
            try
            {
                var allSeries = await GetSeries();
                var normalizedTitle = title.ToLowerInvariant().Trim();

                // Try exact match first
                var match = allSeries.FirstOrDefault(s =>
                    s.Title.ToLowerInvariant().Trim() == normalizedTitle);

                if (match != null)
                {
                    _logger.LogDebug("Found series by exact title match: {Title} -> SonarrId {Id}", title, match.Id);
                    return match;
                }

                // Try contains match
                match = allSeries.FirstOrDefault(s =>
                    s.Title.ToLowerInvariant().Contains(normalizedTitle) ||
                    normalizedTitle.Contains(s.Title.ToLowerInvariant()));

                if (match != null)
                {
                    _logger.LogDebug("Found series by partial title match: {Title} -> SonarrId {Id}", title, match.Id);
                    return match;
                }

                _logger.LogDebug("No series found matching title: {Title}", title);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to lookup series by title: {Title}", title);
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

        public async Task<int?> GetEpisodeId(int seriesId, int seasonNumber, int episodeNumber)
        {
            try
            {
                var request = await CreateRequest(HttpMethod.Get, $"api/v3/episode?seriesId={seriesId}");
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<List<SonarrEpisodeResponse>>(content, _jsonOptions);

                var episode = data?.FirstOrDefault(e =>
                    e.SeasonNumber == seasonNumber && e.EpisodeNumber == episodeNumber);

                return episode?.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get episode ID for series {SeriesId} S{SeasonNumber}E{EpisodeNumber}",
                    seriesId, seasonNumber, episodeNumber);
                return null;
            }
        }

        public async Task<IEnumerable<ReleaseResult>> SearchReleases(int seriesId, int[] episodeIds)
        {
            try
            {
                var episodeIdsParam = string.Join(",", episodeIds);
                var request = await CreateRequest(HttpMethod.Get,
                    $"api/v3/release?seriesId={seriesId}&episodeIds={episodeIdsParam}");
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Sonarr release search for series {SeriesId} returned {StatusCode}", seriesId, response.StatusCode);
                    return Enumerable.Empty<ReleaseResult>();
                }

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<List<SonarrReleaseResponse>>(content, _jsonOptions);

                return data?.Select(r => new ReleaseResult
                {
                    Guid = r.Guid,
                    Title = r.Title,
                    Size = r.Size,
                    IndexerId = r.IndexerId,
                    DownloadAllowed = r.DownloadAllowed,
                    Protocol = r.Protocol,
                    QualityWeight = r.QualityWeight,
                    CustomFormatScore = r.CustomFormatScore,
                    Seeders = r.Seeders
                }) ?? Enumerable.Empty<ReleaseResult>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search releases for series {SeriesId}", seriesId);
                return Enumerable.Empty<ReleaseResult>();
            }
        }

        public async Task<GrabResult> GrabRelease(string guid, int indexerId, int? downloadClientId = null, int? seriesId = null, int[]? episodeIds = null)
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
                if (seriesId.HasValue)
                {
                    body["seriesId"] = seriesId.Value;
                }
                if (episodeIds != null && episodeIds.Length > 0)
                {
                    body["episodeIds"] = episodeIds;
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
                _logger.LogWarning("Failed to grab release {Guid}: {StatusCode} - {Error}",
                    guid, response.StatusCode, errorContent);
                return new GrabResult
                {
                    Success = false,
                    ErrorMessage = $"HTTP {response.StatusCode}: {errorContent}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to grab release {Guid}", guid);
                return new GrabResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
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
                var data = JsonSerializer.Deserialize<List<SonarrDownloadClientResponse>>(content, _jsonOptions);

                return data?.Select(d => new DownloadClientInfo
                {
                    Id = d.Id,
                    Name = d.Name,
                    Implementation = d.Implementation,
                    Enable = d.Enable
                }) ?? Enumerable.Empty<DownloadClientInfo>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get download clients from Sonarr");
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
                var data = JsonSerializer.Deserialize<SonarrBlocklistResponse>(content, _jsonOptions);

                return data?.Records?.Select(r => r.SourceTitle).ToHashSet() ?? new HashSet<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get blocklist from Sonarr");
                return new HashSet<string>();
            }
        }

        public async Task DeleteSeries(int seriesId, bool deleteFiles = false)
        {
            var request = await CreateRequest(HttpMethod.Delete, $"api/v3/series/{seriesId}?deleteFiles={deleteFiles.ToString().ToLowerInvariant()}");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        public async Task SetSeriesMonitorState(int seriesId, bool monitored)
        {
            var getRequest = await CreateRequest(HttpMethod.Get, $"api/v3/series/{seriesId}");
            var getResponse = await _httpClient.SendAsync(getRequest);
            getResponse.EnsureSuccessStatusCode();
            var content = await getResponse.Content.ReadAsStringAsync();
            var series = JsonSerializer.Deserialize<Series>(content, _jsonOptions);
            if (series == null) throw new InvalidOperationException($"Failed to deserialize series {seriesId}");

            series.Monitored = monitored;
            var putRequest = await CreateRequest(HttpMethod.Put, $"api/v3/series/{seriesId}");
            putRequest.Content = new StringContent(JsonSerializer.Serialize(series, _jsonOptions), Encoding.UTF8, "application/json");
            var putResponse = await _httpClient.SendAsync(putRequest);
            putResponse.EnsureSuccessStatusCode();
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
        public int? TmdbId { get; set; }
        public List<SonarrImage>? Images { get; set; }
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

    internal class SonarrEpisodeResponse
    {
        public int Id { get; set; }
        public int SeriesId { get; set; }
        public int SeasonNumber { get; set; }
        public int EpisodeNumber { get; set; }
    }

    internal class SonarrReleaseResponse
    {
        public string Guid { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public long Size { get; set; }
        public int IndexerId { get; set; }
        public bool DownloadAllowed { get; set; }
        public string? Protocol { get; set; }
        public int QualityWeight { get; set; }
        public int CustomFormatScore { get; set; }
        public int? Seeders { get; set; }
    }

    internal class SonarrDownloadClientResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Implementation { get; set; } = string.Empty;
        public bool Enable { get; set; }
    }

    internal class SonarrBlocklistResponse
    {
        public List<SonarrBlocklistItem>? Records { get; set; }
    }

    internal class SonarrBlocklistItem
    {
        public string SourceTitle { get; set; } = string.Empty;
    }
}
