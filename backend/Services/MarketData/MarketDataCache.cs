using System;
using System.Collections.Concurrent;

namespace StoicTrade.Api.Services.MarketData
{
    public class MarketDataCache
    {
        private readonly ConcurrentDictionary<string, SpotData> _spotData = new();
        private readonly ConcurrentDictionary<string, string> _optionChainData = new();
        // Individual option contract prices, keyed by canonical symbol e.g. "NIFTY26AUG23850CE"
        private readonly ConcurrentDictionary<string, decimal> _optionPrices = new();

        public void UpdateSpotData(string symbol, decimal price, DateTime timestamp)
        {
            _spotData[symbol] = new SpotData { Symbol = symbol, Price = price, Timestamp = timestamp };
        }

        public void UpdateOptionChainData(string symbol, string rawJson)
        {
            _optionChainData[symbol] = rawJson;
        }

        /// <summary>
        /// Stores the last-traded price for an individual option contract symbol.
        /// Symbol should be in canonical format, e.g. "NIFTY26AUG23850CE".
        /// </summary>
        public void UpdateOptionPrice(string symbol, decimal price)
        {
            _optionPrices[symbol] = price;
        }

        /// <summary>
        /// Gets the last-traded price for an individual option contract, or null if not cached.
        /// </summary>
        public decimal? GetOptionPrice(string symbol)
        {
            return _optionPrices.TryGetValue(symbol, out var price) ? price : null;
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
