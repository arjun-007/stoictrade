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

        private static string NormaliseSymbol(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string s = raw.Trim();
            if (s.StartsWith("NSE:", System.StringComparison.OrdinalIgnoreCase)) s = s.Substring(4);
            if (s.StartsWith("NIFTYNIFTY", System.StringComparison.OrdinalIgnoreCase)) s = s.Substring(5);
            s = s.Replace(" ", "");
            return s;
        }

        public async Task ExecuteOrderAsync(Signal signal)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<StoicTrade.Api.Data.AppDbContext>();
            var globalSettings = dbContext.GlobalSettings.FirstOrDefault();
            
            if (globalSettings != null && globalSettings.TradeMode == "Paper")
            {
                var optionEngine = scope.ServiceProvider.GetRequiredService<StoicTrade.Api.Services.Strategies.OptionSelectionEngine>();

                // If instrument is raw NIFTY or missing option type, resolve optimal ITM contract
                if (signal.Instrument == "NIFTY" || (!signal.Instrument.Contains("CE") && !signal.Instrument.Contains("PE")))
                {
                    string bias = signal.Action == "BUY" ? "BULLISH" : "BEARISH";
                    var contract = optionEngine.GetOptimalContract("NIFTY", bias, itmDistance: 1, expiryIndex: 1)
                        ?? optionEngine.GetOptimalContract("NIFTY", bias, itmDistance: 1, expiryIndex: 0);

                    if (!string.IsNullOrEmpty(contract))
                    {
                        signal.Instrument = contract.Replace("NSE:", "");
                    }
                }

                var normalisedInstrument = NormaliseSymbol(signal.Instrument);

                // Resolve realistic execution price for the option
                decimal optionLtp = optionEngine.ResolveOptionLtp(normalisedInstrument) ?? 150m;
                decimal executionPrice = signal.ExpectedPrice > 0 && signal.ExpectedPrice < 5000
                    ? signal.ExpectedPrice
                    : (signal.Price > 0 && signal.Price < 5000 ? signal.Price : optionLtp);

                _logger.LogInformation("OrderManagementService [PAPER]: Executing mock order for {Action} {Quantity} {Instrument} at ₹{ExecutionPrice}", 
                    signal.Action, signal.Quantity, normalisedInstrument, executionPrice);
                    
                var position = dbContext.PaperPositions.FirstOrDefault(p => p.Symbol == normalisedInstrument);
                if (position == null)
                {
                    position = new PaperPosition { Symbol = normalisedInstrument };
                    dbContext.PaperPositions.Add(position);
                }

                if (signal.Action == "BUY")
                {
                    position.TotalBuyQty += signal.Quantity;
                    position.TotalBuyValue += signal.Quantity * executionPrice;
                    position.BuyAvg = position.TotalBuyQty > 0 ? position.TotalBuyValue / position.TotalBuyQty : executionPrice;
                    position.NetQty += signal.Quantity;
                }
                else
                {
                    position.TotalSellQty += signal.Quantity;
                    position.TotalSellValue += signal.Quantity * executionPrice;
                    position.SellAvg = position.TotalSellQty > 0 ? position.TotalSellValue / position.TotalSellQty : executionPrice;
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
