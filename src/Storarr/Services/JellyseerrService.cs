using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Storarr.Data;
using Storarr.Models;

namespace Storarr.Services
{
    public class JellyseerrService : IJellyseerrService
    {
        private readonly HttpClient _httpClient;
        private readonly StorarrDbContext _dbContext;
        private readonly ILogger<JellyseerrService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public JellyseerrService(
            HttpClient httpClient,
            StorarrDbContext dbContext,
            ILogger<JellyseerrService> logger)
        {
            _httpClient = httpClient;
            _dbContext = dbContext;
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        }

        private async Task<(string baseUrl, string apiKey)> GetJellyseerrConfig()
        {
            var config = await GetConfig();
            if (string.IsNullOrEmpty(config.JellyseerrUrl) || string.IsNullOrEmpty(config.JellyseerrApiKey))
            {
                throw new InvalidOperationException("Jellyseerr URL or API key not configured");
            }

            return (config.JellyseerrUrl.TrimEnd('/'), config.JellyseerrApiKey);
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string baseUrl, string path, string apiKey)
        {
            var request = new HttpRequestMessage(method, $"{baseUrl}/{path}");
            request.Headers.Add("X-Api-Key", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }

        private async Task<Config> GetConfig()
        {
            return await _dbContext.Configs.FindAsync(Config.SingletonId) ?? new Config();
        }

        public async Task TestConnection()
        {
            var (baseUrl, apiKey) = await GetJellyseerrConfig();
            var request = CreateRequest(HttpMethod.Get, baseUrl, "api/v1/status", apiKey);
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        public async Task<IEnumerable<MediaRequest>> GetRecentRequests(int limit = 50)
        {
            var (baseUrl, apiKey) = await GetJellyseerrConfig();
            try
            {
                var request = CreateRequest(HttpMethod.Get, baseUrl, $"api/v1/request?take={limit}&sort=added", apiKey);
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<JellyseerrRequestsResponse>(content, _jsonOptions);

                return data?.Results?.Select(r => new MediaRequest
                {
                    RequestId = r.Id,
                    MediaId = r.MediaId,
                    TmdbId = r.Media?.TmdbId ?? 0,
                    TvdbId = r.Media?.TvdbId,
                    Type = MapMediaType(r.Media?.MediaType),
                    Title = r.Media?.Title ?? string.Empty,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt
                }) ?? Enumerable.Empty<MediaRequest>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get recent requests");
                return Enumerable.Empty<MediaRequest>();
            }
        }

        public async Task<MediaRequest?> GetRequest(int requestId)
        {
            var (baseUrl, apiKey) = await GetJellyseerrConfig();
            try
            {
                var request = CreateRequest(HttpMethod.Get, baseUrl, $"api/v1/request/{requestId}", apiKey);
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var r = JsonSerializer.Deserialize<JellyseerrRequest>(content, _jsonOptions);

                if (r == null) return null;

                return new MediaRequest
                {
                    RequestId = r.Id,
                    MediaId = r.MediaId,
                    TmdbId = r.Media?.TmdbId ?? 0,
                    TvdbId = r.Media?.TvdbId,
                    Type = MapMediaType(r.Media?.MediaType),
                    Title = r.Media?.Title ?? string.Empty,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get request {RequestId}", requestId);
                return null;
            }
        }

        public async Task<MediaRequest> CreateRequest(int tmdbId, MediaType type, int? tvdbId = null)
        {
            var (baseUrl, apiKey) = await GetJellyseerrConfig();
            try
            {
                var mediaType = type == MediaType.Movie ? "movie" : "tv";

                // Build request body — only include tvdbId if present, use "all" for seasons on TV shows
                var requestBody = new Dictionary<string, object?>
                {
                    ["mediaType"] = mediaType,
                    ["mediaId"] = tmdbId,
                };
                if (tvdbId.HasValue) requestBody["tvdbId"] = tvdbId.Value;
                if (type != MediaType.Movie) requestBody["seasons"] = "all";

                var jsonOptions = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
                var json = JsonSerializer.Serialize(requestBody, jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = CreateRequest(HttpMethod.Post, baseUrl, "api/v1/request", apiKey);
                request.Content = content;

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Jellyseerr CreateRequest failed for TMDB {TmdbId} with {StatusCode}: {Body}", tmdbId, response.StatusCode, responseContent);
                    response.EnsureSuccessStatusCode();
                }

                var r = JsonSerializer.Deserialize<JellyseerrRequest>(responseContent, _jsonOptions);

                _logger.LogInformation("Created Jellyseerr request for TMDB {TmdbId}", tmdbId);

                return new MediaRequest
                {
                    RequestId = r?.Id ?? 0,
                    TmdbId = tmdbId,
                    TvdbId = tvdbId,
                    Type = type,
                    Status = r?.Status ?? "PENDING",
                    CreatedAt = r?.CreatedAt ?? DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create request for TMDB {TmdbId}", tmdbId);
                throw;
            }
        }

        private MediaType MapMediaType(string? type)
        {
            return type?.ToLowerInvariant() switch
            {
                "movie" => MediaType.Movie,
                "tv" => MediaType.Series,
                "anime" => MediaType.Anime,
                _ => MediaType.Movie
            };
        }
    }

    // JSON response models
    internal class JellyseerrRequestsResponse
    {
        public List<JellyseerrRequest>? Results { get; set; }
    }

    internal class JellyseerrRequest
    {
        public int Id { get; set; }
        public int MediaId { get; set; }
        public JellyseerrMedia? Media { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    internal class JellyseerrMedia
    {
        public int TmdbId { get; set; }
        public int? TvdbId { get; set; }
        public string MediaType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }
}
