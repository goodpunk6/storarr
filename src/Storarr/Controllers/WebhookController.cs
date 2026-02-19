using System;
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
    [Route("api/v1/webhooks")]
    public class WebhooksController : ControllerBase
    {
        private readonly StorarrDbContext _dbContext;
        private readonly IFileManagementService _fileService;
        private readonly ILogger<WebhooksController> _logger;

        public WebhooksController(
            StorarrDbContext dbContext,
            IFileManagementService fileService,
            ILogger<WebhooksController> logger)
        {
            _dbContext = dbContext;
            _fileService = fileService;
            _logger = logger;
        }

        [HttpPost("jellyseerr")]
        public async Task<ActionResult> JellyseerrWebhook([FromBody] JellyseerrWebhookPayload payload)
        {
            _logger.LogInformation("Received Jellyseerr webhook: {EventType}", payload.EventType);

            try
            {
                // Handle different event types
                switch (payload.EventType?.ToLowerInvariant())
                {
                    case "request_added":
                    case "request_approved":
                        await HandleNewRequest(payload);
                        break;

                    case "request_available":
                        await HandleRequestAvailable(payload);
                        break;
                }

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Jellyseerr webhook");
                return StatusCode(500);
            }
        }

        [HttpPost("sonarr")]
        public async Task<ActionResult> SonarrWebhook([FromBody] SonarrWebhookPayload payload)
        {
            _logger.LogInformation("Received Sonarr webhook: {EventType}", payload.EventType);

            try
            {
                switch (payload.EventType?.ToLowerInvariant())
                {
                    case "download":
                    case "episodefiledelete":
                        await HandleSonarrDownloadComplete(payload);
                        break;
                }

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Sonarr webhook");
                return StatusCode(500);
            }
        }

        [HttpPost("radarr")]
        public async Task<ActionResult> RadarrWebhook([FromBody] RadarrWebhookPayload payload)
        {
            _logger.LogInformation("Received Radarr webhook: {EventType}", payload.EventType);

            try
            {
                switch (payload.EventType?.ToLowerInvariant())
                {
                    case "download":
                    case "moviefiledelete":
                        await HandleRadarrDownloadComplete(payload);
                        break;
                }

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Radarr webhook");
                return StatusCode(500);
            }
        }

        private async Task HandleNewRequest(JellyseerrWebhookPayload payload)
        {
            if (payload.Media == null) return;

            var mediaType = payload.Media.MediaType?.ToLowerInvariant() == "movie" ? MediaType.Movie : MediaType.Series;

            // Check if we already have this item tracked
            var existing = await _dbContext.MediaItems
                .FirstOrDefaultAsync(m => m.TmdbId == payload.Media.TmdbId);

            if (existing == null)
            {
                // Create a new pending item - the library scanner will pick it up once the symlink is created
                var item = new MediaItem
                {
                    Title = payload.Media.Title,
                    Type = mediaType,
                    TmdbId = payload.Media.TmdbId,
                    TvdbId = payload.Media.TvdbId,
                    JellyseerrRequestId = payload.Request?.RequestId,
                    CurrentState = FileState.PendingSymlink,
                    CreatedAt = DateTime.UtcNow,
                    StateChangedAt = DateTime.UtcNow
                };

                _dbContext.MediaItems.Add(item);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Created pending media item from Jellyseerr request: {Title}", payload.Media.Title);
            }
        }

        private async Task HandleRequestAvailable(JellyseerrWebhookPayload payload)
        {
            if (payload.Media == null) return;

            // For TV series a single TmdbId can match multiple episode items — update all of them
            var items = await _dbContext.MediaItems
                .Where(m => m.TmdbId == payload.Media.TmdbId &&
                    m.CurrentState == FileState.PendingSymlink)
                .ToListAsync();

            foreach (var item in items)
            {
                item.CurrentState = FileState.Symlink;
                item.StateChangedAt = DateTime.UtcNow;
                _logger.LogInformation("Media item {Title} is now available as symlink", item.Title);
            }

            if (items.Count > 0)
                await _dbContext.SaveChangesAsync();
        }

        private async Task HandleSonarrDownloadComplete(SonarrWebhookPayload payload)
        {
            if (payload.EpisodeFile == null) return;

            // Find the item by path
            var item = await _dbContext.MediaItems
                .FirstOrDefaultAsync(m => m.FilePath == payload.EpisodeFile.Path);

            if (item != null && item.CurrentState == FileState.Downloading)
            {
                var isSymlink = await _fileService.IsSymlink(payload.EpisodeFile.Path);
                var previousState = item.CurrentState;

                item.CurrentState = isSymlink ? FileState.Symlink : FileState.Mkv;
                item.StateChangedAt = DateTime.UtcNow;
                item.FileSize = payload.EpisodeFile.Size;
                item.SonarrFileId = payload.EpisodeFile.Id;

                // Log activity
                _dbContext.ActivityLogs.Add(new ActivityLog
                {
                    MediaItemId = item.Id,
                    Action = "DownloadComplete",
                    FromState = previousState.ToString(),
                    ToState = item.CurrentState.ToString(),
                    Details = $"Downloaded via Sonarr: {payload.EpisodeFile.Path}",
                    Timestamp = DateTime.UtcNow
                });

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Sonarr download complete for {Title}", item.Title);
            }
        }

        private async Task HandleRadarrDownloadComplete(RadarrWebhookPayload payload)
        {
            if (payload.MovieFile == null) return;

            // Find the item by path
            var item = await _dbContext.MediaItems
                .FirstOrDefaultAsync(m => m.FilePath == payload.MovieFile.Path);

            if (item != null && item.CurrentState == FileState.Downloading)
            {
                var isSymlink = await _fileService.IsSymlink(payload.MovieFile.Path);
                var previousState = item.CurrentState;

                item.CurrentState = isSymlink ? FileState.Symlink : FileState.Mkv;
                item.StateChangedAt = DateTime.UtcNow;
                item.FileSize = payload.MovieFile.Size;
                item.RadarrFileId = payload.MovieFile.Id;

                // Log activity
                _dbContext.ActivityLogs.Add(new ActivityLog
                {
                    MediaItemId = item.Id,
                    Action = "DownloadComplete",
                    FromState = previousState.ToString(),
                    ToState = item.CurrentState.ToString(),
                    Details = $"Downloaded via Radarr: {payload.MovieFile.Path}",
                    Timestamp = DateTime.UtcNow
                });

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Radarr download complete for {Title}", item.Title);
            }
        }
    }
}
