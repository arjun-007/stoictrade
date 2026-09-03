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
            var optionEngine = scope.ServiceProvider.GetRequiredService<StoicTrade.Api.Services.Strategies.OptionSelectionEngine>();

            if (globalSettings != null && globalSettings.TradeMode == "Paper")
            {
                // 1. Intercept EXIT action to cleanly close existing position
                if (signal.Action == "EXIT")
                {
                    var openPosition = dbContext.PaperPositions.FirstOrDefault(p => 
                        p.NetQty > 0 && 
                        ((!string.IsNullOrEmpty(signal.StrategyName) && p.StrategyName == signal.StrategyName) ||
                         (!string.IsNullOrEmpty(signal.Instrument) && signal.Instrument != "NIFTY" && NormaliseSymbol(p.Symbol) == NormaliseSymbol(signal.Instrument)))
                    );

                    if (openPosition != null)
                    {
                        decimal? exitLtp = optionEngine.ResolveOptionLtp(openPosition.Symbol);
                        decimal exitPrice = (exitLtp.HasValue && exitLtp.Value > 0)
                            ? exitLtp.Value
                            : (openPosition.BuyAvg > 0 ? openPosition.BuyAvg : 150m);

                        int exitQty = openPosition.NetQty;
                        openPosition.TotalSellQty += exitQty;
                        openPosition.TotalSellValue += exitQty * exitPrice;
                        openPosition.SellAvg = openPosition.TotalSellQty > 0 ? openPosition.TotalSellValue / openPosition.TotalSellQty : exitPrice;
                        openPosition.NetQty = 0;
                        openPosition.RealizedProfit += (openPosition.TotalSellValue - openPosition.TotalBuyValue);
                        openPosition.UpdatedAt = System.DateTime.UtcNow;

                        dbContext.TradeLogs.Add(new TradeLog
                        {
                            OrderId = System.Guid.NewGuid().ToString("N").Substring(0, 10).ToUpper(),
                            StrategyName = openPosition.StrategyName ?? signal.StrategyName,
                            Instrument = openPosition.Symbol,
                            TradeType = "SELL",
                            Quantity = exitQty,
                            ExecutionPrice = exitPrice,
                            Timestamp = System.DateTime.UtcNow,
                            Status = "EXECUTED",
                            Reason = "Strategy Exit Signal"
                        });

                        await dbContext.SaveChangesAsync();
                        _logger.LogInformation("OrderManagementService [PAPER]: Cleanly exited position for {Strategy} ({Symbol}) at ₹{ExitPrice}. Realized P&L: ₹{PnL:F2}", 
                            openPosition.StrategyName, openPosition.Symbol, exitPrice, openPosition.RealizedProfit);

                        // Clear Redis state for this strategy
                        var redis = scope.ServiceProvider.GetService<RedisService>();
                        if (redis != null && !string.IsNullOrEmpty(openPosition.StrategyName))
                        {
                            var strat = dbContext.StrategyConfigs.FirstOrDefault(s => s.StrategyName == openPosition.StrategyName);
                            if (strat != null)
                            {
                                await redis.DeleteKeyAsync($"strategy_state_{strat.Id}");
                            }
                        }
                        return;
                    }
                    else
                    {
                        _logger.LogWarning("OrderManagementService [PAPER]: EXIT signal for {Strategy} ({Instrument}) received, but no active open position was found. Skipping to prevent phantom sell order.",
                            signal.StrategyName, signal.Instrument);
                        return;
                    }
                }

                // Handle BUY_PE bias
                string bias = "BULLISH";
                if (signal.Action == "BUY_PE")
                {
                    bias = "BEARISH";
                    signal.Action = "BUY"; // Execution is a BUY of the PE option
                }
                else if (signal.Action == "BUY")
                {
                    bias = "BULLISH";
                }

                // If instrument is raw NIFTY or missing option type, resolve optimal ITM contract
                if (signal.Instrument == "NIFTY" || (!signal.Instrument.Contains("CE") && !signal.Instrument.Contains("PE")))
                {
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

                _logger.LogInformation("OrderManagementService [PAPER]: Executing order for {Action} {Quantity} {Instrument} at ₹{ExecutionPrice} ({Strategy})", 
                    signal.Action, signal.Quantity, normalisedInstrument, executionPrice, signal.StrategyName);

                // Isolate position per (Symbol, StrategyName) so different strategies don't merge or overwrite each other
                var position = dbContext.PaperPositions.FirstOrDefault(p => 
                    p.Symbol == normalisedInstrument && 
                    p.StrategyName == signal.StrategyName && 
                    p.NetQty > 0);

                if (position == null)
                {
                    position = new PaperPosition 
                    { 
                        Symbol = normalisedInstrument,
                        StrategyName = signal.StrategyName
                    };
                    dbContext.PaperPositions.Add(position);
                }

                var stratConfig = dbContext.StrategyConfigs.FirstOrDefault(s => s.StrategyName == signal.StrategyName);
                decimal trailingSl = (stratConfig != null && stratConfig.TrailingStopLossPoint > 0) 
                    ? stratConfig.TrailingStopLossPoint 
                    : (globalSettings?.TrailingStopLossPoint ?? 8.0m);

                position.TotalBuyQty += signal.Quantity;
                position.TotalBuyValue += signal.Quantity * executionPrice;
                position.BuyAvg = position.TotalBuyQty > 0 ? position.TotalBuyValue / position.TotalBuyQty : executionPrice;
                position.NetQty += signal.Quantity;
                position.PeakLtp = executionPrice;
                position.TrailingStopLossPoint = trailingSl > 0 ? trailingSl : 8.0m;
                position.TargetPrice = signal.TargetPrice > 0 ? signal.TargetPrice : Math.Round(executionPrice * 1.25m, 2);
                position.StopLossPrice = signal.StopLossPrice > 0 ? signal.StopLossPrice : Math.Round(Math.Max(5.0m, executionPrice * 0.85m), 2);
                position.UpdatedAt = System.DateTime.UtcNow;

                dbContext.TradeLogs.Add(new TradeLog
                {
                    OrderId = System.Guid.NewGuid().ToString("N").Substring(0, 10).ToUpper(),
                    StrategyName = signal.StrategyName,
                    Instrument = normalisedInstrument,
                    TradeType = "BUY",
                    Quantity = signal.Quantity,
                    ExecutionPrice = executionPrice,
                    Timestamp = System.DateTime.UtcNow,
                    Status = "EXECUTED",
                    Reason = "Strategy Entry Order"
                });

                await dbContext.SaveChangesAsync();
                return;
            }

            // LIVE Trading Mode
            _logger.LogInformation("OrderManagementService [LIVE]: Executing order for {Action} {Quantity} {Instrument} ({Strategy})", 
                signal.Action, signal.Quantity, signal.Instrument, signal.StrategyName);

            string liveSymbol = signal.Instrument;
            if (liveSymbol == "NIFTY" && !string.IsNullOrEmpty(signal.StrategyName))
            {
                var knownPos = dbContext.PaperPositions.FirstOrDefault(p => p.StrategyName == signal.StrategyName && p.NetQty > 0);
                if (knownPos != null) liveSymbol = knownPos.Symbol;
            }

            string fyersAction = signal.Action == "EXIT" ? "SELL" : (signal.Action == "BUY_PE" ? "BUY" : signal.Action);
            await _fyersApiService.PlaceOrderAsync(liveSymbol, fyersAction, signal.Quantity, signal.ExpectedPrice);
        }

        public async Task MonitorActivePositionsAsync(System.IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<StoicTrade.Api.Data.AppDbContext>();
            var globalSettings = dbContext.GlobalSettings.FirstOrDefault();
            var optionEngine = scope.ServiceProvider.GetRequiredService<StoicTrade.Api.Services.Strategies.OptionSelectionEngine>();
            var redis = scope.ServiceProvider.GetService<RedisService>();

            var openPositions = dbContext.PaperPositions.Where(p => p.NetQty > 0).ToList();
            if (!openPositions.Any()) return;

            bool hasChanges = false;
            foreach (var pos in openPositions)
            {
                decimal? ltp = optionEngine.ResolveOptionLtp(pos.Symbol);
                if (!ltp.HasValue || ltp.Value <= 0) continue;
                decimal currentLtp = ltp.Value;

                // 1. Trailing Stop Loss
                if (pos.PeakLtp <= 0) pos.PeakLtp = pos.BuyAvg > 0 ? pos.BuyAvg : currentLtp;
                if (currentLtp > pos.PeakLtp)
                {
                    pos.PeakLtp = currentLtp;
                    hasChanges = true;

                    decimal trailingPts = (pos.TrailingStopLossPoint.HasValue && pos.TrailingStopLossPoint.Value > 0)
                        ? pos.TrailingStopLossPoint.Value
                        : (globalSettings?.TrailingStopLossPoint ?? 8.0m);

                    if (trailingPts > 0)
                    {
                        decimal trailedSl = System.Math.Round(pos.PeakLtp - trailingPts, 2);
                        if (trailedSl > (pos.StopLossPrice ?? 0))
                        {
                            pos.StopLossPrice = trailedSl;
                            _logger.LogInformation("MonitorPositions: Trailed SL for {Strategy} ({Symbol}): Peak ₹{Peak}, New SL ₹{SL}",
                                pos.StrategyName, pos.Symbol, pos.PeakLtp, pos.StopLossPrice);
                        }
                    }
                }

                // 2. Check Target Hit
                bool isTargetHit = pos.TargetPrice.HasValue && pos.TargetPrice.Value > 0 && currentLtp >= pos.TargetPrice.Value;
                // 3. Check Stop Loss / Trailing Stop Loss Hit
                bool isSlHit = pos.StopLossPrice.HasValue && pos.StopLossPrice.Value > 0 && currentLtp <= pos.StopLossPrice.Value;

                if (isTargetHit || isSlHit)
                {
                    string exitReason = isTargetHit 
                        ? $"Target Hit (LTP ₹{currentLtp:F2} >= Target ₹{pos.TargetPrice:F2})" 
                        : $"Stop Loss Hit (LTP ₹{currentLtp:F2} <= SL ₹{pos.StopLossPrice:F2})";

                    int exitQty = pos.NetQty;
                    pos.TotalSellQty += exitQty;
                    pos.TotalSellValue += exitQty * currentLtp;
                    pos.SellAvg = pos.TotalSellQty > 0 ? pos.TotalSellValue / pos.TotalSellQty : currentLtp;
                    pos.NetQty = 0;
                    pos.RealizedProfit += (pos.TotalSellValue - pos.TotalBuyValue);
                    pos.UpdatedAt = System.DateTime.UtcNow;
                    hasChanges = true;

                    dbContext.TradeLogs.Add(new TradeLog
                    {
                        OrderId = System.Guid.NewGuid().ToString("N").Substring(0, 10).ToUpper(),
                        StrategyName = pos.StrategyName ?? "Target/SL Trigger",
                        Instrument = pos.Symbol,
                        TradeType = "SELL",
                        Quantity = exitQty,
                        ExecutionPrice = currentLtp,
                        Timestamp = System.DateTime.UtcNow,
                        Status = "EXECUTED",
                        Reason = exitReason
                    });

                    StoicTrade.Api.Services.Strategies.StrategyEngineService.AddToSignalLog(
                        new StoicTrade.Api.Services.Strategies.SignalLogEntry
                        {
                            StrategyName = pos.StrategyName ?? "Strategy",
                            Action = "EXIT",
                            Instrument = pos.Symbol,
                            Price = currentLtp,
                            TargetPrice = pos.TargetPrice ?? 0,
                            StopLossPrice = pos.StopLossPrice ?? 0,
                            Quantity = exitQty,
                            Status = "ExitSignal",
                            GeneratedAt = System.DateTime.UtcNow,
                            ExpiresAt = System.DateTime.UtcNow.AddMinutes(15)
                        }
                    );

                    // In Live mode, square off via Fyers
                    if (globalSettings != null && globalSettings.TradeMode == "Live" && _fyersApiService.IsEngineRunning)
                    {
                        await _fyersApiService.PlaceOrderAsync(pos.Symbol, "SELL", exitQty, currentLtp);
                    }

                    // Clear Redis lock
                    if (redis != null && !string.IsNullOrEmpty(pos.StrategyName))
                    {
                        var strat = dbContext.StrategyConfigs.FirstOrDefault(s => s.StrategyName == pos.StrategyName);
                        if (strat != null)
                        {
                            await redis.DeleteKeyAsync($"strategy_state_{strat.Id}");
                        }
                    }

                    _logger.LogInformation("MonitorPositions: Position closed: {Reason} for {Strategy} ({Symbol}). Realized P&L: ₹{PnL:F2}",
                        exitReason, pos.StrategyName, pos.Symbol, pos.RealizedProfit);
                }
            }

            if (hasChanges)
            {
                await dbContext.SaveChangesAsync();
            }
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
