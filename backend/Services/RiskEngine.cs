using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StoicTrade.Api.Data;
using StoicTrade.Api.Models;
using StoicTrade.Api.Services;
using StoicTrade.Api.Services.MarketData;

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
        private readonly KillSwitchService _killSwitchService;
        private readonly MarketDataAggregatorService _aggregator;

        public RiskEngine(
            ILogger<RiskEngine> logger,
            IServiceProvider serviceProvider,
            OrderManagementService orderManager,
            RedisService redisService,
            KillSwitchService killSwitchService,
            MarketDataAggregatorService aggregator)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _orderManager = orderManager;
            _redisService = redisService;
            _killSwitchService = killSwitchService;
            _aggregator = aggregator;
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

            // 2. Position exits should ALWAYS execute immediately to protect capital and close exposure
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

            // 3. Check Time Window
            var istZone = TimeZoneHelper.GetIstTimeZone();
            var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, istZone);
            var currentTime = nowIst.TimeOfDay;
            var todayIst = nowIst.Date;

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

            // 4. Check VIX Limits
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

            // 5. Instrument Rule: Only allow NIFTY Index / Options and Equity Stocks (EQ)
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

            // 6. ENFORCE EXISTING RISK & LOSS LIMITS:
            // 6a. Max Trades Per Day Guard
            if (globalSettings.MaxTradesPerDay > 0)
            {
                int tradesExecutedToday = dbContext.TradeLogs
                    .Where(t => t.TradeType == "BUY" && t.Status == "EXECUTED")
                    .AsEnumerable()
                    .Count(t => TimeZoneInfo.ConvertTimeFromUtc(t.Timestamp, istZone).Date == todayIst);

                if (tradesExecutedToday >= globalSettings.MaxTradesPerDay)
                {
                    string tradesMsg = $"Daily trade limit reached ({tradesExecutedToday}/{globalSettings.MaxTradesPerDay} trades executed today)";
                    _logger.LogWarning("RiskEngine: Blocked. {TradesMsg}", tradesMsg);
                    return new RiskEvaluationResult
                    {
                        IsApproved = false,
                        Status = "Blocked",
                        RejectionReason = tradesMsg
                    };
                }
            }

            // 6b. Max Failed (Losing) Trades Per Day Guard
            if (globalSettings.MaxFailedTrades > 0)
            {
                int failedTradesToday = dbContext.PaperPositions
                    .Where(p => p.NetQty == 0 && (p.RealizedProfit ?? 0m) < 0)
                    .AsEnumerable()
                    .Count(p => TimeZoneInfo.ConvertTimeFromUtc(p.UpdatedAt, istZone).Date == todayIst);

                if (failedTradesToday >= globalSettings.MaxFailedTrades)
                {
                    string failedMsg = $"Daily failed trades limit reached ({failedTradesToday}/{globalSettings.MaxFailedTrades} loss trades today)";
                    _logger.LogWarning("RiskEngine: Blocked. {FailedMsg}", failedMsg);
                    return new RiskEvaluationResult
                    {
                        IsApproved = false,
                        Status = "Blocked",
                        RejectionReason = failedMsg
                    };
                }
            }

            // 6c. Max Daily Loss Guard (Realized P&L + Open Unrealized P&L)
            if (globalSettings.MaxDailyLoss > 0)
            {
                decimal realizedLossToday = dbContext.PaperPositions
                    .AsEnumerable()
                    .Where(p => TimeZoneInfo.ConvertTimeFromUtc(p.UpdatedAt, istZone).Date == todayIst)
                    .Sum(p => p.RealizedProfit ?? 0m);

                decimal unrealizedLoss = 0m;
                var currentOpenPositions = dbContext.PaperPositions.Where(p => p.NetQty > 0).ToList();
                var optionEngine = scope.ServiceProvider.GetRequiredService<StoicTrade.Api.Services.Strategies.OptionSelectionEngine>();

                foreach (var pos in currentOpenPositions)
                {
                    decimal? ltp = optionEngine.ResolveOptionLtp(pos.Symbol);
                    if (ltp.HasValue && ltp.Value > 0)
                    {
                        unrealizedLoss += (ltp.Value - (pos.BuyAvg ?? 0m)) * pos.NetQty;
                    }
                }

                decimal totalDailyPnl = realizedLossToday + unrealizedLoss;
                if (totalDailyPnl <= -globalSettings.MaxDailyLoss)
                {
                    string dailyLossMsg = $"Daily loss limit breached (Net P&L: ₹{totalDailyPnl:F2} <= -₹{globalSettings.MaxDailyLoss:F2}). Locking engine.";
                    _logger.LogCritical("RiskEngine: Blocked. {DailyLossMsg}", dailyLossMsg);
                    await _killSwitchService.TriggerMasterKillSwitchAsync(accountId, "Max daily loss limit breached");
                    return new RiskEvaluationResult
                    {
                        IsApproved = false,
                        Status = "Blocked",
                        RejectionReason = dailyLossMsg
                    };
                }
            }

            // 6d. Capital Margin Requirement Guard (MaxCapitalPerTrade)
            decimal executionPriceEstimate = signal.ExpectedPrice > 0 ? signal.ExpectedPrice : (signal.Price > 0 ? signal.Price : 150m);
            decimal capitalRequired = signal.Quantity * executionPriceEstimate;
            if (globalSettings.MaxCapitalPerTrade > 0 && capitalRequired > globalSettings.MaxCapitalPerTrade)
            {
                string capMsg = $"Capital requirement ₹{capitalRequired:N0} exceeds MaxCapitalPerTrade limit of ₹{globalSettings.MaxCapitalPerTrade:N0}";
                _logger.LogWarning("RiskEngine: Blocked. {CapMsg}", capMsg);
                return new RiskEvaluationResult
                {
                    IsApproved = false,
                    Status = "Blocked",
                    RejectionReason = capMsg
                };
            }

            // 7. CONCURRENCY & CONFLICT RESOLUTION
            var openPositions = dbContext.PaperPositions.Where(p => p.NetQty > 0).ToList();

            // Scenario 3 & 4: Same Strategy Checks
            var sameStrategyPosition = openPositions.FirstOrDefault(p => p.StrategyName == signal.StrategyName);
            if (sameStrategyPosition != null)
            {
                bool samePosIsCall = IsCallSymbol(sameStrategyPosition.Symbol);
                bool samePosIsPut = IsPutSymbol(sameStrategyPosition.Symbol);
                bool incomingIsCall = IsCall(signal);
                bool incomingIsPut = IsPut(signal);

                bool isSameDirection = (incomingIsCall && samePosIsCall) || (incomingIsPut && samePosIsPut);

                if (isSameDirection)
                {
                    // Scenario 3: Same Strategy Duplicate Entry (Anti-Pyramiding)
                    if (globalSettings.PreventSameStrategyPyramiding)
                    {
                        string dupMsg = $"Duplicate entry blocked: Strategy '{signal.StrategyName}' already holds an active position in {sameStrategyPosition.Symbol}";
                        _logger.LogWarning("RiskEngine: Blocked. {DupMsg}", dupMsg);
                        return new RiskEvaluationResult
                        {
                            IsApproved = false,
                            Status = "Blocked",
                            RejectionReason = dupMsg
                        };
                    }
                }
                else
                {
                    // Scenario 4: Same Strategy Reversal (Stop-and-Reverse)
                    _logger.LogInformation("RiskEngine: Same strategy reversal detected for '{Strategy}'. Exiting current {Symbol} to enter reverse {NewInstrument}.",
                        signal.StrategyName, sameStrategyPosition.Symbol, signal.Instrument);

                    await _orderManager.ExecuteOrderAsync(new Signal
                    {
                        Action = "EXIT",
                        StrategyName = signal.StrategyName,
                        Instrument = sameStrategyPosition.Symbol,
                        Price = sameStrategyPosition.PeakLtp ?? signal.Price
                    });

                    // Refresh active positions
                    openPositions = dbContext.PaperPositions.Where(p => p.NetQty > 0).ToList();
                }
            }

            // Scenario 2: Cross-Strategy Directional Conflict - Option B (Higher-Timeframe Filter Overwrite)
            bool signalIsCall = IsCall(signal);
            bool signalIsPut = IsPut(signal);

            var oppositeActivePositions = openPositions.Where(p => 
                (signalIsCall && IsPutSymbol(p.Symbol)) || 
                (signalIsPut && IsCallSymbol(p.Symbol))
            ).ToList();

            if (oppositeActivePositions.Any())
            {
                var conflictingPos = oppositeActivePositions.First();

                if (globalSettings.DisallowOppositeLegs)
                {
                    // Evaluate Option B: Check Higher-Timeframe (15m EMA/VWAP) confirmation & Priority
                    string newHtfDirection = signalIsCall ? "BUY" : "SELL";
                    bool htfConfirmed = false;
                    try
                    {
                        htfConfirmed = StoicTrade.Api.Services.Strategies.StrategyFilterHelper.CheckHtfGate(_aggregator, newHtfDirection);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "RiskEngine: Error evaluating HTF gate for {Direction}", newHtfDirection);
                    }

                    bool hasOverwritePriority = signal.Priority <= 2; // Priority 1 or 2 represents high conviction/squad consensus

                    if (globalSettings.AllowHtfReversalOverwrite && (htfConfirmed || hasOverwritePriority))
                    {
                        _logger.LogInformation("RiskEngine [Option B Overwrite]: Directional reversal confirmed by HTF ({HtfOk}) / Priority ({Pri}) for {Strategy}. Squaring off conflicting position {OppStrategy} ({OppSymbol}) first.",
                            htfConfirmed, signal.Priority, signal.StrategyName, conflictingPos.StrategyName, conflictingPos.Symbol);

                        foreach (var oppPos in oppositeActivePositions)
                        {
                            await _orderManager.ExecuteOrderAsync(new Signal
                            {
                                Action = "EXIT",
                                StrategyName = oppPos.StrategyName ?? "Strategy",
                                Instrument = oppPos.Symbol,
                                Price = oppPos.PeakLtp ?? signal.Price
                            });
                        }

                        // Refresh active positions list
                        openPositions = dbContext.PaperPositions.Where(p => p.NetQty > 0).ToList();
                    }
                    else
                    {
                        string oppMsg = $"Directional conflict: Portfolio already holds opposite position ({conflictingPos.StrategyName} in {conflictingPos.Symbol}). New signal lacks HTF confirmation.";
                        _logger.LogWarning("RiskEngine: Blocked. {OppMsg}", oppMsg);
                        return new RiskEvaluationResult
                        {
                            IsApproved = false,
                            Status = "Blocked",
                            RejectionReason = oppMsg
                        };
                    }
                }
            }

            // Scenario 1: Max Concurrent Active Positions Limit
            if (globalSettings.MaxActivePositions > 0 && openPositions.Count >= globalSettings.MaxActivePositions)
            {
                var existingPos = openPositions.First();
                string posLimitMsg = $"Capital Guard: Max active positions limit ({globalSettings.MaxActivePositions}) reached. Active position held by {existingPos.StrategyName} ({existingPos.Symbol}).";
                _logger.LogWarning("RiskEngine: Blocked. {PosLimitMsg}", posLimitMsg);
                return new RiskEvaluationResult
                {
                    IsApproved = false,
                    Status = "Blocked",
                    RejectionReason = posLimitMsg
                };
            }

            // 8. Check Operating Mode (Supports both StrategyGroups and StrategyConfigs)
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

            if (operatingMode == "SignalOnly")
            {
                _logger.LogInformation("RiskEngine: Mode is SignalOnly for {StrategyName}. Logging signal but not executing.", signal.StrategyName);
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

            // 9. If all checks pass and mode is Automatic: Execute Order
            _logger.LogInformation("RiskEngine: Signal APPROVED for {StrategyName} ({Instrument}). Passing to Order Manager.", signal.StrategyName, signal.Instrument);
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

        private static bool IsCall(Signal signal)
        {
            if (signal.Action == "BUY_PE") return false;
            if (signal.Instrument.Contains("PE", StringComparison.OrdinalIgnoreCase)) return false;
            if (signal.Instrument.Contains("CE", StringComparison.OrdinalIgnoreCase)) return true;
            return signal.Action == "BUY";
        }

        private static bool IsPut(Signal signal)
        {
            if (signal.Action == "BUY_PE") return true;
            if (signal.Instrument.Contains("PE", StringComparison.OrdinalIgnoreCase)) return true;
            if (signal.Instrument.Contains("CE", StringComparison.OrdinalIgnoreCase)) return false;
            return signal.Action == "SELL";
        }

        private static bool IsCallSymbol(string symbol)
        {
            return symbol.Contains("CE", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPutSymbol(string symbol)
        {
            return symbol.Contains("PE", StringComparison.OrdinalIgnoreCase);
        }
    }
}
