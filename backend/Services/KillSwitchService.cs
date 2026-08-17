using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using StoicTrade.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace StoicTrade.Api.Services
{
    public class KillSwitchService
    {
        private readonly RedisService _redisService;
        private readonly FyersApiService _fyersApi;
        private readonly ILogger<KillSwitchService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public KillSwitchService(RedisService redisService, FyersApiService fyersApi, ILogger<KillSwitchService> logger, IServiceScopeFactory scopeFactory)
        {
            _redisService = redisService;
            _fyersApi = fyersApi;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public async Task TriggerMasterKillSwitchAsync(string accountId, string reason)
        {
            _logger.LogWarning("MASTER KILL SWITCH TRIGGERED for {AccountId}. Reason: {Reason}", accountId, reason);

            int shutdownMinutes = 720;
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var settings = await context.GlobalSettings.FirstOrDefaultAsync();
                if (settings != null)
                {
                    shutdownMinutes = settings.KillSwitchShutdownMinutes;
                }
            }

            // Sets a lock that the RiskEngine uses to block all NEW orders
            await _redisService.SetValueAsync($"kill_switch:{accountId}", "LOCKED", TimeSpan.FromMinutes(shutdownMinutes));
            
            _logger.LogWarning("Account {AccountId} is now LOCKED for new orders for {Minutes} minutes.", accountId, shutdownMinutes);
        }

        public async Task EmergencySquareOffAsync(string accountId)
        {
            _logger.LogCritical("EMERGENCY SQUARE-OFF INITIATED for {AccountId}.", accountId);

            // Set pending flag for idempotency
            await _redisService.SetValueAsync($"emergency_squareoff_pending:{accountId}", "true", TimeSpan.FromMinutes(10));

            // 1. Fetch & cancel all pending orders
            await _fyersApi.CancelAllPendingOrdersAsync(accountId);

            // 2. Fetch all active positions & fire opposite market orders to square off.
            await _fyersApi.SquareOffAllPositionsAsync(accountId);

            // 3. (In a real scenario, this would loop and verify positions = 0)
            
            // Clear pending flag
            await _redisService.DeleteKeyAsync($"emergency_squareoff_pending:{accountId}");

            _logger.LogInformation("Emergency square-off completed for {AccountId}.", accountId);
        }
        
        public async Task<bool> IsKillSwitchActiveAsync(string accountId)
        {
            return await _redisService.IsLockedAsync($"kill_switch:{accountId}");
        }
    }
}
