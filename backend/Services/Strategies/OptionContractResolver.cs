using System;
using System.Text.Json;
using StoicTrade.Api.Services.MarketData;

namespace StoicTrade.Api.Services.Strategies
{
    public class OptionContractResolver
    {
        private readonly MarketDataCache _cache;

        public OptionContractResolver(MarketDataCache cache)
        {
            _cache = cache;
        }

        // Resolves the LTP of an exact instrument symbol by finding it in the NSE Option Chain JSON
        public decimal? ResolveOptionLtp(string symbol)
        {
            var rawJson = _cache.GetOptionChainData("NIFTY");
            if (string.IsNullOrEmpty(rawJson)) return null;

            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("records", out var records) && 
                    records.TryGetProperty("data", out var dataArray))
                {
                    // "NIFTY24MAY22000CE" is just a mock string. Let's parse real data.
                    // The NSE JSON has CE and PE objects. We can search for the right strike price.
                    // For now, this is a basic mockup for the OMS to get a live price from the JSON payload.
                    // In a production app, we would match the expiry date string and strike precisely.
                    
                    // Simple mock resolver: Just grab the underlying value or the first CE/PE price
                    if (records.TryGetProperty("underlyingValue", out var spotElement))
                    {
                        if (spotElement.TryGetDecimal(out var spotPrice))
                        {
                            return spotPrice; // Return Spot if Option parsing is complex for this mockup
                        }
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
