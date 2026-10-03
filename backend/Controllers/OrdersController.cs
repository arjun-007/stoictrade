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
        public async Task<IActionResult> PlaceOrder(
            [FromBody] OrderRequest request,
            [FromServices] StoicTrade.Api.Data.AppDbContext dbContext,
            [FromServices] StoicTrade.Api.Services.FyersApiService fyersApi)
        {
            // Normalize order action/side ("BUY" vs "SELL")
            string side = (!string.IsNullOrWhiteSpace(request.Action) ? request.Action : request.OrderType).Trim().ToUpper();
            if (side != "BUY" && side != "SELL") side = "BUY";

            var normalisedInstrument = NormaliseSymbol(request.Instrument);
            if (string.IsNullOrEmpty(normalisedInstrument))
            {
                return BadRequest(new { error = "Invalid instrument symbol provided." });
            }

            var resolver = HttpContext.RequestServices.GetRequiredService<StoicTrade.Api.Services.Strategies.OptionSelectionEngine>();
            var ltp = resolver.ResolveOptionLtp(normalisedInstrument) ?? 0m;
            decimal executionPrice = (request.EntryPrice.HasValue && request.EntryPrice.Value > 0)
                ? request.EntryPrice.Value
                : (request.Price.HasValue && request.Price.Value > 0 ? request.Price.Value : (ltp > 0 ? ltp : 100m));

            var settings = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(dbContext.GlobalSettings);
            bool isPaper = settings == null || string.Equals(settings.TradeMode, "Paper", StringComparison.OrdinalIgnoreCase);

            if (isPaper)
            {
                var trade = new StoicTrade.Api.Models.TradeLog
                {
                    OrderId = Guid.NewGuid().ToString("N").Substring(0, 10).ToUpper(),
                    StrategyName = "Manual",
                    Instrument = normalisedInstrument,
                    TradeType = side,
                    Quantity = request.Quantity,
                    ExecutionPrice = executionPrice,
                    Timestamp = DateTime.UtcNow,
                    Status = "EXECUTED",
                    Reason = "Manual Paper Order"
                };
                
                dbContext.TradeLogs.Add(trade);
                
                // Match by normalized symbol or exact symbol (prioritize open positions)
                var allPositions = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(dbContext.PaperPositions);
                var position = allPositions.FirstOrDefault(p => NormaliseSymbol(p.Symbol) == normalisedInstrument && p.NetQty > 0)
                    ?? allPositions.FirstOrDefault(p => NormaliseSymbol(p.Symbol) == normalisedInstrument);
                
                if (position == null)
                {
                    position = new StoicTrade.Api.Models.PaperPosition { Symbol = normalisedInstrument };
                    dbContext.PaperPositions.Add(position);
                }
                else
                {
                    position.Symbol = normalisedInstrument;
                }

                if (side == "BUY")
                {
                    decimal totalVal = ((position.BuyAvg ?? 0m) * position.TotalBuyQty) + (trade.ExecutionPrice * request.Quantity);
                    position.TotalBuyQty += request.Quantity;
                    position.BuyAvg = position.TotalBuyQty > 0 ? totalVal / position.TotalBuyQty : trade.ExecutionPrice;
                    position.NetQty += request.Quantity;
                    position.TotalBuyValue = (position.TotalBuyValue ?? 0m) + (trade.ExecutionPrice * request.Quantity);
                    position.PeakLtp = trade.ExecutionPrice;
                    position.StrategyName = "Manual Entry";
                    position.TargetPrice = request.TargetPrice ?? request.Target ?? Math.Round(trade.ExecutionPrice * 1.25m, 2);
                    position.StopLossPrice = request.StopLossPrice ?? request.Stoploss ?? Math.Round(Math.Max(5.0m, trade.ExecutionPrice * 0.85m), 2);
                }
                else
                {
                    decimal totalVal = ((position.SellAvg ?? 0m) * position.TotalSellQty) + (trade.ExecutionPrice * request.Quantity);
                    position.TotalSellQty += request.Quantity;
                    position.SellAvg = position.TotalSellQty > 0 ? totalVal / position.TotalSellQty : trade.ExecutionPrice;
                    position.NetQty -= request.Quantity;
                    position.TotalSellValue = (position.TotalSellValue ?? 0m) + (trade.ExecutionPrice * request.Quantity);
                    
                    if (position.NetQty >= 0)
                    {
                        position.RealizedProfit = (position.RealizedProfit ?? 0m) + (trade.ExecutionPrice - (position.BuyAvg ?? 0m)) * request.Quantity;
                    }
                }

                if (position.NetQty == 0)
                {
                    position.TotalBuyQty = 0;
                    position.TotalSellQty = 0;
                    position.TotalBuyValue = 0;
                    position.TotalSellValue = 0;
                    position.BuyAvg = 0;
                    position.SellAvg = 0;
                    position.TargetPrice = null;
                    position.StopLossPrice = null;
                }

                position.UpdatedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync();
                
                return Ok(new { Message = $"[PAPER] Order for {normalisedInstrument} filled at CMP: ₹{trade.ExecutionPrice}" });
            }

            // ─── LIVE TRADING ORDER EXECUTION ─────────────────────────────────────
            string fyersSymbol = normalisedInstrument.StartsWith("NSE:") ? normalisedInstrument : $"NSE:{normalisedInstrument}";
            string productType = !string.IsNullOrWhiteSpace(request.ProductType) ? request.ProductType : "INTRADAY";

            var (success, brokerResponse, orderId) = await fyersApi.PlaceOrderAsync(
                fyersSymbol, 
                side, 
                request.Quantity, 
                executionPrice, 
                productType);

            if (!success)
            {
                return BadRequest(new { 
                    error = brokerResponse,
                    message = $"Fyers rejected order for {fyersSymbol}: {brokerResponse}" 
                });
            }

            // Record live trade in database for local audit and reporting
            var liveTrade = new StoicTrade.Api.Models.TradeLog
            {
                OrderId = !string.IsNullOrEmpty(orderId) ? orderId : Guid.NewGuid().ToString("N").Substring(0, 10).ToUpper(),
                StrategyName = "Manual",
                Instrument = normalisedInstrument,
                TradeType = side,
                Quantity = request.Quantity,
                ExecutionPrice = executionPrice,
                Timestamp = DateTime.UtcNow,
                Status = "EXECUTED",
                Reason = $"Live Broker Order ({productType})"
            };
            dbContext.TradeLogs.Add(liveTrade);

            // Maintain position tracking ledger so Trailing SL and Targets actively monitor this live position
            var allPositionsLive = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(dbContext.PaperPositions);
            var positionLive = allPositionsLive.FirstOrDefault(p => NormaliseSymbol(p.Symbol) == normalisedInstrument && p.NetQty > 0)
                ?? allPositionsLive.FirstOrDefault(p => NormaliseSymbol(p.Symbol) == normalisedInstrument);

            if (positionLive == null)
            {
                positionLive = new StoicTrade.Api.Models.PaperPosition { Symbol = normalisedInstrument };
                dbContext.PaperPositions.Add(positionLive);
            }
            else
            {
                positionLive.Symbol = normalisedInstrument;
            }

            if (side == "BUY")
            {
                decimal totalVal = ((positionLive.BuyAvg ?? 0m) * positionLive.TotalBuyQty) + (executionPrice * request.Quantity);
                positionLive.TotalBuyQty += request.Quantity;
                positionLive.BuyAvg = positionLive.TotalBuyQty > 0 ? totalVal / positionLive.TotalBuyQty : executionPrice;
                positionLive.NetQty += request.Quantity;
                positionLive.TotalBuyValue = (positionLive.TotalBuyValue ?? 0m) + (executionPrice * request.Quantity);
                positionLive.PeakLtp = executionPrice;
                positionLive.StrategyName = "Manual Live";
                positionLive.TargetPrice = request.TargetPrice ?? request.Target ?? Math.Round(executionPrice * 1.25m, 2);
                positionLive.StopLossPrice = request.StopLossPrice ?? request.Stoploss ?? Math.Round(Math.Max(5.0m, executionPrice * 0.85m), 2);
                positionLive.TrailingStopLossPoint = settings?.TrailingStopLossPoint ?? 18.0m;
                positionLive.TrailingActivationPoint = settings?.TrailingActivationPoint ?? 15.0m;
                positionLive.IsTrailingActive = false;
                positionLive.IsPartialBooked = false;
            }
            else
            {
                decimal totalVal = ((positionLive.SellAvg ?? 0m) * positionLive.TotalSellQty) + (executionPrice * request.Quantity);
                positionLive.TotalSellQty += request.Quantity;
                positionLive.SellAvg = positionLive.TotalSellQty > 0 ? totalVal / positionLive.TotalSellQty : executionPrice;
                positionLive.NetQty -= request.Quantity;
                positionLive.TotalSellValue = (positionLive.TotalSellValue ?? 0m) + (executionPrice * request.Quantity);

                if (positionLive.NetQty >= 0)
                {
                    positionLive.RealizedProfit = (positionLive.RealizedProfit ?? 0m) + (executionPrice - (positionLive.BuyAvg ?? 0m)) * request.Quantity;
                }
            }

            if (positionLive.NetQty == 0)
            {
                positionLive.TotalBuyQty = 0;
                positionLive.TotalSellQty = 0;
                positionLive.TotalBuyValue = 0;
                positionLive.TotalSellValue = 0;
                positionLive.BuyAvg = 0;
                positionLive.SellAvg = 0;
                positionLive.TargetPrice = null;
                positionLive.StopLossPrice = null;
            }

            positionLive.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();

            return Ok(new { 
                Message = $"[LIVE] Order for {fyersSymbol} ({side} {request.Quantity} qty) placed successfully on Fyers.",
                OrderId = liveTrade.OrderId,
                BrokerResponse = brokerResponse
            });
        }
    }

    public class OrderRequest
    {
        public string Instrument { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string OrderType { get; set; } = string.Empty;
        public string? Action { get; set; }
        public string? OrderMode { get; set; }
        public decimal? EntryPrice { get; set; }
        public decimal? Price { get; set; }
        public decimal? Stoploss { get; set; }
        public decimal? Target { get; set; }
        public decimal? StopLossPrice { get; set; }
        public decimal? TargetPrice { get; set; }
        public string? ProductType { get; set; }
    }
}
