using System;
using System.ComponentModel.DataAnnotations;

namespace StoicTrade.Api.Models
{
    public class PaperPosition
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Symbol { get; set; } = string.Empty;
        public int NetQty { get; set; }
        public decimal BuyAvg { get; set; }
        public decimal SellAvg { get; set; }
        public decimal RealizedProfit { get; set; }
        public int TotalBuyQty { get; set; }
        public int TotalSellQty { get; set; }
        public decimal TotalBuyValue { get; set; }
        public decimal TotalSellValue { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
