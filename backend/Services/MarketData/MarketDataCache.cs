using System;
using System.Collections.Concurrent;

namespace StoicTrade.Api.Services.MarketData
{
    public class MarketDataCache
    {
        private readonly ConcurrentDictionary<string, SpotData> _spotData = new();
        private readonly ConcurrentDictionary<string, string> _optionChainData = new();

        public void UpdateSpotData(string symbol, decimal price, DateTime timestamp)
        {
            _spotData[symbol] = new SpotData { Symbol = symbol, Price = price, Timestamp = timestamp };
        }

        public void UpdateOptionChainData(string symbol, string rawJson)
        {
            _optionChainData[symbol] = rawJson;
        }

        public SpotData? GetSpotData(string symbol)
        {
            return _spotData.TryGetValue(symbol, out var data) ? data : null;
        }

        public string? GetOptionChainData(string symbol)
        {
            return _optionChainData.TryGetValue(symbol, out var data) ? data : null;
        }
    }

    public class SpotData
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
