using Microsoft.EntityFrameworkCore;
using StoicTrade.Api.Data;
using StoicTrade.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using StoicTrade.Api.Services.MarketData;
using StoicTrade.Api.Services.Strategies;
DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers(options =>
{
    var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
    options.Filters.Add(new AuthorizeFilter(policy));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure JWT Authentication
var jwtSecret = builder.Configuration["JWT_SECRET"] ?? "super_secret_jwt_key_that_must_be_long_enough_for_hmac_sha256";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

// Configure SQLite Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("SQLite") ?? "Data Source=stoictrade.db"));

// Configure Redis Service
builder.Services.AddSingleton<RedisService>();
builder.Services.AddSingleton<MarketDataCache>();
builder.Services.AddSingleton<MarketDataAggregatorService>();
builder.Services.AddHostedService<FyersDataPollingService>();
builder.Services.AddSingleton<StoicTrade.Api.Services.MarketData.OptionChainAnalysisService>();
builder.Services.AddSingleton<StoicTrade.Api.Services.MarketData.MorningMarketConditionService>();
builder.Services.AddSingleton<StoicTrade.Api.Services.Strategies.OptionSelectionEngine>();
builder.Services.AddSingleton<FyersApiService>();
builder.Services.AddSingleton<KillSwitchService>();
builder.Services.AddSingleton<StoicTrade.Api.Services.OrderManagementService>();
builder.Services.AddSingleton<StoicTrade.Api.Services.RiskEngine>();

// Configure Strategies
builder.Services.AddSingleton<StoicTrade.Api.Services.Strategies.IStrategy, StoicTrade.Api.Services.Strategies.SupertrendStrategy>();
builder.Services.AddSingleton<StoicTrade.Api.Services.Strategies.IStrategy, StoicTrade.Api.Services.Strategies.OrbStrategy>();
builder.Services.AddSingleton<StoicTrade.Api.Services.Strategies.IStrategy, StoicTrade.Api.Services.Strategies.EmaPullbackStrategy>();
builder.Services.AddSingleton<StoicTrade.Api.Services.Strategies.IStrategy, StoicTrade.Api.Services.Strategies.BollingerSqueezeStrategy>();
builder.Services.AddSingleton<StoicTrade.Api.Services.Strategies.IStrategy, StoicTrade.Api.Services.Strategies.Nr7Strategy>();
builder.Services.AddSingleton<StoicTrade.Api.Services.Strategies.IStrategy, StoicTrade.Api.Services.Strategies.MacdStrategy>();
builder.Services.AddSingleton<StoicTrade.Api.Services.Strategies.IStrategy, StoicTrade.Api.Services.Strategies.WyckoffSpringStrategy>();
builder.Services.AddSingleton<StoicTrade.Api.Services.Strategies.IStrategy, StoicTrade.Api.Services.Strategies.FairValueGapStrategy>();

builder.Services.AddSingleton<StoicTrade.Api.Services.Strategies.SignalAggregatorService>();

// Register Strategy Engine Background Service
builder.Services.AddHostedService<StoicTrade.Api.Services.Strategies.StrategyEngineService>();
builder.Services.AddHostedService<StoicTrade.Api.Services.BrokerReconciliationService>();

// Configure CORS for Next.js frontend
var allowedOrigins = builder.Configuration["AllowedOrigins"]?.Split(',') ?? new[] { "http://localhost:3000", "https://stoictrade-production.up.railway.app", "https://www.stoictrade.in", "https://stoictrade.in" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy =>
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Run migrations and seed data
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated(); // Simple approach for now instead of full migrations

    try 
    {
        dbContext.Database.ExecuteSqlRaw("ALTER TABLE GlobalSettings ADD COLUMN TradingWindowStart TEXT DEFAULT '09:30:00'");
        dbContext.Database.ExecuteSqlRaw("ALTER TABLE GlobalSettings ADD COLUMN TradingWindowEnd TEXT DEFAULT '15:10:00'");
    } 
    catch {}

    try
    {
        dbContext.Database.ExecuteSqlRaw("ALTER TABLE StrategyConfigs ADD COLUMN OperatingMode TEXT DEFAULT 'ApprovalRequired'");
    }
    catch {}

    try
    {
        dbContext.Database.ExecuteSqlRaw("ALTER TABLE GlobalSettings ADD COLUMN AutoTradeLots INTEGER DEFAULT 1");
    }
    catch {}

    try
    {
        dbContext.Database.ExecuteSqlRaw("ALTER TABLE GlobalSettings ADD COLUMN TrailingStopLossPoint TEXT DEFAULT '8.0'");
    }
    catch {}

    try
    {
        dbContext.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS PaperPositions (
                Id TEXT PRIMARY KEY,
                Symbol TEXT NOT NULL,
                NetQty INTEGER NOT NULL,
                BuyAvg TEXT NOT NULL,
                SellAvg TEXT NOT NULL,
                RealizedProfit TEXT NOT NULL,
                TotalBuyQty INTEGER NOT NULL,
                TotalSellQty INTEGER NOT NULL,
                TotalBuyValue TEXT NOT NULL,
                TotalSellValue TEXT NOT NULL,
                TargetPrice TEXT,
                StopLossPrice TEXT,
                TrailingStopLossPoint TEXT,
                PeakLtp TEXT,
                StrategyName TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )");
    }
    catch {}

    try
    {
        dbContext.Database.ExecuteSqlRaw("ALTER TABLE PaperPositions ADD COLUMN TargetPrice TEXT");
    }
    catch {}

    try
    {
        dbContext.Database.ExecuteSqlRaw("ALTER TABLE PaperPositions ADD COLUMN StopLossPrice TEXT");
    }
    catch {}

    try
    {
        dbContext.Database.ExecuteSqlRaw("ALTER TABLE PaperPositions ADD COLUMN TrailingStopLossPoint TEXT");
    }
    catch {}

    try
    {
        dbContext.Database.ExecuteSqlRaw("ALTER TABLE PaperPositions ADD COLUMN PeakLtp TEXT");
    }
    catch {}

    try
    {
        dbContext.Database.ExecuteSqlRaw("ALTER TABLE PaperPositions ADD COLUMN StrategyName TEXT");
    }
    catch {}

    try
    {
        if (!dbContext.StrategyConfigs.Any(s => s.Id == 7))
        {
            dbContext.StrategyConfigs.Add(new StoicTrade.Api.Models.StrategyConfig { Id = 7, StrategyName = "Wyckoff Spring (Liquidity Sweep)", IsEnabled = false, OperatingMode = "ApprovalRequired", PerTradeStopLossPoint = 10, PerTradeGainPoint = 35, TimeframeMinutes = 5, TrailingStopLossPoint = 8 });
        }
        if (!dbContext.StrategyConfigs.Any(s => s.Id == 8))
        {
            dbContext.StrategyConfigs.Add(new StoicTrade.Api.Models.StrategyConfig { Id = 8, StrategyName = "Fair Value Gap (FVG) / Order Block", IsEnabled = false, OperatingMode = "ApprovalRequired", PerTradeStopLossPoint = 12, PerTradeGainPoint = 30, TimeframeMinutes = 5, TrailingStopLossPoint = 6 });
        }
        dbContext.SaveChanges();
    }
    catch {}

    try
    {
        dbContext.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS StrategyGroups (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Description TEXT,
                IsEnabled INTEGER NOT NULL,
                StrategyIdsJson TEXT NOT NULL,
                ConsensusRule TEXT NOT NULL,
                MinAgreeingStrategies INTEGER NOT NULL,
                OperatingMode TEXT NOT NULL,
                PerTradeStopLossPoint TEXT NOT NULL,
                PerTradeGainPoint TEXT NOT NULL,
                TrailingStopLossPoint TEXT NOT NULL,
                TimeframeMinutes INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )");

        if (!dbContext.StrategyGroups.Any())
        {
            dbContext.StrategyGroups.AddRange(
                new StoicTrade.Api.Models.StrategyGroup
                {
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
                new StoicTrade.Api.Models.StrategyGroup
                {
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
                new StoicTrade.Api.Models.StrategyGroup
                {
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
                new StoicTrade.Api.Models.StrategyGroup
                {
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
                new StoicTrade.Api.Models.StrategyGroup
                {
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
            dbContext.SaveChanges();
        }
    }
    catch {}
}

app.Run();
