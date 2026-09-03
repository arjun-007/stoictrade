using System.ComponentModel.DataAnnotations;

namespace StoicTrade.Api.Models
{
    public class GlobalSettings
    {
        [Key]
        public int Id { get; set; }
        
        public decimal MaxLossPerTrade { get; set; }
        public decimal MaxDailyLoss { get; set; }
        public int MaxTradesPerDay { get; set; }
        public int MaxFailedTrades { get; set; }
        public decimal VixMinLimit { get; set; }
        public decimal VixMaxLimit { get; set; }
        public decimal PerTradeStopLossPoint { get; set; }
        public decimal PerTradeGainPoint { get; set; }
        public TimeSpan TradingWindowStart { get; set; } = new TimeSpan(9, 30, 0);
        public TimeSpan TradingWindowEnd { get; set; } = new TimeSpan(15, 10, 0);
        public string TradeMode { get; set; } = "Paper";
        public int KillSwitchShutdownMinutes { get; set; } = 720;
        public int AutoTradeLots { get; set; } = 1;
        public int BaseLotSize { get; set; } = 65;
        public decimal TrailingStopLossPoint { get; set; } = 8.0m;
    }
}
