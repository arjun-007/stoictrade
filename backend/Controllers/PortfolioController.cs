using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        private readonly ILogger<PortfolioController> _logger;

        public PortfolioController(FyersApiService fyersApi, AppDbContext dbContext, MarketDataCache marketDataCache, ILogger<PortfolioController> logger)
        {
            _fyersApi = fyersApi;
            _dbContext = dbContext;
            _marketDataCache = marketDataCache;
            _logger = logger;
        }

        private static DateTime ToIst(DateTime date)
        {
            var ist = TimeZoneHelper.GetIstTimeZone();
            var utc = DateTime.SpecifyKind(date, DateTimeKind.Utc);
            return TimeZoneInfo.ConvertTimeFromUtc(utc, ist);
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
            var nowIst = ToIst(DateTime.UtcNow);
            var todayIst = nowIst.Date;

            // 0. Auto-heal any NULL values in SQLite to prevent EF Core null-at-ordinal crashes
            try
            {
                _dbContext.Database.ExecuteSqlRaw(@"
                    UPDATE PaperPositions SET PeakLtp = '0' WHERE PeakLtp IS NULL;
                    UPDATE PaperPositions SET SellAvg = '0' WHERE SellAvg IS NULL;
                    UPDATE PaperPositions SET BuyAvg = '0' WHERE BuyAvg IS NULL;
                    UPDATE PaperPositions SET RealizedProfit = '0' WHERE RealizedProfit IS NULL;
                    UPDATE PaperPositions SET TotalBuyValue = '0' WHERE TotalBuyValue IS NULL;
                    UPDATE PaperPositions SET TotalSellValue = '0' WHERE TotalSellValue IS NULL;
                ");
            }
            catch {}

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
                .Where(p => p.NetQty == 0 && ToIst(p.UpdatedAt).Date < todayIst)
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

            var paperPositions = _dbContext.PaperPositions
                .Where(p => !string.IsNullOrWhiteSpace(p.Symbol) && p.Symbol != "NIFTY")
                .OrderByDescending(p => p.CreatedAt)
                .ToList();

            var netPositions = new List<object>();
            decimal totalPnL = 0;
            int activePositionsCount = 0;

            foreach (var pos in paperPositions)
            {
                string canonicalSymbol = NormaliseOptionSymbol(pos.Symbol);
                string strategyName = pos.StrategyName ?? "Strategy";
                int netQty = pos.NetQty;
                var lastUpdatedIst = ToIst(pos.UpdatedAt).Date;

                // Only include:
                // a) Open / Carry-forward positions (netQty != 0)
                // b) Current day's closed trades (netQty == 0 && lastUpdatedIst == todayIst)
                if (netQty == 0 && lastUpdatedIst < todayIst)
                {
                    continue; // Exclude previous days' closed trades
                }

                // Priority: individual option price cache → spot data → last trade avg
                decimal? cachedLtp = _marketDataCache.GetOptionPrice(canonicalSymbol);

                decimal buyAvg = (pos.BuyAvg ?? 0m) > 0 && (pos.BuyAvg ?? 0m) < 5000 
                    ? pos.BuyAvg.Value 
                    : ((pos.BuyAvg ?? 0m) > 0 ? (cachedLtp ?? pos.BuyAvg.Value) : 0m);

                decimal sellAvg = (pos.SellAvg ?? 0m) > 0 && (pos.SellAvg ?? 0m) < 5000 
                    ? pos.SellAvg.Value 
                    : (cachedLtp ?? 0m);

                // If sellAvg or buyAvg was stored as spot price (> 5000), fix it using option LTP
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
                    ?? (netQty > 0 ? buyAvg : (sellAvg > 0 ? sellAvg : buyAvg));

                int tradeQty = pos.TotalBuyQty > 0 
                    ? pos.TotalBuyQty 
                    : (pos.TotalSellQty > 0 ? pos.TotalSellQty : Math.Abs(netQty));

                decimal realizedProfit = pos.RealizedProfit ?? 0m;
                // Fallback: If position is closed with 0 realized profit recorded, calculate from sellAvg - buyAvg
                if (netQty == 0 && realizedProfit == 0m && buyAvg > 0 && sellAvg > 0 && tradeQty > 0)
                {
                    realizedProfit = (sellAvg - buyAvg) * tradeQty;
                }

                decimal unrealized = 0m;
                if (netQty > 0) unrealized = (ltp - buyAvg) * netQty;
                else if (netQty < 0) unrealized = (sellAvg - ltp) * Math.Abs(netQty);

                decimal targetPrice = pos.TargetPrice.HasValue && pos.TargetPrice > 0 
                    ? pos.TargetPrice.Value 
                    : (buyAvg > 0 ? Math.Round(buyAvg * 1.25m, 2) : 0m);

                decimal stopLossPrice = pos.StopLossPrice.HasValue && pos.StopLossPrice > 0 
                    ? pos.StopLossPrice.Value 
                    : (buyAvg > 0 ? Math.Round(Math.Max(5.0m, buyAvg * 0.85m), 2) : 0m);

                decimal trailingStopLossPoint = pos.TrailingStopLossPoint.HasValue && pos.TrailingStopLossPoint > 0 
                    ? pos.TrailingStopLossPoint.Value 
                    : 8.0m;

                decimal peakLtp = pos.PeakLtp ?? 0m;

                netPositions.Add(new {
                    symbol = canonicalSymbol,
                    netQty = netQty,
                    qty = tradeQty,
                    tradeQty = tradeQty,
                    buyAvg = buyAvg,
                    sellAvg = sellAvg,
                    ltp = ltp,
                    targetPrice = targetPrice,
                    stopLossPrice = stopLossPrice,
                    trailingStopLossPoint = trailingStopLossPoint,
                    peakLtp = peakLtp,
                    strategyName = strategyName,
                    realized_profit = realizedProfit,
                    realizedProfit = realizedProfit,
                    unrealized_profit = unrealized,
                    unrealizedProfit = unrealized,
                    pl = realizedProfit + unrealized,
                    slNo = 1,
                    id = pos.Id,
                    createdAt = pos.CreatedAt,
                    updatedAt = pos.UpdatedAt,
                    isCarryForward = netQty != 0 && ToIst(pos.CreatedAt).Date < todayIst
                });

                totalPnL += (realizedProfit + unrealized);
                if (netQty != 0) activePositionsCount++;
            }

            return (totalPnL, activePositionsCount, netPositions);
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            try
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get portfolio summary");
                return Ok(new
                {
                    AvailableMargin = 1000000.00m,
                    DailyPnL = 0m,
                    ActivePositionsCount = 0,
                    error = ex.Message
                });
            }
        }

        [HttpGet("positions")]
        public async Task<IActionResult> GetPositions()
        {
            try
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
                return Ok(new { netPositions = new List<object>() });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get positions");
                return Ok(new { netPositions = new List<object>(), error = ex.Message });
            }
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
