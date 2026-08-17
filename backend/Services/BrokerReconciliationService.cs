using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StoicTrade.Api.Services
{
    public class BrokerReconciliationService : BackgroundService
    {
        private readonly ILogger<BrokerReconciliationService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public BrokerReconciliationService(ILogger<BrokerReconciliationService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BrokerReconciliationService is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var fyersApi = scope.ServiceProvider.GetRequiredService<FyersApiService>();
                    var redisService = scope.ServiceProvider.GetRequiredService<RedisService>();

                    if (fyersApi.IsEngineRunning)
                    {
                        // 1. Fetch FYERS positions
                        var fyersPositions = await fyersApi.GetPositionsAsync();

                        // 2. Fetch Internal positions from DB or Redis (stubbed for now)
                        // var internalPositions = await GetInternalPositionsAsync();

                        // 3. Compare (Dummy logic for now: just log)
                        _logger.LogDebug("Reconciliation: Checked {Count} positions.", fyersPositions.Count);
                        
                        bool mismatchDetected = false; 
                        
                        if (mismatchDetected)
                        {
                            _logger.LogCritical("Reconciliation: Mismatch detected! Locking account.");
                            await redisService.SetLockAsync("kill_switch:default_account", TimeSpan.FromHours(12));
                            fyersApi.Disconnect(); // Halt further actions
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during broker reconciliation.");
                }

                // Wait 5 seconds before next reconciliation check
                await Task.Delay(5000, stoppingToken);
            }

            _logger.LogInformation("BrokerReconciliationService is stopping.");
        }
    }
}
