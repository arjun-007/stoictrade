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
        /// Replaces hard-coded strategy logic.
        /// </summary>
        public string? GetOptimalContract(string underlyingSymbol, string bias, int itmDistance = 0)
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
                    
                    // Simple ITM logic
                    int targetStrike = atmStrike;
                    string optionType = bias == "BULLISH" ? "CE" : "PE";

                    if (optionType == "CE")
                    {
                        targetStrike -= (itmDistance * 50); // Lower strike for ITM CE
                    }
                    else
                    {
                        targetStrike += (itmDistance * 50); // Higher strike for ITM PE
                    }

                    // Grab the expiry date from the first record
                    var firstRecord = dataArray.EnumerateArray().FirstOrDefault();
                    if (firstRecord.ValueKind != JsonValueKind.Undefined && firstRecord.TryGetProperty("expiryDate", out var expiryEl))
                    {
                        string expiry = expiryEl.GetString() ?? "";
                        
                        // Format: NSE:NIFTY26AUG22000CE
                        return $"NSE:{underlyingSymbol}{expiry}{targetStrike}{optionType}";
                    }
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
            // Dummy logic for now, in reality you'd search the JSON for the exact symbol string
            return _cache.GetSpotData("NIFTY")?.Price; 
        }
    }
}
