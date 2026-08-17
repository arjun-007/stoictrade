using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StoicTrade.Api.Services.MarketData
{
    public class FyersDataPollingService : BackgroundService
    {
        private readonly ILogger<FyersDataPollingService> _logger;
        private readonly HttpClient _httpClient;
        private readonly MarketDataCache _cache;
        private readonly FyersApiService _fyersApi;

        public FyersDataPollingService(
            ILogger<FyersDataPollingService> logger,
            MarketDataCache cache,
            FyersApiService fyersApi)
        {
            _logger = logger;
            _cache = cache;
            _fyersApi = fyersApi;
            _httpClient = new HttpClient();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Fyers Data Polling Service is starting.");

            // Wait a few seconds for DB/app initialization
            await Task.Delay(2000, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 1. Ensure we have a valid token
                    var token = _fyersApi.GetAccessToken();
                    if (string.IsNullOrEmpty(token))
                    {
                        _logger.LogWarning("Fyers Poller: Waiting for access token...");
                        await Task.Delay(5000, stoppingToken);
                        continue;
                    }

                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    // 2. We need NIFTY Spot to know which strikes to query. 
                    // First fetch just the spot price.
                    var spotRes = await _httpClient.GetAsync("https://api-t1.fyers.in/data/quotes?symbols=NSE:NIFTY50-INDEX", stoppingToken);
                    if (!spotRes.IsSuccessStatusCode)
                    {
                        await Task.Delay(3000, stoppingToken);
                        continue;
                    }

                    var spotJson = await spotRes.Content.ReadAsStringAsync(stoppingToken);
                    var spotDoc = JsonDocument.Parse(spotJson);
                    
                    decimal spotPrice = 0;
                    try
                    {
                        var dataArray = spotDoc.RootElement.GetProperty("d");
                        if (dataArray.GetArrayLength() > 0)
                        {
                            spotPrice = dataArray[0].GetProperty("v").GetProperty("lp").GetDecimal();
                            _cache.UpdateSpotData("NIFTY", spotPrice, DateTime.UtcNow);
                        }
                    }
                    catch { /* Handle unexpected JSON safely */ }

                    if (spotPrice == 0)
                    {
                        await Task.Delay(2000, stoppingToken);
                        continue;
                    }

                    // 3. Generate Option Symbols for +/- 5 strikes (Step size 50)
                    int atmStrike = (int)Math.Round(spotPrice / 50.0m) * 50;
                    var optionSymbols = new List<string>();
                    var expiryFormatted = GetNextThursday().ToString("yyMMM").ToUpper(); // e.g. 26AUG
                    
                    // Note: Fyers weekly format is slightly complex. If month expiry, it's yyMMM (26AUG). 
                    // For simplicity in this paper trading mode, we'll request a generic strike format and handle 404s gracefully.
                    // Example format: NSE:NIFTY26AUG22000CE
                    
                    for (int i = -5; i <= 5; i++)
                    {
                        int strike = atmStrike + (i * 50);
                        optionSymbols.Add($"NSE:NIFTY{expiryFormatted}{strike}CE");
                        optionSymbols.Add($"NSE:NIFTY{expiryFormatted}{strike}PE");
                    }

                    var symbolQuery = "NSE:NIFTY50-INDEX," + string.Join(",", optionSymbols);
                    var optRes = await _httpClient.GetAsync($"https://api-t1.fyers.in/data/quotes?symbols={symbolQuery}", stoppingToken);
                    
                    if (optRes.IsSuccessStatusCode)
                    {
                        var optJson = await optRes.Content.ReadAsStringAsync(stoppingToken);
                        var optDoc = JsonDocument.Parse(optJson);
                        
                        // Map Fyers JSON back to NSE format for backward compatibility with Watchlist UI
                        var mappedJson = MapFyersToNseFormat(optDoc, spotPrice, expiryFormatted);
                        _cache.UpdateOptionChainData("NIFTY", mappedJson);
                        _logger.LogDebug($"Fyers Poller: Updated Option Chain around {atmStrike}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fyers Poller Unexpected Error");
                }

                // Poll every 2 seconds
                await Task.Delay(2000, stoppingToken);
            }

            _logger.LogInformation("Fyers Data Polling Service is stopping.");
        }

        private string MapFyersToNseFormat(JsonDocument fyersDoc, decimal spotPrice, string expiry)
        {
            var dataArray = fyersDoc.RootElement.GetProperty("d");
            var strikesMap = new Dictionary<int, Dictionary<string, object>>();

            foreach (var item in dataArray.EnumerateArray())
            {
                var v = item.GetProperty("v");
                if (v.ValueKind == JsonValueKind.Null) continue;

                var symbol = v.GetProperty("short_name").GetString();
                if (symbol == "NIFTY50-INDEX") continue;

                // Parse symbol NSE:NIFTY26AUG22000CE
                try
                {
                    if (string.IsNullOrEmpty(symbol)) continue;
                    
                    var strikeStr = new string(symbol.Where(char.IsDigit).ToArray()).Substring(2); // Skip year
                    int strike = int.Parse(strikeStr);
                    string type = symbol.EndsWith("CE") ? "CE" : "PE";
                    decimal lp = v.GetProperty("lp").GetDecimal();
                    decimal change = v.GetProperty("chp").GetDecimal();

                    if (!strikesMap.ContainsKey(strike))
                        strikesMap[strike] = new Dictionary<string, object>();

                    strikesMap[strike][type] = new { lastPrice = lp, change = change };
                }
                catch { }
            }

            var recordsData = new List<object>();
            foreach (var kvp in strikesMap.OrderBy(x => x.Key))
            {
                recordsData.Add(new
                {
                    strikePrice = kvp.Key,
                    expiryDate = expiry,
                    CE = kvp.Value.ContainsKey("CE") ? kvp.Value["CE"] : null,
                    PE = kvp.Value.ContainsKey("PE") ? kvp.Value["PE"] : null
                });
            }

            var result = new
            {
                records = new
                {
                    underlyingValue = spotPrice,
                    data = recordsData
                }
            };

            return JsonSerializer.Serialize(result);
        }

        private DateTime GetNextThursday()
        {
            DateTime today = DateTime.UtcNow.Date;
            int daysUntilThursday = ((int)DayOfWeek.Thursday - (int)today.DayOfWeek + 7) % 7;
            if (daysUntilThursday == 0 && today.Hour > 15) daysUntilThursday = 7;
            return today.AddDays(daysUntilThursday);
        }
    }
}
