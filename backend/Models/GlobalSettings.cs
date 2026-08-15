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
        public string TradeMode { get; set; } = "Paper";
        public int KillSwitchShutdownMinutes { get; set; } = 720;
    }
}
