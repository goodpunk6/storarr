using System;
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
                // Use CountAsync per state instead of loading all items into memory
                var symlinkCount = await _dbContext.MediaItems.AsNoTracking().CountAsync(m => m.CurrentState == FileState.Symlink);
                var mkvCount = await _dbContext.MediaItems.AsNoTracking().CountAsync(m => m.CurrentState == FileState.Mkv);
                var downloadingCount = await _dbContext.MediaItems.AsNoTracking().CountAsync(m => m.CurrentState == FileState.Downloading);
                var pendingSymlinkCount = await _dbContext.MediaItems.AsNoTracking().CountAsync(m => m.CurrentState == FileState.PendingSymlink);
                var totalItems = symlinkCount + mkvCount + downloadingCount + pendingSymlinkCount;
                var totalSizeBytes = await _dbContext.MediaItems.AsNoTracking().SumAsync(m => m.FileSize ?? 0);

                _logger.LogDebug("[DashboardController] State breakdown - Symlinks: {Symlink}, MKVs: {Mkv}, Downloading: {Downloading}, Pending: {Pending}",
                    symlinkCount, mkvCount, downloadingCount, pendingSymlinkCount);

                var upcomingTransitions = await _transitionService.GetUpcomingTransitions(10);
                _logger.LogDebug("[DashboardController] Found {Count} upcoming transitions", upcomingTransitions.Count());

                var dashboard = new DashboardDto
                {
                    TotalItems = totalItems,
                    SymlinkCount = symlinkCount,
                    MkvCount = mkvCount,
                    DownloadingCount = downloadingCount,
                    PendingSymlinkCount = pendingSymlinkCount,
                    TotalSizeBytes = totalSizeBytes,
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
}
