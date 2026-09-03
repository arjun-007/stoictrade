using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StoicTrade.Api.Data;
using StoicTrade.Api.Models;

namespace StoicTrade.Api.Services.Strategies
{
    /// <summary>
    /// Holds a single generated signal entry for the live signal log on the Strategy Analysis page.
    /// </summary>
    public class SignalLogEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string StrategyName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;   // BUY | SELL | EXIT
        public string Instrument { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal TargetPrice { get; set; }
        public decimal StopLossPrice { get; set; }
        public int Quantity { get; set; }
        /// <summary>AutoExecuted | AwaitingApproval | SignalOnly | Blocked | ExitSignal</summary>
        public string Status { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(15);
        public bool IsActive => DateTime.UtcNow <= ExpiresAt;
    }

    public class StrategyEngineService : BackgroundService
    {
        private readonly ILogger<StrategyEngineService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IEnumerable<IStrategy> _strategies;
        private readonly FyersApiService _fyersApi;

        // Static log of the last 100 signals — accessible via EngineController
        public static readonly ConcurrentQueue<SignalLogEntry> RecentSignals = new();
        private static readonly ConcurrentDictionary<string, DateTime> _lastSignalEmissionTime = new();
        private static readonly TimeSpan SignalCooldown = TimeSpan.FromMinutes(3);
        private const int MaxLogSize = 100;
        private DateTime? _lastSquareOffDate = null;
        private DateTime? _lastDailyResetDate = null;
        private DateTime? _lastStateResetDate = null;

        public StrategyEngineService(
            ILogger<StrategyEngineService> logger,
            IServiceProvider serviceProvider,
            IEnumerable<IStrategy> strategies,
            FyersApiService fyersApi)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _strategies = strategies;
            _fyersApi = fyersApi;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Strategy Engine is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var ist = StoicTrade.Api.Services.TimeZoneHelper.GetIstTimeZone();
                    var nowUtc = DateTime.UtcNow;
                    var nowIstDateTime = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, ist);
                    var nowIst = nowIstDateTime.TimeOfDay;
                    var todayIst = nowIstDateTime.Date;

                    var squareOffTime = new TimeSpan(15, 10, 0); // 3:10 PM IST
                    var autoStopCutoff = new TimeSpan(15, 40, 0); // 3:40 PM IST
                    var stateResetTime = new TimeSpan(9, 0, 0);   // 9:00 AM IST
                    var marketOpen = new TimeSpan(9, 15, 0);      // 9:15 AM IST
                    var marketClose = new TimeSpan(15, 30, 0);    // 3:30 PM IST

                    // 0. Daily 09:00 AM IST State Reset: Flush stuck strategy states from Redis
                    if (nowIst >= stateResetTime && _lastStateResetDate != todayIst)
                    {
                        _logger.LogInformation("Daily 09:00 AM IST reached. Flushing all strategy state locks from Redis.");
                        using var stateScope = _serviceProvider.CreateScope();
                        var redis = stateScope.ServiceProvider.GetRequiredService<StoicTrade.Api.Services.RedisService>();
                        var stateKeys = await redis.GetKeysByPrefixAsync("strategy_state_");
                        foreach (var key in stateKeys)
                        {
                            await redis.DeleteKeyAsync(key);
                            _logger.LogInformation("Deleted stale state key: {Key}", key);
                        }
                        _lastStateResetDate = todayIst;
                    }

                    // 1. Daily 9:15 AM IST Session Reset: Clear stale signal logs for clean day-to-day tracking
                    if (nowIst >= marketOpen && _lastDailyResetDate != todayIst)
                    {
                        _logger.LogInformation("Daily 09:15 AM IST market session opened. Resetting Live Signal Log for clean day-to-day session tracking.");
                        ClearSignals();
                        _lastDailyResetDate = todayIst;
                    }

                    // 2. Daily 3:10 PM IST Auto-Square-Off for active positions (Paper & Live)
                    if (nowIst >= squareOffTime && nowIst < autoStopCutoff)
                    {
                        if (_lastSquareOffDate != todayIst)
                        {
                            _logger.LogInformation("Daily 3:10 PM IST reached. Triggering Auto-Square-Off for all active positions.");
                            using var autoScope = _serviceProvider.CreateScope();
                            var autoSquareManager = autoScope.ServiceProvider.GetRequiredService<OrderManagementService>();
                            await autoSquareManager.AutoSquareOffAllPositionsAsync("Daily_310PM_AutoSquareOff");
                            _lastSquareOffDate = todayIst;
                        }
                    }

                    using var scope = _serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var globalSettings = dbContext.GlobalSettings.FirstOrDefault();
                    string tradeMode = globalSettings?.TradeMode ?? "Paper";

                    // 3. Daily 3:40 PM IST Auto-Stop Engine (Live mode only)
                    if (tradeMode == "Live" && (nowIst >= autoStopCutoff || nowIst < marketOpen))
                    {
                        if (_fyersApi.IsEngineRunning)
                        {
                            _logger.LogInformation("Daily 3:40 PM IST market cutoff reached in Live mode. Automatically stopping Strategy Engine and disconnecting broker session.");
                            _fyersApi.Disconnect();
                        }

                        // Off-market hours in Live mode: sleep with 10s delay to eliminate CPU and hosting costs
                        await Task.Delay(10000, stoppingToken);
                        continue;
                    }

                    // 4. Strict Indian Market Hours Guard: Only evaluate strategies between 09:15 AM and 03:30 PM IST
                    if (nowIst < marketOpen || nowIst > marketClose)
                    {
                        await Task.Delay(5000, stoppingToken);
                        continue;
                    }

                    // 5. If Engine is Stopped (manually or by auto-stop), pause execution and make NO outbound calls
                    if (!_fyersApi.IsEngineRunning)
                    {
                        await Task.Delay(5000, stoppingToken);
                        continue;
                    }

                    // 5b. Monitor active positions for Target, Stop Loss, and Trailing Stop Loss
                    var orderManager = scope.ServiceProvider.GetRequiredService<OrderManagementService>();
                    await orderManager.MonitorActivePositionsAsync(_serviceProvider);

                    // 6. Fetch enabled strategies and strategy groups from DB
                    var activeConfigs = dbContext.StrategyConfigs.Where(s => s.IsEnabled).ToList();
                    var activeGroups = dbContext.StrategyGroups.Where(g => g.IsEnabled).ToList();

                    // 7. Fetch market data based on TradeMode
                    var marketCache = scope.ServiceProvider.GetRequiredService<StoicTrade.Api.Services.MarketData.MarketDataCache>();
                    var spot = marketCache.GetSpotData("NIFTY");
                    decimal currentPrice = (spot != null && spot.Price > 0) ? spot.Price : 24250.0m;
                    string marketDataJson = $"{{\"symbol\": \"NIFTY\", \"price\": {currentPrice}}}";

                    // 8. Evaluate each active standalone strategy
                    var tickSignals = new List<Signal>();
                    var optionEngine = scope.ServiceProvider.GetRequiredService<OptionSelectionEngine>();
                    foreach (var config in activeConfigs)
                    {
                        var strategy = _strategies.FirstOrDefault(s => s.Name == config.StrategyName);
                        if (strategy != null)
                        {
                            var signal = await strategy.ExecuteAsync(config, marketDataJson);
                            if (signal != null)
                            {
                                // If position exit signal, resolve actual open position symbol & price
                                if (signal.Action == "EXIT")
                                {
                                    if (!IsInCooldown(signal.StrategyName))
                                    {
                                        var openPos = dbContext.PaperPositions.FirstOrDefault(p => p.StrategyName == signal.StrategyName && p.NetQty > 0);
                                        if (openPos != null)
                                        {
                                            signal.Instrument = openPos.Symbol;
                                            decimal? optLtp = optionEngine.ResolveOptionLtp(openPos.Symbol);
                                            signal.Price = (optLtp.HasValue && optLtp.Value > 0) ? optLtp.Value : (openPos.BuyAvg > 0 ? openPos.BuyAvg : signal.Price);
                                            signal.Quantity = openPos.NetQty;
                                            signal.TargetPrice = openPos.TargetPrice ?? 0;
                                            signal.StopLossPrice = openPos.StopLossPrice ?? 0;
                                        }

                                        tickSignals.Add(signal);
                                        AddToSignalLog(new SignalLogEntry
                                        {
                                            StrategyName = signal.StrategyName,
                                            Action = "EXIT",
                                            Instrument = signal.Instrument,
                                            Price = signal.Price,
                                            TargetPrice = signal.TargetPrice,
                                            StopLossPrice = signal.StopLossPrice,
                                            Quantity = signal.Quantity,
                                            Status = "ExitSignal",
                                            GeneratedAt = DateTime.UtcNow,
                                            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
                                        });
                                    }
                                    continue;
                                }

                                // If signal is for NIFTY underlying, select the optimal 2nd weekly ITM option contract
                                if (signal.Instrument == "NIFTY" || (!signal.Instrument.Contains("CE") && !signal.Instrument.Contains("PE")))
                                {
                                    string bias = (signal.Action == "BUY") ? "BULLISH" : "BEARISH";
                                    var contract = optionEngine.GetOptimalContract("NIFTY", bias, itmDistance: 1, expiryIndex: 2)
                                        ?? optionEngine.GetOptimalContract("NIFTY", bias, itmDistance: 1, expiryIndex: 1);
                                    if (!string.IsNullOrEmpty(contract))
                                    {
                                        string optSymbol = contract.Replace("NSE:", "");
                                        signal.Instrument = optSymbol;
                                        var optLtp = optionEngine.ResolveOptionLtp(optSymbol);
                                        signal.Price = (optLtp.HasValue && optLtp.Value > 0) ? optLtp.Value : 150m;
                                        signal.ExpectedPrice = signal.Price;
                                        // For option buyers: whether the index was Bullish (Buy CE) or Bearish (Buy PE),
                                        // the order action on the option contract is ALWAYS "BUY"
                                        signal.Action = "BUY";

                                        // Set Option Target and Stop Loss prices (~1:2 R:R)
                                        decimal targetGainPts = (decimal)config.PerTradeGainPoint;
                                        decimal slLossPts = (decimal)config.PerTradeStopLossPoint;
                                        decimal optionTargetDelta = targetGainPts > 0 ? (targetGainPts * 0.55m) : (signal.Price * 0.25m);
                                        decimal optionSlDelta = slLossPts > 0 ? (slLossPts * 0.55m) : (signal.Price * 0.15m);

                                        signal.TargetPrice = Math.Round(signal.Price + optionTargetDelta, 2);
                                        signal.StopLossPrice = Math.Round(Math.Max(5.0m, signal.Price - optionSlDelta), 2);
                                    }
                                }

                                // Anti-Spam / Cooldown Check: avoid repeating the same strategy signal within cooldown window
                                if (!IsInCooldown(signal.StrategyName))
                                {
                                    tickSignals.Add(signal);
                                    // Determine log status based on operating mode
                                    string logStatus = config.OperatingMode switch
                                    {
                                        "Automatic" => "AutoExecuted",
                                        "ApprovalRequired" => "AwaitingApproval",
                                        "SignalOnly" => "SignalOnly",
                                        _ => "SignalOnly"
                                    };
                                    AddToSignalLog(new SignalLogEntry
                                    {
                                        StrategyName = signal.StrategyName,
                                        Action = signal.Action,
                                        Instrument = signal.Instrument,
                                        Price = signal.Price,
                                        TargetPrice = signal.TargetPrice,
                                        StopLossPrice = signal.StopLossPrice,
                                        Quantity = signal.Quantity,
                                        Status = logStatus,
                                        GeneratedAt = DateTime.UtcNow,
                                        ExpiresAt = DateTime.UtcNow.AddMinutes(15)
                                    });
                                }
                            }
                            else
                            {
                                _logger.LogDebug("Strategy {StrategyName} (ID: {Id}) evaluated but returned no signal. (Skipped or Filter Rejected).", config.StrategyName, config.Id);
                            }
                        }
                    }

                    // 9. Evaluate active Strategy Groups (Multi-Strategy Consensus)
                    var allConfigs = dbContext.StrategyConfigs.ToList();
                    foreach (var group in activeGroups)
                    {
                        List<int> memberIds = new();
                        try
                        {
                            memberIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(group.StrategyIdsJson) ?? new();
                        }
                        catch {}

                        if (!memberIds.Any()) continue;

                        var groupMemberSignals = new List<Signal>();
                        foreach (var memberId in memberIds)
                        {
                            var memberConfig = allConfigs.FirstOrDefault(c => c.Id == memberId);
                            if (memberConfig == null) continue;

                            var memberStrategy = _strategies.FirstOrDefault(s => s.Name == memberConfig.StrategyName);
                            if (memberStrategy != null)
                            {
                                var memberSig = await memberStrategy.ExecuteAsync(memberConfig, marketDataJson);
                                if (memberSig != null && memberSig.Action != "EXIT") groupMemberSignals.Add(memberSig);
                            }
                        }

                        if (!groupMemberSignals.Any()) continue;

                        // Consensus evaluation
                        var buyVotes = groupMemberSignals.Count(s => s.Action == "BUY");
                        var sellVotes = groupMemberSignals.Count(s => s.Action == "SELL" || s.Action == "SHORT");
                        int requiredVotes = group.ConsensusRule switch
                        {
                            "Unanimous" => memberIds.Count,
                            "Majority" => Math.Max(2, group.MinAgreeingStrategies),
                            "Any" => 1,
                            _ => Math.Max(2, group.MinAgreeingStrategies)
                        };

                        string? consensusAction = null;
                        if (buyVotes >= requiredVotes && sellVotes == 0) consensusAction = "BUY";
                        else if (sellVotes >= requiredVotes && buyVotes == 0) consensusAction = "SELL";

                        if (!string.IsNullOrEmpty(consensusAction))
                        {
                            var leaderSignal = groupMemberSignals.First(s => s.Action == (consensusAction == "BUY" ? "BUY" : (groupMemberSignals.Any(x => x.Action == "SHORT") ? "SHORT" : "SELL")));
                            var groupSignal = new Signal
                            {
                                StrategyName = $"Group: {group.Name} ({buyVotes + sellVotes}/{memberIds.Count} Agree)",
                                Action = consensusAction,
                                Instrument = "NIFTY",
                                Price = leaderSignal.Price,
                                StopLossPrice = leaderSignal.StopLossPrice,
                                TargetPrice = leaderSignal.TargetPrice,
                                Quantity = leaderSignal.Quantity,
                                Atr = leaderSignal.Atr,
                                Rvol = leaderSignal.Rvol,
                                Priority = 3 // High priority for multi-strategy consensus
                            };

                            // Resolve option contract
                            string bias = consensusAction == "BUY" ? "BULLISH" : "BEARISH";
                            var contract = optionEngine.GetOptimalContract("NIFTY", bias, itmDistance: 1, expiryIndex: 2)
                                ?? optionEngine.GetOptimalContract("NIFTY", bias, itmDistance: 1, expiryIndex: 1);
                            if (!string.IsNullOrEmpty(contract))
                            {
                                string optSymbol = contract.Replace("NSE:", "");
                                groupSignal.Instrument = optSymbol;
                                var optLtp = optionEngine.ResolveOptionLtp(optSymbol);
                                groupSignal.Price = (optLtp.HasValue && optLtp.Value > 0) ? optLtp.Value : 150m;
                                groupSignal.ExpectedPrice = groupSignal.Price;
                                // For option buyers: option contract order action is always "BUY"
                                groupSignal.Action = "BUY";

                                decimal targetGainPts = group.PerTradeGainPoint;
                                decimal slLossPts = group.PerTradeStopLossPoint;
                                decimal optionTargetDelta = targetGainPts > 0 ? (targetGainPts * 0.55m) : (groupSignal.Price * 0.25m);
                                decimal optionSlDelta = slLossPts > 0 ? (slLossPts * 0.55m) : (groupSignal.Price * 0.15m);

                                groupSignal.TargetPrice = Math.Round(groupSignal.Price + optionTargetDelta, 2);
                                groupSignal.StopLossPrice = Math.Round(Math.Max(5.0m, groupSignal.Price - optionSlDelta), 2);
                            }

                            // Anti-Spam / Cooldown Check for Squad signals
                            if (!IsInCooldown(groupSignal.StrategyName))
                            {
                                tickSignals.Add(groupSignal);

                                string logStatus = group.OperatingMode switch
                                {
                                    "Automatic" => "AutoExecuted",
                                    "ApprovalRequired" => "AwaitingApproval",
                                    "SignalOnly" => "SignalOnly",
                                    _ => "SignalOnly"
                                };
                                AddToSignalLog(new SignalLogEntry
                                {
                                    StrategyName = groupSignal.StrategyName,
                                    Action = groupSignal.Action,
                                    Instrument = groupSignal.Instrument,
                                    Price = groupSignal.Price,
                                    TargetPrice = groupSignal.TargetPrice,
                                    StopLossPrice = groupSignal.StopLossPrice,
                                    Quantity = groupSignal.Quantity,
                                    Status = logStatus,
                                    GeneratedAt = DateTime.UtcNow,
                                    ExpiresAt = DateTime.UtcNow.AddMinutes(15)
                                });
                            }
                        }
                    }

                    // 10. Aggregate signals
                    var aggregator = scope.ServiceProvider.GetRequiredService<SignalAggregatorService>();
                    var aggregatedSignals = aggregator.Aggregate(tickSignals);

                    // 11. Send to Risk Engine
                    var riskEngine = scope.ServiceProvider.GetRequiredService<StoicTrade.Api.Services.RiskEngine>();
                    foreach (var signal in aggregatedSignals)
                    {
                        if (globalSettings != null)
                        {
                            // Multiply the base quantity by AutoTradeLots (default 1)
                            signal.Quantity = globalSettings.BaseLotSize * Math.Max(1, globalSettings.AutoTradeLots);
                        }
                        
                        await riskEngine.EvaluateAndExecuteAsync(signal);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in Strategy Engine.");
                }

                // Wait before next tick (simulated 1 second tick for now)
                await Task.Delay(1000, stoppingToken);
            }

            _logger.LogInformation("Strategy Engine is stopping.");
        }

        private static bool IsInCooldown(string strategyName)
        {
            string key = strategyName;
            if (_lastSignalEmissionTime.TryGetValue(key, out var lastTime))
            {
                if (DateTime.UtcNow - lastTime < SignalCooldown)
                {
                    return true;
                }
            }
            _lastSignalEmissionTime[key] = DateTime.UtcNow;
            return false;
        }

        public static void ClearSignals()
        {
            while (RecentSignals.TryDequeue(out _)) { }
            _lastSignalEmissionTime.Clear();
        }

        public static void AddToSignalLog(SignalLogEntry entry)
        {
            RecentSignals.Enqueue(entry);
            // Trim to last MaxLogSize entries
            while (RecentSignals.Count > MaxLogSize)
            {
                RecentSignals.TryDequeue(out _);
            }
        }
    }
}
