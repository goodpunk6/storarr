using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Storarr.Services;

namespace Storarr.BackgroundServices
{
    public class TransitionScheduler : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TransitionScheduler> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

        public TransitionScheduler(IServiceProvider serviceProvider, ILogger<TransitionScheduler> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("TransitionScheduler started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var transitionService = scope.ServiceProvider.GetRequiredService<ITransitionService>();

                    await transitionService.CheckAndProcessTransitions();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in TransitionScheduler");
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }
    }
}
