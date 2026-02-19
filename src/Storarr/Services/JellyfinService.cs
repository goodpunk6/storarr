using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Storarr.Data;
using Storarr.Models;

namespace Storarr.Services
{
    public class JellyfinService : IJellyfinService
    {
        private readonly HttpClient _httpClient;
        private readonly StorarrDbContext _dbContext;
        private readonly ILogger<JellyfinService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        // Cache for GetLastPlayedDate to avoid N+1 Jellyfin API calls
        private List<JellyfinItem>? _cachedItems;
        private DateTime _cacheExpiry = DateTime.MinValue;
        private static readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);
        private static readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(5);

        public JellyfinService(
            HttpClient httpClient,
            StorarrDbContext dbContext,
            ILogger<JellyfinService> logger)
        {
            _httpClient = httpClient;
            _dbContext = dbContext;
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        }

        private async Task ConfigureClient()
        {
            var config = await GetConfig();
            if (string.IsNullOrEmpty(config.JellyfinUrl) || string.IsNullOrEmpty(config.JellyfinApiKey))
            {
                throw new InvalidOperationException("Jellyfin URL or API key not configured");
            }

            _httpClient.BaseAddress = new Uri(config.JellyfinUrl.TrimEnd('/') + "/");
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-Emby-Token", config.JellyfinApiKey);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private async Task<Config> GetConfig()
        {
            return await _dbContext.Configs.FindAsync(Config.SingletonId) ?? new Config();
        }

        public async Task TestConnection()
        {
            await ConfigureClient();
            var response = await _httpClient.GetAsync("System/Info");
            response.EnsureSuccessStatusCode();
        }

        public async Task<DateTime?> GetLastPlayedDate(string filePath)
        {
            await ConfigureClient();
            try
            {
                // Fetch all items once and cache them for a short TTL to avoid N+1 calls
                var allItems = await GetCachedItems();

                var item = allItems.FirstOrDefault(i =>
                    i.Path?.Equals(filePath, StringComparison.OrdinalIgnoreCase) == true);

                return item?.LastPlayedDate;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get last played date for {FilePath}", filePath);
                return null;
            }
        }

        private async Task<List<JellyfinItem>> GetCachedItems()
        {
            if (_cachedItems != null && DateTime.UtcNow < _cacheExpiry)
                return _cachedItems;

            await _cacheLock.WaitAsync();
            try
            {
                // Double-check after acquiring lock
                if (_cachedItems != null && DateTime.UtcNow < _cacheExpiry)
                    return _cachedItems;

                var response = await _httpClient.GetAsync("Items?Recursive=true&Fields=Path,LastPlayedDate");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<JellyfinItemsResult>(content, _jsonOptions);

                _cachedItems = data?.Items ?? new List<JellyfinItem>();
                _cacheExpiry = DateTime.UtcNow + _cacheTtl;

                return _cachedItems;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        public async Task ScanLibrary()
        {
            await ConfigureClient();
            try
            {
                var response = await _httpClient.PostAsync("Library/Refresh", null);
                response.EnsureSuccessStatusCode();
                _logger.LogInformation("Library scan initiated");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initiate library scan");
            }
        }

        public async Task<MediaItemInfo?> GetItemByPath(string filePath)
        {
            await ConfigureClient();
            try
            {
                var response = await _httpClient.GetAsync("Items?Recursive=true&Fields=Path");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<JellyfinItemsResult>(content, _jsonOptions);

                var item = data?.Items?.FirstOrDefault(i =>
                    i.Path?.Equals(filePath, StringComparison.OrdinalIgnoreCase) == true);

                if (item == null) return null;

                return new MediaItemInfo
                {
                    Id = item.Id,
                    Name = item.Name,
                    Path = item.Path ?? string.Empty,
                    Type = MapMediaType(item.Type),
                    SeasonNumber = item.SeasonNumber,
                    EpisodeNumber = item.EpisodeNumber
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get item by path {FilePath}", filePath);
                return null;
            }
        }

        public async Task<List<MediaItemInfo>> GetAllMediaItems()
        {
            await ConfigureClient();
            try
            {
                var response = await _httpClient.GetAsync("Items?Recursive=true&IncludeItemTypes=Movie,Episode&Fields=Path");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<JellyfinItemsResult>(content, _jsonOptions);

                return data?.Items?.Select(i => new MediaItemInfo
                {
                    Id = i.Id,
                    Name = i.Name,
                    Path = i.Path ?? string.Empty,
                    Type = MapMediaType(i.Type),
                    SeasonNumber = i.SeasonNumber,
                    EpisodeNumber = i.EpisodeNumber,
                    Size = i.Size
                }).ToList() ?? new List<MediaItemInfo>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get all media items");
                return new List<MediaItemInfo>();
            }
        }

        private MediaType MapMediaType(string? type)
        {
            return type?.ToLowerInvariant() switch
            {
                "movie" => MediaType.Movie,
                "episode" => MediaType.Series,
                "series" => MediaType.Series,
                _ => MediaType.Movie
            };
        }
    }

    // JSON response models
    internal class JellyfinPlaybackInfo
    {
        public List<JellyfinMediaSource>? MediaSources { get; set; }
    }

    internal class JellyfinMediaSource
    {
        public string? Id { get; set; }
        public string? Path { get; set; }
    }

    internal class JellyfinItemsResult
    {
        public List<JellyfinItem>? Items { get; set; }
    }

    internal class JellyfinItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Path { get; set; }
        public string? Type { get; set; }
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
        public long? Size { get; set; }
        public DateTime? LastPlayedDate { get; set; }
    }
}
