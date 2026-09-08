using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StoicTrade.Api.Data;
using StoicTrade.Api.Models;
using StoicTrade.Api.Services;

namespace StoicTrade.Api.Services
{
    public class RiskEvaluationResult
    {
        public bool IsApproved { get; set; }
        public string Status { get; set; } = "SignalOnly"; // AutoExecuted | AwaitingApproval | SignalOnly | Blocked | ExitSignal
        public string? RejectionReason { get; set; }
    }

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

        public async Task<RiskEvaluationResult> EvaluateAndExecuteAsync(Signal signal, string accountId = "default_account")
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
                return new RiskEvaluationResult
                {
                    IsApproved = false,
                    Status = "Blocked",
                    RejectionReason = $"Kill switch is active for account {accountId}"
                };
            }

            // 2. Check Time Window
            var istZone = TimeZoneHelper.GetIstTimeZone();
            var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, istZone);
            var currentTime = nowIst.TimeOfDay;

            if (currentTime < globalSettings.TradingWindowStart || currentTime > globalSettings.TradingWindowEnd)
            {
                string windowMsg = $"Outside trading window ({globalSettings.TradingWindowStart:hh\\:mm} - {globalSettings.TradingWindowEnd:hh\\:mm} IST)";
                _logger.LogWarning("RiskEngine: Blocked. Current time {CurrentTime} is {WindowMsg}", currentTime, windowMsg);
                return new RiskEvaluationResult
                {
                    IsApproved = false,
                    Status = "Blocked",
                    RejectionReason = windowMsg
                };
            }

            // 3. Check VIX
            var currentVixStr = await _redisService.GetValueAsync("market:vix");
            if (double.TryParse(currentVixStr, out double currentVix))
            {
                if ((decimal)currentVix < globalSettings.VixMinLimit || (decimal)currentVix > globalSettings.VixMaxLimit)
                {
                    string vixMsg = $"VIX ({currentVix:F1}) outside allowed limits ({globalSettings.VixMinLimit} - {globalSettings.VixMaxLimit})";
                    _logger.LogWarning("RiskEngine: Blocked. {VixMsg}", vixMsg);
                    return new RiskEvaluationResult
                    {
                        IsApproved = false,
                        Status = "Blocked",
                        RejectionReason = vixMsg
                    };
                }
            }
            else
            {
                _logger.LogWarning("RiskEngine: VIX data unavailable, skipping VIX check.");
            }

            // 4. Instrument Rule: Only allow NIFTY Index / Options and Equity Stocks (EQ)
            var instrument = signal.Instrument.ToUpperInvariant();
            bool isNifty = (instrument == "NIFTY" || (instrument.StartsWith("NIFTY") && (instrument.Contains("CE") || instrument.Contains("PE")))) && !instrument.Contains("BANK");
            bool isEquity = instrument.EndsWith("-EQ");

            if (!isNifty && !isEquity)
            {
                string instMsg = $"Instrument {instrument} not allowed";
                _logger.LogWarning("RiskEngine: Blocked. {InstMsg}", instMsg);
                return new RiskEvaluationResult
                {
                    IsApproved = false,
                    Status = "Blocked",
                    RejectionReason = instMsg
                };
            }

            // 5. Check Operating Mode (Supports both StrategyGroups and StrategyConfigs)
            string operatingMode = "Automatic";

            if (signal.StrategyName.StartsWith("Group: ", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var activeGroups = dbContext.StrategyGroups.ToList();
                    var matchingGroup = activeGroups.FirstOrDefault(g => signal.StrategyName.Contains(g.Name, StringComparison.OrdinalIgnoreCase));
                    if (matchingGroup != null)
                    {
                        operatingMode = matchingGroup.OperatingMode;
                    }
                }
                catch { }
            }
            else
            {
                try
                {
                    var activeGroups = dbContext.StrategyGroups.Where(g => g.IsEnabled).ToList();
                    var strat = dbContext.StrategyConfigs.FirstOrDefault(s => s.StrategyName == signal.StrategyName);
                    if (strat != null)
                    {
                        bool isPartOfActiveGroup = activeGroups.Any(g => {
                            try {
                                var ids = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<int>>(g.StrategyIdsJson) ?? new();
                                return ids.Contains(strat.Id);
                            } catch { return false; }
                        });

                        // If it is part of an active squad and not explicitly enabled standalone, suppress individual alert
                        if (isPartOfActiveGroup && !strat.IsEnabled)
                        {
                            _logger.LogInformation("RiskEngine: Suppressing standalone alert for {StrategyName} because it belongs to an active Strategy Group.", signal.StrategyName);
                            return new RiskEvaluationResult
                            {
                                IsApproved = false,
                                Status = "Blocked",
                                RejectionReason = "Suppressed: Member of active squad"
                            };
                        }

                        operatingMode = strat.OperatingMode;
                    }
                }
                catch { }
            }

            // Position exits should always execute immediately to protect capital and close exposure
            if (signal.Action == "EXIT")
            {
                _logger.LogInformation("RiskEngine: Signal Action is EXIT for {StrategyName} ({Instrument}). Executing immediate square-off.", signal.StrategyName, signal.Instrument);
                bool exitOk = await _orderManager.ExecuteOrderAsync(signal);
                return new RiskEvaluationResult
                {
                    IsApproved = exitOk,
                    Status = exitOk ? "ExitSignal" : "Blocked",
                    RejectionReason = exitOk ? null : "No matching open position to exit"
                };
            }

            if (operatingMode == "SignalOnly")
            {
                _logger.LogInformation("RiskEngine: Mode is SignalOnly. Logging signal but not executing.");
                return new RiskEvaluationResult
                {
                    IsApproved = true,
                    Status = "SignalOnly"
                };
            }
            
            if (operatingMode == "ApprovalRequired")
            {
                _logger.LogInformation("RiskEngine: Mode is ApprovalRequired for {StrategyName}. Holding signal for manual approval.", signal.StrategyName);
                var pendingSignalId = Guid.NewGuid().ToString();
                var pendingSignalJson = System.Text.Json.JsonSerializer.Serialize(signal);
                await _redisService.SetValueAsync($"pending_approval:{accountId}:{pendingSignalId}", pendingSignalJson, TimeSpan.FromMinutes(10));
                return new RiskEvaluationResult
                {
                    IsApproved = true,
                    Status = "AwaitingApproval"
                };
            }

            // If all checks pass and mode is Automatic:
            _logger.LogInformation("RiskEngine: Signal APPROVED. Passing to Order Manager.");
            bool executed = await _orderManager.ExecuteOrderAsync(signal);
            if (!executed)
            {
                _logger.LogWarning("RiskEngine: Order execution failed or was aborted by Order Manager for {StrategyName}", signal.StrategyName);
                return new RiskEvaluationResult
                {
                    IsApproved = false,
                    Status = "Blocked",
                    RejectionReason = "Order execution failed or unable to resolve contract"
                };
            }

            return new RiskEvaluationResult
            {
                IsApproved = true,
                Status = "AutoExecuted"
            };
        }
    }
}
