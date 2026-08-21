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

        public void UpdateSpotData(string symbol, decimal price, DateTime timestamp, decimal prevClose = 0, decimal change = 0, decimal changePercent = 0)
        {
            decimal ch = change != 0 ? change : (prevClose > 0 ? price - prevClose : 0);
            decimal chp = changePercent != 0 ? changePercent : (prevClose > 0 ? ((price - prevClose) / prevClose) * 100m : 0);
            _spotData[symbol] = new SpotData
            {
                Symbol = symbol,
                Price = price,
                PrevClose = prevClose,
                Change = Math.Round(ch, 2),
                ChangePercent = Math.Round(chp, 2),
                Timestamp = timestamp
            };
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
        public decimal PrevClose { get; set; }
        public decimal Change { get; set; }
        public decimal ChangePercent { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
