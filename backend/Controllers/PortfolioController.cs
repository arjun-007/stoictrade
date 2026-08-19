using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoicTrade.Api.Services;
using System.Text.Json;
using System.Threading.Tasks;

namespace StoicTrade.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Requires JWT
    public class PortfolioController : ControllerBase
    {
        private readonly FyersApiService _fyersApi;

        public PortfolioController(FyersApiService fyersApi)
        {
            _fyersApi = fyersApi;
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            var funds = await _fyersApi.GetFundsAsync();
            var positions = await _fyersApi.GetPositionsAsync();
            
            decimal availableMargin = 0;
            decimal totalPnL = 0;
            int activePositionsCount = 0;

            if (funds.ValueKind != JsonValueKind.Undefined && funds.TryGetProperty("fund_limit", out var fundLimitArray))
            {
                foreach (var fund in fundLimitArray.EnumerateArray())
                {
                    if (fund.TryGetProperty("title", out var titleProp) && titleProp.GetString() == "Available Balance")
                    {
                        availableMargin = fund.GetProperty("equityAmount").GetDecimal();
                        break;
                    }
                }
            }

            if (positions.ValueKind != JsonValueKind.Undefined && positions.TryGetProperty("netPositions", out var netPositionsArray))
            {
                foreach (var pos in netPositionsArray.EnumerateArray())
                {
                    decimal realized = pos.TryGetProperty("realized_profit", out var r) ? r.GetDecimal() : 0;
                    decimal unrealized = pos.TryGetProperty("unrealized_profit", out var ur) ? ur.GetDecimal() : 0;
                    totalPnL += (realized + unrealized);

                    int netQty = pos.TryGetProperty("netQty", out var q) ? q.GetInt32() : 0;
                    if (netQty != 0) activePositionsCount++;
                }
            }

            return Ok(new
            {
                AvailableMargin = availableMargin,
                DailyPnL = totalPnL,
                ActivePositionsCount = activePositionsCount
            });
        }

        [HttpGet("positions")]
        public async Task<IActionResult> GetPositions()
        {
            var positions = await _fyersApi.GetPositionsAsync();
            if (positions.ValueKind != JsonValueKind.Undefined)
            {
                return Ok(positions);
            }
            return NotFound(new { error = "Could not fetch positions from Fyers" });
        }

        [HttpGet("holdings")]
        public async Task<IActionResult> GetHoldings()
        {
            var holdings = await _fyersApi.GetHoldingsAsync();
            if (holdings.ValueKind != JsonValueKind.Undefined)
            {
                return Ok(holdings);
            }
            return NotFound(new { error = "Could not fetch holdings from Fyers" });
        }
    }
}
