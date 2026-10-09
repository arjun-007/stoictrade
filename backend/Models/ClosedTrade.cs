using System;
using System.ComponentModel.DataAnnotations;

namespace StoicTrade.Api.Models
{
    public class ClosedTrade
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string Symbol { get; set; } = string.Empty;
        
        public string StrategyName { get; set; } = string.Empty;
        
        public string TradeMode { get; set; } = "Paper"; // "Paper" or "Live"
        
        public DateTime EntryTime { get; set; } = DateTime.UtcNow;
        
        public DateTime ExitTime { get; set; } = DateTime.UtcNow;
        
        public decimal BuyPrice { get; set; }
        
        public decimal SellPrice { get; set; }
        
        public int Quantity { get; set; }
        
        public decimal RealizedProfit { get; set; }
        
        public string ExitReason { get; set; } = string.Empty;
        
        public string DateIst { get; set; } = string.Empty; // "YYYY-MM-DD"
    }
}
