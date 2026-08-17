using System;

namespace StoicTrade.Api.Models
{
    public class Signal
    {
        public string StrategyName { get; set; } = string.Empty;
        public string Instrument { get; set; } = string.Empty;
        public string Action { get; set; } = "BUY"; // BUY or SELL
        public int Quantity { get; set; }
        public decimal ExpectedPrice { get; set; }
        public decimal Price { get; set; }
        public string OrderType { get; set; } = "MARKET";
        public int Priority { get; set; } = 0;
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }
}
