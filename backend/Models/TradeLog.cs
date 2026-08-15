using System;
using System.ComponentModel.DataAnnotations;

namespace StoicTrade.Api.Models
{
    public class TradeLog
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string OrderId { get; set; } = string.Empty;
        
        public string StrategyName { get; set; } = string.Empty;
        
        public string Instrument { get; set; } = string.Empty;
        
        public string TradeType { get; set; } = string.Empty; // BUY or SELL
        
        public int Quantity { get; set; }
        
        public decimal ExecutionPrice { get; set; }
        
        public DateTime Timestamp { get; set; }
        
        public string Status { get; set; } = string.Empty; // EXECUTED, CANCELLED, FAILED
        
        public string Reason { get; set; } = string.Empty;
    }
}
