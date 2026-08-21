using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoicTrade.Api.Services;
using StoicTrade.Api.Data;
using StoicTrade.Api.Services.MarketData;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System;

namespace StoicTrade.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Requires JWT
    public class PortfolioController : ControllerBase
    {
        private readonly FyersApiService _fyersApi;
        private readonly AppDbContext _dbContext;
        private readonly MarketDataCache _marketDataCache;

        public PortfolioController(FyersApiService fyersApi, AppDbContext dbContext, MarketDataCache marketDataCache)
        {
            _fyersApi = fyersApi;
            _dbContext = dbContext;
            _marketDataCache = marketDataCache;
        }

        private bool IsPaperMode()
        {
            var globalSettings = _dbContext.GlobalSettings.FirstOrDefault();
            return globalSettings != null && globalSettings.TradeMode == "Paper";
        }

        private (decimal totalPnL, int activeCount, List<object> mockNetPositions) GetMockPaperData()
        {
            var paperPositions = _dbContext.PaperPositions.ToList();
            var netPositions = new List<object>();
            decimal totalPnL = 0;
            int activePositionsCount = 0;

            foreach (var pos in paperPositions)
            {
                // Priority: individual option price cache → spot data → last trade avg
                decimal ltp = _marketDataCache.GetOptionPrice(pos.Symbol)
                    ?? _marketDataCache.GetSpotData(pos.Symbol)?.Price
                    ?? (pos.NetQty > 0 ? pos.BuyAvg : pos.SellAvg);
                decimal unrealized = 0;
                
                if (pos.NetQty > 0) unrealized = (ltp - pos.BuyAvg) * pos.NetQty;
                else if (pos.NetQty < 0) unrealized = (pos.SellAvg - ltp) * Math.Abs(pos.NetQty);

                netPositions.Add(new {
                    symbol = pos.Symbol,
                    netQty = pos.NetQty,
                    buyAvg = pos.BuyAvg,
                    sellAvg = pos.SellAvg,
                    ltp = ltp,
                    realized_profit = pos.RealizedProfit,
                    unrealized_profit = unrealized,
                    pl = pos.RealizedProfit + unrealized,
                    slNo = 1,
                    id = pos.Id
                });

                totalPnL += (pos.RealizedProfit + unrealized);
                if (pos.NetQty != 0) activePositionsCount++;
            }

            return (totalPnL, activePositionsCount, netPositions);
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            if (IsPaperMode())
            {
                var mockData = GetMockPaperData();
                return Ok(new
                {
                    AvailableMargin = 1000000.00m, // Dummy fixed paper margin
                    DailyPnL = mockData.totalPnL,
                    ActivePositionsCount = mockData.activeCount
                });
            }

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
            if (IsPaperMode())
            {
                var mockData = GetMockPaperData();
                return Ok(new { netPositions = mockData.mockNetPositions });
            }

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
            if (IsPaperMode())
            {
                return Ok(new { holdings = new List<object>() }); // Return empty for paper holdings
            }

            var holdings = await _fyersApi.GetHoldingsAsync();
            if (holdings.ValueKind != JsonValueKind.Undefined)
            {
                return Ok(holdings);
            }
            return NotFound(new { error = "Could not fetch holdings from Fyers" });
        }
    }
}
