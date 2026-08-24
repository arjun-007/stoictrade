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

        private readonly Microsoft.Extensions.Configuration.IConfiguration _config;
        private readonly IServiceProvider _serviceProvider;
        private readonly OptionChainAnalysisService _optionChainService;
        private decimal _simulatedNiftyPrice = 24250.0m;
        private readonly Random _random = new Random();

        public FyersDataPollingService(
            ILogger<FyersDataPollingService> logger,
            MarketDataCache cache,
            FyersApiService fyersApi,
            MarketDataAggregatorService aggregator,
            Microsoft.Extensions.Configuration.IConfiguration config,
            IServiceProvider serviceProvider,
            OptionChainAnalysisService optionChainService)
        {
            _logger = logger;
            _cache = cache;
            _fyersApi = fyersApi;
            _aggregator = aggregator;
            _config = config;
            _serviceProvider = serviceProvider;
            _optionChainService = optionChainService;
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
                    // 1. If engine is stopped, DO NOT poll or generate ticks
                    if (!_fyersApi.IsEngineRunning)
                    {
                        await Task.Delay(5000, stoppingToken);
                        continue;
                    }

                    // Check TradeMode from DB
                    string tradeMode = "Paper";
                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var dbContext = scope.ServiceProvider.GetRequiredService<StoicTrade.Api.Data.AppDbContext>();
                        var globalSettings = dbContext.GlobalSettings.FirstOrDefault();
                        if (globalSettings != null && !string.IsNullOrEmpty(globalSettings.TradeMode))
                        {
                            tradeMode = globalSettings.TradeMode;
                        }
                    }
                    catch { /* Fallback to Paper mode */ }

                    // 2. Off-market check (>= 3:40 PM or < 9:15 AM IST)
                    var ist = StoicTrade.Api.Services.TimeZoneHelper.GetIstTimeZone();
                    var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ist).TimeOfDay;
                    var autoStopCutoff = new TimeSpan(15, 40, 0); // 3:40 PM IST
                    var marketOpen = new TimeSpan(9, 15, 0);      // 9:15 AM IST
                    bool isMarketHours = (nowIst < autoStopCutoff && nowIst >= marketOpen);

                    if (tradeMode == "Live" && !isMarketHours)
                    {
                        _logger.LogInformation("Fyers Poller: Off-market hours in Live mode (>= 3:40 PM or < 9:15 AM IST). Disconnecting engine.");
                        _fyersApi.Disconnect();
                        await Task.Delay(10000, stoppingToken);
                        continue;
                    }

                    var token = _fyersApi.GetAccessToken();

                    // If Live mode, or Paper mode with a valid token during market hours: poll live Fyers REST API
                    if (isMarketHours && !string.IsNullOrEmpty(token))
                    {
                        await PollLiveFyersDataAsync(token, stoppingToken);
                    }
                    else
                    {
                        // In Paper mode or off-market hours without live token: generate realistic simulated market data
                        await GeneratePaperMarketDataAsync(stoppingToken);
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

        private async Task PollLiveFyersDataAsync(string token, CancellationToken stoppingToken)
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"{_config["FYERS_APP_ID"]}:{token}");

            // 1. Query spot prices for all TrackedSymbols
            var trackedSymbols = _config.GetSection("TrackedSymbols").Get<string[]>() ?? new[] { "NSE:NIFTY50-INDEX" };
            var spotQuery = string.Join(",", trackedSymbols);
            var spotRes = await _httpClient.GetAsync($"https://api-t1.fyers.in/data/quotes?symbols={spotQuery}", stoppingToken);
            if (!spotRes.IsSuccessStatusCode)
            {
                // Fall back to paper generation if Fyers API fails
                await GeneratePaperMarketDataAsync(stoppingToken);
                return;
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
                    decimal prevClose = v.TryGetProperty("prev_close_price", out var pcProp) ? pcProp.GetDecimal() : 0m;
                    decimal ch = v.TryGetProperty("ch", out var chProp) ? chProp.GetDecimal() : (prevClose > 0 ? lp - prevClose : 0m);
                    decimal chp = v.TryGetProperty("chp", out var chpProp) ? chpProp.GetDecimal() : (prevClose > 0 ? ((lp - prevClose) / prevClose) * 100m : 0m);
                    string queryName = item.TryGetProperty("n", out var nProp) ? nProp.GetString() ?? "" : "";
                    string sym = v.TryGetProperty("symbol", out var sProp) ? sProp.GetString() ?? "" : "";
                    string shortName = v.TryGetProperty("short_name", out var snProp) ? snProp.GetString() ?? "" : "";
                    string rawName = !string.IsNullOrEmpty(queryName) ? queryName : (!string.IsNullOrEmpty(sym) ? sym : shortName);
                    var volume = v.TryGetProperty("volume", out var vol) ? vol.GetDecimal() : 0m;

                    if (rawName.Contains("NIFTY", StringComparison.OrdinalIgnoreCase))
                    {
                        niftySpotPrice = lp;
                        _simulatedNiftyPrice = lp;
                        _cache.UpdateSpotData("NIFTY", lp, DateTime.UtcNow, prevClose, ch, chp);
                        _cache.UpdateSpotData("NIFTY50-INDEX", lp, DateTime.UtcNow, prevClose, ch, chp);
                        if (!_isAggregatorInitialized)
                        {
                            await _aggregator.InitializeSymbolAsync("NSE:NIFTY50-INDEX", new[] { 1, 5, 15 }, niftySpotPrice);
                            _isAggregatorInitialized = true;
                        }
                        _aggregator.UpdateTick("NSE:NIFTY50-INDEX", niftySpotPrice, volume);
                    }
                    else
                    {
                        string cleanKey = rawName.Replace("NSE:", "").Replace("-EQ", "").Trim();
                        _cache.UpdateSpotData(cleanKey, lp, DateTime.UtcNow, prevClose, ch, chp);
                    }
                }
            }
            catch { /* Handle unexpected JSON safely */ }

            if (niftySpotPrice == 0)
            {
                await GeneratePaperMarketDataAsync(stoppingToken);
                return;
            }

            // 2. Generate Option Symbols across all expiries in batches
            int atmStrike = (int)Math.Round(niftySpotPrice / 50.0m) * 50;
            var expiries = GetUpcomingExpiries();

            const int StrikesEachSide = 8;
            const int ExpiryBatchSize = 1;
            var allOptionDocs = new List<JsonDocument>();

            for (int batchStart = 0; batchStart < expiries.Count; batchStart += ExpiryBatchSize)
            {
                var batchExpiries = expiries.Skip(batchStart).Take(ExpiryBatchSize).ToList();
                var batchSymbols = new List<string>();

                foreach (var expiry in batchExpiries)
                {
                    for (int i = -StrikesEachSide; i <= StrikesEachSide; i++)
                    {
                        int strike = atmStrike + (i * 50);
                        batchSymbols.Add($"NSE:NIFTY{expiry}{strike}CE");
                        batchSymbols.Add($"NSE:NIFTY{expiry}{strike}PE");
                    }
                }

                var batchQuery = string.Join(",", batchSymbols);
                try
                {
                    var batchRes = await _httpClient.GetAsync($"https://api-t1.fyers.in/data/quotes?symbols={batchQuery}", stoppingToken);
                    if (batchRes.IsSuccessStatusCode)
                    {
                        var batchJson = await batchRes.Content.ReadAsStringAsync(stoppingToken);
                        allOptionDocs.Add(JsonDocument.Parse(batchJson));
                    }
                }
                catch { /* skip failed batch */ }

                if (batchStart + ExpiryBatchSize < expiries.Count)
                    await Task.Delay(100, stoppingToken);
            }

            if (allOptionDocs.Count > 0)
            {
                var mappedJson = MapFyersToNseFormatMulti(allOptionDocs, niftySpotPrice);
                _cache.UpdateOptionChainData("NIFTY", mappedJson);

                foreach (var doc in allOptionDocs)
                    CacheIndividualOptionPrices(doc);
            }
        }

        private async Task GeneratePaperMarketDataAsync(CancellationToken stoppingToken)
        {
            // Realistic random micro-walk for NIFTY spot (-2.5 to +2.5 pts)
            double tickDelta = (_random.NextDouble() * 5.0) - 2.45;
            _simulatedNiftyPrice = Math.Round(Math.Max(23000m, Math.Min(26000m, _simulatedNiftyPrice + (decimal)tickDelta)), 2);

            decimal prevClose = 24180.0m;
            decimal change = _simulatedNiftyPrice - prevClose;
            decimal changePercent = Math.Round((change / prevClose) * 100m, 2);

            _cache.UpdateSpotData("NIFTY", _simulatedNiftyPrice, DateTime.UtcNow, prevClose, change, changePercent);
            _cache.UpdateSpotData("NIFTY50-INDEX", _simulatedNiftyPrice, DateTime.UtcNow, prevClose, change, changePercent);
            _cache.UpdateSpotData("HDFCBANK", 1645.50m + (decimal)((_random.NextDouble() * 2) - 1), DateTime.UtcNow, 1640m);
            _cache.UpdateSpotData("RELIANCE", 2985.20m + (decimal)((_random.NextDouble() * 3) - 1.5), DateTime.UtcNow, 2975m);

            if (!_isAggregatorInitialized)
            {
                await _aggregator.InitializeSymbolAsync("NSE:NIFTY50-INDEX", new[] { 1, 5, 15 }, _simulatedNiftyPrice);
                _isAggregatorInitialized = true;
            }

            decimal tickVol = _random.Next(2000, 12000);
            _aggregator.UpdateTick("NSE:NIFTY50-INDEX", _simulatedNiftyPrice, tickVol);

            // Generate full synthetic option chain
            int atmStrike = (int)Math.Round(_simulatedNiftyPrice / 50.0m) * 50;
            var expiries = GetUpcomingExpiries();
            var strikesMap = new Dictionary<string, Dictionary<int, Dictionary<string, object>>>();

            const int StrikesEachSide = 15; // ATM ± 15 strikes (31 strikes total across expiries)

            for (int eIdx = 0; eIdx < expiries.Count; eIdx++)
            {
                var expiry = expiries[eIdx];
                if (!strikesMap.ContainsKey(expiry))
                    strikesMap[expiry] = new Dictionary<int, Dictionary<string, object>>();

                // Time value base increases with further expiries
                decimal timeValueAtm = 120m + (eIdx * 65m);

                for (int i = -StrikesEachSide; i <= StrikesEachSide; i++)
                {
                    int strike = atmStrike + (i * 50);
                    strikesMap[expiry][strike] = new Dictionary<string, object>();

                    // CE Pricing
                    decimal ceIntrinsic = Math.Max(0m, _simulatedNiftyPrice - strike);
                    decimal ceOtmDist = Math.Max(0m, strike - _simulatedNiftyPrice);
                    decimal ceExtrinsic = Math.Max(1.5m, timeValueAtm * (decimal)Math.Exp((double)(-ceOtmDist / 450m)));
                    decimal ceLtp = Math.Round(ceIntrinsic + ceExtrinsic, 2);
                    decimal ceChange = Math.Round(change * 0.45m, 2);

                    strikesMap[expiry][strike]["CE"] = new { lastPrice = ceLtp, change = ceChange };
                    _cache.UpdateOptionPrice($"NIFTY{expiry}{strike}CE", ceLtp);

                    // PE Pricing
                    decimal peIntrinsic = Math.Max(0m, strike - _simulatedNiftyPrice);
                    decimal peOtmDist = Math.Max(0m, _simulatedNiftyPrice - strike);
                    decimal peExtrinsic = Math.Max(1.5m, timeValueAtm * (decimal)Math.Exp((double)(-peOtmDist / 450m)));
                    decimal peLtp = Math.Round(peIntrinsic + peExtrinsic, 2);
                    decimal peChange = Math.Round(-change * 0.45m, 2);

                    strikesMap[expiry][strike]["PE"] = new { lastPrice = peLtp, change = peChange };
                    _cache.UpdateOptionPrice($"NIFTY{expiry}{strike}PE", peLtp);

                    // Feed option chain analysis metrics
                    decimal callOi = Math.Max(50000, 2500000 - (Math.Abs(strike - atmStrike) * 3500));
                    decimal putOi = Math.Max(50000, 2800000 - (Math.Abs(strike - atmStrike) * 3500));
                    _optionChainService.UpdateStrike("NIFTY", strike, callOi, putOi, ceLtp, peLtp);
                }
            }

            var mappedJson = SerialiseStrikesMap(strikesMap, _simulatedNiftyPrice);
            _cache.UpdateOptionChainData("NIFTY", mappedJson);
        }

        /// <summary>
        /// Normalises a Fyers option symbol to extract just the expiry+strike+type part.
        /// Fyers short_name can be: "NSE:NIFTY26AUG24250CE", "NIFTY26AUG24250CE", or "26AUG24250CE".
        /// We always want actualExpiry = "26AUG", strike = 24250, type = "CE".
        /// </summary>
        private static bool TryParseOptionSymbol(string? raw, out string actualExpiry, out int strike, out string type)
        {
            actualExpiry = ""; strike = 0; type = "";
            if (string.IsNullOrEmpty(raw) || raw == "NIFTY50-INDEX") return false;

            // Strip known prefixes so we always work with e.g. "26AUG24250CE" or "2682824250CE"
            string s = raw;
            if (s.StartsWith("NSE:NIFTY")) s = s.Substring(9);
            else if (s.StartsWith("NSE:"))    s = s.Substring(4);
            if (s.StartsWith("NIFTY"))       s = s.Substring(5);

            if (s.Length < 6) return false;

            type = s.EndsWith("CE") ? "CE" : s.EndsWith("PE") ? "PE" : "";
            if (string.IsNullOrEmpty(type)) return false;

            string noSuffix = s.Substring(0, s.Length - 2); // remove CE/PE

            // Count trailing digits → strike
            int strikeLen = 0;
            for (int i = noSuffix.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(noSuffix[i])) strikeLen++;
                else break;
            }
            if (strikeLen == 0) return false;

            if (strikeLen == noSuffix.Length)
            {
                // ── All-digits case: weekly expiry format e.g. "2682824250" ──────────────
                // Fyers weekly expiry is ALWAYS 5 chars: yy + monthChar(1) + dd(2)
                // NIFTY strike is always the remaining digits (typically 5 but can be 4-6).
                // So: first 5 chars = expiry, rest = strike.
                if (noSuffix.Length < 6) return false; // need at least 5 (expiry) + 1 (strike)
                actualExpiry = noSuffix.Substring(0, 5);
                if (!int.TryParse(noSuffix.Substring(5), out strike)) return false;
                return strike > 0;
            }

            // ── Mixed case: monthly expiry like "26AUG24250" ─────────────────────────
            if (!int.TryParse(noSuffix.Substring(noSuffix.Length - strikeLen), out strike)) return false;
            actualExpiry = noSuffix.Substring(0, noSuffix.Length - strikeLen);
            return true;
        }

        private static Dictionary<string, Dictionary<int, Dictionary<string, object>>> BuildStrikesMap(JsonDocument fyersDoc)
        {
            var strikesMap = new Dictionary<string, Dictionary<int, Dictionary<string, object>>>();

            // If Fyers returns a global error for the batch (e.g. invalid symbol), 'd' array won't exist.
            if (fyersDoc.RootElement.TryGetProperty("s", out var statusProp) && statusProp.GetString() == "error")
                return strikesMap;

            if (!fyersDoc.RootElement.TryGetProperty("d", out var dataArray) || dataArray.ValueKind != JsonValueKind.Array)
                return strikesMap;

            foreach (var item in dataArray.EnumerateArray())
            {
                if (item.TryGetProperty("s", out var sProp) && sProp.GetString() == "error") continue;
                var v = item.GetProperty("v");
                if (v.ValueKind == JsonValueKind.Null) continue;

                string? symbolName = null;
                if (item.TryGetProperty("n", out var nProp)) symbolName = nProp.GetString();
                else if (v.TryGetProperty("symbol", out var symProp)) symbolName = symProp.GetString();
                else if (v.TryGetProperty("short_name", out var snProp)) symbolName = snProp.GetString();

                if (string.IsNullOrEmpty(symbolName)) continue;
                if (!TryParseOptionSymbol(symbolName, out var expiry, out var strike, out var type)) continue;
                if (!v.TryGetProperty("lp", out var lpProp) || !v.TryGetProperty("chp", out var chpProp)) continue;

                decimal lp = lpProp.GetDecimal();
                decimal change = chpProp.GetDecimal();

                if (!strikesMap.ContainsKey(expiry))
                    strikesMap[expiry] = new Dictionary<int, Dictionary<string, object>>();
                if (!strikesMap[expiry].ContainsKey(strike))
                    strikesMap[expiry][strike] = new Dictionary<string, object>();

                strikesMap[expiry][strike][type] = new { lastPrice = lp, change = change };
            }
            return strikesMap;
        }

        private string MapFyersToNseFormatMulti(IEnumerable<JsonDocument> fyersDocs, decimal spotPrice)
        {
            // Merge all batch documents into one strikes map
            var merged = new Dictionary<string, Dictionary<int, Dictionary<string, object>>>();
            foreach (var doc in fyersDocs)
            {
                var map = BuildStrikesMap(doc);
                foreach (var (expiry, strikes) in map)
                {
                    if (!merged.ContainsKey(expiry)) merged[expiry] = new Dictionary<int, Dictionary<string, object>>();
                    foreach (var (strike, types) in strikes)
                    {
                        if (!merged[expiry].ContainsKey(strike)) merged[expiry][strike] = new Dictionary<string, object>();
                        foreach (var (t, v) in types) merged[expiry][strike][t] = v;
                    }
                }
            }
            return SerialiseStrikesMap(merged, spotPrice);
        }

        private string MapFyersToNseFormat(JsonDocument fyersDoc, decimal spotPrice)
        {
            return SerialiseStrikesMap(BuildStrikesMap(fyersDoc), spotPrice);
        }

        private static string SerialiseStrikesMap(Dictionary<string, Dictionary<int, Dictionary<string, object>>> strikesMap, decimal spotPrice)
        {
            var recordsData = new List<object>();
            // Sort expiries then strikes
            foreach (var expiryKvp in strikesMap.OrderBy(e => e.Key))
            {
                foreach (var strikeKvp in expiryKvp.Value.OrderBy(x => x.Key))
                {
                    recordsData.Add(new
                    {
                        strikePrice = strikeKvp.Key,
                        expiryDate = expiryKvp.Key,
                        CE = strikeKvp.Value.ContainsKey("CE") ? strikeKvp.Value["CE"] : null,
                        PE = strikeKvp.Value.ContainsKey("PE") ? strikeKvp.Value["PE"] : null
                    });
                }
            }

            return JsonSerializer.Serialize(new
            {
                records = new { underlyingValue = spotPrice, data = recordsData }
            });
        }

        private void CacheIndividualOptionPrices(JsonDocument fyersDoc)
        {
            try
            {
                if (fyersDoc.RootElement.TryGetProperty("s", out var statusProp) && statusProp.GetString() == "error")
                    return;

                if (!fyersDoc.RootElement.TryGetProperty("d", out var dataArray) || dataArray.ValueKind != JsonValueKind.Array)
                    return;

                foreach (var item in dataArray.EnumerateArray())
                {
                    if (item.TryGetProperty("s", out var sProp) && sProp.GetString() == "error") continue;
                    var v = item.GetProperty("v");
                    if (v.ValueKind == JsonValueKind.Null) continue;

                    string? symbolName = null;
                    if (item.TryGetProperty("n", out var nProp)) symbolName = nProp.GetString();
                    else if (v.TryGetProperty("symbol", out var symProp)) symbolName = symProp.GetString();
                    else if (v.TryGetProperty("short_name", out var snProp)) symbolName = snProp.GetString();

                    if (string.IsNullOrEmpty(symbolName)) continue;
                    if (!TryParseOptionSymbol(symbolName, out var expiry, out var strike, out var type)) continue;
                    if (!v.TryGetProperty("lp", out var lpProp)) continue;
                    decimal lp = lpProp.GetDecimal();

                    // Store under canonical key "NIFTY{expiry}{strike}{type}" e.g. "NIFTY26AUG24250CE"
                    string canonicalKey = $"NIFTY{expiry}{strike}{type}";
                    _cache.UpdateOptionPrice(canonicalKey, lp);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CacheIndividualOptionPrices: Failed to parse option prices.");
            }
        }

        private List<string> GetUpcomingExpiries()
        {
            var expiries = new List<string>();
            // Use IST to match NSE trading calendar
            var ist = TimeZoneHelper.GetIstTimeZone();
            DateTime today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ist).Date;

            // ── 1. Weekly expiries for Tuesdays (next 4 Tuesdays) ─────────────
            int daysUntilTuesday = ((int)DayOfWeek.Tuesday - (int)today.DayOfWeek + 7) % 7;
            for (int i = 0; i < 4; i++)
            {
                DateTime tues = today.AddDays(daysUntilTuesday + (i * 7));
                int month = tues.Month;
                string monthChar = month <= 9 ? month.ToString()
                    : month == 10 ? "O" : month == 11 ? "N" : "D";
                expiries.Add($"{tues:yy}{monthChar}{tues:dd}");
            }

            // ── 2. Weekly expiries for Thursdays (next 4 Thursdays) ───────────
            int daysUntilThursday = ((int)DayOfWeek.Thursday - (int)today.DayOfWeek + 7) % 7;
            for (int i = 0; i < 4; i++)
            {
                DateTime thurs = today.AddDays(daysUntilThursday + (i * 7));
                DateTime lastThurs = GetLastThursdayOfMonth(thurs);

                // If this Thursday is the last Thursday of the month, NSE issues a monthly contract
                // instead of a weekly contract. Use the monthly format (YYMMM) so Fyers doesn't reject it.
                if (thurs.Date == lastThurs.Date)
                {
                    expiries.Add(thurs.ToString("yyMMM").ToUpper());
                }
                else
                {
                    int month = thurs.Month;
                    string monthChar = month <= 9 ? month.ToString()
                        : month == 10 ? "O" : month == 11 ? "N" : "D";
                    expiries.Add($"{thurs:yy}{monthChar}{thurs:dd}");
                }
            }

            // ── 3. Monthly expiries: last Thursday of current + next 5 months ──
            for (int m = 0; m < 6; m++)
            {
                var monthStart = new DateTime(today.Year, today.Month, 1).AddMonths(m);
                DateTime lastThurs = GetLastThursdayOfMonth(monthStart);

                string monthlyFmt = lastThurs.ToString("yyMMM").ToUpper();
                expiries.Add(monthlyFmt);
            }

            return expiries.Distinct().ToList();
        }

        /// <summary>Returns the last Thursday of the month containing <paramref name="anyDayInMonth"/>.</summary>
        private static DateTime GetLastThursdayOfMonth(DateTime anyDayInMonth)
        {
            var lastDay = new DateTime(anyDayInMonth.Year, anyDayInMonth.Month,
                DateTime.DaysInMonth(anyDayInMonth.Year, anyDayInMonth.Month));
            int daysBack = ((int)lastDay.DayOfWeek - (int)DayOfWeek.Thursday + 7) % 7;
            return lastDay.AddDays(-daysBack);
        }
    }
}
