using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Storarr.Services;

namespace Storarr.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class TransitionsController : ControllerBase
    {
        private readonly ITransitionService _transitionService;
        private readonly ILogger<TransitionsController> _logger;

        public TransitionsController(
            ITransitionService transitionService,
            ILogger<TransitionsController> logger)
        {
            _transitionService = transitionService;
            _logger = logger;
        }

        [HttpPost("process")]
        public async Task<ActionResult> ProcessTransitions()
        {
            _logger.LogInformation("[TransitionsController] Manual transition processing triggered");

            try
            {
                await _transitionService.CheckAndProcessTransitions();
                _logger.LogInformation("[TransitionsController] Transition processing completed");
                return Ok(new { message = "Transition processing completed", timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TransitionsController] Transition processing failed");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
