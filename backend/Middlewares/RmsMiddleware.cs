using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using StoicTrade.Api.Services;

namespace StoicTrade.Api.Middlewares
{
    public class RmsMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;

        public RmsMiddleware(RequestDelegate next, IServiceProvider serviceProvider, IConfiguration configuration)
        {
            _next = next;
            _serviceProvider = serviceProvider;
            _configuration = configuration;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Only intercept order placement API
            if (context.Request.Path.StartsWithSegments("/api/orders") && context.Request.Method == "POST")
            {
                using var scope = _serviceProvider.CreateScope();
                var redisService = scope.ServiceProvider.GetRequiredService<RedisService>();

                // Account ID should ideally come from JWT token claims, hardcoding for now or taking from header
                var accountId = context.Request.Headers["X-Account-Id"].FirstOrDefault() ?? "default_account";

                // 1. Check Kill Switch Lock
                if (await redisService.IsLockedAsync($"kill_switch:{accountId}"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new { Error = "Account is locked due to Risk Management rules. Kill switch is active." });
                    return;
                }

                // 2. Check Time Window (09:30 AM to 03:10 PM IST)
                bool bypassTimeLock = _configuration.GetValue<bool>("TEST_MODE_BYPASS_TIME_LOCK");
                
                if (!bypassTimeLock)
                {
                    var istZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
                    var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, istZone);
                    
                    var startTime = new TimeSpan(9, 30, 0);
                    var endTime = new TimeSpan(15, 10, 0);
                    var currentTime = nowIst.TimeOfDay;

                    if (currentTime < startTime || currentTime > endTime)
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsJsonAsync(new { Error = "Trading only allowed between 9:30 AM and 3:10 PM." });
                        return;
                    }
                }

                // 3. Check Pre-Trade Rule: India VIX
                // (In a real app, this would query a live cache of the VIX)
                var currentVixStr = await redisService.GetValueAsync("market:vix");
                if (double.TryParse(currentVixStr, out double currentVix))
                {
                    if (currentVix < 11 || currentVix > 22)
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsJsonAsync(new { Error = $"Trading blocked. VIX ({currentVix}) is outside allowed limits (11-22)." });
                        return;
                    }
                }

                // 4. Instrument Rule: Only allow NIFTY Index Options and Equity Stocks (EQ)
                // Need to read the request body to validate instrument
                context.Request.EnableBuffering();
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
                var bodyString = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0; // Reset position for next middleware/controller

                if (!string.IsNullOrEmpty(bodyString))
                {
                    try
                    {
                        using var jsonDoc = JsonDocument.Parse(bodyString);
                        if (jsonDoc.RootElement.TryGetProperty("instrument", out var instrumentElement))
                        {
                            var instrument = instrumentElement.GetString()?.ToUpperInvariant() ?? "";
                            
                            // Basic validation: MUST start with NIFTY and be an option, OR end with -EQ
                            bool isNiftyOption = instrument.StartsWith("NIFTY") && (instrument.Contains("CE") || instrument.Contains("PE"));
                            bool isEquity = instrument.EndsWith("-EQ");

                            if (!isNiftyOption && !isEquity)
                            {
                                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                await context.Response.WriteAsJsonAsync(new { Error = "Instrument not allowed. Only NIFTY Index Options and Equity Stocks (EQ) are permitted." });
                                return;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore JSON parsing errors here, let the controller handle it
                    }
                }
            }

            // Call the next delegate/middleware in the pipeline.
            await _next(context);
        }
    }
}
