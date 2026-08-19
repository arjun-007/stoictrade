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
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )");
    }
    catch {}
}

app.Run();
