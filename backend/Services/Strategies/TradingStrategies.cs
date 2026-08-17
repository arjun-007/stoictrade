using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using StoicTrade.Api.Models;
using StoicTrade.Api.Services.MarketData;

namespace StoicTrade.Api.Services.Strategies
{
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
            int atrPeriod = paramsDoc.RootElement.TryGetProperty("atrPeriod", out var atrElem) ? atrElem.GetInt32() : 14;
            int multiplier = paramsDoc.RootElement.TryGetProperty("multiplier", out var multElem) ? multElem.GetInt32() : 3;

            var stResults = candles.GetSuperTrend(atrPeriod, multiplier).ToList();
            var lastSt = stResults.LastOrDefault();
            var lastCandle = candles.LastOrDefault();
            
            if (lastSt == null || lastCandle == null || lastSt.SuperTrend == null) return null;

            string stateKey = $"strategy_state_{config.Id}";
            var currentState = await _redis.GetValueAsync(stateKey) ?? "Idle";

            if (currentState == "Idle" && lastCandle.Close > (decimal)lastSt.SuperTrend)
            {
                await _redis.SetValueAsync(stateKey, "InPosition", TimeSpan.FromHours(8));
                return new Signal { StrategyName = Name, Instrument = "NIFTY", Action = "BUY", Quantity = 50, OrderType = "MARKET", Price = lastCandle.Close, Priority = 2 };
            }
            else if (currentState == "InPosition" && lastCandle.Close < (decimal)lastSt.SuperTrend)
            {
                await _redis.DeleteKeyAsync(stateKey);
                return new Signal { StrategyName = Name, Instrument = "NIFTY", Action = "SELL", Quantity = 50, OrderType = "MARKET", Price = lastCandle.Close, Priority = 2 };
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
            if (candles.Count < 1) return null;

            var paramsDoc = JsonDocument.Parse(config.AdditionalParamsJson ?? "{}");
            bool useVwap = paramsDoc.RootElement.TryGetProperty("useVwap", out var vwapElem) ? vwapElem.GetBoolean() : true;

            // Simple ORB logic: Find high/low of first 15 mins of today
            var today = DateTime.UtcNow.Date;
            var todayCandles = candles.Where(c => c.Date >= today).ToList();
            if (todayCandles.Count < 3) return null; // Need at least some candles

            var orbHigh = todayCandles.Take(3).Max(c => c.High); // first 3 5-min candles = 15 mins
            var orbLow = todayCandles.Take(3).Min(c => c.Low);
            
            var lastCandle = todayCandles.Last();
            if (lastCandle.Date < today.AddMinutes(15)) return null; // Wait for ORB to form

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
                if (lastCandle.Close > orbHigh && (!useVwap || lastCandle.Close > vwap))
                {
                    await _redis.SetValueAsync(stateKey, "InPosition_Long", TimeSpan.FromHours(8));
                    return new Signal { StrategyName = Name, Instrument = "NIFTY", Action = "BUY", Quantity = 50, OrderType = "MARKET", Price = lastCandle.Close, Priority = 3 };
                }
            }
            else if (currentState == "InPosition_Long")
            {
                // Stoploss hits ORB low or target
                if (lastCandle.Close < orbLow)
                {
                    await _redis.DeleteKeyAsync(stateKey);
                    return new Signal { StrategyName = Name, Instrument = "NIFTY", Action = "SELL", Quantity = 50, OrderType = "MARKET", Price = lastCandle.Close, Priority = 3 };
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
                    await _redis.SetValueAsync(stateKey, "InPosition", TimeSpan.FromHours(8));
                    return new Signal { StrategyName = Name, Instrument = "NIFTY", Action = "BUY", Quantity = 50, OrderType = "MARKET", Price = lastCandle.Close, Priority = 1 };
                }
            }
            else if (currentState == "InPosition")
            {
                // Stoploss: closes below slow EMA
                if (lastCandle.Close < (decimal)lastSlow.Ema)
                {
                    await _redis.DeleteKeyAsync(stateKey);
                    return new Signal { StrategyName = Name, Instrument = "NIFTY", Action = "SELL", Quantity = 50, OrderType = "MARKET", Price = lastCandle.Close, Priority = 1 };
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
            
            if (lastBb.UpperBand == null || lastBb.LowerBand == null) return null;

            string stateKey = $"strategy_state_{config.Id}";
            var currentState = await _redis.GetValueAsync(stateKey) ?? "Idle";

            // Squeeze Breakout: Close crosses upper band
            if (currentState == "Idle" && lastCandle.Close > (decimal)lastBb.UpperBand)
            {
                await _redis.SetValueAsync(stateKey, "InPosition", TimeSpan.FromHours(8));
                return new Signal { StrategyName = Name, Instrument = "NIFTY", Action = "BUY", Quantity = 50, OrderType = "MARKET", Price = lastCandle.Close, Priority = 2 };
            }
            else if (currentState == "InPosition" && lastCandle.Close < (decimal)lastBb.Sma)
            {
                await _redis.DeleteKeyAsync(stateKey);
                return new Signal { StrategyName = Name, Instrument = "NIFTY", Action = "SELL", Quantity = 50, OrderType = "MARKET", Price = lastCandle.Close, Priority = 2 };
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
            var last7 = candles.Skip(candles.Count - 8).Take(7).ToList(); // Get previous 7 closed candles
            var ranges = last7.Select(c => c.High - c.Low).ToList();
            
            bool isNr7 = ranges.Last() == ranges.Min();
            var nr7High = last7.Last().High;
            var nr7Low = last7.Last().Low;
            
            var currentCandle = candles.Last();

            if (currentState == "Idle" && isNr7 && currentCandle.Close > nr7High)
            {
                await _redis.SetValueAsync(stateKey, "InPosition", TimeSpan.FromHours(8));
                return new Signal { StrategyName = Name, Instrument = "NIFTY", Action = "BUY", Quantity = 50, OrderType = "MARKET", Price = currentCandle.Close, Priority = 3 };
            }
            else if (currentState == "InPosition" && currentCandle.Close < nr7Low)
            {
                await _redis.DeleteKeyAsync(stateKey);
                return new Signal { StrategyName = Name, Instrument = "NIFTY", Action = "SELL", Quantity = 50, OrderType = "MARKET", Price = currentCandle.Close, Priority = 3 };
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

            string stateKey = $"strategy_state_{config.Id}";
            var currentState = await _redis.GetValueAsync(stateKey) ?? "Idle";

            // Crossover above zero
            bool crossover = prevMacd.Macd < prevMacd.Signal && lastMacd.Macd > lastMacd.Signal;

            if (currentState == "Idle" && crossover && lastMacd.Macd < 0)
            {
                await _redis.SetValueAsync(stateKey, "InPosition", TimeSpan.FromHours(8));
                return new Signal { StrategyName = Name, Instrument = "NIFTY", Action = "BUY", Quantity = 50, OrderType = "MARKET", Price = lastCandle.Close, Priority = 1 };
            }
            else if (currentState == "InPosition" && lastMacd.Macd < lastMacd.Signal)
            {
                await _redis.DeleteKeyAsync(stateKey);
                return new Signal { StrategyName = Name, Instrument = "NIFTY", Action = "SELL", Quantity = 50, OrderType = "MARKET", Price = lastCandle.Close, Priority = 1 };
            }
            return null;
        }
    }
}
