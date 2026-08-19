using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using StoicTrade.Api.Models;
using System.Linq;

namespace StoicTrade.Api.Services
{
    public class OrderManagementService
    {
        private readonly ILogger<OrderManagementService> _logger;
        private readonly FyersApiService _fyersApiService;
        private readonly IServiceProvider _serviceProvider;

        public OrderManagementService(ILogger<OrderManagementService> logger, FyersApiService fyersApiService, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _fyersApiService = fyersApiService;
            _serviceProvider = serviceProvider;
        }

        public async Task ExecuteOrderAsync(Signal signal)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<StoicTrade.Api.Data.AppDbContext>();
            var globalSettings = dbContext.GlobalSettings.FirstOrDefault();
            
            if (globalSettings != null && globalSettings.TradeMode == "Paper")
            {
                _logger.LogInformation("OrderManagementService [PAPER]: Executing mock order for {Action} {Quantity} {Instrument} at {ExpectedPrice}", 
                    signal.Action, signal.Quantity, signal.Instrument, signal.ExpectedPrice);
                    
                var position = dbContext.PaperPositions.FirstOrDefault(p => p.Symbol == signal.Instrument);
                if (position == null)
                {
                    position = new PaperPosition { Symbol = signal.Instrument };
                    dbContext.PaperPositions.Add(position);
                }

                if (signal.Action == "BUY")
                {
                    position.TotalBuyQty += signal.Quantity;
                    position.TotalBuyValue += signal.Quantity * signal.ExpectedPrice;
                    position.BuyAvg = position.TotalBuyValue / position.TotalBuyQty;
                    position.NetQty += signal.Quantity;
                }
                else
                {
                    position.TotalSellQty += signal.Quantity;
                    position.TotalSellValue += signal.Quantity * signal.ExpectedPrice;
                    position.SellAvg = position.TotalSellValue / position.TotalSellQty;
                    position.NetQty -= signal.Quantity;
                }
                
                // If position is closed (NetQty == 0), calculate realized profit
                if (position.NetQty == 0)
                {
                    position.RealizedProfit += (position.TotalSellValue - position.TotalBuyValue);
                    // Reset accumulators for next trade in same symbol
                    position.TotalBuyQty = 0;
                    position.TotalSellQty = 0;
                    position.TotalBuyValue = 0;
                    position.TotalSellValue = 0;
                    position.BuyAvg = 0;
                    position.SellAvg = 0;
                }

                position.UpdatedAt = System.DateTime.UtcNow;
                await dbContext.SaveChangesAsync();
                return;
            }

            _logger.LogInformation("OrderManagementService [LIVE]: Executing order for {Action} {Quantity} {Instrument}", 
                signal.Action, signal.Quantity, signal.Instrument);

            await _fyersApiService.PlaceOrderAsync(signal.Instrument, signal.Action, signal.Quantity, signal.ExpectedPrice);
        }
    }
}
