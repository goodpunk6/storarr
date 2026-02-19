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

        private async Task ConfigureClient()
        {
            var config = await GetConfig();
            if (string.IsNullOrEmpty(config.JellyseerrUrl) || string.IsNullOrEmpty(config.JellyseerrApiKey))
            {
                throw new InvalidOperationException("Jellyseerr URL or API key not configured");
            }

            _httpClient.BaseAddress = new Uri(config.JellyseerrUrl.TrimEnd('/') + "/");
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-Api-Key", config.JellyseerrApiKey);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private async Task<Config> GetConfig()
        {
            return await _dbContext.Configs.FindAsync(Config.SingletonId) ?? new Config();
        }

        public async Task TestConnection()
        {
            await ConfigureClient();
            var response = await _httpClient.GetAsync("api/v1/status");
            response.EnsureSuccessStatusCode();
        }

        public async Task<IEnumerable<MediaRequest>> GetRecentRequests(int limit = 50)
        {
            await ConfigureClient();
            try
            {
                var response = await _httpClient.GetAsync($"api/v1/request?take={limit}&sort=added");
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
            await ConfigureClient();
            try
            {
                var response = await _httpClient.GetAsync($"api/v1/request/{requestId}");
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
            await ConfigureClient();
            try
            {
                var mediaType = type == MediaType.Movie ? "movie" : "tv";

                var requestBody = new
                {
                    mediaType,
                    mediaId = tmdbId,
                    tvdbId,
                    // Pass null so Jellyseerr requests all available seasons (previously hardcoded new[] { 1 } only requested season 1)
                    seasons = (int[]?)null
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("api/v1/request", content);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
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
