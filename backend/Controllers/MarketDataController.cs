using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoicTrade.Api.Services.MarketData;

namespace StoicTrade.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Requires JWT
    public class MarketDataController : ControllerBase
    {
        private readonly MarketDataCache _cache;

        public MarketDataController(MarketDataCache cache)
        {
            _cache = cache;
        }

        [HttpGet("spot")]
        public IActionResult GetSpotData([FromQuery] string symbol = "NIFTY")
        {
            var data = _cache.GetSpotData(symbol);
            if (data == null) 
            {
                return Ok(new SpotData { Symbol = symbol, Price = 0, Change = 0, ChangePercent = 0, Timestamp = DateTime.UtcNow });
            }
            return Ok(data);
        }

        [HttpGet("options")]
        public IActionResult GetOptionChain([FromQuery] string symbol = "NIFTY")
        {
            var rawJson = _cache.GetOptionChainData(symbol);
            if (rawJson == null) return NotFound(new { Message = "Option chain data not available yet." });

            // We can return the raw JSON directly as a Content result since it's already a JSON string
            return Content(rawJson, "application/json");
        }

        [HttpGet("all")]
        public IActionResult GetAllMarketData()
        {
            var rawJson = _cache.GetOptionChainData("NIFTY");
            object? optionsData = null;
            if (!string.IsNullOrEmpty(rawJson))
            {
                try
                {
                    optionsData = System.Text.Json.JsonDocument.Parse(rawJson).RootElement;
                }
                catch { /* Ignore parse error */ }
            }

            var spots = new System.Collections.Generic.Dictionary<string, object>();
            
            // Common symbols we might be tracking
            var symbolsToCheck = new[] { "NIFTY", "HDFCBANK-EQ", "RELIANCE-EQ", "SBIN-EQ", "ICICIBANK-EQ" };
            foreach (var sym in symbolsToCheck)
            {
                var spot = _cache.GetSpotData(sym);
                if (spot != null)
                {
                    spots[sym] = new {
                        lastPrice = spot.Price,
                        prevClose = spot.PrevClose,
                        change = spot.Change,
                        changePercent = spot.ChangePercent,
                        timestamp = spot.Timestamp
                    };
                }
            }

            return Ok(new {
                options = optionsData,
                spots = spots
            });
        }
    }
}
