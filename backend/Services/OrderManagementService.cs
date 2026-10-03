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

        public async Task<bool> ExecuteOrderAsync(Signal signal)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<StoicTrade.Api.Data.AppDbContext>();
            var globalSettings = dbContext.GlobalSettings.FirstOrDefault() ?? new GlobalSettings { TradeMode = "Paper" };
            var optionEngine = scope.ServiceProvider.GetRequiredService<StoicTrade.Api.Services.Strategies.OptionSelectionEngine>();

            bool isPaperMode = string.Equals(globalSettings.TradeMode, "Paper", System.StringComparison.OrdinalIgnoreCase);

            // 1. Intercept EXIT action to cleanly close existing position
            if (signal.Action == "EXIT")
            {
                PaperPosition? openPosition = null;
                if (!string.IsNullOrEmpty(signal.StrategyName))
                {
                    openPosition = dbContext.PaperPositions.FirstOrDefault(p => 
                        p.NetQty > 0 && 
                        p.StrategyName == signal.StrategyName &&
                        (string.IsNullOrEmpty(signal.Instrument) || signal.Instrument == "NIFTY" || NormaliseSymbol(p.Symbol) == NormaliseSymbol(signal.Instrument))
                    );
                }

                if (openPosition == null && !string.IsNullOrEmpty(signal.Instrument) && signal.Instrument != "NIFTY")
                {
                    openPosition = dbContext.PaperPositions.FirstOrDefault(p => 
                        p.NetQty > 0 && 
                        NormaliseSymbol(p.Symbol) == NormaliseSymbol(signal.Instrument)
                    );
                }

                if (openPosition != null)
                {
                    decimal? exitLtp = optionEngine.ResolveOptionLtp(openPosition.Symbol);
                    decimal exitPrice = (exitLtp.HasValue && exitLtp.Value > 0)
                        ? exitLtp.Value
                        : (signal.ExpectedPrice > 0 && signal.ExpectedPrice < 5000 
                            ? signal.ExpectedPrice 
                            : (signal.Price > 0 && signal.Price < 5000 
                                ? signal.Price 
                                : ((openPosition.BuyAvg ?? 0m) > 0 ? (openPosition.BuyAvg ?? 0m) : 150m)));

                    int exitQty = openPosition.NetQty;
                    openPosition.TotalSellQty += exitQty;
                    openPosition.TotalSellValue = (openPosition.TotalSellValue ?? 0m) + (exitQty * exitPrice);
                    openPosition.SellAvg = openPosition.TotalSellQty > 0 ? (openPosition.TotalSellValue ?? 0m) / openPosition.TotalSellQty : exitPrice;
                    openPosition.NetQty = 0;
                    decimal tradePnl = (exitPrice - (openPosition.BuyAvg ?? 0m)) * exitQty;
                    openPosition.RealizedProfit = (openPosition.RealizedProfit ?? 0m) + tradePnl;
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
                    _logger.LogInformation("OrderManagementService [{Mode}]: Cleanly exited position for {Strategy} ({Symbol}) at ₹{ExitPrice}. Realized P&L: ₹{PnL:F2}", 
                        isPaperMode ? "PAPER" : "LIVE", openPosition.StrategyName, openPosition.Symbol, exitPrice, openPosition.RealizedProfit);

                    // If Live Mode, fire exit order to broker
                    if (!isPaperMode && _fyersApiService.IsEngineRunning)
                    {
                        await _fyersApiService.PlaceOrderAsync(openPosition.Symbol, "SELL", exitQty, exitPrice);
                    }

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
                    return true;
                }
                else
                {
                    _logger.LogWarning("OrderManagementService: EXIT signal for {Strategy} ({Instrument}) received, but no active open position was found. Skipping to prevent phantom sell order.",
                        signal.StrategyName, signal.Instrument);
                    return false;
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

            // If instrument is raw NIFTY or missing option type, resolve optimal ITM contract based on TargetExpiryPreference
            if (signal.Instrument == "NIFTY" || (!signal.Instrument.Contains("CE") && !signal.Instrument.Contains("PE")))
            {
                int targetExpiryIndex = globalSettings?.TargetExpiryPreference switch
                {
                    "CurrentWeek" => 0,
                    "TwoWeeksOut" => 2,
                    "Monthly" => 3,
                    _ => 1 // "NextWeek" by default (e.g. Oct 13)
                };
                int fallbackExpiryIndex = targetExpiryIndex == 1 ? 2 : 1;

                var contract = optionEngine.GetOptimalContract("NIFTY", bias, itmDistance: 1, expiryIndex: targetExpiryIndex)
                    ?? optionEngine.GetOptimalContract("NIFTY", bias, itmDistance: 1, expiryIndex: fallbackExpiryIndex)
                    ?? optionEngine.GetOptimalContract("NIFTY", bias, itmDistance: 0, expiryIndex: targetExpiryIndex)
                    ?? optionEngine.GetOptimalContract("NIFTY", bias, itmDistance: 1, expiryIndex: 0);

                if (!string.IsNullOrEmpty(contract))
                {
                    signal.Instrument = contract.Replace("NSE:", "");
                }
            }

            var normalisedInstrument = NormaliseSymbol(signal.Instrument);

            if (normalisedInstrument == "NIFTY")
            {
                _logger.LogError("OrderManagementService: Failed to resolve tradeable option contract for {Strategy}. Aborting to prevent invalid NIFTY index trade.", signal.StrategyName);
                return false;
            }

            // Always prioritize real-time live Market LTP at the exact moment of execution
            decimal? currentLiveLtp = optionEngine.ResolveOptionLtp(normalisedInstrument);
            decimal executionPrice = (currentLiveLtp.HasValue && currentLiveLtp.Value > 0)
                ? currentLiveLtp.Value
                : (signal.ExpectedPrice > 0 && signal.ExpectedPrice < 5000 
                    ? signal.ExpectedPrice 
                    : (signal.Price > 0 && signal.Price < 5000 ? signal.Price : 150m));

            _logger.LogInformation("OrderManagementService [{Mode}]: Executing order for {Action} {Quantity} {Instrument} at ₹{ExecutionPrice} ({Strategy})", 
                isPaperMode ? "PAPER" : "LIVE", signal.Action, signal.Quantity, normalisedInstrument, executionPrice, signal.StrategyName);

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
                    StrategyName = signal.StrategyName,
                    BuyAvg = 0m,
                    SellAvg = 0m,
                    RealizedProfit = 0m,
                    TotalBuyQty = 0,
                    TotalSellQty = 0,
                    TotalBuyValue = 0m,
                    TotalSellValue = 0m,
                    PeakLtp = executionPrice
                };
                dbContext.PaperPositions.Add(position);
            }

            var stratConfig = dbContext.StrategyConfigs.FirstOrDefault(s => s.StrategyName == signal.StrategyName);
            decimal trailingSl = (stratConfig != null && stratConfig.TrailingStopLossPoint > 0) 
                ? stratConfig.TrailingStopLossPoint 
                : (globalSettings?.TrailingStopLossPoint ?? 18.0m);

            decimal trailingActivation = (stratConfig != null && stratConfig.TrailingActivationPoint > 0)
                ? stratConfig.TrailingActivationPoint
                : (globalSettings?.TrailingActivationPoint ?? 15.0m);

            position.TotalBuyQty += signal.Quantity;
            position.TotalBuyValue = (position.TotalBuyValue ?? 0m) + (signal.Quantity * executionPrice);
            position.BuyAvg = position.TotalBuyQty > 0 ? (position.TotalBuyValue ?? 0m) / position.TotalBuyQty : executionPrice;
            position.NetQty += signal.Quantity;
            position.PeakLtp = executionPrice;
            position.TrailingStopLossPoint = trailingSl > 0 ? trailingSl : 18.0m;
            position.TrailingActivationPoint = trailingActivation > 0 ? trailingActivation : 15.0m;
            position.IsTrailingActive = false;
            position.IsPartialBooked = false;
            position.TargetPrice = signal.TargetPrice > 0 ? signal.TargetPrice : Math.Round(executionPrice + Math.Max(35.0m, executionPrice * 0.30m), 2);
            position.StopLossPrice = signal.StopLossPrice > 0 ? signal.StopLossPrice : Math.Round(Math.Max(5.0m, executionPrice - Math.Max(18.0m, executionPrice * 0.18m)), 2);
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

            // If Live Mode, dispatch real order to Fyers
            if (!isPaperMode && _fyersApiService.IsEngineRunning)
            {
                await _fyersApiService.PlaceOrderAsync(normalisedInstrument, "BUY", signal.Quantity, executionPrice);
            }

            return true;
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

            bool isPaperMode = globalSettings == null || string.Equals(globalSettings.TradeMode, "Paper", System.StringComparison.OrdinalIgnoreCase);
            bool hasChanges = false;
            foreach (var pos in openPositions)
            {
                decimal? ltp = optionEngine.ResolveOptionLtp(pos.Symbol);
                if (!ltp.HasValue || ltp.Value <= 0) continue;
                decimal currentLtp = ltp.Value;

                decimal activationPts = (pos.TrailingActivationPoint.HasValue && pos.TrailingActivationPoint.Value > 0)
                    ? pos.TrailingActivationPoint.Value
                    : (globalSettings?.TrailingActivationPoint ?? 15.0m);

                decimal trailingPts = (pos.TrailingStopLossPoint.HasValue && pos.TrailingStopLossPoint.Value > 0)
                    ? pos.TrailingStopLossPoint.Value
                    : (globalSettings?.TrailingStopLossPoint ?? 18.0m);

                decimal buyAvg = (pos.BuyAvg.HasValue && pos.BuyAvg.Value > 0) ? pos.BuyAvg.Value : currentLtp;
                if ((pos.PeakLtp ?? 0m) <= 0) pos.PeakLtp = buyAvg;

                // 1. Trailing Activation Check: Require meaningful profit buffer (e.g. +15 pts) before trailing activates
                if (!pos.IsTrailingActive && (currentLtp - buyAvg) >= activationPts)
                {
                    pos.IsTrailingActive = true;
                    hasChanges = true;
                    // Move hard SL to Breakeven (Cost) immediately
                    if (buyAvg > (pos.StopLossPrice ?? 0))
                    {
                        pos.StopLossPrice = buyAvg;
                        _logger.LogInformation("MonitorPositions: Trailing ACTIVATED for {Strategy} ({Symbol}). Locked Breakeven at ₹{SL}",
                            pos.StrategyName, pos.Symbol, pos.StopLossPrice);
                    }
                }

                // 2. Trailing Stop Loss: Only trail once activated and price prints a higher high
                if (pos.IsTrailingActive && currentLtp > (pos.PeakLtp ?? 0m))
                {
                    pos.PeakLtp = currentLtp;
                    hasChanges = true;

                    if (trailingPts > 0)
                    {
                        decimal peak = pos.PeakLtp ?? currentLtp;
                        decimal trailedSl = System.Math.Round(peak - trailingPts, 2);
                        // Once active, trailed SL should never drop below breakeven
                        trailedSl = System.Math.Max(trailedSl, buyAvg);
                        if (trailedSl > (pos.StopLossPrice ?? 0))
                        {
                            pos.StopLossPrice = trailedSl;
                            _logger.LogInformation("MonitorPositions: Trailed SL for {Strategy} ({Symbol}): Peak ₹{Peak}, New SL ₹{SL}",
                                pos.StrategyName, pos.Symbol, pos.PeakLtp, pos.StopLossPrice);
                        }
                    }
                }

                // 3. Target and Stop Loss evaluations
                bool isTargetHit = pos.TargetPrice.HasValue && pos.TargetPrice.Value > 0 && currentLtp >= pos.TargetPrice.Value;
                bool isSlHit = pos.StopLossPrice.HasValue && pos.StopLossPrice.Value > 0 && currentLtp <= pos.StopLossPrice.Value;
                decimal currentTradeLoss = (buyAvg - currentLtp) * pos.NetQty;
                bool isMaxLossHit = globalSettings != null && globalSettings.MaxLossPerTrade > 0 && currentTradeLoss >= globalSettings.MaxLossPerTrade;

                // 4. Partial Profit Booking: If Target 1 is hit on a multi-lot position (e.g. 2 lots = 130 qty)
                bool enablePartial = globalSettings == null || globalSettings.EnablePartialProfitBooking;
                if (isTargetHit && enablePartial && !pos.IsPartialBooked && pos.NetQty > 65)
                {
                    int partialQty = (pos.NetQty / 65 / 2) * 65;
                    if (partialQty == 0) partialQty = pos.NetQty / 2;

                    pos.TotalSellQty += partialQty;
                    pos.TotalSellValue = (pos.TotalSellValue ?? 0m) + (partialQty * currentLtp);
                    pos.SellAvg = pos.TotalSellQty > 0 ? (pos.TotalSellValue ?? 0m) / pos.TotalSellQty : currentLtp;
                    pos.NetQty -= partialQty;
                    decimal partialPnl = (currentLtp - buyAvg) * partialQty;
                    pos.RealizedProfit = (pos.RealizedProfit ?? 0m) + partialPnl;
                    pos.IsPartialBooked = true;
                    pos.IsTrailingActive = true;

                    // Move Stop Loss to guaranteed profit above Breakeven
                    pos.StopLossPrice = System.Math.Max(pos.StopLossPrice ?? 0, System.Math.Round(buyAvg + (activationPts * 0.5m), 2));
                    // Extend Target 2 to allow the runner lot to capture 150-250 point trends
                    decimal currentTgt = pos.TargetPrice ?? currentLtp;
                    decimal targetSpan = (currentTgt - buyAvg);
                    pos.TargetPrice = System.Math.Round(currentTgt + targetSpan, 2);
                    pos.UpdatedAt = System.DateTime.UtcNow;
                    hasChanges = true;

                    string partialReason = $"Target 1 Partial Booking (Booked {partialQty} Qty at ₹{currentLtp:F2}, Remaining {pos.NetQty} Qty Trailing to Target 2: ₹{pos.TargetPrice:F2})";
                    _logger.LogInformation("MonitorPositions: {Reason} for {Strategy} ({Symbol}). Locked Gain: ₹{PnL:F2}",
                        partialReason, pos.StrategyName, pos.Symbol, partialPnl);

                    dbContext.TradeLogs.Add(new TradeLog
                    {
                        OrderId = System.Guid.NewGuid().ToString("N").Substring(0, 10).ToUpper(),
                        StrategyName = pos.StrategyName ?? "Target 1 Partial Exit",
                        Instrument = pos.Symbol,
                        TradeType = "SELL",
                        Quantity = partialQty,
                        ExecutionPrice = currentLtp,
                        Timestamp = System.DateTime.UtcNow,
                        Status = "EXECUTED",
                        Reason = partialReason
                    });

                    // In Live mode, square off the partial lots
                    if (!isPaperMode && _fyersApiService.IsEngineRunning)
                    {
                        var (success, msg, orderId) = await _fyersApiService.PlaceOrderAsync(pos.Symbol, "SELL", partialQty, currentLtp);
                        _logger.LogInformation("MonitorPositions [LIVE]: Partial profit exit order sent to Fyers for {Symbol} ({Qty} qty): Success={Success}, Msg={Msg}",
                            pos.Symbol, partialQty, success, msg);
                    }
                    continue; // Keep the remaining position active as a runner!
                }

                if (isTargetHit || isSlHit || isMaxLossHit)
                {
                    string exitReason = isTargetHit 
                        ? $"Target Hit (LTP ₹{currentLtp:F2} >= Target ₹{pos.TargetPrice:F2})" 
                        : (isMaxLossHit 
                            ? $"Max Loss Per Trade Hit (Current Loss ₹{currentTradeLoss:F2} >= Limit ₹{globalSettings!.MaxLossPerTrade:F2})"
                            : $"Stop Loss Hit (LTP ₹{currentLtp:F2} <= SL ₹{pos.StopLossPrice:F2})");

                    int exitQty = pos.NetQty;
                    pos.TotalSellQty += exitQty;
                    pos.TotalSellValue = (pos.TotalSellValue ?? 0m) + (exitQty * currentLtp);
                    pos.SellAvg = pos.TotalSellQty > 0 ? (pos.TotalSellValue ?? 0m) / pos.TotalSellQty : currentLtp;
                    pos.NetQty = 0;
                    decimal tradePnl = (currentLtp - (pos.BuyAvg ?? 0m)) * exitQty;
                    pos.RealizedProfit = (pos.RealizedProfit ?? 0m) + tradePnl;
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
                    if (!isPaperMode && _fyersApiService.IsEngineRunning)
                    {
                        var (success, msg, orderId) = await _fyersApiService.PlaceOrderAsync(pos.Symbol, "SELL", exitQty, currentLtp);
                        _logger.LogInformation("MonitorPositions [LIVE]: Full exit order sent to Fyers for {Symbol} ({Qty} qty): Success={Success}, Msg={Msg}",
                            pos.Symbol, exitQty, success, msg);
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

            bool isPaperMode = globalSettings == null || string.Equals(globalSettings.TradeMode, "Paper", System.StringComparison.OrdinalIgnoreCase);

            if (isPaperMode)
            {
                var openPositions = dbContext.PaperPositions.Where(p => p.NetQty != 0).ToList();
                foreach (var pos in openPositions)
                {
                    decimal exitPrice = optionEngine.ResolveOptionLtp(pos.Symbol) ?? ((pos.BuyAvg ?? 0m) > 0 ? (pos.BuyAvg ?? 0m) : 150m);
                    int exitQty = System.Math.Abs(pos.NetQty);

                    if (pos.NetQty > 0) // Long -> SELL to close
                    {
                        pos.TotalSellQty += exitQty;
                        pos.TotalSellValue = (pos.TotalSellValue ?? 0m) + (exitQty * exitPrice);
                        pos.SellAvg = pos.TotalSellQty > 0 ? (pos.TotalSellValue ?? 0m) / pos.TotalSellQty : exitPrice;
                    }
                    else if (pos.NetQty < 0) // Short -> BUY to close
                    {
                        pos.TotalBuyQty += exitQty;
                        pos.TotalBuyValue = (pos.TotalBuyValue ?? 0m) + (exitQty * exitPrice);
                        pos.BuyAvg = pos.TotalBuyQty > 0 ? (pos.TotalBuyValue ?? 0m) / pos.TotalBuyQty : exitPrice;
                    }

                    pos.NetQty = 0;
                    pos.RealizedProfit = (pos.RealizedProfit ?? 0m) + ((pos.TotalSellValue ?? 0m) - (pos.TotalBuyValue ?? 0m));
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
                    _logger.LogError(ex, "AutoSquareOff [LIVE]: Error while closing live positions on broker.");
                }

                // Also synchronize local DB position tracking
                var localOpenPositions = dbContext.PaperPositions.Where(p => p.NetQty != 0).ToList();
                foreach (var pos in localOpenPositions)
                {
                    pos.NetQty = 0;
                    pos.UpdatedAt = System.DateTime.UtcNow;
                }
                if (localOpenPositions.Any())
                {
                    await dbContext.SaveChangesAsync();
                }
            }

            return closedCount;
        }
    }
}
