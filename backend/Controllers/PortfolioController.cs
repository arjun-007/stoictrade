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

        /// <summary>
        /// Resolves a canonical option symbol key from the stored PaperPosition symbol.
        /// Handles cases where old positions stored double-NIFTY (e.g. "NIFTYNIFTY26AUG24000CE")
        /// or NSE: prefix variants or spaces.
        /// </summary>
        private static string NormaliseOptionSymbol(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string s = raw.Trim();
            if (s.StartsWith("NSE:", StringComparison.OrdinalIgnoreCase)) s = s.Substring(4);
            // Collapse accidental double-NIFTY prefix: "NIFTYNIFTY..." → "NIFTY..."
            if (s.StartsWith("NIFTYNIFTY", StringComparison.OrdinalIgnoreCase)) s = s.Substring(5);
            s = s.Replace(" ", "");
            return s;
        }

        private (decimal totalPnL, int activeCount, List<object> mockNetPositions) GetMockPaperData()
        {
            var ist = TimeZoneHelper.GetIstTimeZone();
            var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ist);
            var todayIst = nowIst.Date;

            // 1. Purge invalid test symbols
            var invalidPositions = _dbContext.PaperPositions
                .Where(p => p.Symbol == "NIFTY" || (p.Symbol.StartsWith("NIFTY") && (p.Symbol.Contains("1300") || p.Symbol.Contains("1250") || p.Symbol.Contains("1350"))))
                .ToList();
            if (invalidPositions.Any())
            {
                _dbContext.PaperPositions.RemoveRange(invalidPositions);
                try { _dbContext.SaveChanges(); } catch {}
            }

            // 2. Purge past days' closed trades (NetQty == 0 and Updated before today IST) to keep DB healthy and lightweight
            var staleClosedPositions = _dbContext.PaperPositions
                .AsEnumerable()
                .Where(p => p.NetQty == 0 && TimeZoneInfo.ConvertTimeFromUtc(p.UpdatedAt, ist).Date < todayIst)
                .ToList();

            if (staleClosedPositions.Any())
            {
                _dbContext.PaperPositions.RemoveRange(staleClosedPositions);
                try { _dbContext.SaveChanges(); } catch {}
            }

            // 3. Purge old TradeLogs older than 7 days
            var staleTradeLogs = _dbContext.TradeLogs
                .Where(t => t.Timestamp < DateTime.UtcNow.AddDays(-7))
                .ToList();
            if (staleTradeLogs.Any())
            {
                _dbContext.TradeLogs.RemoveRange(staleTradeLogs);
                try { _dbContext.SaveChanges(); } catch {}
            }

            var paperPositions = _dbContext.PaperPositions.ToList();
            var netPositions = new List<object>();
            decimal totalPnL = 0;
            int activePositionsCount = 0;

            var grouped = paperPositions
                .Where(p => !string.IsNullOrWhiteSpace(p.Symbol) && p.Symbol != "NIFTY")
                .GroupBy(p => NormaliseOptionSymbol(p.Symbol));

            foreach (var group in grouped)
            {
                string canonicalSymbol = group.Key;
                int netQty = group.Sum(p => p.NetQty);
                decimal realizedProfit = group.Sum(p => p.RealizedProfit);
                var lastUpdatedIst = TimeZoneInfo.ConvertTimeFromUtc(group.Max(p => p.UpdatedAt), ist).Date;

                // Only include:
                // a) Open / Carry-forward positions (netQty != 0)
                // b) Current day's closed trades (netQty == 0 && lastUpdatedIst == todayIst)
                if (netQty == 0 && lastUpdatedIst < todayIst)
                {
                    continue; // Exclude previous days' closed trades
                }

                // Priority: individual option price cache → spot data → last trade avg
                decimal? cachedLtp = _marketDataCache.GetOptionPrice(canonicalSymbol);
                
                decimal buyAvg = group.FirstOrDefault(p => p.BuyAvg > 0 && p.BuyAvg < 5000)?.BuyAvg 
                    ?? group.FirstOrDefault(p => p.BuyAvg > 0)?.BuyAvg 
                    ?? 0m;
                    
                decimal sellAvg = group.FirstOrDefault(p => p.SellAvg > 0 && p.SellAvg < 5000)?.SellAvg 
                    ?? (cachedLtp.HasValue ? cachedLtp.Value : 0m);

                // If sellAvg was stored as spot price (> 5000), fix it using option LTP
                if (sellAvg > 5000 && cachedLtp.HasValue)
                {
                    sellAvg = cachedLtp.Value;
                }
                if (buyAvg > 5000 && cachedLtp.HasValue)
                {
                    buyAvg = cachedLtp.Value;
                }

                decimal ltp = cachedLtp
                    ?? _marketDataCache.GetSpotData(canonicalSymbol)?.Price
                    ?? (netQty > 0 ? buyAvg : sellAvg);

                decimal unrealized = 0;
                if (netQty > 0) unrealized = (ltp - buyAvg) * netQty;
                else if (netQty < 0) unrealized = (sellAvg - ltp) * Math.Abs(netQty);

                netPositions.Add(new {
                    symbol = canonicalSymbol,
                    netQty = netQty,
                    buyAvg = buyAvg,
                    sellAvg = sellAvg,
                    ltp = ltp,
                    realized_profit = realizedProfit,
                    unrealized_profit = unrealized,
                    pl = realizedProfit + unrealized,
                    slNo = 1,
                    id = group.First().Id,
                    isCarryForward = netQty != 0 && TimeZoneInfo.ConvertTimeFromUtc(group.Min(p => p.CreatedAt), ist).Date < todayIst
                });

                totalPnL += (realizedProfit + unrealized);
                if (netQty != 0) activePositionsCount++;
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

        [HttpPost("reset-paper")]
        public async Task<IActionResult> ResetPaperPositions()
        {
            var allPositions = await _dbContext.PaperPositions.ToListAsync();
            _dbContext.PaperPositions.RemoveRange(allPositions);

            var allLogs = await _dbContext.TradeLogs.ToListAsync();
            _dbContext.TradeLogs.RemoveRange(allLogs);

            await _dbContext.SaveChangesAsync();
            return Ok(new { Message = "All paper positions and trade history have been reset." });
        }
    }
}
