using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace StoicTrade.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private static string NormaliseSymbol(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string s = raw.Trim();
            if (s.StartsWith("NSE:", StringComparison.OrdinalIgnoreCase)) s = s.Substring(4);
            if (s.StartsWith("NIFTYNIFTY", StringComparison.OrdinalIgnoreCase)) s = s.Substring(5);
            s = s.Replace(" ", "");
            return s;
        }

        [HttpPost]
        public async Task<IActionResult> PlaceOrder([FromBody] OrderRequest request, [FromServices] StoicTrade.Api.Data.AppDbContext dbContext)
        {
            // By the time this is reached, the RmsMiddleware has already validated:
            // 1. Kill Switch is NOT active
            // 2. We are within the trading time window
            // 3. VIX is within limits
            // 4. Instrument is valid (NIFTY option or EQ)
            
            var settings = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(dbContext.GlobalSettings);
            if (settings != null && settings.TradeMode == "Paper")
            {
                var normalisedInstrument = NormaliseSymbol(request.Instrument);
                var resolver = HttpContext.RequestServices.GetRequiredService<StoicTrade.Api.Services.Strategies.OptionSelectionEngine>();
                var ltp = resolver.ResolveOptionLtp(normalisedInstrument) ?? 0m;
                decimal executionPrice = (request.EntryPrice.HasValue && request.EntryPrice.Value > 0)
                    ? request.EntryPrice.Value
                    : (ltp > 0 ? ltp : 100m);
                
                var trade = new StoicTrade.Api.Models.TradeLog
                {
                    OrderId = Guid.NewGuid().ToString("N").Substring(0, 10).ToUpper(),
                    StrategyName = "Manual",
                    Instrument = normalisedInstrument,
                    TradeType = request.OrderType,
                    Quantity = request.Quantity,
                    ExecutionPrice = executionPrice,
                    Timestamp = DateTime.UtcNow,
                    Status = "EXECUTED"
                };
                
                dbContext.TradeLogs.Add(trade);
                
                // Match by normalized symbol or exact symbol
                var allPositions = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(dbContext.PaperPositions);
                var position = allPositions.FirstOrDefault(p => NormaliseSymbol(p.Symbol) == normalisedInstrument);
                
                if (position == null)
                {
                    position = new StoicTrade.Api.Models.PaperPosition { Symbol = normalisedInstrument };
                    dbContext.PaperPositions.Add(position);
                }
                else
                {
                    // Standardize symbol in database if it was legacy/un-normalized
                    position.Symbol = normalisedInstrument;
                }

                if (request.OrderType == "BUY")
                {
                    decimal totalVal = (position.BuyAvg * position.TotalBuyQty) + (trade.ExecutionPrice * request.Quantity);
                    position.TotalBuyQty += request.Quantity;
                    position.BuyAvg = totalVal / position.TotalBuyQty;
                    position.NetQty += request.Quantity;
                    position.TotalBuyValue += trade.ExecutionPrice * request.Quantity;
                }
                else
                {
                    decimal totalVal = (position.SellAvg * position.TotalSellQty) + (trade.ExecutionPrice * request.Quantity);
                    position.TotalSellQty += request.Quantity;
                    position.SellAvg = totalVal / position.TotalSellQty;
                    position.NetQty -= request.Quantity;
                    position.TotalSellValue += trade.ExecutionPrice * request.Quantity;
                    
                    if (position.NetQty >= 0)
                    {
                        position.RealizedProfit += (trade.ExecutionPrice - position.BuyAvg) * request.Quantity;
                    }
                }

                if (position.NetQty == 0)
                {
                    // Reset accumulators for next trade in same symbol
                    position.TotalBuyQty = 0;
                    position.TotalSellQty = 0;
                    position.TotalBuyValue = 0;
                    position.TotalSellValue = 0;
                    position.BuyAvg = 0;
                    position.SellAvg = 0;
                }

                position.UpdatedAt = DateTime.UtcNow;

                await dbContext.SaveChangesAsync();
                
                return Ok(new { Message = $"[PAPER] Order for {normalisedInstrument} filled at CMP: {trade.ExecutionPrice}" });
            }

            // Proceed to place order with Fyers API (mocked for now)
            return Ok(new { Message = $"[LIVE] Order for {request.Instrument} placed successfully." });
        }
    }

    public class OrderRequest
    {
        public string Instrument { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string OrderType { get; set; } = string.Empty;
        public decimal? EntryPrice { get; set; }
    }
}
