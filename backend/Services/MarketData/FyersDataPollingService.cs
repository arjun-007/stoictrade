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

                    // 3. Generate Option Symbols across all expiries in batches of ≤48 symbols
                    int atmStrike = (int)Math.Round(niftySpotPrice / 50.0m) * 50;
                    var expiries = GetUpcomingExpiries();

                    // Batch: 8 strikes each side (17 strikes × 2 = 34 sym) per expiry → 1 expiry per batch (34 sym)
                    // Safely under Fyers 50-symbol limit and ensures one unlisted expiry does not fail others.
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

                        // Small delay between batches to avoid rate limiting
                        if (batchStart + ExpiryBatchSize < expiries.Count)
                            await Task.Delay(100, stoppingToken);
                    }

                    if (allOptionDocs.Count > 0)
                    {
                        // Merge all batch docs and update cache
                        var mappedJson = MapFyersToNseFormatMulti(allOptionDocs, niftySpotPrice);
                        _cache.UpdateOptionChainData("NIFTY", mappedJson);

                        foreach (var doc in allOptionDocs)
                            CacheIndividualOptionPrices(doc);

                        _logger.LogDebug($"Fyers Poller: Updated {allOptionDocs.Count} batches, {expiries.Count} expiries around {atmStrike}");
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

            // ── Weekly expiries: next 4 Thursdays ──────────────────────────────
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

            // ── Monthly expiries: last Thursday of current + next 5 months ─────
            for (int m = 0; m < 6; m++)
            {
                var monthStart = new DateTime(today.Year, today.Month, 1).AddMonths(m);
                DateTime lastThurs = GetLastThursdayOfMonth(monthStart);

                // Skip if it's the same as a weekly expiry already added
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
