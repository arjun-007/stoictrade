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
        private readonly MarketDataAggregatorService _aggregator;
        private bool _isAggregatorInitialized = false;

        public FyersDataPollingService(
            ILogger<FyersDataPollingService> logger,
            MarketDataCache cache,
            FyersApiService fyersApi,
            MarketDataAggregatorService aggregator,
            Microsoft.Extensions.Configuration.IConfiguration config)
        {
            _logger = logger;
            _cache = cache;
            _fyersApi = fyersApi;
            _aggregator = aggregator;
            _httpClient = new HttpClient();
            _config = config;
        }

        private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

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

                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"{_config["FYERS_APP_ID"]}:{token}");

                    // 2. Query spot prices for all TrackedSymbols
                    var trackedSymbols = _config.GetSection("TrackedSymbols").Get<string[]>() ?? new[] { "NSE:NIFTY50-INDEX" };
                    var spotQuery = string.Join(",", trackedSymbols);
                    var spotRes = await _httpClient.GetAsync($"https://api-t1.fyers.in/data/quotes?symbols={spotQuery}", stoppingToken);
                    if (!spotRes.IsSuccessStatusCode)
                    {
                        await Task.Delay(3000, stoppingToken);
                        continue;
                    }

                    var spotJson = await spotRes.Content.ReadAsStringAsync(stoppingToken);
                    var spotDoc = JsonDocument.Parse(spotJson);
                    
                    decimal niftySpotPrice = 0;
                    try
                    {
                        var dataArray = spotDoc.RootElement.GetProperty("d");
                        foreach (var item in dataArray.EnumerateArray())
                        {
                            var v = item.GetProperty("v");
                            var lp = v.GetProperty("lp").GetDecimal();
                            var symbol = v.GetProperty("short_name").GetString() ?? "";
                            var volume = v.TryGetProperty("volume", out var vol) ? vol.GetDecimal() : 0m;
                            
                            // Normalize symbol name (e.g. NIFTY50-INDEX -> NIFTY)
                            string cacheKey = symbol.Replace("NSE:", "").Replace("50-INDEX", "");
                            _cache.UpdateSpotData(cacheKey, lp, DateTime.UtcNow);

                            if (symbol == "NIFTY50-INDEX")
                            {
                                niftySpotPrice = lp;
                                if (!_isAggregatorInitialized)
                                {
                                    await _aggregator.InitializeSymbolAsync("NSE:NIFTY50-INDEX", new[] { 1, 5, 15 });
                                    _isAggregatorInitialized = true;
                                }
                                _aggregator.UpdateTick("NSE:NIFTY50-INDEX", niftySpotPrice, volume);
                            }
                        }
                    }
                    catch { /* Handle unexpected JSON safely */ }

                    if (niftySpotPrice == 0)
                    {
                        await Task.Delay(2000, stoppingToken);
                        continue;
                    }

                    // 3. Generate Option Symbols for +/- 5 strikes (Step size 50) for multiple expiries
                    int atmStrike = (int)Math.Round(niftySpotPrice / 50.0m) * 50;
                    var optionSymbols = new List<string>();
                    var expiries = GetUpcomingExpiries();
                    
                    foreach (var expiry in expiries)
                    {
                        for (int i = -5; i <= 5; i++)
                        {
                            int strike = atmStrike + (i * 50);
                            optionSymbols.Add($"NSE:NIFTY{expiry}{strike}CE");
                            optionSymbols.Add($"NSE:NIFTY{expiry}{strike}PE");
                        }
                    }

                    // Query in batches if too many symbols, but Fyers allows up to 50 symbols. 
                    // Actually Fyers allows comma separated. Let's limit the query to avoid URL too long.
                    // If expiries count is large, this might exceed limits. We have ~4 expiries * 22 symbols = 88 symbols. Fyers quote API allows up to 50.
                    // We'll just take the first 2 expiries for now to stay under 50. (2 * 22 = 44 symbols)
                    var queryExpiries = expiries.Take(2).ToList();
                    optionSymbols.Clear();
                    foreach (var expiry in queryExpiries)
                    {
                        for (int i = -5; i <= 5; i++)
                        {
                            int strike = atmStrike + (i * 50);
                            optionSymbols.Add($"NSE:NIFTY{expiry}{strike}CE");
                            optionSymbols.Add($"NSE:NIFTY{expiry}{strike}PE");
                        }
                    }

                    var symbolQueryOpts = string.Join(",", optionSymbols);
                    var optRes = await _httpClient.GetAsync($"https://api-t1.fyers.in/data/quotes?symbols={symbolQueryOpts}", stoppingToken);
                    
                    if (optRes.IsSuccessStatusCode)
                    {
                        var optJson = await optRes.Content.ReadAsStringAsync(stoppingToken);
                        var optDoc = JsonDocument.Parse(optJson);
                        
                        // Map Fyers JSON back to NSE format for backward compatibility with Watchlist UI
                        var mappedJson = MapFyersToNseFormat(optDoc, niftySpotPrice);
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

        private string MapFyersToNseFormat(JsonDocument fyersDoc, decimal spotPrice)
        {
            var dataArray = fyersDoc.RootElement.GetProperty("d");
            // strikesMap[expiry][strike][type]
            var strikesMap = new Dictionary<string, Dictionary<int, Dictionary<string, object>>>();

            foreach (var item in dataArray.EnumerateArray())
            {
                if (item.TryGetProperty("s", out var sProp) && sProp.GetString() == "error") continue;

                var v = item.GetProperty("v");
                if (v.ValueKind == JsonValueKind.Null) continue;

                if (!v.TryGetProperty("short_name", out var shortNameProp)) continue;
                var symbol = shortNameProp.GetString();
                if (symbol == "NIFTY50-INDEX" || string.IsNullOrEmpty(symbol)) continue;

                try
                {
                    // symbol format: NSE:NIFTY26AUG23850CE or NSE:NIFTY2682023850CE
                    string noPrefix = symbol.StartsWith("NSE:NIFTY") ? symbol.Substring(9) : symbol;
                    string type = noPrefix.EndsWith("CE") ? "CE" : "PE";
                    string noSuffix = noPrefix.Substring(0, noPrefix.Length - 2);
                    
                    int strikeLength = 0;
                    for (int i = noSuffix.Length - 1; i >= 0; i--)
                    {
                        if (char.IsDigit(noSuffix[i])) strikeLength++;
                        else break;
                    }
                    
                    if (strikeLength == 0) continue;
                    
                    string strikeStr = noSuffix.Substring(noSuffix.Length - strikeLength);
                    string actualExpiry = noSuffix.Substring(0, noSuffix.Length - strikeLength);
                    int strike = int.Parse(strikeStr);

                    if (!v.TryGetProperty("lp", out var lpProp) || !v.TryGetProperty("chp", out var chpProp)) continue;
                    
                    decimal lp = lpProp.GetDecimal();
                    decimal change = chpProp.GetDecimal();

                    if (!strikesMap.ContainsKey(actualExpiry))
                        strikesMap[actualExpiry] = new Dictionary<int, Dictionary<string, object>>();

                    if (!strikesMap[actualExpiry].ContainsKey(strike))
                        strikesMap[actualExpiry][strike] = new Dictionary<string, object>();

                    strikesMap[actualExpiry][strike][type] = new { lastPrice = lp, change = change };
                }
                catch { }
            }

            var recordsData = new List<object>();
            foreach (var expiryKvp in strikesMap)
            {
                var expiry = expiryKvp.Key;
                foreach (var strikeKvp in expiryKvp.Value.OrderBy(x => x.Key))
                {
                    recordsData.Add(new
                    {
                        strikePrice = strikeKvp.Key,
                        expiryDate = expiry,
                        CE = strikeKvp.Value.ContainsKey("CE") ? strikeKvp.Value["CE"] : null,
                        PE = strikeKvp.Value.ContainsKey("PE") ? strikeKvp.Value["PE"] : null
                    });
                }
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

        private List<string> GetUpcomingExpiries()
        {
            var expiries = new List<string>();
            DateTime today = DateTime.UtcNow.Date;
            
            // Generate next 4 Thursdays
            for (int i = 0; i < 4; i++)
            {
                int daysUntilThursday = ((int)DayOfWeek.Thursday - (int)today.DayOfWeek + 7) % 7;
                if (daysUntilThursday == 0 && DateTime.UtcNow.Hour > 15) daysUntilThursday = 7;
                DateTime thurs = today.AddDays(daysUntilThursday + (i * 7));
                
                int month = thurs.Month;
                string monthChar = month <= 9 ? month.ToString() : (month == 10 ? "O" : (month == 11 ? "N" : "D"));
                string weeklyFormat = $"{thurs.ToString("yy")}{monthChar}{thurs.ToString("dd")}";
                string monthlyFormat = thurs.ToString("yyMMM").ToUpper();
                
                expiries.Add(weeklyFormat);
                expiries.Add(monthlyFormat);
            }
            return expiries.Distinct().ToList();
        }
    }
}
