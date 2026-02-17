using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Storarr.Data;
using Storarr.DTOs;

namespace Storarr.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class ActivityController : ControllerBase
    {
        private readonly StorarrDbContext _dbContext;

        public ActivityController(StorarrDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ActivityLogDto>>> GetActivity(
            [FromQuery] int? mediaItemId = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            var query = _dbContext.ActivityLogs
                .Include(a => a.MediaItem)
                .AsQueryable();

            if (mediaItemId.HasValue)
                query = query.Where(a => a.MediaItemId == mediaItemId.Value);

            var logs = await query
                .OrderByDescending(a => a.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new ActivityLogDto
                {
                    Id = a.Id,
                    MediaItemId = a.MediaItemId,
                    MediaTitle = a.MediaItem != null ? a.MediaItem.Title : null,
                    Action = a.Action,
                    FromState = a.FromState,
                    ToState = a.ToState,
                    Details = a.Details,
                    Timestamp = a.Timestamp
                })
                .ToListAsync();

            return Ok(logs);
        }
    }
}
