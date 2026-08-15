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

        public async Task TriggerKillSequenceAsync(string accountId, string reason)
        {
            _logger.LogWarning("KILL SWITCH TRIGGERED for {AccountId}. Reason: {Reason}", accountId, reason);

            // 1. Fetch & cancel all pending orders via the Fyers API.
            await _fyersApi.CancelAllPendingOrdersAsync(accountId);

            // 2. Fetch all active positions & fire opposite market orders to square off.
            await _fyersApi.SquareOffAllPositionsAsync(accountId);

            // 3. Set the Redis kill_switch flag to LOCKED with the configured expiration.
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

            await _redisService.SetValueAsync($"kill_switch:{accountId}", "LOCKED", TimeSpan.FromMinutes(shutdownMinutes));
            
            _logger.LogWarning("Account {AccountId} is now LOCKED for {Minutes} minutes.", accountId, shutdownMinutes);
        }
        
        public async Task<bool> IsKillSwitchActiveAsync(string accountId)
        {
            return await _redisService.IsLockedAsync($"kill_switch:{accountId}");
        }
    }
}
