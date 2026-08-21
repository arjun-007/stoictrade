using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using StoicTrade.Api.Models;
using StoicTrade.Api.Services.MarketData;

namespace StoicTrade.Api.Services.Strategies
{
    /// <summary>
    /// Shared precision calculation utilities for relative volume, ATR risk targets, 
    /// higher timeframe gates, and intraday chop filters.
    /// </summary>
    public static class StrategyFilterHelper
    {
        public static decimal CalculateRvol(List<Candle> candles, int period = 10)
        {
            if (candles == null || candles.Count < period + 1) return 1.5m; // Default pass if initializing
            var lastCandle = candles.Last();
            var prevCandles = candles.Skip(candles.Count - period - 1).Take(period).ToList();
            decimal avgVol = prevCandles.Any() ? prevCandles.Average(c => c.Volume) : 1m;
            if (avgVol <= 0) return 1.5m;
            return Math.Round(lastCandle.Volume / avgVol, 2);
        }

        public static decimal CalculateAtr(List<Candle> candles, int period = 14)
        {
            if (candles == null || candles.Count < period) return 25.0m;
            try
            {
                var atrList = candles.GetAtr(period).ToList();
                var lastAtr = atrList.LastOrDefault()?.Atr;
                return lastAtr.HasValue ? Math.Round((decimal)lastAtr.Value, 2) : 25.0m;
            }
            catch
            {
                return 25.0m;
            }
        }

        public static bool IsMiddayChopHours()
        {
            var ist = TimeZoneHelper.GetIstTimeZone();
            var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ist).TimeOfDay;
            // 11:30 AM to 01:15 PM IST (Low volume chop & theta bleed window)
            var chopStart = new TimeSpan(11, 30, 0);
            var chopEnd = new TimeSpan(13, 15, 0);
            return nowIst >= chopStart && nowIst <= chopEnd;
        }

        public static bool CheckHtfGate(MarketDataAggregatorService aggregator, string action)
        {
            var htfCandles = aggregator.GetCandles("NSE:NIFTY50-INDEX", 15);
            if (htfCandles == null || htfCandles.Count < 10) return true; // Pass if initializing

            var last15m = htfCandles.Last();
            int emaPeriod = Math.Min(50, Math.Max(5, htfCandles.Count - 1));
            var emaList = htfCandles.GetEma(emaPeriod).ToList();
            var emaVal = emaList.LastOrDefault()?.Ema;
            
            var vwapList = htfCandles.GetVwap().ToList();
            var vwapVal = vwapList.LastOrDefault()?.Vwap;

            if (action == "BUY")
            {
                bool aboveEma = emaVal.HasValue && last15m.Close >= (decimal)emaVal.Value;
                bool aboveVwap = vwapVal.HasValue && last15m.Close >= (decimal)vwapVal.Value;
                return aboveEma || aboveVwap;
            }
            else if (action == "SELL")
            {
                bool belowEma = emaVal.HasValue && last15m.Close <= (decimal)emaVal.Value;
                bool belowVwap = vwapVal.HasValue && last15m.Close <= (decimal)vwapVal.Value;
                return belowEma || belowVwap;
            }

            return true;
        }
    }

    public class SupertrendStrategy : IStrategy
    {
        private readonly ILogger<SupertrendStrategy> _logger;
        private readonly MarketDataAggregatorService _aggregator;
        private readonly RedisService _redis;

        public string Name => "Supertrend Rider";

        public SupertrendStrategy(ILogger<SupertrendStrategy> logger, MarketDataAggregatorService aggregator, RedisService redis)
        {
            _logger = logger;
            _aggregator = aggregator;
            _redis = redis;
        }

        public async Task<Signal?> ExecuteAsync(StrategyConfig config, string marketData)
        {
            var candles = _aggregator.GetCandles("NSE:NIFTY50-INDEX", config.TimeframeMinutes);
            if (candles.Count < 20) return null;

            var paramsDoc = JsonDocument.Parse(config.AdditionalParamsJson ?? "{}");
            int atrPeriod = paramsDoc.RootElement.TryGetProperty("atrPeriod", out var atrElem) ? atrElem.GetInt32() : 10;
            int multiplier = paramsDoc.RootElement.TryGetProperty("multiplier", out var multElem) ? multElem.GetInt32() : 2;

            var stResults = candles.GetSuperTrend(atrPeriod, multiplier).ToList();
            var lastSt = stResults.LastOrDefault();
            var prevSt = stResults.Count >= 2 ? stResults[stResults.Count - 2] : null;
            var lastCandle = candles.Last();
            var prevCandle = candles.Count >= 2 ? candles[candles.Count - 2] : null;
            
            if (lastSt == null || lastCandle == null || lastSt.SuperTrend == null) return null;

            string stateKey = $"strategy_state_{config.Id}";
            var currentState = await _redis.GetValueAsync(stateKey) ?? "Idle";

            // Entry filter: Require two consecutive green closes above SuperTrend line or HTF trend alignment
            bool consecutiveGreen = prevCandle != null && prevSt != null && prevSt.SuperTrend != null &&
                                    prevCandle.Close > (decimal)prevSt.SuperTrend &&
                                    lastCandle.Close > (decimal)lastSt.SuperTrend;

            if (currentState == "Idle" && (consecutiveGreen || lastCandle.Close > (decimal)lastSt.SuperTrend))
            {
                // Midday chop check
                decimal rvol = StrategyFilterHelper.CalculateRvol(candles);
                if (StrategyFilterHelper.IsMiddayChopHours() && rvol < 2.0m)
                    return null;

                // HTF Gate
                if (!StrategyFilterHelper.CheckHtfGate(_aggregator, "BUY"))
                    return null;

                decimal atr = StrategyFilterHelper.CalculateAtr(candles);
                decimal sl = Math.Round(lastCandle.Close - (1.0m * atr), 2);
                decimal target = Math.Round(lastCandle.Close + (1.8m * atr), 2);

                await _redis.SetValueAsync(stateKey, "InPosition", TimeSpan.FromHours(8));
                return new Signal
                {
                    StrategyName = Name,
                    Instrument = "NIFTY",
                    Action = "BUY",
                    Quantity = 65,
                    OrderType = "MARKET",
                    Price = lastCandle.Close,
                    StopLossPrice = sl,
                    TargetPrice = target,
                    Atr = atr,
                    Rvol = rvol,
                    Priority = 2
                };
            }
            else if (currentState == "InPosition" && lastCandle.Close < (decimal)lastSt.SuperTrend)
            {
                await _redis.DeleteKeyAsync(stateKey);
                return new Signal
                {
                    StrategyName = Name,
                    Instrument = "NIFTY",
                    Action = "SELL",
                    Quantity = 65,
                    OrderType = "MARKET",
                    Price = lastCandle.Close,
                    Priority = 2
                };
            }
            return null;
        }
    }

    public class OrbStrategy : IStrategy
    {
        private readonly ILogger<OrbStrategy> _logger;
        private readonly MarketDataAggregatorService _aggregator;
        private readonly RedisService _redis;

        public string Name => "Opening Range Breakout (ORB)";

        public OrbStrategy(ILogger<OrbStrategy> logger, MarketDataAggregatorService aggregator, RedisService redis)
        {
            _logger = logger;
            _aggregator = aggregator;
            _redis = redis;
        }

        public async Task<Signal?> ExecuteAsync(StrategyConfig config, string marketData)
        {
            var candles = _aggregator.GetCandles("NSE:NIFTY50-INDEX", config.TimeframeMinutes);
            if (candles.Count < 5) return null;

            var paramsDoc = JsonDocument.Parse(config.AdditionalParamsJson ?? "{}");
            bool useVwap = paramsDoc.RootElement.TryGetProperty("useVwap", out var vwapElem) ? vwapElem.GetBoolean() : true;

            var ist = TimeZoneHelper.GetIstTimeZone();
            var todayIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ist).Date;
            var todayCandles = candles.Where(c => TimeZoneInfo.ConvertTimeFromUtc(c.Date, ist).Date == todayIst).ToList();
            if (todayCandles.Count < 3) return null; // Need at least 15m of opening candles

            var orbHigh = todayCandles.Take(3).Max(c => c.High);
            var orbLow = todayCandles.Take(3).Min(c => c.Low);
            
            var lastCandle = todayCandles.Last();
            
            decimal vwap = 0;
            if (useVwap)
            {
                var vwapResults = todayCandles.GetVwap().ToList();
                vwap = (decimal)(vwapResults.LastOrDefault()?.Vwap ?? 0);
            }

            string stateKey = $"strategy_state_{config.Id}";
            var currentState = await _redis.GetValueAsync(stateKey) ?? "Idle";

            if (currentState == "Idle")
            {
                // Breakout above ORB High with RVOL confirmation
                decimal rvol = StrategyFilterHelper.CalculateRvol(todayCandles, 10);
                if (rvol < 1.5m) return null; // RVOL Gate for Breakouts

                if (StrategyFilterHelper.IsMiddayChopHours() && rvol < 2.5m)
                    return null;

                if (lastCandle.Close > orbHigh && (!useVwap || lastCandle.Close > vwap))
                {
                    if (!StrategyFilterHelper.CheckHtfGate(_aggregator, "BUY"))
                        return null;

                    decimal atr = StrategyFilterHelper.CalculateAtr(candles);
                    decimal sl = orbLow > 0 ? orbLow : Math.Round(lastCandle.Close - (1.0m * atr), 2);
                    decimal target = Math.Round(lastCandle.Close + (1.8m * atr), 2);

                    await _redis.SetValueAsync(stateKey, "InPosition_Long", TimeSpan.FromHours(8));
                    return new Signal
                    {
                        StrategyName = Name,
                        Instrument = "NIFTY",
                        Action = "BUY",
                        Quantity = 65,
                        OrderType = "MARKET",
                        Price = lastCandle.Close,
                        StopLossPrice = sl,
                        TargetPrice = target,
                        Atr = atr,
                        Rvol = rvol,
                        Priority = 3
                    };
                }
            }
            else if (currentState == "InPosition_Long")
            {
                if (lastCandle.Close < orbLow)
                {
                    await _redis.DeleteKeyAsync(stateKey);
                    return new Signal
                    {
                        StrategyName = Name,
                        Instrument = "NIFTY",
                        Action = "SELL",
                        Quantity = 65,
                        OrderType = "MARKET",
                        Price = lastCandle.Close,
                        Priority = 3
                    };
                }
            }
            return null;
        }
    }

    public class EmaPullbackStrategy : IStrategy
    {
        private readonly ILogger<EmaPullbackStrategy> _logger;
        private readonly MarketDataAggregatorService _aggregator;
        private readonly RedisService _redis;

        public string Name => "EMA Pullback";

        public EmaPullbackStrategy(ILogger<EmaPullbackStrategy> logger, MarketDataAggregatorService aggregator, RedisService redis)
        {
            _logger = logger;
            _aggregator = aggregator;
            _redis = redis;
        }

        public async Task<Signal?> ExecuteAsync(StrategyConfig config, string marketData)
        {
            var candles = _aggregator.GetCandles("NSE:NIFTY50-INDEX", config.TimeframeMinutes);
            if (candles.Count < 30) return null;

            var paramsDoc = JsonDocument.Parse(config.AdditionalParamsJson ?? "{}");
            int fastPeriod = paramsDoc.RootElement.TryGetProperty("fastEma", out var fElem) ? fElem.GetInt32() : 9;
            int slowPeriod = paramsDoc.RootElement.TryGetProperty("slowEma", out var sElem) ? sElem.GetInt32() : 21;

            // Trend strength filter: ADX(14) > 20
            var adxResults = candles.GetAdx(14).ToList();
            var lastAdx = adxResults.LastOrDefault()?.Adx;
            if (lastAdx == null || lastAdx < 20.0) return null; // Reject choppy sideways markets

            var fastEma = candles.GetEma(fastPeriod).ToList();
            var slowEma = candles.GetEma(slowPeriod).ToList();

            var lastCandle = candles.Last();
            var lastFast = fastEma.Last();
            var lastSlow = slowEma.Last();
            
            if (lastFast.Ema == null || lastSlow.Ema == null) return null;

            string stateKey = $"strategy_state_{config.Id}";
            var currentState = await _redis.GetValueAsync(stateKey) ?? "Idle";

            // Uptrend: Fast > Slow. Pullback: Low dips below Fast EMA but closes above.
            if (currentState == "Idle" && lastFast.Ema > lastSlow.Ema)
            {
                if (lastCandle.Low <= (decimal)lastFast.Ema && lastCandle.Close > (decimal)lastFast.Ema)
                {
                    if (StrategyFilterHelper.IsMiddayChopHours()) return null;
                    if (!StrategyFilterHelper.CheckHtfGate(_aggregator, "BUY")) return null;

                    decimal atr = StrategyFilterHelper.CalculateAtr(candles);
                    decimal sl = Math.Round(lastCandle.Close - (1.0m * atr), 2);
                    decimal target = Math.Round(lastCandle.Close + (1.8m * atr), 2);
                    decimal rvol = StrategyFilterHelper.CalculateRvol(candles);

                    await _redis.SetValueAsync(stateKey, "InPosition", TimeSpan.FromHours(8));
                    return new Signal
                    {
                        StrategyName = Name,
                        Instrument = "NIFTY",
                        Action = "BUY",
                        Quantity = 65,
                        OrderType = "MARKET",
                        Price = lastCandle.Close,
                        StopLossPrice = sl,
                        TargetPrice = target,
                        Atr = atr,
                        Rvol = rvol,
                        Priority = 1
                    };
                }
            }
            else if (currentState == "InPosition")
            {
                if (lastCandle.Close < (decimal)lastSlow.Ema)
                {
                    await _redis.DeleteKeyAsync(stateKey);
                    return new Signal
                    {
                        StrategyName = Name,
                        Instrument = "NIFTY",
                        Action = "SELL",
                        Quantity = 65,
                        OrderType = "MARKET",
                        Price = lastCandle.Close,
                        Priority = 1
                    };
                }
            }
            return null;
        }
    }

    public class BollingerSqueezeStrategy : IStrategy
    {
        private readonly ILogger<BollingerSqueezeStrategy> _logger;
        private readonly MarketDataAggregatorService _aggregator;
        private readonly RedisService _redis;

        public string Name => "Bollinger Volatility Squeeze";

        public BollingerSqueezeStrategy(ILogger<BollingerSqueezeStrategy> logger, MarketDataAggregatorService aggregator, RedisService redis)
        {
            _logger = logger;
            _aggregator = aggregator;
            _redis = redis;
        }

        public async Task<Signal?> ExecuteAsync(StrategyConfig config, string marketData)
        {
            var candles = _aggregator.GetCandles("NSE:NIFTY50-INDEX", config.TimeframeMinutes);
            if (candles.Count < 30) return null;

            var paramsDoc = JsonDocument.Parse(config.AdditionalParamsJson ?? "{}");
            int period = paramsDoc.RootElement.TryGetProperty("bbPeriod", out var pElem) ? pElem.GetInt32() : 20;
            double stdDev = paramsDoc.RootElement.TryGetProperty("bbStdDev", out var dElem) ? dElem.GetDouble() : 2.0;

            var bb = candles.GetBollingerBands(period, stdDev).ToList();
            var lastBb = bb.Last();
            var lastCandle = candles.Last();
            
            if (lastBb.UpperBand == null || lastBb.LowerBand == null || lastBb.Sma == null) return null;

            string stateKey = $"strategy_state_{config.Id}";
            var currentState = await _redis.GetValueAsync(stateKey) ?? "Idle";

            // Squeeze Breakout: Solid candle body closes outside the upper band
            bool solidBodyBreakout = lastCandle.Close > (decimal)lastBb.UpperBand && lastCandle.Open >= (decimal)lastBb.Sma;

            if (currentState == "Idle" && solidBodyBreakout)
            {
                decimal rvol = StrategyFilterHelper.CalculateRvol(candles);
                if (rvol < 1.5m) return null; // Reject low-volume false expansion wicks

                if (StrategyFilterHelper.IsMiddayChopHours() && rvol < 2.5m) return null;
                if (!StrategyFilterHelper.CheckHtfGate(_aggregator, "BUY")) return null;

                decimal atr = StrategyFilterHelper.CalculateAtr(candles);
                decimal sl = Math.Round(lastCandle.Close - (1.0m * atr), 2);
                decimal target = Math.Round(lastCandle.Close + (1.8m * atr), 2);

                await _redis.SetValueAsync(stateKey, "InPosition", TimeSpan.FromHours(8));
                return new Signal
                {
                    StrategyName = Name,
                    Instrument = "NIFTY",
                    Action = "BUY",
                    Quantity = 65,
                    OrderType = "MARKET",
                    Price = lastCandle.Close,
                    StopLossPrice = sl,
                    TargetPrice = target,
                    Atr = atr,
                    Rvol = rvol,
                    Priority = 2
                };
            }
            else if (currentState == "InPosition" && lastCandle.Close < (decimal)lastBb.Sma)
            {
                await _redis.DeleteKeyAsync(stateKey);
                return new Signal
                {
                    StrategyName = Name,
                    Instrument = "NIFTY",
                    Action = "SELL",
                    Quantity = 65,
                    OrderType = "MARKET",
                    Price = lastCandle.Close,
                    Priority = 2
                };
            }
            return null;
        }
    }

    public class Nr7Strategy : IStrategy
    {
        private readonly ILogger<Nr7Strategy> _logger;
        private readonly MarketDataAggregatorService _aggregator;
        private readonly RedisService _redis;

        public string Name => "NR7 Breakout";

        public Nr7Strategy(ILogger<Nr7Strategy> logger, MarketDataAggregatorService aggregator, RedisService redis)
        {
            _logger = logger;
            _aggregator = aggregator;
            _redis = redis;
        }

        public async Task<Signal?> ExecuteAsync(StrategyConfig config, string marketData)
        {
            var candles = _aggregator.GetCandles("NSE:NIFTY50-INDEX", config.TimeframeMinutes);
            if (candles.Count < 10) return null;

            string stateKey = $"strategy_state_{config.Id}";
            var currentState = await _redis.GetValueAsync(stateKey) ?? "Idle";

            // NR7 logic
            var last7 = candles.Skip(candles.Count - 8).Take(7).ToList();
            var ranges = last7.Select(c => c.High - c.Low).ToList();
            
            bool isNr7 = ranges.Last() == ranges.Min();
            var nr7High = last7.Last().High;
            var nr7Low = last7.Last().Low;
            
            var currentCandle = candles.Last();

            if (currentState == "Idle" && isNr7 && currentCandle.Close > nr7High)
            {
                decimal rvol = StrategyFilterHelper.CalculateRvol(candles);
                if (rvol < 1.5m) return null; // Reject low-volume traps

                if (StrategyFilterHelper.IsMiddayChopHours() && rvol < 2.5m) return null;
                if (!StrategyFilterHelper.CheckHtfGate(_aggregator, "BUY")) return null;

                decimal atr = StrategyFilterHelper.CalculateAtr(candles);
                decimal sl = nr7Low > 0 ? nr7Low : Math.Round(currentCandle.Close - (1.0m * atr), 2);
                decimal target = Math.Round(currentCandle.Close + (1.8m * atr), 2);

                await _redis.SetValueAsync(stateKey, "InPosition", TimeSpan.FromHours(8));
                return new Signal
                {
                    StrategyName = Name,
                    Instrument = "NIFTY",
                    Action = "BUY",
                    Quantity = 65,
                    OrderType = "MARKET",
                    Price = currentCandle.Close,
                    StopLossPrice = sl,
                    TargetPrice = target,
                    Atr = atr,
                    Rvol = rvol,
                    Priority = 3
                };
            }
            else if (currentState == "InPosition" && currentCandle.Close < nr7Low)
            {
                await _redis.DeleteKeyAsync(stateKey);
                return new Signal
                {
                    StrategyName = Name,
                    Instrument = "NIFTY",
                    Action = "SELL",
                    Quantity = 65,
                    OrderType = "MARKET",
                    Price = currentCandle.Close,
                    Priority = 3
                };
            }
            return null;
        }
    }

    public class MacdStrategy : IStrategy
    {
        private readonly ILogger<MacdStrategy> _logger;
        private readonly MarketDataAggregatorService _aggregator;
        private readonly RedisService _redis;

        public string Name => "MACD Zero-Line";

        public MacdStrategy(ILogger<MacdStrategy> logger, MarketDataAggregatorService aggregator, RedisService redis)
        {
            _logger = logger;
            _aggregator = aggregator;
            _redis = redis;
        }

        public async Task<Signal?> ExecuteAsync(StrategyConfig config, string marketData)
        {
            var candles = _aggregator.GetCandles("NSE:NIFTY50-INDEX", config.TimeframeMinutes);
            if (candles.Count < 35) return null;

            var paramsDoc = JsonDocument.Parse(config.AdditionalParamsJson ?? "{}");
            int fast = paramsDoc.RootElement.TryGetProperty("macdFast", out var fElem) ? fElem.GetInt32() : 12;
            int slow = paramsDoc.RootElement.TryGetProperty("macdSlow", out var sElem) ? sElem.GetInt32() : 26;
            int sig = paramsDoc.RootElement.TryGetProperty("macdSignal", out var sigElem) ? sigElem.GetInt32() : 9;

            var macd = candles.GetMacd(fast, slow, sig).ToList();
            var prevMacd = macd[macd.Count - 2];
            var lastMacd = macd.Last();
            var lastCandle = candles.Last();

            if (lastMacd.Macd == null || lastMacd.Signal == null || prevMacd.Macd == null) return null;

            // Momentum confirmation: RSI(14) > 50
            var rsiList = candles.GetRsi(14).ToList();
            var lastRsi = rsiList.LastOrDefault()?.Rsi;
            if (lastRsi == null || lastRsi < 50.0) return null;

            // VWAP confirmation
            var vwapList = candles.GetVwap().ToList();
            var lastVwap = vwapList.LastOrDefault()?.Vwap;
            if (lastVwap.HasValue && lastCandle.Close < (decimal)lastVwap.Value) return null;

            string stateKey = $"strategy_state_{config.Id}";
            var currentState = await _redis.GetValueAsync(stateKey) ?? "Idle";

            // Crossover above zero
            bool crossover = prevMacd.Macd < prevMacd.Signal && lastMacd.Macd > lastMacd.Signal;

            if (currentState == "Idle" && crossover && lastMacd.Macd < 0)
            {
                if (StrategyFilterHelper.IsMiddayChopHours()) return null;
                if (!StrategyFilterHelper.CheckHtfGate(_aggregator, "BUY")) return null;

                decimal atr = StrategyFilterHelper.CalculateAtr(candles);
                decimal sl = Math.Round(lastCandle.Close - (1.0m * atr), 2);
                decimal target = Math.Round(lastCandle.Close + (1.8m * atr), 2);
                decimal rvol = StrategyFilterHelper.CalculateRvol(candles);

                await _redis.SetValueAsync(stateKey, "InPosition", TimeSpan.FromHours(8));
                return new Signal
                {
                    StrategyName = Name,
                    Instrument = "NIFTY",
                    Action = "BUY",
                    Quantity = 65,
                    OrderType = "MARKET",
                    Price = lastCandle.Close,
                    StopLossPrice = sl,
                    TargetPrice = target,
                    Atr = atr,
                    Rvol = rvol,
                    Priority = 1
                };
            }
            else if (currentState == "InPosition" && lastMacd.Macd < lastMacd.Signal)
            {
                await _redis.DeleteKeyAsync(stateKey);
                return new Signal
                {
                    StrategyName = Name,
                    Instrument = "NIFTY",
                    Action = "SELL",
                    Quantity = 65,
                    OrderType = "MARKET",
                    Price = lastCandle.Close,
                    Priority = 1
                };
            }
            return null;
        }
    }
}
