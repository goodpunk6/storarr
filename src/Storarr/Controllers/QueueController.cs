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

            // Load all Downloading items into a dictionary to avoid N+1 queries
            var downloadingItems = await _dbContext.MediaItems
                .AsNoTracking()
                .Where(m => m.CurrentState == FileState.Downloading)
                .ToListAsync();

            var sonarrDict = downloadingItems
                .Where(m => m.SonarrId.HasValue)
                .GroupBy(m => m.SonarrId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            var radarrDict = downloadingItems
                .Where(m => m.RadarrId.HasValue)
                .GroupBy(m => m.RadarrId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            // Get Sonarr queue
            try
            {
                var sonarrQueue = await _sonarrService.GetQueue();
                foreach (var item in sonarrQueue)
                {
                    sonarrDict.TryGetValue(item.SeriesId, out var mediaItem);

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
                    radarrDict.TryGetValue(item.MovieId, out var mediaItem);

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
            var config = await _dbContext.Configs.FindAsync(Config.SingletonId);
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
}
