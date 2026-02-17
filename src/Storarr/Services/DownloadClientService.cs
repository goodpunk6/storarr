using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Storarr.Models;

namespace Storarr.Services
{
    public class DownloadClientService : IDownloadClientService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<DownloadClientService> _logger;

        public DownloadClientService(IHttpClientFactory httpClientFactory, ILogger<DownloadClientService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<bool> TestConnection(DownloadClientType type, string url, string? username, string? password, string? apiKey)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                switch (type)
                {
                    case DownloadClientType.QBittorrent:
                        return await TestQBittorrent(client, url, username, password);
                    case DownloadClientType.Transmission:
                        return await TestTransmission(client, url, username, password);
                    case DownloadClientType.Sabnzbd:
                        return await TestSabnzbd(client, url, apiKey);
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to test connection to {Type} at {Url}", type, url);
                return false;
            }
        }

        public async Task<IEnumerable<DownloadQueueItem>> GetQueue(DownloadClientType type, string url, string? username, string? password, string? apiKey)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);

                switch (type)
                {
                    case DownloadClientType.QBittorrent:
                        return await GetQBittorrentQueue(client, url, username, password);
                    case DownloadClientType.Transmission:
                        return await GetTransmissionQueue(client, url, username, password);
                    case DownloadClientType.Sabnzbd:
                        return await GetSabnzbdQueue(client, url, apiKey);
                    default:
                        return new List<DownloadQueueItem>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get queue from {Type} at {Url}", type, url);
                return new List<DownloadQueueItem>();
            }
        }

        #region qBittorrent

        private async Task<bool> TestQBittorrent(HttpClient client, string url, string? username, string? password)
        {
            var loginUrl = $"{url.TrimEnd('/')}/api/v2/auth/login";
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "username", username ?? "admin" },
                { "password", password ?? "" }
            });

            var response = await client.PostAsync(loginUrl, content);
            return response.IsSuccessStatusCode;
        }

        private async Task<IEnumerable<DownloadQueueItem>> GetQBittorrentQueue(HttpClient client, string url, string? username, string? password)
        {
            // Login first
            var loginUrl = $"{url.TrimEnd('/')}/api/v2/auth/login";
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "username", username ?? "admin" },
                { "password", password ?? "" }
            });

            await client.PostAsync(loginUrl, content);

            // Get torrents
            var torrentsUrl = $"{url.TrimEnd('/')}/api/v2/torrents/info";
            var response = await client.GetAsync(torrentsUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var torrents = JsonSerializer.Deserialize<List<QBittorrentTorrent>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var items = new List<DownloadQueueItem>();
            if (torrents != null)
            {
                foreach (var torrent in torrents)
                {
                    if (torrent.Progress < 1.0) // Only active downloads
                    {
                        items.Add(new DownloadQueueItem
                        {
                            Id = torrent.Hash,
                            Name = torrent.Name,
                            Size = torrent.Size,
                            SizeRemaining = (long)(torrent.Size * (1 - torrent.Progress)),
                            Progress = torrent.Progress * 100,
                            Status = MapQBittorrentStatus(torrent.State),
                            ClientType = DownloadClientType.QBittorrent
                        });
                    }
                }
            }

            return items;
        }

        private string MapQBittorrentStatus(string state)
        {
            return state?.ToLower() switch
            {
                "downloading" => "Downloading",
                "stalleddl" => "Stalled",
                "queueddl" => "Queued",
                "pauseddl" => "Paused",
                "error" => "Error",
                _ => "Unknown"
            };
        }

        #endregion

        #region Transmission

        private async Task<bool> TestTransmission(HttpClient client, string url, string? username, string? password)
        {
            var request = CreateTransmissionRequest(url, "session-get", username, password);
            var response = await client.SendAsync(request);
            return response.IsSuccessStatusCode;
        }

        private async Task<IEnumerable<DownloadQueueItem>> GetTransmissionQueue(HttpClient client, string url, string? username, string? password)
        {
            var request = CreateTransmissionRequest(url, "torrent-get", username, password, new { fields = new[] { "id", "name", "totalSize", "leftUntilDone", "percentDone", "status", "errorString" } });
            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TransmissionResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var items = new List<DownloadQueueItem>();
            if (result?.Arguments?.Torrents != null)
            {
                foreach (var torrent in result.Arguments.Torrents)
                {
                    if (torrent.PercentDone < 1.0)
                    {
                        items.Add(new DownloadQueueItem
                        {
                            Id = torrent.Id.ToString(),
                            Name = torrent.Name,
                            Size = torrent.TotalSize,
                            SizeRemaining = torrent.LeftUntilDone,
                            Progress = torrent.PercentDone * 100,
                            Status = MapTransmissionStatus(torrent.Status),
                            ErrorMessage = torrent.ErrorString,
                            ClientType = DownloadClientType.Transmission
                        });
                    }
                }
            }

            return items;
        }

        private HttpRequestMessage CreateTransmissionRequest(string url, string method, string? username, string? password, object? arguments = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{url.TrimEnd('/')}/rpc");

            var body = new { method, @arguments = arguments };
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
            }

            return request;
        }

        private string MapTransmissionStatus(int status)
        {
            return status switch
            {
                0 => "Paused",
                1 => "Queued",
                2 => "Downloading",
                3 => "Downloading",
                4 => "Seeding",
                5 => "Seeding",
                6 => "Error",
                _ => "Unknown"
            };
        }

        #endregion

        #region SABnzbd

        private async Task<bool> TestSabnzbd(HttpClient client, string url, string? apiKey)
        {
            var testUrl = $"{url.TrimEnd('/')}/api?mode=server_stats&output=json&apikey={apiKey}";
            var response = await client.GetAsync(testUrl);
            return response.IsSuccessStatusCode;
        }

        private async Task<IEnumerable<DownloadQueueItem>> GetSabnzbdQueue(HttpClient client, string url, string? apiKey)
        {
            var queueUrl = $"{url.TrimEnd('/')}/api?mode=queue&output=json&apikey={apiKey}";
            var response = await client.GetAsync(queueUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SabnzbdResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var items = new List<DownloadQueueItem>();
            if (result?.Queue?.Slots != null)
            {
                foreach (var slot in result.Queue.Slots)
                {
                    items.Add(new DownloadQueueItem
                    {
                        Id = slot.NzoId,
                        Name = slot.Filename,
                        Size = ParseSize(slot.Size),
                        SizeRemaining = (long)(ParseSize(slot.Size) * (100 - slot.Percentage) / 100),
                        Progress = slot.Percentage,
                        Status = slot.Status,
                        ErrorMessage = slot.ErrorMessage,
                        ClientType = DownloadClientType.Sabnzbd
                    });
                }
            }

            return items;
        }

        private long ParseSize(string sizeStr)
        {
            if (string.IsNullOrEmpty(sizeStr)) return 0;
            if (long.TryParse(sizeStr, out var size)) return size;

            var match = System.Text.RegularExpressions.Regex.Match(sizeStr, @"([\d.]+)\s*([KMGT]?B?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success) return 0;

            var value = double.Parse(match.Groups[1].Value);
            var unit = match.Groups[2].Value.ToUpper();

            return unit switch
            {
                "KB" => (long)(value * 1024),
                "MB" => (long)(value * 1024 * 1024),
                "GB" => (long)(value * 1024 * 1024 * 1024),
                "TB" => (long)(value * 1024 * 1024 * 1024 * 1024),
                _ => (long)value
            };
        }

        private long ParseSize(double size)
        {
            return (long)size;
        }

        #endregion
    }

    #region JSON Models

    public class QBittorrentTorrent
    {
        public string Hash { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public double Progress { get; set; }
        public string State { get; set; } = string.Empty;
    }

    public class TransmissionResponse
    {
        public TransmissionArguments? Arguments { get; set; }
    }

    public class TransmissionArguments
    {
        public List<TransmissionTorrent>? Torrents { get; set; }
    }

    public class TransmissionTorrent
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public long TotalSize { get; set; }
        public long LeftUntilDone { get; set; }
        public double PercentDone { get; set; }
        public int Status { get; set; }
        public string? ErrorString { get; set; }
    }

    public class SabnzbdResponse
    {
        public SabnzbdQueue? Queue { get; set; }
    }

    public class SabnzbdQueue
    {
        public List<SabnzbdSlot>? Slots { get; set; }
    }

    public class SabnzbdSlot
    {
        public string NzoId { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public double Percentage { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
    }

    #endregion
}
