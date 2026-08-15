using System.ComponentModel.DataAnnotations;

namespace StoicTrade.Api.Models
{
    public class StrategyConfig
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string StrategyName { get; set; } = string.Empty;
        
        public bool IsEnabled { get; set; }
        
        // Strategy parameters
        public decimal PerTradeStopLossPoint { get; set; }
        public decimal PerTradeGainPoint { get; set; }
        public int TimeframeMinutes { get; set; }
        public decimal TrailingStopLossPoint { get; set; }
        
        // Strategy specific parameters (stored as JSON)
        public string AdditionalParamsJson { get; set; } = "{}";
    }
}
