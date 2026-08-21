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
        public string Action { get; set; } = string.Empty;   // BUY | SELL
        public string Instrument { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        /// <summary>AutoExecuted | AwaitingApproval | SignalOnly | Blocked</summary>
        public string Status { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    public class StrategyEngineService : BackgroundService
    {
        private readonly ILogger<StrategyEngineService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IEnumerable<IStrategy> _strategies;
        private readonly FyersApiService _fyersApi;

        // Static log of the last 100 signals — accessible via EngineController
        public static readonly ConcurrentQueue<SignalLogEntry> RecentSignals = new();
        private const int MaxLogSize = 100;
        private DateTime? _lastSquareOffDate = null;

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
                    var marketOpen = new TimeSpan(9, 15, 0);      // 9:15 AM IST

                    // 1. Daily 3:10 PM IST Auto-Square-Off for active positions (Paper & Live)
                    if (nowIst >= squareOffTime && nowIst < autoStopCutoff)
                    {
                        if (_lastSquareOffDate != todayIst)
                        {
                            _logger.LogInformation("Daily 3:10 PM IST reached. Triggering Auto-Square-Off for all active positions.");
                            using var autoScope = _serviceProvider.CreateScope();
                            var orderManager = autoScope.ServiceProvider.GetRequiredService<OrderManagementService>();
                            await orderManager.AutoSquareOffAllPositionsAsync("Daily_310PM_AutoSquareOff");
                            _lastSquareOffDate = todayIst;
                        }
                    }

                    // 2. Daily 3:40 PM IST Auto-Stop Engine
                    if (nowIst >= autoStopCutoff || nowIst < marketOpen)
                    {
                        if (_fyersApi.IsEngineRunning)
                        {
                            _logger.LogInformation("Daily 3:40 PM IST market cutoff reached. Automatically stopping Strategy Engine and disconnecting broker session.");
                            _fyersApi.Disconnect();
                        }

                        // Off-market hours: sleep with 10s delay to eliminate CPU and hosting costs
                        await Task.Delay(10000, stoppingToken);
                        continue;
                    }

                    // 3. If Engine is Stopped (manually or by auto-stop), pause execution and make NO outbound calls
                    if (!_fyersApi.IsEngineRunning)
                    {
                        await Task.Delay(5000, stoppingToken);
                        continue;
                    }

                    using var scope = _serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    // 4. Fetch enabled strategies from DB
                    var activeConfigs = dbContext.StrategyConfigs.Where(s => s.IsEnabled).ToList();

                    // 5. Fetch market data based on TradeMode
                    var globalSettings = dbContext.GlobalSettings.FirstOrDefault();
                    string marketDataJson = "{\"symbol\": \"NIFTY\", \"price\": 22000}"; // Default fallback

                    if (globalSettings != null && globalSettings.TradeMode == "Paper")
                    {
                        var marketCache = scope.ServiceProvider.GetRequiredService<StoicTrade.Api.Services.MarketData.MarketDataCache>();
                        var spot = marketCache.GetSpotData("NIFTY");
                        if (spot != null)
                        {
                            marketDataJson = $"{{\"symbol\": \"NIFTY\", \"price\": {spot.Price}}}";
                        }
                    }
                    else
                    {
                        // In reality this comes from Fyers WebSocket for Live trading
                        // marketDataJson = GetFromFyers()
                    }

                    // 3. Evaluate each active strategy
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
                                // If signal is for NIFTY underlying, select the optimal 2nd weekly ITM option contract
                                if (signal.Instrument == "NIFTY" || (!signal.Instrument.Contains("CE") && !signal.Instrument.Contains("PE")))
                                {
                                    string bias = signal.Action == "BUY" ? "BULLISH" : "BEARISH";
                                    var contract = optionEngine.GetOptimalContract("NIFTY", bias, itmDistance: 1, expiryIndex: 1)
                                        ?? optionEngine.GetOptimalContract("NIFTY", bias, itmDistance: 1, expiryIndex: 0);
                                    if (!string.IsNullOrEmpty(contract))
                                    {
                                        string optSymbol = contract.Replace("NSE:", "");
                                        signal.Instrument = optSymbol;
                                        var optLtp = optionEngine.ResolveOptionLtp(optSymbol);
                                        signal.Price = (optLtp.HasValue && optLtp.Value > 0) ? optLtp.Value : 150m;
                                        signal.ExpectedPrice = signal.Price;
                                    }
                                }

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
                                    Quantity = signal.Quantity,
                                    Status = logStatus,
                                    GeneratedAt = DateTime.UtcNow
                                });
                            }
                        }
                    }

                    // 4. Aggregate signals
                    var aggregator = scope.ServiceProvider.GetRequiredService<SignalAggregatorService>();
                    var aggregatedSignals = aggregator.Aggregate(tickSignals);

                    // 5. Send to Risk Engine
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

        private static void AddToSignalLog(SignalLogEntry entry)
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
