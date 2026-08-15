using Microsoft.AspNetCore.Mvc;

namespace StoicTrade.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
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
                return Ok(new { Message = $"[PAPER] Order for {request.Instrument} filled instantly at CMP." });
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
    }
}
