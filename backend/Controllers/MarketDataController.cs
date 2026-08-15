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
            if (data == null) return NotFound(new { Message = "Spot data not available yet." });
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
    }
}
