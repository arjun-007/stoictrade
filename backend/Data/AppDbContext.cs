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
        public DbSet<TradeLog> TradeLogs { get; set; }
        public DbSet<GlobalSettings> GlobalSettings { get; set; }

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
                new StrategyConfig { Id = 6, StrategyName = "MACD Zero-Line", IsEnabled = false, OperatingMode = "ApprovalRequired", PerTradeStopLossPoint = 10, PerTradeGainPoint = 20, TimeframeMinutes = 5, TrailingStopLossPoint = 5 }
            );
        }
    }
}
