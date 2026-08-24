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

                // Always prioritize real-time live Market LTP at the exact moment of execution
                decimal? currentLiveLtp = optionEngine.ResolveOptionLtp(normalisedInstrument);
                decimal executionPrice = (currentLiveLtp.HasValue && currentLiveLtp.Value > 0)
                    ? currentLiveLtp.Value
                    : (signal.ExpectedPrice > 0 && signal.ExpectedPrice < 5000 
                        ? signal.ExpectedPrice 
                        : (signal.Price > 0 && signal.Price < 5000 ? signal.Price : 150m));

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

        public async Task<int> AutoSquareOffAllPositionsAsync(string reason = "AutoSquareOff_310PM")
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<StoicTrade.Api.Data.AppDbContext>();
            var globalSettings = dbContext.GlobalSettings.FirstOrDefault();
            var optionEngine = scope.ServiceProvider.GetRequiredService<StoicTrade.Api.Services.Strategies.OptionSelectionEngine>();
            int closedCount = 0;

            if (globalSettings != null && globalSettings.TradeMode == "Paper")
            {
                var openPositions = dbContext.PaperPositions.Where(p => p.NetQty != 0).ToList();
                foreach (var pos in openPositions)
                {
                    decimal exitPrice = optionEngine.ResolveOptionLtp(pos.Symbol) ?? (pos.BuyAvg > 0 ? pos.BuyAvg : 150m);
                    int exitQty = System.Math.Abs(pos.NetQty);

                    if (pos.NetQty > 0) // Long -> SELL to close
                    {
                        pos.TotalSellQty += exitQty;
                        pos.TotalSellValue += exitQty * exitPrice;
                        pos.SellAvg = pos.TotalSellQty > 0 ? pos.TotalSellValue / pos.TotalSellQty : exitPrice;
                    }
                    else if (pos.NetQty < 0) // Short -> BUY to close
                    {
                        pos.TotalBuyQty += exitQty;
                        pos.TotalBuyValue += exitQty * exitPrice;
                        pos.BuyAvg = pos.TotalBuyQty > 0 ? pos.TotalBuyValue / pos.TotalBuyQty : exitPrice;
                    }

                    pos.NetQty = 0;
                    pos.RealizedProfit += (pos.TotalSellValue - pos.TotalBuyValue);
                    pos.UpdatedAt = System.DateTime.UtcNow;
                    closedCount++;

                    _logger.LogInformation("AutoSquareOff [PAPER]: Closed position {Symbol} at ₹{ExitPrice} (Reason: {Reason})",
                        pos.Symbol, exitPrice, reason);
                }

                if (closedCount > 0)
                {
                    await dbContext.SaveChangesAsync();
                }
                return closedCount;
            }

            // Live Mode Square-off
            if (_fyersApiService.IsEngineRunning)
            {
                try
                {
                    var positionsJson = await _fyersApiService.GetPositionsAsync();
                    if (positionsJson.ValueKind != System.Text.Json.JsonValueKind.Undefined && 
                        positionsJson.TryGetProperty("netPositions", out var netPositionsArray))
                    {
                        foreach (var p in netPositionsArray.EnumerateArray())
                        {
                            int netQty = p.TryGetProperty("netQty", out var nq) ? nq.GetInt32() : 0;
                            string symbol = p.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "";

                            if (netQty != 0 && !string.IsNullOrEmpty(symbol))
                            {
                                string exitAction = netQty > 0 ? "SELL" : "BUY";
                                int exitQty = System.Math.Abs(netQty);
                                await _fyersApiService.PlaceOrderAsync(symbol, exitAction, exitQty, 0);
                                closedCount++;
                                _logger.LogInformation("AutoSquareOff [LIVE]: Sent exit order {Action} {Qty} for {Symbol} (Reason: {Reason})",
                                    exitAction, exitQty, symbol, reason);
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    _logger.LogError(ex, "AutoSquareOff [LIVE]: Error while closing live positions.");
                }
            }

            return closedCount;
        }
    }
}
