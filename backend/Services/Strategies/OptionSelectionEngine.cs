using System;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StoicTrade.Api.Services.MarketData;

namespace StoicTrade.Api.Services.Strategies
{
    public class OptionSelectionEngine
    {
        private readonly MarketDataCache _cache;
        private readonly ILogger<OptionSelectionEngine> _logger;

        public OptionSelectionEngine(MarketDataCache cache, ILogger<OptionSelectionEngine> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        /// <summary>
        /// Selects the optimal option contract based on underlying bias (BULLISH/BEARISH).
        /// Supports target expiry index (e.g. 0 = current week, 1 = 2nd expiry week) and ITM distance (e.g. 1 = 1 strike ITM).
        /// </summary>
        public string? GetOptimalContract(string underlyingSymbol, string bias, int itmDistance = 1, int expiryIndex = 1)
        {
            var rawJson = _cache.GetOptionChainData(underlyingSymbol);
            var spotData = _cache.GetSpotData(underlyingSymbol);

            if (string.IsNullOrEmpty(rawJson) || spotData == null)
            {
                _logger.LogWarning("OptionSelectionEngine: Missing market data for {Underlying}", underlyingSymbol);
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("records", out var records) && 
                    records.TryGetProperty("data", out var dataArray))
                {
                    decimal spotPrice = spotData.Price;
                    int atmStrike = (int)Math.Round(spotPrice / 50.0m) * 50;
                    
                    // ITM logic:
                    // CE (BULLISH): Strike is lower than ATM (In-The-Money)
                    // PE (BEARISH): Strike is higher than ATM (In-The-Money)
                    string optionType = bias == "BULLISH" ? "CE" : "PE";
                    int targetStrike = optionType == "CE" 
                        ? atmStrike - (itmDistance * 50) 
                        : atmStrike + (itmDistance * 50);

                    // Collect all distinct expiries
                    var distinctExpiries = dataArray.EnumerateArray()
                        .Select(x => x.TryGetProperty("expiryDate", out var e) ? e.GetString() : null)
                        .Where(x => !string.IsNullOrEmpty(x))
                        .Distinct()
                        .ToList();

                    if (!distinctExpiries.Any()) return null;

                    // Select the requested expiry (e.g. expiryIndex = 1 for 2nd expiry / next week)
                    string chosenExpiry = (expiryIndex >= 0 && expiryIndex < distinctExpiries.Count)
                        ? (distinctExpiries[expiryIndex] ?? distinctExpiries.First() ?? "")
                        : (distinctExpiries.First() ?? "");

                    return $"NSE:{underlyingSymbol}{chosenExpiry}{targetStrike}{optionType}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error selecting optimal option contract");
            }

            return null;
        }

        public decimal? ResolveOptionLtp(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return null;
            string canonical = symbol.Trim();
            if (canonical.StartsWith("NSE:", StringComparison.OrdinalIgnoreCase)) canonical = canonical.Substring(4);
            if (canonical.StartsWith("NIFTYNIFTY", StringComparison.OrdinalIgnoreCase)) canonical = canonical.Substring(5);
            canonical = canonical.Replace(" ", "");

            return _cache.GetOptionPrice(canonical)
                ?? _cache.GetOptionPrice(symbol)
                ?? _cache.GetSpotData(canonical)?.Price;
        }
    }
}
