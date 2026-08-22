using Microsoft.EntityFrameworkCore;
using StoicTrade.Api.Models;

namespace StoicTrade.Api.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<StrategyConfig> StrategyConfigs { get; set; }
        public DbSet<StrategyGroup> StrategyGroups { get; set; }
        public DbSet<TradeLog> TradeLogs { get; set; }
        public DbSet<GlobalSettings> GlobalSettings { get; set; }
        public DbSet<PaperPosition> PaperPositions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Seed initial global settings
            modelBuilder.Entity<GlobalSettings>().HasData(
                new GlobalSettings 
                { 
                    Id = 1, 
                    MaxLossPerTrade = 2000,
                    MaxDailyLoss = 10000, 
                    MaxTradesPerDay = 5,
                    MaxFailedTrades = 3,
                    VixMinLimit = 11,
                    VixMaxLimit = 22,
                    PerTradeStopLossPoint = 10,
                    PerTradeGainPoint = 30,
                    TradeMode = "Paper",
                    TradingWindowStart = new TimeSpan(9, 30, 0),
                    TradingWindowEnd = new TimeSpan(15, 10, 0),
                    KillSwitchShutdownMinutes = 720
                }
            );

            // Seed initial strategy configurations
            modelBuilder.Entity<StrategyConfig>().HasData(
                new StrategyConfig { Id = 1, StrategyName = "Supertrend Rider", IsEnabled = false, OperatingMode = "ApprovalRequired", PerTradeStopLossPoint = 10, PerTradeGainPoint = 30, TimeframeMinutes = 5, TrailingStopLossPoint = 5 },
                new StrategyConfig { Id = 2, StrategyName = "Opening Range Breakout (ORB)", IsEnabled = false, OperatingMode = "ApprovalRequired", PerTradeStopLossPoint = 15, PerTradeGainPoint = 40, TimeframeMinutes = 15, TrailingStopLossPoint = 10 },
                new StrategyConfig { Id = 3, StrategyName = "EMA Pullback", IsEnabled = false, OperatingMode = "ApprovalRequired", PerTradeStopLossPoint = 10, PerTradeGainPoint = 20, TimeframeMinutes = 5, TrailingStopLossPoint = 5 },
                new StrategyConfig { Id = 4, StrategyName = "Bollinger Volatility Squeeze", IsEnabled = false, OperatingMode = "ApprovalRequired", PerTradeStopLossPoint = 12, PerTradeGainPoint = 25, TimeframeMinutes = 5, TrailingStopLossPoint = 5 },
                new StrategyConfig { Id = 5, StrategyName = "NR7 Breakout", IsEnabled = false, OperatingMode = "ApprovalRequired", PerTradeStopLossPoint = 15, PerTradeGainPoint = 35, TimeframeMinutes = 5, TrailingStopLossPoint = 8 },
                new StrategyConfig { Id = 6, StrategyName = "MACD Zero-Line", IsEnabled = false, OperatingMode = "ApprovalRequired", PerTradeStopLossPoint = 10, PerTradeGainPoint = 20, TimeframeMinutes = 5, TrailingStopLossPoint = 5 },
                new StrategyConfig { Id = 7, StrategyName = "Wyckoff Spring (Liquidity Sweep)", IsEnabled = false, OperatingMode = "ApprovalRequired", PerTradeStopLossPoint = 10, PerTradeGainPoint = 35, TimeframeMinutes = 5, TrailingStopLossPoint = 8 },
                new StrategyConfig { Id = 8, StrategyName = "Fair Value Gap (FVG) / Order Block", IsEnabled = false, OperatingMode = "ApprovalRequired", PerTradeStopLossPoint = 12, PerTradeGainPoint = 30, TimeframeMinutes = 5, TrailingStopLossPoint = 6 }
            );

            // Seed initial preset strategy groups
            modelBuilder.Entity<StrategyGroup>().HasData(
                new StrategyGroup
                {
                    Id = 1,
                    Name = "Morning Alpha & Liquidity Sweep",
                    Description = "Combines ORB and Wyckoff Spring to capture explosive opening breakouts and catch fakeout stop hunts.",
                    IsEnabled = false,
                    StrategyIdsJson = "[2, 7]",
                    ConsensusRule = "Majority",
                    MinAgreeingStrategies = 2,
                    OperatingMode = "ApprovalRequired",
                    PerTradeStopLossPoint = 12.0m,
                    PerTradeGainPoint = 35.0m,
                    TrailingStopLossPoint = 8.0m,
                    TimeframeMinutes = 5
                },
                new StrategyGroup
                {
                    Id = 2,
                    Name = "Institutional Trend & Mitigation",
                    Description = "Combines Supertrend Rider, EMA Pullback, and Fair Value Gap (FVG) for high-conviction trend continuation.",
                    IsEnabled = false,
                    StrategyIdsJson = "[1, 3, 8]",
                    ConsensusRule = "Majority",
                    MinAgreeingStrategies = 2,
                    OperatingMode = "ApprovalRequired",
                    PerTradeStopLossPoint = 10.0m,
                    PerTradeGainPoint = 30.0m,
                    TrailingStopLossPoint = 6.0m,
                    TimeframeMinutes = 5
                },
                new StrategyGroup
                {
                    Id = 3,
                    Name = "Volatility Compression & Expansion",
                    Description = "Combines Bollinger Volatility Squeeze and NR7 Breakout to catch massive explosive breakout moves.",
                    IsEnabled = false,
                    StrategyIdsJson = "[4, 5]",
                    ConsensusRule = "Unanimous",
                    MinAgreeingStrategies = 2,
                    OperatingMode = "ApprovalRequired",
                    PerTradeStopLossPoint = 12.0m,
                    PerTradeGainPoint = 40.0m,
                    TrailingStopLossPoint = 10.0m,
                    TimeframeMinutes = 5
                },
                new StrategyGroup
                {
                    Id = 4,
                    Name = "Momentum Trend Reversal Strike Force",
                    Description = "Combines Supertrend Rider, MACD Zero-Line, and Wyckoff Spring for high-precision trend reversal entries.",
                    IsEnabled = false,
                    StrategyIdsJson = "[1, 6, 7]",
                    ConsensusRule = "Majority",
                    MinAgreeingStrategies = 2,
                    OperatingMode = "ApprovalRequired",
                    PerTradeStopLossPoint = 10.0m,
                    PerTradeGainPoint = 30.0m,
                    TrailingStopLossPoint = 7.0m,
                    TimeframeMinutes = 5
                },
                new StrategyGroup
                {
                    Id = 5,
                    Name = "Master All-Weather Confluence Squad",
                    Description = "Heavy 5-strategy confluence unit. Requires at least 3 concurring strategies (Supertrend, ORB, EMA, Wyckoff, FVG) before firing.",
                    IsEnabled = false,
                    StrategyIdsJson = "[1, 2, 3, 7, 8]",
                    ConsensusRule = "Majority",
                    MinAgreeingStrategies = 3,
                    OperatingMode = "ApprovalRequired",
                    PerTradeStopLossPoint = 12.0m,
                    PerTradeGainPoint = 45.0m,
                    TrailingStopLossPoint = 10.0m,
                    TimeframeMinutes = 5
                }
            );
        }
    }
}
