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

        public async Task InitializeSymbolAsync(string symbol, int[] resolutionsInMinutes, decimal initialPrice = 24000m, int daysOfHistory = 5)
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
                if (candles == null || candles.Count < 25)
                {
                    candles = GenerateSeedCandles(initialPrice > 0 ? initialPrice : 24200m, res, 35);
                    _logger.LogInformation("Seeded {Count} initial candles for {Symbol} ({Resolution}m)", candles.Count, symbol, res);
                }

                _historicalCandles[symbol][res] = candles;
                
                _logger.LogInformation("Loaded {Count} historical candles for {Symbol} ({Resolution}m)", candles.Count, symbol, res);
            }
        }

        private static List<Candle> GenerateSeedCandles(decimal basePrice, int resolutionMinutes, int count)
        {
            var list = new List<Candle>();
            var now = DateTime.UtcNow;
            var rand = new Random(42);
            decimal currentPrice = basePrice > 0 ? basePrice : 24200m;

            for (int i = count; i >= 1; i--)
            {
                var candleTime = now.AddMinutes(-i * resolutionMinutes);
                decimal delta = (decimal)(rand.NextDouble() * 30.0 - 14.8);
                decimal open = currentPrice;
                decimal close = open + delta;
                decimal high = Math.Max(open, close) + (decimal)(rand.NextDouble() * 10.0);
                decimal low = Math.Min(open, close) - (decimal)(rand.NextDouble() * 10.0);
                decimal vol = rand.Next(1000, 15000);

                list.Add(new Candle
                {
                    Date = candleTime,
                    Open = Math.Round(open, 2),
                    High = Math.Round(high, 2),
                    Low = Math.Round(low, 2),
                    Close = Math.Round(close, 2),
                    Volume = vol
                });

                currentPrice = close;
            }
            return list;
        }

        private long _totalTicksReceived = 0;

        public void UpdateTick(string symbol, decimal price, decimal volume = 0, DateTime? timestamp = null)
        {
            var time = timestamp ?? DateTime.UtcNow;
            
            long currentTicks = System.Threading.Interlocked.Increment(ref _totalTicksReceived);

            if (!_historicalCandles.TryGetValue(symbol, out var resolutions))
                return;

            foreach (var kvp in resolutions)
            {
                var resMinutes = kvp.Key;
                var candles = kvp.Value;

                if (candles == null) continue;

                // Log tick count periodically or every time if debug is needed (we log every 100 ticks to avoid spam, or on specific conditions)
                if (currentTicks % 100 == 0 || currentTicks == 1)
                {
                    _logger.LogDebug("Ticks Received: {Count} | Last Spot: {Price} | Candle Count ({Res}m): {CandleCount}", 
                        currentTicks, price, resMinutes, candles.Count);
                }

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
