using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Storarr.Data;
using Storarr.DTOs;
using Storarr.Models;
using Storarr.Services;

namespace Storarr.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class QueueController : ControllerBase
    {
        private readonly StorarrDbContext _dbContext;
        private readonly ISonarrService _sonarrService;
        private readonly IRadarrService _radarrService;
        private readonly IDownloadClientService _downloadClientService;
        private readonly ILogger<QueueController> _logger;

        public QueueController(
            StorarrDbContext dbContext,
            ISonarrService sonarrService,
            IRadarrService radarrService,
            IDownloadClientService downloadClientService,
            ILogger<QueueController> logger)
        {
            _dbContext = dbContext;
            _sonarrService = sonarrService;
            _radarrService = radarrService;
            _downloadClientService = downloadClientService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<QueueResponse>> GetQueue()
        {
            var items = new List<QueueItemDto>();

            // Get Sonarr queue
            try
            {
                var sonarrQueue = await _sonarrService.GetQueue();
                foreach (var item in sonarrQueue)
                {
                    var mediaItem = await _dbContext.MediaItems
                        .FirstOrDefaultAsync(m => m.SonarrId == item.SeriesId && m.CurrentState == FileState.Downloading);

                    items.Add(new QueueItemDto
                    {
                        DownloadId = item.DownloadId,
                        Title = item.Title,
                        Status = item.Status,
                        Size = item.Size,
                        SizeLeft = item.SizeLeft,
                        Progress = item.Size > 0 ? (1 - (double)item.SizeLeft / item.Size) * 100 : 0,
                        ErrorMessage = item.ErrorMessage,
                        Source = "Sonarr",
                        MediaItemId = mediaItem?.Id ?? 0
                    });
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, "[QueueController] Failed to get Sonarr queue");
            }

            // Get Radarr queue
            try
            {
                var radarrQueue = await _radarrService.GetQueue();
                foreach (var item in radarrQueue)
                {
                    var mediaItem = await _dbContext.MediaItems
                        .FirstOrDefaultAsync(m => m.RadarrId == item.MovieId && m.CurrentState == FileState.Downloading);

                    items.Add(new QueueItemDto
                    {
                        DownloadId = item.DownloadId,
                        Title = item.Title,
                        Status = item.Status,
                        Size = item.Size,
                        SizeLeft = item.SizeLeft,
                        Progress = item.Size > 0 ? (1 - (double)item.SizeLeft / item.Size) * 100 : 0,
                        ErrorMessage = item.ErrorMessage,
                        Source = "Radarr",
                        MediaItemId = mediaItem?.Id ?? 0
                    });
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, "[QueueController] Failed to get Radarr queue");
            }

            return Ok(new QueueResponse
            {
                Items = items,
                TotalCount = items.Count
            });
        }

        [HttpGet("clients")]
        public async Task<ActionResult<DownloadClientQueueResponse>> GetDownloadClientQueues()
        {
            var config = await _dbContext.Configs.FindAsync(1);
            if (config == null)
            {
                return NotFound();
            }

            var clientQueues = new List<DownloadClientQueueDto>();

            // Download Client 1
            if (config.DownloadClient1Enabled && !string.IsNullOrEmpty(config.DownloadClient1Url))
            {
                var queue = await GetClientQueue(config.DownloadClient1Type, config.DownloadClient1Url,
                    config.DownloadClient1Username, config.DownloadClient1Password, config.DownloadClient1ApiKey);
                clientQueues.Add(new DownloadClientQueueDto
                {
                    ClientType = config.DownloadClient1Type.ToString(),
                    ClientUrl = config.DownloadClient1Url,
                    Items = queue
                });
            }

            // Download Client 2
            if (config.DownloadClient2Enabled && !string.IsNullOrEmpty(config.DownloadClient2Url))
            {
                var queue = await GetClientQueue(config.DownloadClient2Type, config.DownloadClient2Url,
                    config.DownloadClient2Username, config.DownloadClient2Password, config.DownloadClient2ApiKey);
                clientQueues.Add(new DownloadClientQueueDto
                {
                    ClientType = config.DownloadClient2Type.ToString(),
                    ClientUrl = config.DownloadClient2Url,
                    Items = queue
                });
            }

            // Download Client 3
            if (config.DownloadClient3Enabled && !string.IsNullOrEmpty(config.DownloadClient3Url))
            {
                var queue = await GetClientQueue(config.DownloadClient3Type, config.DownloadClient3Url,
                    null, null, config.DownloadClient3ApiKey);
                clientQueues.Add(new DownloadClientQueueDto
                {
                    ClientType = config.DownloadClient3Type.ToString(),
                    ClientUrl = config.DownloadClient3Url,
                    Items = queue
                });
            }

            return Ok(new DownloadClientQueueResponse
            {
                Clients = clientQueues,
                TotalClients = clientQueues.Count,
                TotalItems = clientQueues.Sum(c => c.Items.Count)
            });
        }

        private async Task<List<DownloadClientItemDto>> GetClientQueue(
            DownloadClientType type, string url, string? username, string? password, string? apiKey)
        {
            try
            {
                var items = await _downloadClientService.GetQueue(type, url, username, password, apiKey);
                return items.Select(i => new DownloadClientItemDto
                {
                    Id = i.Id,
                    Name = i.Name,
                    Size = i.Size,
                    SizeRemaining = i.SizeRemaining,
                    Progress = i.Progress,
                    Status = i.Status,
                    ErrorMessage = i.ErrorMessage
                }).ToList();
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, "[QueueController] Failed to get queue from {Type} at {Url}", type, url);
                return new List<DownloadClientItemDto>();
            }
        }
    }

    // DTOs
    public class QueueResponse
    {
        public List<QueueItemDto> Items { get; set; } = new List<QueueItemDto>();
        public int TotalCount { get; set; }
    }

    public class QueueItemDto
    {
        public string DownloadId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public long Size { get; set; }
        public long SizeLeft { get; set; }
        public double Progress { get; set; }
        public string? ErrorMessage { get; set; }
        public string Source { get; set; } = string.Empty;
        public int MediaItemId { get; set; }
    }

    public class DownloadClientQueueResponse
    {
        public List<DownloadClientQueueDto> Clients { get; set; } = new List<DownloadClientQueueDto>();
        public int TotalClients { get; set; }
        public int TotalItems { get; set; }
    }

    public class DownloadClientQueueDto
    {
        public string ClientType { get; set; } = string.Empty;
        public string ClientUrl { get; set; } = string.Empty;
        public List<DownloadClientItemDto> Items { get; set; } = new List<DownloadClientItemDto>();
    }

    public class DownloadClientItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public long SizeRemaining { get; set; }
        public double Progress { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
    }
}
