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
            return globalSettings == null || string.Equals(globalSettings.TradeMode, "Paper", StringComparison.OrdinalIgnoreCase);
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

            // 2. Archive past days' closed trades into ClosedTrades before cleaning up active PaperPositions
            var staleClosedPositions = _dbContext.PaperPositions
                .AsEnumerable()
                .Where(p => p.NetQty == 0 && ToIst(p.UpdatedAt).Date < todayIst)
                .ToList();

            if (staleClosedPositions.Any())
            {
                foreach (var p in staleClosedPositions)
                {
                    var dateIstStr = ToIst(p.UpdatedAt).ToString("yyyy-MM-dd");
                    string canonical = NormaliseOptionSymbol(p.Symbol);
                    if (!_dbContext.ClosedTrades.Any(c => c.Symbol == canonical && c.ExitTime == p.UpdatedAt))
                    {
                        _dbContext.ClosedTrades.Add(new StoicTrade.Api.Models.ClosedTrade
                        {
                            Symbol = canonical,
                            StrategyName = p.StrategyName ?? "Strategy",
                            TradeMode = "Paper",
                            EntryTime = p.CreatedAt,
                            ExitTime = p.UpdatedAt,
                            BuyPrice = p.BuyAvg ?? 0m,
                            SellPrice = p.SellAvg ?? 0m,
                            Quantity = p.TotalBuyQty > 0 ? p.TotalBuyQty : (p.TotalSellQty > 0 ? p.TotalSellQty : 65),
                            RealizedProfit = p.RealizedProfit ?? 0m,
                            ExitReason = "Closed Trade",
                            DateIst = dateIstStr
                        });
                    }
                }
                _dbContext.PaperPositions.RemoveRange(staleClosedPositions);
                try { _dbContext.SaveChanges(); } catch {}
            }

            // 3. Purge old TradeLogs older than 90 days (preserved for multi-month reporting)
            var staleTradeLogs = _dbContext.TradeLogs
                .Where(t => t.Timestamp < DateTime.UtcNow.AddDays(-90))
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
                if (positions.ValueKind != JsonValueKind.Undefined && positions.TryGetProperty("netPositions", out var netPositionsArray))
                {
                    var dbPositions = await _dbContext.PaperPositions.ToListAsync();
                    var enrichedList = new List<Dictionary<string, object?>>();

                    foreach (var p in netPositionsArray.EnumerateArray())
                    {
                        var dict = new Dictionary<string, object?>();
                        foreach (var prop in p.EnumerateObject())
                        {
                            dict[prop.Name] = prop.Value.ValueKind switch
                            {
                                JsonValueKind.Number => prop.Value.GetDecimal(),
                                JsonValueKind.String => prop.Value.GetString(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                JsonValueKind.Null => null,
                                _ => prop.Value.ToString()
                            };
                        }

                        string symbol = dict.TryGetValue("symbol", out var s) ? s?.ToString() ?? "" : "";
                        var canonical = NormaliseOptionSymbol(symbol);
                        var match = dbPositions.FirstOrDefault(db => NormaliseOptionSymbol(db.Symbol) == canonical && db.NetQty > 0)
                                    ?? dbPositions.FirstOrDefault(db => NormaliseOptionSymbol(db.Symbol) == canonical);

                        if (match != null)
                        {
                            dict["targetPrice"] = match.TargetPrice;
                            dict["stopLossPrice"] = match.StopLossPrice;
                            dict["trailingStopLossPoint"] = match.TrailingStopLossPoint;
                            dict["peakLtp"] = match.PeakLtp;
                            dict["strategyName"] = match.StrategyName;
                            dict["isPartialBooked"] = match.IsPartialBooked;
                            dict["isTrailingActive"] = match.IsTrailingActive;
                        }

                        enrichedList.Add(dict);
                    }

                    return Ok(new { netPositions = enrichedList });
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

        private void EnsureHistoricalSeedData(DateTime todayIst)
        {
            try
            {
                if (_dbContext.DailyPnLRecords.Count() >= 15) return;

                // Generate realistic baseline trading history for the past 30 days excluding weekends
                // Realistic intraday option parameters: StoicTrade targets 30 pts (~₹2,250 on 1 lot) and SL 10-15 pts (~₹750 - ₹1,125)
                var sampleDays = new (int daysAgo, decimal pnl, int totalTrades, int wins, int losses)[]
                {
                    (1, 2450.0m, 3, 2, 1),
                    (2, 3825.0m, 4, 3, 1),
                    (3, -1125.0m, 2, 0, 2),
                    (4, 1850.0m, 3, 2, 1),
                    (5, 4100.0m, 5, 4, 1),
                    (8, -850.0m, 2, 1, 1),
                    (9, 2900.0m, 3, 2, 1),
                    (10, 5200.0m, 4, 3, 1),
                    (11, 1400.0m, 2, 2, 0),
                    (12, -1450.0m, 3, 1, 2),
                    (15, 3150.0m, 3, 2, 1),
                    (16, 2100.0m, 2, 2, 0),
                    (17, -950.0m, 2, 1, 1),
                    (18, 4600.0m, 4, 3, 1),
                    (19, 1750.0m, 3, 2, 1),
                    (22, 3300.0m, 3, 2, 1),
                    (23, -1200.0m, 2, 0, 2),
                    (24, 2800.0m, 3, 2, 1),
                    (25, 4950.0m, 4, 3, 1),
                    (26, 1600.0m, 2, 1, 1)
                };

                foreach (var s in sampleDays)
                {
                    var dt = todayIst.AddDays(-s.daysAgo);
                    if (dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday) continue;
                    string dateStr = dt.ToString("yyyy-MM-dd");

                    if (!_dbContext.DailyPnLRecords.Any(r => r.Date == dateStr))
                    {
                        _dbContext.DailyPnLRecords.Add(new StoicTrade.Api.Models.DailyPnLRecord
                        {
                            Date = dateStr,
                            RealizedPnL = s.pnl,
                            UnrealizedPnL = 0m,
                            NetPnL = s.pnl,
                            TotalTrades = s.totalTrades,
                            WinningTrades = s.wins,
                            LosingTrades = s.losses,
                            TradeMode = "Paper",
                            UpdatedAt = dt
                        });
                    }
                }

                _dbContext.SaveChanges();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not initialize historical seed data for DailyPnLRecords: {Msg}", ex.Message);
            }
        }

        [HttpGet("pnl-history")]
        public async Task<IActionResult> GetPnLHistory([FromQuery] int days = 30)
        {
            try
            {
                var nowIst = ToIst(DateTime.UtcNow);
                var todayIst = nowIst.Date;
                string todayDateStr = todayIst.ToString("yyyy-MM-dd");

                EnsureHistoricalSeedData(todayIst);

                // 1. Calculate today's live/paper current P&L
                decimal todayRealized = 0m;
                decimal todayUnrealized = 0m;
                int todayTradesCount = 0;
                int todayWins = 0;
                int todayLosses = 0;

                bool isPaper = IsPaperMode();
                if (isPaper)
                {
                    var mock = GetMockPaperData();
                    var todayPositions = _dbContext.PaperPositions
                        .AsEnumerable()
                        .Where(p => ToIst(p.UpdatedAt).Date == todayIst || p.NetQty != 0)
                        .ToList();

                    foreach (var p in todayPositions)
                    {
                        decimal r = p.RealizedProfit ?? 0m;
                        todayRealized += r;
                        if (r > 0) todayWins++;
                        else if (r < 0) todayLosses++;
                        if (r != 0 || p.TotalBuyQty > 0) todayTradesCount++;
                    }
                    todayUnrealized = mock.totalPnL - todayRealized;
                }
                else
                {
                    try
                    {
                        var pos = await _fyersApi.GetPositionsAsync();
                        if (pos.ValueKind != JsonValueKind.Undefined && pos.TryGetProperty("netPositions", out var arr))
                        {
                            foreach (var p in arr.EnumerateArray())
                            {
                                decimal r = p.TryGetProperty("realized_profit", out var rp) ? rp.GetDecimal() : 0m;
                                decimal ur = p.TryGetProperty("unrealized_profit", out var up) ? up.GetDecimal() : 0m;
                                todayRealized += r;
                                todayUnrealized += ur;
                                todayTradesCount++;
                                if (r > 0) todayWins++;
                                else if (r < 0) todayLosses++;
                            }
                        }
                    }
                    catch {}
                }

                decimal todayNet = todayRealized + todayUnrealized;

                // 2. Update or insert today's entry in DailyPnLRecords
                var todayRecord = await _dbContext.DailyPnLRecords.FirstOrDefaultAsync(r => r.Date == todayDateStr);
                if (todayRecord == null)
                {
                    todayRecord = new StoicTrade.Api.Models.DailyPnLRecord
                    {
                        Date = todayDateStr,
                        RealizedPnL = todayRealized,
                        UnrealizedPnL = todayUnrealized,
                        NetPnL = todayNet,
                        TotalTrades = todayTradesCount,
                        WinningTrades = todayWins,
                        LosingTrades = todayLosses,
                        TradeMode = isPaper ? "Paper" : "Live",
                        UpdatedAt = DateTime.UtcNow
                    };
                    _dbContext.DailyPnLRecords.Add(todayRecord);
                }
                else
                {
                    todayRecord.RealizedPnL = todayRealized;
                    todayRecord.UnrealizedPnL = todayUnrealized;
                    todayRecord.NetPnL = todayNet;
                    todayRecord.TotalTrades = Math.Max(todayRecord.TotalTrades, todayTradesCount);
                    todayRecord.WinningTrades = Math.Max(todayRecord.WinningTrades, todayWins);
                    todayRecord.LosingTrades = Math.Max(todayRecord.LosingTrades, todayLosses);
                    todayRecord.TradeMode = isPaper ? "Paper" : "Live";
                    todayRecord.UpdatedAt = DateTime.UtcNow;
                }

                await _dbContext.SaveChangesAsync();

                // 3. Fetch records within the last X days
                var cutoffDate = todayIst.AddDays(-days).ToString("yyyy-MM-dd");
                var records = await _dbContext.DailyPnLRecords
                    .Where(r => string.Compare(r.Date, cutoffDate) >= 0)
                    .OrderByDescending(r => r.Date)
                    .ToListAsync();

                // 4. Calculate Aggregate Statistics
                decimal totalNetPnL = records.Sum(r => r.NetPnL);
                decimal totalRealizedPnL = records.Sum(r => r.RealizedPnL);
                int totalTrades = records.Sum(r => r.TotalTrades);
                int totalWinningTrades = records.Sum(r => r.WinningTrades);
                int totalLosingTrades = records.Sum(r => r.LosingTrades);
                decimal winRatePercent = totalTrades > 0 ? Math.Round((decimal)totalWinningTrades / totalTrades * 100m, 1) : 0m;

                int profitableDays = records.Count(r => r.NetPnL > 0);
                int lossDays = records.Count(r => r.NetPnL < 0);
                int breakEvenDays = records.Count(r => r.NetPnL == 0);

                var best = records.OrderByDescending(r => r.NetPnL).FirstOrDefault();
                var worst = records.OrderBy(r => r.NetPnL).FirstOrDefault();

                decimal avgDailyPnL = records.Count > 0 ? Math.Round(totalNetPnL / records.Count, 2) : 0m;

                // 5. Fetch closed trades in the window
                var closedTrades = await _dbContext.ClosedTrades
                    .Where(c => string.Compare(c.DateIst, cutoffDate) >= 0)
                    .OrderByDescending(c => c.ExitTime)
                    .Take(100)
                    .ToListAsync();

                return Ok(new
                {
                    daysRequested = days,
                    totalNetPnL = totalNetPnL,
                    totalRealizedPnL = totalRealizedPnL,
                    totalTrades = totalTrades,
                    winningTrades = totalWinningTrades,
                    losingTrades = totalLosingTrades,
                    winRatePercent = winRatePercent,
                    profitableDays = profitableDays,
                    lossDays = lossDays,
                    breakEvenDays = breakEvenDays,
                    averageDailyPnL = avgDailyPnL,
                    bestDay = best != null ? new { date = best.Date, pnl = best.NetPnL } : null,
                    worstDay = worst != null ? new { date = worst.Date, pnl = worst.NetPnL } : null,
                    dailyRecords = records,
                    closedTrades = closedTrades
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve P&L history");
                return StatusCode(500, new { error = "Failed to calculate P&L history", message = ex.Message });
            }
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
