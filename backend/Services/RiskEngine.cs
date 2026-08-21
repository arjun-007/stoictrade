using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StoicTrade.Api.Data;
using StoicTrade.Api.Models;
using StoicTrade.Api.Services;

namespace StoicTrade.Api.Services
{
    public class RiskEngine
    {
        private readonly ILogger<RiskEngine> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly OrderManagementService _orderManager;
        private readonly RedisService _redisService;

        public RiskEngine(ILogger<RiskEngine> logger, IServiceProvider serviceProvider, OrderManagementService orderManager, RedisService redisService)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _orderManager = orderManager;
            _redisService = redisService;
        }

        public async Task<bool> EvaluateAndExecuteAsync(Signal signal, string accountId = "default_account")
        {
            _logger.LogInformation("RiskEngine: Evaluating signal {StrategyName} {Action} {Instrument}", 
                signal.StrategyName, signal.Action, signal.Instrument);

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var globalSettings = dbContext.GlobalSettings.FirstOrDefault() ?? new GlobalSettings();

            // 1. Check Kill Switch Lock
            if (await _redisService.IsLockedAsync($"kill_switch:{accountId}"))
            {
                _logger.LogWarning("RiskEngine: Blocked. Kill switch is active for account {AccountId}", accountId);
                return false;
            }

            // 2. Check Time Window
            var istZone = TimeZoneHelper.GetIstTimeZone();
            var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, istZone);
            var currentTime = nowIst.TimeOfDay;

            if (currentTime < globalSettings.TradingWindowStart || currentTime > globalSettings.TradingWindowEnd)
            {
                _logger.LogWarning("RiskEngine: Blocked. Current time {CurrentTime} is outside trading window ({Start}-{End})", 
                    currentTime, globalSettings.TradingWindowStart, globalSettings.TradingWindowEnd);
                return false;
            }

            // 3. Check VIX
            var currentVixStr = await _redisService.GetValueAsync("market:vix");
            if (double.TryParse(currentVixStr, out double currentVix))
            {
                if ((decimal)currentVix < globalSettings.VixMinLimit || (decimal)currentVix > globalSettings.VixMaxLimit)
                {
                    _logger.LogWarning("RiskEngine: Blocked. VIX ({Vix}) is outside allowed limits ({Min}-{Max})", 
                        currentVix, globalSettings.VixMinLimit, globalSettings.VixMaxLimit);
                    return false;
                }
            }
            else
            {
                _logger.LogWarning("RiskEngine: VIX data unavailable, skipping VIX check or blocking if strict mode.");
                // Depending on strictness, we might return false here. For now, continue.
            }

            // 4. Instrument Rule: Only allow NIFTY Index / Options and Equity Stocks (EQ)
            var instrument = signal.Instrument.ToUpperInvariant();
            bool isNifty = instrument == "NIFTY" || (instrument.StartsWith("NIFTY") && (instrument.Contains("CE") || instrument.Contains("PE")));
            bool isEquity = instrument.EndsWith("-EQ");

            if (!isNifty && !isEquity)
            {
                _logger.LogWarning("RiskEngine: Blocked. Instrument {Instrument} not allowed.", instrument);
                return false;
            }

            // 5. Check Operating Mode
            var strategyConfig = dbContext.StrategyConfigs.FirstOrDefault(s => s.StrategyName == signal.StrategyName);
            if (strategyConfig != null)
            {
                if (strategyConfig.OperatingMode == "SignalOnly")
                {
                    _logger.LogInformation("RiskEngine: Mode is SignalOnly. Logging signal but not executing.");
                    return true;
                }
                
                if (strategyConfig.OperatingMode == "ApprovalRequired")
                {
                    _logger.LogInformation("RiskEngine: Mode is ApprovalRequired. Holding signal for manual approval.");
                    var pendingSignalId = Guid.NewGuid().ToString();
                    var pendingSignalJson = System.Text.Json.JsonSerializer.Serialize(signal);
                    await _redisService.SetValueAsync($"pending_approval:{accountId}:{pendingSignalId}", pendingSignalJson, TimeSpan.FromMinutes(10));
                    return true;
                }
            }

            // If all checks pass and mode is Automatic:
            _logger.LogInformation("RiskEngine: Signal APPROVED. Passing to Order Manager.");
            await _orderManager.ExecuteOrderAsync(signal);
            return true;
        }
    }
}
