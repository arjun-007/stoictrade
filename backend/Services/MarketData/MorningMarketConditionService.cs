using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using StoicTrade.Api.Models;
using StoicTrade.Api.Services.Strategies;

namespace StoicTrade.Api.Services.MarketData
{
    public class MorningMarketConditionService
    {
        private readonly ILogger<MorningMarketConditionService> _logger;
        private readonly MarketDataAggregatorService _aggregator;
        private readonly MarketDataCache _marketCache;
        private readonly OptionChainAnalysisService _optionChainService;

        public MorningMarketConditionService(
            ILogger<MorningMarketConditionService> logger,
            MarketDataAggregatorService aggregator,
            MarketDataCache marketCache,
            OptionChainAnalysisService optionChainService)
        {
            _logger = logger;
            _aggregator = aggregator;
            _marketCache = marketCache;
            _optionChainService = optionChainService;
        }

        public MorningMarketCondition AnalyzeMorningCondition(string symbol = "NSE:NIFTY50-INDEX")
        {
            var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
            var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ist);
            var todayDateIst = nowIst.Date;

            var spot = _marketCache.GetSpotData("NIFTY");
            decimal currentPrice = (spot != null && spot.Price > 0) ? spot.Price : 24250.0m;

            var result = new MorningMarketCondition
            {
                Timestamp = DateTime.UtcNow,
                SpotPrice = currentPrice
            };

            // 1. Step 1: Pre-Market Context (Daily Range Compression NR4 / NR7 / Inside Day)
            AnalyzePreMarketCompression(symbol, todayDateIst, ist, result);

            // 2. Fetch today's 5-minute candles
            var fiveMinCandles = _aggregator.GetCandles(symbol, 5);
            var todayCandles = fiveMinCandles
                .Where(c => TimeZoneInfo.ConvertTimeFromUtc(c.Date, ist).Date == todayDateIst)
                .OrderBy(c => c.Date)
                .ToList();

            if (!todayCandles.Any())
            {
                // Fallback to recent 5m candles if off-hours / testing
                todayCandles = fiveMinCandles.TakeLast(12).ToList();
            }

            if (!todayCandles.Any())
            {
                result.MarketRegime = "DEVELOPING";
                result.RegimeLabel = "Awaiting Market Open";
                result.ActionDirective = "Market opens at 09:15 AM. Scanning opening candles (09:15–10:00 AM).";
                return result;
            }

            result.OpenPrice0915 = todayCandles.First().Open;
            result.SessionHigh = todayCandles.Max(c => c.High);
            result.SessionLow = todayCandles.Min(c => c.Low);

            // 3. Step 2: 15-Minute Rejection Rule (09:15 – 09:35 IST)
            Analyze15MinRejectionRule(todayCandles, result);

            // 4. Step 3: Anchor to VWAP
            AnalyzeVwapAnchor(todayCandles, currentPrice, result);

            // 5. Step 4: Option Strike Skew (OI Building)
            AnalyzeOptionStrikeSkew(currentPrice, result);

            // 6. Comprehensive Morning Option Buyer Trap Detector
            DetectBuyerTraps(symbol, todayCandles, currentPrice, result);

            // 7. Synthesis & Final Regime Determination
            SynthesizeRegime(result, nowIst);

            return result;
        }

        private void AnalyzePreMarketCompression(string symbol, DateTime todayDateIst, TimeZoneInfo ist, MorningMarketCondition result)
        {
            var dailyCandles = _aggregator.GetCandles(symbol, 1440);
            var pastDays = dailyCandles
                .Where(c => TimeZoneInfo.ConvertTimeFromUtc(c.Date, ist).Date < todayDateIst)
                .OrderBy(c => c.Date)
                .ToList();

            if (pastDays.Any())
            {
                var priorDay = pastDays.Last();
                result.PriorDayHigh = priorDay.High;
                result.PriorDayLow = priorDay.Low;
                result.PriorDayRange = priorDay.High - priorDay.Low;
            }

            if (pastDays.Count >= 7)
            {
                var priorDay = pastDays.Last();
                var priorRange = result.PriorDayRange;

                var last4 = pastDays.TakeLast(4).ToList();
                var last7 = pastDays.TakeLast(7).ToList();
                var dayBeforePrior = pastDays[pastDays.Count - 2];

                bool isNr7 = last7.All(d => (d.High - d.Low) >= priorRange);
                bool isNr4 = last4.All(d => (d.High - d.Low) >= priorRange);
                bool isInsideDay = priorDay.High <= dayBeforePrior.High && priorDay.Low >= dayBeforePrior.Low;

                if (isNr7)
                {
                    result.IsPriorDayCompressed = true;
                    result.CompressionType = "NR7 (Narrowest Range in 7 Days)";
                }
                else if (isNr4)
                {
                    result.IsPriorDayCompressed = true;
                    result.CompressionType = "NR4 (Narrowest Range in 4 Days)";
                }
                else if (isInsideDay)
                {
                    result.IsPriorDayCompressed = true;
                    result.CompressionType = "Inside Day Range Compression";
                }
                else
                {
                    result.IsPriorDayCompressed = false;
                    result.CompressionType = "Normal Volatility";
                }
            }
            else
            {
                result.IsPriorDayCompressed = false;
                result.CompressionType = "Normal Volatility";
            }
        }

        private void Analyze15MinRejectionRule(List<Candle> todayCandles, MorningMarketCondition result)
        {
            // First four 5m candles: 09:15, 09:20, 09:25, 09:30
            var openingCandles = todayCandles.Take(4).ToList();
            if (!openingCandles.Any()) return;

            decimal open0915 = openingCandles.First().Open;
            decimal maxLowerWickRatio = 0;
            decimal maxUpperWickRatio = 0;

            foreach (var candle in openingCandles)
            {
                decimal range = candle.High - candle.Low;
                if (range < 12.0m) continue;

                decimal bodyTop = Math.Max(candle.Open, candle.Close);
                decimal bodyBottom = Math.Min(candle.Open, candle.Close);

                decimal lowerWick = bodyBottom - candle.Low;
                decimal upperWick = candle.High - bodyTop;

                if (lowerWick >= 8.0m)
                {
                    decimal lowerRatio = lowerWick / range;
                    if (lowerRatio > maxLowerWickRatio) maxLowerWickRatio = lowerRatio;
                }

                if (upperWick >= 8.0m)
                {
                    decimal upperRatio = upperWick / range;
                    if (upperRatio > maxUpperWickRatio) maxUpperWickRatio = upperRatio;
                }
            }

            result.MaxRejectionWickRatio = Math.Round(Math.Max(maxLowerWickRatio, maxUpperWickRatio), 2);

            // Reclaim check: Did price push down then close back above 09:15 open?
            var minEarlyLow = openingCandles.Min(c => c.Low);
            var maxEarlyHigh = openingCandles.Max(c => c.High);
            var lastOpeningCandle = todayCandles.Count >= 3 ? todayCandles[Math.Min(3, todayCandles.Count - 1)] : openingCandles.Last();

            bool sweptLowAndReclaimed = maxLowerWickRatio >= 0.35m && (lastOpeningCandle.Close >= open0915);
            bool trappedHighAndFailed = maxUpperWickRatio >= 0.35m && (lastOpeningCandle.Close <= open0915);

            if (sweptLowAndReclaimed)
            {
                result.LiquidityRejection = "BULLISH_SWEEP";
            }
            else if (trappedHighAndFailed)
            {
                result.LiquidityRejection = "BEARISH_TRAP";
            }
            else
            {
                result.LiquidityRejection = "None";
            }
        }

        private void AnalyzeVwapAnchor(List<Candle> todayCandles, decimal currentPrice, MorningMarketCondition result)
        {
            decimal cumulativeVolume = 0;
            decimal cumulativeVolPrice = 0;
            var vwapPoints = new List<decimal>();

            foreach (var c in todayCandles)
            {
                decimal tp = (c.High + c.Low + c.Close) / 3.0m;
                decimal vol = c.Volume > 0 ? c.Volume : 1000m;

                cumulativeVolume += vol;
                cumulativeVolPrice += (tp * vol);

                decimal vwap = cumulativeVolume > 0 ? (cumulativeVolPrice / cumulativeVolume) : currentPrice;
                vwapPoints.Add(vwap);
            }

            decimal currentVwap = vwapPoints.Any() ? vwapPoints.Last() : currentPrice;
            result.Vwap = Math.Round(currentVwap, 2);

            // Calculate VWAP slope over last 3 candles
            decimal vwapSlope = 0;
            if (vwapPoints.Count >= 3)
            {
                vwapSlope = vwapPoints.Last() - vwapPoints[vwapPoints.Count - 3];
            }
            result.VwapSlope = Math.Round(vwapSlope, 2);

            // Crossings count to detect chop
            int vwapCrossings = 0;
            for (int i = 1; i < todayCandles.Count; i++)
            {
                bool prevAbove = todayCandles[i - 1].Close >= vwapPoints[i - 1];
                bool currAbove = todayCandles[i].Close >= vwapPoints[i];
                if (prevAbove != currAbove) vwapCrossings++;
            }

            if (vwapCrossings >= 3)
            {
                result.VwapStatus = "Whipsaw_Choppy";
            }
            else if (currentPrice >= currentVwap && vwapSlope >= -1.0m)
            {
                result.VwapStatus = "AboveVwap_Rising";
            }
            else if (currentPrice < currentVwap && vwapSlope <= 1.0m)
            {
                result.VwapStatus = "BelowVwap_Falling";
            }
            else
            {
                result.VwapStatus = "Flat";
            }
        }

        private void AnalyzeOptionStrikeSkew(decimal currentPrice, MorningMarketCondition result)
        {
            var metrics = _optionChainService.GetMetrics("NIFTY", currentPrice);
            result.Pcr = metrics.Pcr;
            result.InstitutionalFloorStrike = metrics.InstitutionalFloorStrike;
            result.InstitutionalCeilingStrike = metrics.InstitutionalCeilingStrike;

            if (metrics.Pcr >= 1.05m && metrics.IsBullishPutWritingDominant)
            {
                result.OptionOiBias = "Bullish_PutWriting";
            }
            else if (metrics.Pcr <= 0.90m)
            {
                result.OptionOiBias = "Bearish_CallWriting";
            }
            else
            {
                result.OptionOiBias = "ATM_Straddle_Pin";
            }
        }

        private void DetectBuyerTraps(string symbol, List<Candle> todayCandles, decimal currentPrice, MorningMarketCondition result)
        {
            if (!todayCandles.Any()) return;

            var spot = _marketCache.GetSpotData("NIFTY");
            var firstCandle = todayCandles.First();
            decimal open0915 = firstCandle.Open;
            decimal prevClose = spot != null && spot.PrevClose > 0 ? spot.PrevClose : open0915;
            decimal gap = open0915 - prevClose;

            // ── TRAP 1: Gap Exhaustion Fade (Open=High or Open=Low) ──
            if (gap >= 45m && (firstCandle.High - open0915 <= 5.0m) && (firstCandle.Close < open0915 || (todayCandles.Count >= 2 && todayCandles[1].Close < open0915)))
            {
                result.DetectedTraps.Add(new BuyerTrapAlert
                {
                    TrapId = "GAP_EXHAUSTION_HIGH",
                    Name = "Gap-Up Exhaustion Fade (Open=High Trap)",
                    Severity = "Critical",
                    Description = "Nifty opened with a large gap-up but made Open=High immediately. Institutional profit booking is capping the top.",
                    FootprintEvidence = $"Gap: +{gap:F1} pts | Open: ₹{open0915:F1} | High: ₹{firstCandle.High:F1} (within 5 pts). Red follow-through.",
                    BuyerDirective = "DO NOT BUY CALLS (CE). The gap is fading. Expect a pullback into VWAP or gap-fill."
                });
            }
            else if (gap <= -45m && (open0915 - firstCandle.Low <= 5.0m) && (firstCandle.Close > open0915 || (todayCandles.Count >= 2 && todayCandles[1].Close > open0915)))
            {
                result.DetectedTraps.Add(new BuyerTrapAlert
                {
                    TrapId = "GAP_EXHAUSTION_LOW",
                    Name = "Gap-Down Exhaustion Squeeze (Open=Low Trap)",
                    Severity = "Critical",
                    Description = "Nifty opened with a large gap-down but printed Open=Low immediately. Short covering is underway.",
                    FootprintEvidence = $"Gap: {gap:F1} pts | Open: ₹{open0915:F1} | Low: ₹{firstCandle.Low:F1} (within 5 pts). Green follow-through.",
                    BuyerDirective = "DO NOT BUY PUTS (PE). Shorts are getting squeezed. Expect a mean reversion toward VWAP."
                });
            }

            // ── TRAP 2: Heavyweight / Index Divergence (Nifty vs Bank Nifty / Reliance vs HDFC) ──
            var bankNifty = _marketCache.GetSpotData("BANKNIFTY") ?? _marketCache.GetSpotData("NSE:NIFTYBANK-INDEX");
            var rel = _marketCache.GetSpotData("RELIANCE-EQ") ?? _marketCache.GetSpotData("NSE:RELIANCE-EQ");
            var hdfc = _marketCache.GetSpotData("HDFCBANK-EQ") ?? _marketCache.GetSpotData("NSE:HDFCBANK-EQ");

            if (spot != null && bankNifty != null)
            {
                bool indexDivergence = (spot.ChangePercent >= 0.15m && bankNifty.ChangePercent <= -0.15m) ||
                                      (spot.ChangePercent <= -0.15m && bankNifty.ChangePercent >= 0.15m);
                if (indexDivergence)
                {
                    result.DetectedTraps.Add(new BuyerTrapAlert
                    {
                        TrapId = "INDEX_DIVERGENCE",
                        Name = "Index Polar Divergence (Nifty vs Bank Nifty Conflict)",
                        Severity = "High",
                        Description = "Nifty and Bank Nifty are moving in completely opposite directions, tearing the market apart.",
                        FootprintEvidence = $"Nifty: {spot.ChangePercent:F2}% vs Bank Nifty: {bankNifty.ChangePercent:F2}%. Breadth is fractured.",
                        BuyerDirective = "CHOP ALERT: When indices pull in opposite polarities, trends fail. Both CE & PE face severe stop-loss whipsaws."
                    });
                }
            }

            if (rel != null && hdfc != null)
            {
                bool heavyDivergence = (rel.ChangePercent >= 0.70m && hdfc.ChangePercent <= -0.70m) ||
                                       (rel.ChangePercent <= -0.70m && hdfc.ChangePercent >= 0.70m);
                if (heavyDivergence)
                {
                    result.DetectedTraps.Add(new BuyerTrapAlert
                    {
                        TrapId = "HEAVYWEIGHT_TUG_OF_WAR",
                        Name = "Heavyweight Tug-of-War (Reliance vs HDFC Bank)",
                        Severity = "High",
                        Description = "Reliance and HDFC Bank are fighting each other. Their index weights neutralize directional momentum.",
                        FootprintEvidence = $"Reliance: {rel.ChangePercent:F2}% vs HDFC Bank: {hdfc.ChangePercent:F2}%. Net index momentum is zero.",
                        BuyerDirective = "RANGE PIN TRAP: Index will whip around VWAP. Avoid breakout buying in options."
                    });
                }
            }

            // ── TRAP 3: Institutional Short Straddle Pin ──
            if (result.OptionOiBias == "ATM_Straddle_Pin" && result.Pcr >= 0.90m && result.Pcr <= 1.10m && todayCandles.Count >= 4)
            {
                decimal sessionRange = result.SessionHigh - result.SessionLow;
                if (sessionRange <= 45.0m)
                {
                    result.DetectedTraps.Add(new BuyerTrapAlert
                    {
                        TrapId = "STRADDLE_PIN",
                        Name = "Institutional Short Straddle Pin (The Coffin)",
                        Severity = "Critical",
                        Description = "Massive simultaneous Call and Put writing at ATM strikes. Institutional writers are actively defending a tight range.",
                        FootprintEvidence = $"ATM Range: {sessionRange:F1} pts | PCR: {result.Pcr:F2} (Neutral) | Floor: {result.InstitutionalFloorStrike}, Ceiling: {result.InstitutionalCeilingStrike}.",
                        BuyerDirective = "STAY IN CASH / DO NOT OVERTRADE: Rapid double-sided theta decay. False breakouts will burn both CE and PE."
                    });
                }
            }

            // ── TRAP 4: Volume Absorption / Climax Without Progress ──
            var earlyCandles = todayCandles.Take(4).ToList();
            if (earlyCandles.Count >= 2)
            {
                var highVolCandle = earlyCandles.FirstOrDefault(c => c.Volume > 0 && StrategyFilterHelper.CalculateRvol(todayCandles) >= 2.0m);
                if (highVolCandle != null)
                {
                    decimal bodySize = Math.Abs(highVolCandle.Close - highVolCandle.Open);
                    if (bodySize <= 12.0m)
                    {
                        result.DetectedTraps.Add(new BuyerTrapAlert
                        {
                            TrapId = "VOLUME_ABSORPTION",
                            Name = "Volume Absorption Climax (Hidden Distribution)",
                            Severity = "High",
                            Description = "Massive institutional volume (RVOL >= 2.0x) occurred on a tiny candle body. Large limit orders absorbed aggressive retail market orders.",
                            FootprintEvidence = $"Volume Spike: Body only {bodySize:F1} pts despite high volume. High probability of an opposite-side reversal.",
                            BuyerDirective = "DO NOT CHASE BREAKOUTS: High volume without price expansion is institutional absorption. Wait for 09:45 candle close."
                        });
                    }
                }
            }

            // ── TRAP 5: Double Inside-Bar Compression Squeeze ──
            if (earlyCandles.Count >= 3)
            {
                var c0 = earlyCandles[0];
                var c1 = earlyCandles[1];
                var c2 = earlyCandles[2];

                bool isInside1 = c1.High <= c0.High && c1.Low >= c0.Low;
                bool isInside2 = c2.High <= c1.High && c2.Low >= c1.Low;

                if (isInside1 && isInside2)
                {
                    result.DetectedTraps.Add(new BuyerTrapAlert
                    {
                        TrapId = "DOUBLE_INSIDE_BAR",
                        Name = "Double Inside-Bar Compression Trap",
                        Severity = "Moderate",
                        Description = "Opening 5m candles are coiled tightly inside the 09:15 mother candle. Immediate tick breakouts frequently fail.",
                        FootprintEvidence = $"Mother Candle (09:15): High ₹{c0.High:F1}, Low ₹{c0.Low:F1}. Candles 2 & 3 coiled inside.",
                        BuyerDirective = "AVOID TICK BREAKOUT ORDERS: Wait for a 15-minute candle to CLOSE outside the mother candle's boundary."
                    });
                }
            }

            // ── TRAP 6: Morning India VIX Slump / IV Crush ──
            var vix = _marketCache.GetSpotData("INDIAVIX") ?? _marketCache.GetSpotData("NSE:INDIAVIX-INDEX");
            if (vix != null && vix.ChangePercent <= -3.0m)
            {
                result.DetectedTraps.Add(new BuyerTrapAlert
                {
                    TrapId = "IV_CRUSH",
                    Name = "Morning IV Slump / Volatility Crush",
                    Severity = "High",
                    Description = "India VIX is collapsing sharply. Option premiums are actively melting across all strikes.",
                    FootprintEvidence = $"India VIX Change: {vix.ChangePercent:F2}%. Vega loss is actively destroying option buyer premiums.",
                    BuyerDirective = "AVOID OTM OPTIONS: Vega decay will overwhelm delta gains. Even if the index moves in your favor, option prices will drop."
                });
            }

            // ── TRAP 7: 10:00 AM PDH/PDL Sweep & Volume Confirmation ──
            var opening9Candles = todayCandles.Take(9).ToList();
            if (opening9Candles.Count >= 4)
            {
                decimal maxPrice = opening9Candles.Max(c => c.High);
                decimal minPrice = opening9Candles.Min(c => c.Low);
                decimal closeAt10am = opening9Candles.Last().Close;
                decimal openAt915 = opening9Candles.First().Open;
                decimal vwapAt10am = result.Vwap;

                // Elevated morning volume (RVOL >= 1.3 or first 3 bars volume elevated)
                bool elevatedVolume = StrategyFilterHelper.CalculateRvol(todayCandles) >= 1.3m || 
                                     opening9Candles.Take(3).Any(c => c.Volume > 0);

                // Benchmark PDH / PDL: if past day available use that, else fallback to session boundaries
                decimal pdh = result.PriorDayHigh > 0 ? result.PriorDayHigh : (openAt915 + 25m);
                decimal pdl = result.PriorDayLow > 0 ? result.PriorDayLow : (openAt915 - 25m);

                bool bearishTrap = (maxPrice > pdh) && (closeAt10am < openAt915) && (closeAt10am < vwapAt10am);
                bool bullishTrap = (minPrice < pdl) && (closeAt10am > openAt915) && (closeAt10am > vwapAt10am);

                if (bearishTrap && elevatedVolume)
                {
                    result.DetectedTraps.Add(new BuyerTrapAlert
                    {
                        TrapId = "BEARISH_DISTRIBUTION_DAY",
                        Name = "Bearish Distribution Day (PDH Breakout Trap)",
                        Severity = "Critical",
                        Description = "Morning spike swept above Prior Day High / Resistance, trapped breakout Call buyers on elevated volume, and closed below Open & VWAP by 10:00 AM.",
                        FootprintEvidence = $"High: ₹{maxPrice:F1} (probed above PDH ₹{pdh:F1}). 10:00 AM Close: ₹{closeAt10am:F1} < Open ₹{openAt915:F1} & VWAP ₹{vwapAt10am:F1}.",
                        BuyerDirective = "BEARISH DISTRIBUTION CONFIRMED: Expect mid-day stall and late cascade/DLC. STRICTLY AVOID CALLS (CE). Focus on Put (PE) retests below VWAP."
                    });
                }
                else if (bullishTrap && elevatedVolume)
                {
                    result.DetectedTraps.Add(new BuyerTrapAlert
                    {
                        TrapId = "BULLISH_ABSORPTION_DAY",
                        Name = "Bullish Absorption Day (PDL Liquidity Spring)",
                        Severity = "Critical",
                        Description = "Morning plunge swept below Prior Day Low / Support, absorbed panic selling on elevated volume, and closed above Open & VWAP by 10:00 AM.",
                        FootprintEvidence = $"Low: ₹{minPrice:F1} (dipped below PDL ₹{pdl:F1}). 10:00 AM Close: ₹{closeAt10am:F1} > Open ₹{openAt915:F1} & VWAP ₹{vwapAt10am:F1}.",
                        BuyerDirective = "BULLISH ABSORPTION CONFIRMED: Expect mid-day coil and afternoon gamma squeeze. STRICTLY AVOID PUTS (PE). Focus on Call (CE) pullbacks to VWAP."
                    });
                }
            }
        }

        private void SynthesizeRegime(MorningMarketCondition result, DateTime nowIst)
        {
            // If any Critical or High severity buyer trap is active, trigger Overtrading Shield
            if (result.DetectedTraps.Any())
            {
                var topTrap = result.DetectedTraps.OrderByDescending(t => t.Severity == "Critical" ? 3 : t.Severity == "High" ? 2 : 1).First();
                result.OvertradingShieldActive = true;
                result.WarningLevel = "Danger";
                result.MarketRegime = "TRAP_ACTIVE";
                result.RegimeLabel = $"⚠️ {topTrap.Name}";
                result.ActionDirective = topTrap.BuyerDirective;
                return;
            }

            bool isBullish = result.LiquidityRejection == "BULLISH_SWEEP" &&
                             result.VwapStatus == "AboveVwap_Rising" &&
                             result.OptionOiBias != "Bearish_CallWriting";

            bool isBearish = result.LiquidityRejection == "BEARISH_TRAP" &&
                             result.VwapStatus == "BelowVwap_Falling" &&
                             result.OptionOiBias != "Bullish_PutWriting";

            bool isChoppy = result.VwapStatus == "Whipsaw_Choppy" ||
                            (result.LiquidityRejection == "None" && result.OptionOiBias == "ATM_Straddle_Pin" && Math.Abs(result.SessionHigh - result.SessionLow) < 45m);

            if (isBullish)
            {
                result.MarketRegime = "BULLISH_TREND_DAY";
                result.RegimeLabel = "Bullish Trend Day (Liquidity Sweep Long)";
                result.ActionDirective = "BUY CALLS (CE) ONLY on pullbacks near VWAP (2nd weekly ITM). STRICTLY AVOID PE.";
                result.OvertradingShieldActive = false;
                result.WarningLevel = "Normal";
            }
            else if (isBearish)
            {
                result.MarketRegime = "BEARISH_TREND_DAY";
                result.RegimeLabel = "Bearish Trend Day (Distribution Cascade Short)";
                result.ActionDirective = "BUY PUTS (PE) ONLY on retests below VWAP (2nd weekly ITM). STRICTLY AVOID CE.";
                result.OvertradingShieldActive = false;
                result.WarningLevel = "Normal";
            }
            else if (isChoppy)
            {
                result.MarketRegime = "CHOPPY_RANGE_BOUND";
                result.RegimeLabel = "DANGER: High-Risk Choppy Market (Straddle Pin)";
                result.ActionDirective = "HIGH THETA DECAY TRAP! High probability of false breakouts. DO NOT OVERTRADE. Sit on hands or trade 1 lot max.";
                result.OvertradingShieldActive = true;
                result.WarningLevel = "Danger";
            }
            else
            {
                result.MarketRegime = "DEVELOPING";
                result.RegimeLabel = "Developing Market Structure";
                result.ActionDirective = "Opening range is stabilizing. Wait for VWAP retest before taking direction.";
                result.OvertradingShieldActive = false;
                result.WarningLevel = "Caution";
            }
        }
    }
}
