using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StoicTrade.Api.Models;

namespace StoicTrade.Api.Services.MarketData
{
    public class MarketDataAggregatorService
    {
        private readonly ILogger<MarketDataAggregatorService> _logger;
        private readonly FyersApiService _fyersApi;
        
        // Dictionary mapping Symbol -> Resolution(int minutes) -> List of Candles
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, List<Candle>>> _historicalCandles = new();

        public MarketDataAggregatorService(ILogger<MarketDataAggregatorService> logger, FyersApiService fyersApi)
        {
            _logger = logger;
            _fyersApi = fyersApi;
        }

        public async Task InitializeSymbolAsync(string symbol, int[] resolutionsInMinutes, int daysOfHistory = 5)
        {
            var now = DateTime.UtcNow;
            var from = now.AddDays(-daysOfHistory);

            if (!_historicalCandles.ContainsKey(symbol))
            {
                _historicalCandles[symbol] = new ConcurrentDictionary<int, List<Candle>>();
            }

            foreach (var res in resolutionsInMinutes)
            {
                var resolutionStr = res == 1440 ? "1D" : res.ToString();
                _logger.LogInformation("Fetching historical data for {Symbol} at resolution {Resolution}", symbol, resolutionStr);
                
                var candles = await _fyersApi.GetHistoricalCandlesAsync(symbol, resolutionStr, from, now);
                _historicalCandles[symbol][res] = candles;
                
                _logger.LogInformation("Loaded {Count} historical candles for {Symbol} ({Resolution}m)", candles.Count, symbol, res);
            }
        }

        public void UpdateTick(string symbol, decimal price, decimal volume = 0, DateTime? timestamp = null)
        {
            var time = timestamp ?? DateTime.UtcNow;

            if (!_historicalCandles.TryGetValue(symbol, out var resolutions))
                return;

            foreach (var kvp in resolutions)
            {
                var resMinutes = kvp.Key;
                var candles = kvp.Value;

                if (candles == null) continue;

                // Determine the candle start time for this resolution
                // e.g., if time is 10:12:45 and res is 5m, start time is 10:10:00
                long ticks = time.Ticks;
                long resTicks = TimeSpan.FromMinutes(resMinutes).Ticks;
                var candleStartTime = new DateTime(ticks - (ticks % resTicks), time.Kind);

                lock (candles) // Ensure thread safety while modifying the list
                {
                    var lastCandle = candles.LastOrDefault();

                    if (lastCandle == null || lastCandle.Date < candleStartTime)
                    {
                        // Open new candle
                        candles.Add(new Candle
                        {
                            Date = candleStartTime,
                            Open = price,
                            High = price,
                            Low = price,
                            Close = price,
                            Volume = volume
                        });
                        
                        // Optional: Keep list size bounded (e.g. max 1000 candles) to avoid memory leak
                        if (candles.Count > 1500)
                        {
                            candles.RemoveRange(0, 500);
                        }
                    }
                    else if (lastCandle.Date == candleStartTime)
                    {
                        // Update existing candle
                        lastCandle.Close = price;
                        if (price > lastCandle.High) lastCandle.High = price;
                        if (price < lastCandle.Low) lastCandle.Low = price;
                        lastCandle.Volume += volume; // aggregate volume
                    }
                }
            }
        }

        public List<Candle> GetCandles(string symbol, int resolutionInMinutes)
        {
            if (_historicalCandles.TryGetValue(symbol, out var resolutions))
            {
                if (resolutions.TryGetValue(resolutionInMinutes, out var candles))
                {
                    lock (candles)
                    {
                        return candles.ToList(); // Return a copy to avoid enumeration exceptions
                    }
                }
            }
            return new List<Candle>();
        }
    }
}
