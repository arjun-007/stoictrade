using System;
using System.ComponentModel.DataAnnotations;

namespace StoicTrade.Api.Models
{
    /// <summary>
    /// Represents a dynamic squad of multiple strategies that run together
    /// and require multi-strategy consensus (e.g., Majority Vote, Unanimous) to trigger an order.
    /// </summary>
    public class StrategyGroup
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = false;

        /// <summary>JSON Array of Member Strategy IDs (e.g. "[1, 2, 7]")</summary>
        public string StrategyIdsJson { get; set; } = "[]";

        /// <summary>
        /// Consensus Rule:
        /// "Majority" - At least MinAgreeingStrategies (e.g. 2+) must agree on the same direction.
        /// "Unanimous" - All member strategies must agree on the same direction.
        /// "Any" - Any member strategy firing triggers the group.
        /// </summary>
        [MaxLength(50)]
        public string ConsensusRule { get; set; } = "Majority";

        public int MinAgreeingStrategies { get; set; } = 2;

        /// <summary>Operating Mode: "Automatic" | "ApprovalRequired" | "SignalOnly"</summary>
        [MaxLength(50)]
        public string OperatingMode { get; set; } = "ApprovalRequired";

        public decimal PerTradeStopLossPoint { get; set; } = 12.0m;
        public decimal PerTradeGainPoint { get; set; } = 35.0m;
        public decimal TrailingStopLossPoint { get; set; } = 8.0m;
        public int TimeframeMinutes { get; set; } = 5;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
