using System;
using System.Collections.Generic;
using System.Linq;
using StoicTrade.Api.Models;

namespace StoicTrade.Api.Services.Strategies
{
    public class FairValueGap
    {
        public bool IsBullish { get; set; }
        public decimal TopPrice { get; set; }
        public decimal BottomPrice { get; set; }
        public decimal Equilibrium => Math.Round((TopPrice + BottomPrice) / 2m, 2);
        public DateTime CreatedAt { get; set; }
        public bool IsMitigated { get; set; }
    }

    public class LiquiditySweepResult
    {
        public bool IsSweepDetected { get; set; }
        public bool IsBullishSpring { get; set; }
        public decimal SweepPrice { get; set; }
        public decimal ReclaimedSupportLevel { get; set; }
        public decimal Rvol { get; set; }
        public decimal StopLossPrice { get; set; }
    }

    public class VolumeProfileResult
    {
        public decimal PocPrice { get; set; } // Point of Control (Highest Volume Price Bucket)
        public decimal ValueAreaHigh { get; set; }
        public decimal ValueAreaLow { get; set; }
        public bool IsPriceAbovePoc { get; set; }
    }

    /// <summary>
    /// Algorithmic helper for Smart Money Concepts (SMC), Volume Spread Analysis (VSA),
    /// Fair Value Gaps (FVG), Order Blocks, and Wyckoff Spring Liquidity Sweeps.
    /// </summary>
    public static class SmartMoneyStructureHelper
    {
        /// <summary>
        /// Detects active Fair Value Gaps (FVGs) in the recent candle series.
        /// A Bullish FVG exists when Candle[t-2].High < Candle[t].Low (leaving an unfilled imbalance gap in Candle[t-1]).
        /// </summary>
        public static List<FairValueGap> DetectFairValueGaps(List<Candle> candles, decimal minGapPoints = 5.0m)
        {
            var fvgs = new List<FairValueGap>();
            if (candles == null || candles.Count < 4) return fvgs;

            for (int i = 2; i < candles.Count - 1; i++)
            {
                var cPrev2 = candles[i - 2];
                var cCurr = candles[i];

                // Bullish FVG: Space between Candle 1 High and Candle 3 Low
                if (cCurr.Low > cPrev2.High && (cCurr.Low - cPrev2.High) >= minGapPoints)
                {
                    decimal top = cCurr.Low;
                    decimal bottom = cPrev2.High;

                    // Check if subsequent candles have mitigated (retested) this gap
                    bool mitigated = false;
                    for (int j = i + 1; j < candles.Count; j++)
                    {
                        if (candles[j].Low <= bottom)
                        {
                            mitigated = true; // Fully closed
                            break;
                        }
                    }

                    if (!mitigated)
                    {
                        fvgs.Add(new FairValueGap
                        {
                            IsBullish = true,
                            TopPrice = top,
                            BottomPrice = bottom,
                            CreatedAt = candles[i - 1].Date,
                            IsMitigated = false
                        });
                    }
                }
                // Bearish FVG: Space between Candle 1 Low and Candle 3 High
                else if (cPrev2.Low > cCurr.High && (cPrev2.Low - cCurr.High) >= minGapPoints)
                {
                    decimal top = cPrev2.Low;
                    decimal bottom = cCurr.High;

                    bool mitigated = false;
                    for (int j = i + 1; j < candles.Count; j++)
                    {
                        if (candles[j].High >= top)
                        {
                            mitigated = true;
                            break;
                        }
                    }

                    if (!mitigated)
                    {
                        fvgs.Add(new FairValueGap
                        {
                            IsBullish = false,
                            TopPrice = top,
                            BottomPrice = bottom,
                            CreatedAt = candles[i - 1].Date,
                            IsMitigated = false
                        });
                    }
                }
            }

            return fvgs;
        }

        /// <summary>
        /// Detects Wyckoff Spring: Price pierces below a key prior swing support, triggers stop losses,
        /// and sharply closes back above the support line on high relative volume (institutional absorption).
        /// </summary>
        public static LiquiditySweepResult DetectWyckoffSpring(List<Candle> candles, int lookback = 20, decimal minRvol = 1.8m)
        {
            if (candles == null || candles.Count < lookback + 2)
            {
                return new LiquiditySweepResult { IsSweepDetected = false };
            }

            var recent = candles.Take(candles.Count - 1).ToList();
            var lastCandle = candles.Last();

            // Find key swing support level within lookback (excluding the latest candle)
            var lookbackCandles = recent.Skip(Math.Max(0, recent.Count - lookback)).ToList();
            decimal supportLevel = lookbackCandles.Min(c => c.Low);

            decimal rvol = StrategyFilterHelper.CalculateRvol(candles, 10);

            // Bullish Wyckoff Spring Signature:
            // 1. Current or previous candle breached the support level (Low < supportLevel)
            // 2. Current candle closes firmly back above the support level (Close > supportLevel)
            // 3. High RVOL (smart money stepping in to absorb retail stop-market sell orders)
            // 4. Candle body is strong (Close in upper 40% of the candle's total range)
            decimal candleRange = lastCandle.High - lastCandle.Low;
            bool strongClose = candleRange > 0 && (lastCandle.Close - lastCandle.Low) / candleRange >= 0.50m;

            if (lastCandle.Low < supportLevel && lastCandle.Close > supportLevel && rvol >= minRvol && strongClose)
            {
                return new LiquiditySweepResult
                {
                    IsSweepDetected = true,
                    IsBullishSpring = true,
                    SweepPrice = lastCandle.Low,
                    ReclaimedSupportLevel = supportLevel,
                    Rvol = rvol,
                    StopLossPrice = Math.Round(lastCandle.Low - 5.0m, 2) // SL just below the sweep wick
                };
            }

            // Check 2-candle sweep: Previous candle dipped below, current candle reclaimed
            var prevCandle = recent.LastOrDefault();
            if (prevCandle != null && prevCandle.Low < supportLevel && prevCandle.Close <= supportLevel &&
                lastCandle.Close > supportLevel && rvol >= minRvol && strongClose)
            {
                decimal sweepLow = Math.Min(prevCandle.Low, lastCandle.Low);
                return new LiquiditySweepResult
                {
                    IsSweepDetected = true,
                    IsBullishSpring = true,
                    SweepPrice = sweepLow,
                    ReclaimedSupportLevel = supportLevel,
                    Rvol = rvol,
                    StopLossPrice = Math.Round(sweepLow - 5.0m, 2)
                };
            }

            return new LiquiditySweepResult { IsSweepDetected = false };
        }

        /// <summary>
        /// Volume Spread Analysis (VSA) - Checks for Narrow Spread + High Volume Absorption at Multi-period Lows
        /// </summary>
        public static bool IsVsaAbsorptionAtSupport(List<Candle> candles, int period = 14)
        {
            if (candles == null || candles.Count < period) return false;

            var last = candles.Last();
            var prevs = candles.Skip(candles.Count - period - 1).Take(period).ToList();

            decimal avgSpread = prevs.Average(c => c.High - c.Low);
            decimal currentSpread = last.High - last.Low;
            decimal rvol = StrategyFilterHelper.CalculateRvol(candles, period);

            // Narrow spread (< 75% of average spread) with high volume (RVOL > 1.8) near the bottom of recent range
            decimal lowestLow = prevs.Min(c => c.Low);
            bool nearSupport = Math.Abs(last.Low - lowestLow) <= (avgSpread * 0.5m);

            return currentSpread <= (avgSpread * 0.75m) && rvol >= 1.8m && nearSupport;
        }

        /// <summary>
        /// Computes Intraday Volume Profile and Point of Control (POC)
        /// </summary>
        public static VolumeProfileResult CalculateVolumeProfile(List<Candle> candles, decimal bucketSize = 10m)
        {
            if (candles == null || !candles.Any())
            {
                return new VolumeProfileResult { PocPrice = 24000m, IsPriceAbovePoc = true };
            }

            var priceVolumeMap = new Dictionary<decimal, decimal>();
            foreach (var c in candles)
            {
                // Round close price to nearest bucket
                decimal bucket = Math.Round(c.Close / bucketSize) * bucketSize;
                if (!priceVolumeMap.ContainsKey(bucket))
                    priceVolumeMap[bucket] = 0;
                priceVolumeMap[bucket] += c.Volume;
            }

            var sortedBuckets = priceVolumeMap.OrderByDescending(kvp => kvp.Value).ToList();
            decimal poc = sortedBuckets.FirstOrDefault().Key;
            decimal lastClose = candles.Last().Close;

            decimal minPrice = candles.Min(c => c.Low);
            decimal maxPrice = candles.Max(c => c.High);
            decimal vah = poc + ((maxPrice - poc) * 0.7m);
            decimal val = poc - ((poc - minPrice) * 0.7m);

            return new VolumeProfileResult
            {
                PocPrice = poc,
                ValueAreaHigh = Math.Round(vah, 2),
                ValueAreaLow = Math.Round(val, 2),
                IsPriceAbovePoc = lastClose >= poc
            };
        }
    }
}
