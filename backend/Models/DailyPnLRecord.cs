using System;
using System.ComponentModel.DataAnnotations;

namespace StoicTrade.Api.Models
{
    public class DailyPnLRecord
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string Date { get; set; } = string.Empty; // "YYYY-MM-DD" in IST
        
        public decimal RealizedPnL { get; set; }
        public decimal UnrealizedPnL { get; set; }
        public decimal NetPnL { get; set; }
        public int TotalTrades { get; set; }
        public int WinningTrades { get; set; }
        public int LosingTrades { get; set; }
        public string TradeMode { get; set; } = "Paper"; // "Paper" or "Live"
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
