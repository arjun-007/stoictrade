using System;
using System.Collections.Concurrent;

namespace StoicTrade.Api.Services.MarketData
{
    public class OptionQuoteEntry
    {
        public decimal Price { get; set; }
        public DateTime TimestampUtc { get; set; }
        public bool TradedToday { get; set; }
    }

    public class MarketDataCache
    {
        private readonly ConcurrentDictionary<string, SpotData> _spotData = new();
        private readonly ConcurrentDictionary<string, string> _optionChainData = new();
        private readonly ConcurrentDictionary<string, DateTime> _optionChainUpdatedAt = new();
        // Individual option contract prices, keyed by canonical symbol e.g. "NIFTY26AUG23850CE"
        private readonly ConcurrentDictionary<string, OptionQuoteEntry> _optionPrices = new();

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
            _optionChainUpdatedAt[symbol] = DateTime.UtcNow;
        }

        public DateTime? GetOptionChainUpdatedAt(string symbol)
        {
            return _optionChainUpdatedAt.TryGetValue(symbol, out var dt) ? dt : null;
        }

        /// <summary>
        /// Stores the last-traded price for an individual option contract symbol.
        /// Symbol should be in canonical format, e.g. "NIFTY26AUG23850CE".
        /// </summary>
        public void UpdateOptionPrice(string symbol, decimal price, bool tradedToday = true)
        {
            _optionPrices[symbol] = new OptionQuoteEntry
            {
                Price = price,
                TimestampUtc = DateTime.UtcNow,
                TradedToday = tradedToday
            };
        }

        /// <summary>
        /// Gets the last-traded price for an individual option contract, or null if not cached.
        /// When requireLiveTodayQuote is true (used during market hours for trade execution & SL/Target monitoring),
        /// rejects any quote fetched before today's 09:15:00 AM IST market open or where the contract hasn't traded today yet (yesterday's close).
        /// </summary>
        public decimal? GetOptionPrice(string symbol, bool requireLiveTodayQuote = false)
        {
            if (!_optionPrices.TryGetValue(symbol, out var entry))
                return null;

            if (requireLiveTodayQuote)
            {
                if (!entry.TradedToday)
                    return null;

                var ist = TimeZoneHelper.GetIstTimeZone();
                var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ist);
                var quoteIst = TimeZoneInfo.ConvertTimeFromUtc(entry.TimestampUtc, ist);
                var marketOpenTodayIst = nowIst.Date.Add(new TimeSpan(9, 15, 0));

                // During market hours, reject any quote cached before 09:15:00 AM IST today
                if (nowIst.TimeOfDay >= new TimeSpan(9, 15, 0) && quoteIst < marketOpenTodayIst)
                    return null;
            }

            return entry.Price;
        }

        /// <summary>
        /// Clears all cached option prices and option chain snapshots (used at 09:15 AM IST market open
        /// to flush any pre-market / previous trading day closing prices).
        /// </summary>
        public void ClearOptionPrices()
        {
            _optionPrices.Clear();
            _optionChainData.Clear();
            _optionChainUpdatedAt.Clear();
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
