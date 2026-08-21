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

        // Static log of the last 100 signals — accessible via EngineController
        public static readonly ConcurrentQueue<SignalLogEntry> RecentSignals = new();
        private const int MaxLogSize = 100;

        public StrategyEngineService(
            ILogger<StrategyEngineService> logger,
            IServiceProvider serviceProvider,
            IEnumerable<IStrategy> strategies)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _strategies = strategies;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Strategy Engine is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    // 1. Fetch enabled strategies from DB
                    var activeConfigs = dbContext.StrategyConfigs.Where(s => s.IsEnabled).ToList();

                    // 2. Fetch market data based on TradeMode
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
                                // If signal is for NIFTY underlying, select the optimal ATM option contract
                                if (signal.Instrument == "NIFTY")
                                {
                                    string bias = signal.Action == "BUY" ? "BULLISH" : "BEARISH";
                                    var contract = optionEngine.GetOptimalContract("NIFTY", bias, 0);
                                    if (!string.IsNullOrEmpty(contract))
                                    {
                                        string optSymbol = contract.Replace("NSE:", "");
                                        signal.Instrument = optSymbol;
                                        signal.Price = optionEngine.ResolveOptionLtp(optSymbol) ?? signal.Price;
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
