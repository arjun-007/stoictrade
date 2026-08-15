using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StoicTrade.Api.Data;

namespace StoicTrade.Api.Services.Strategies
{
    public class StrategyEngineService : BackgroundService
    {
        private readonly ILogger<StrategyEngineService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IEnumerable<IStrategy> _strategies;

        public StrategyEngineService(ILogger<StrategyEngineService> logger, IServiceProvider serviceProvider, IEnumerable<IStrategy> strategies)
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

                    if (globalSettings != null && globalSettings.TradeMode == "PaperTrading")
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
                    foreach (var config in activeConfigs)
                    {
                        var strategy = _strategies.FirstOrDefault(s => s.Name == config.StrategyName);
                        if (strategy != null)
                        {
                            await strategy.ExecuteAsync(config, marketDataJson);
                        }
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
    }
}
