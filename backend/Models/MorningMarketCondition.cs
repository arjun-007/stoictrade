using System;

namespace StoicTrade.Api.Models
{
    public class MorningMarketCondition
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public decimal SpotPrice { get; set; }
        public decimal Vwap { get; set; }

        // Step 1: Pre-Market Context
        public bool IsPriorDayCompressed { get; set; }
        public string CompressionType { get; set; } = "None"; // "NR7", "NR4", "InsideDay", "Normal"
        public decimal PriorDayRange { get; set; }

        // Step 2: 15-Minute Rejection Rule (09:15 - 09:35)
        public string LiquidityRejection { get; set; } = "None"; // "BULLISH_SWEEP", "BEARISH_TRAP", "None"
        public decimal OpenPrice0915 { get; set; }
        public decimal SessionHigh { get; set; }
        public decimal SessionLow { get; set; }
        public decimal MaxRejectionWickRatio { get; set; }

        // Step 3: 09:45 VWAP Anchor
        public string VwapStatus { get; set; } = "Flat"; // "AboveVwap_Rising", "BelowVwap_Falling", "Whipsaw_Choppy"
        public decimal VwapSlope { get; set; }

        // Step 4: Option Strike Skew (OI Building)
        public string OptionOiBias { get; set; } = "Neutral"; // "Bullish_PutWriting", "Bearish_CallWriting", "ATM_Straddle_Pin"
        public decimal Pcr { get; set; }
        public decimal InstitutionalFloorStrike { get; set; }
        public decimal InstitutionalCeilingStrike { get; set; }

        // Final Regime & Option Buyer Directives
        public string MarketRegime { get; set; } = "DEVELOPING"; // "BULLISH_TREND_DAY", "BEARISH_TREND_DAY", "CHOPPY_RANGE_BOUND", "DEVELOPING"
        public string RegimeLabel { get; set; } = "Developing Session";
        public string ActionDirective { get; set; } = "Observe opening structure (09:15-10:00 AM).";
        public bool OvertradingShieldActive { get; set; } = false;
        public string WarningLevel { get; set; } = "Normal"; // "Normal", "Caution", "Danger"

        // Active Option Buyer Traps
        public System.Collections.Generic.List<BuyerTrapAlert> DetectedTraps { get; set; } = new();
        public int TrapCount => DetectedTraps.Count;
    }

    public class BuyerTrapAlert
    {
        public string TrapId { get; set; } = string.Empty; // GAP_EXHAUSTION, DIVERGENCE, STRADDLE_PIN, VOLUME_ABSORPTION, INSIDE_BAR_COMPRESSION, IV_CRUSH
        public string Name { get; set; } = string.Empty;
        public string Severity { get; set; } = "High"; // "Critical", "High", "Moderate"
        public string Description { get; set; } = string.Empty;
        public string FootprintEvidence { get; set; } = string.Empty;
        public string BuyerDirective { get; set; } = string.Empty;
    }
}
