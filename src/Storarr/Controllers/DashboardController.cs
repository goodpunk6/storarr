using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Storarr.Data;
using Storarr.Models;
using Storarr.Services;

namespace Storarr.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly StorarrDbContext _dbContext;
        private readonly ITransitionService _transitionService;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(
            StorarrDbContext dbContext,
            ITransitionService transitionService,
            ILogger<DashboardController> logger)
        {
            _dbContext = dbContext;
            _transitionService = transitionService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<DashboardDto>> GetDashboard()
        {
            _logger.LogDebug("[DashboardController] GetDashboard called");

            try
            {
                var items = await _dbContext.MediaItems.ToListAsync();

                _logger.LogDebug("[DashboardController] Found {Count} media items", items.Count);

                var symlinkCount = items.Count(m => m.CurrentState == FileState.Symlink);
                var mkvCount = items.Count(m => m.CurrentState == FileState.Mkv);
                var downloadingCount = items.Count(m => m.CurrentState == FileState.Downloading);
                var pendingSymlinkCount = items.Count(m => m.CurrentState == FileState.PendingSymlink);

                _logger.LogDebug("[DashboardController] State breakdown - Symlinks: {Symlink}, MKVs: {Mkv}, Downloading: {Downloading}, Pending: {Pending}",
                    symlinkCount, mkvCount, downloadingCount, pendingSymlinkCount);

                var upcomingTransitions = await _transitionService.GetUpcomingTransitions(10);
                _logger.LogDebug("[DashboardController] Found {Count} upcoming transitions", upcomingTransitions.Count());

                var dashboard = new DashboardDto
                {
                    TotalItems = items.Count,
                    SymlinkCount = symlinkCount,
                    MkvCount = mkvCount,
                    DownloadingCount = downloadingCount,
                    PendingSymlinkCount = pendingSymlinkCount,
                    TotalSizeBytes = items.Sum(m => m.FileSize ?? 0),
                    UpcomingTransitions = upcomingTransitions
                        .Select(t => new TransitionDto
                        {
                            MediaItemId = t.MediaItemId,
                            Title = t.Title,
                            CurrentState = t.CurrentState.ToString(),
                            TargetState = t.TargetState.ToString(),
                            DaysUntilTransition = t.DaysUntilTransition,
                            TransitionDate = t.TransitionDate
                        }).ToList()
                };

                return Ok(dashboard);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DashboardController] Error in GetDashboard");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class DashboardDto
    {
        public int TotalItems { get; set; }
        public int SymlinkCount { get; set; }
        public int MkvCount { get; set; }
        public int DownloadingCount { get; set; }
        public int PendingSymlinkCount { get; set; }
        public long TotalSizeBytes { get; set; }
        public List<TransitionDto> UpcomingTransitions { get; set; } = new List<TransitionDto>();
    }

    public class TransitionDto
    {
        public int MediaItemId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string CurrentState { get; set; } = string.Empty;
        public string TargetState { get; set; } = string.Empty;
        public int DaysUntilTransition { get; set; }
        public DateTime? TransitionDate { get; set; }
    }
}
