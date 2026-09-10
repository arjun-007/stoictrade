using System;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StoicTrade.Api.Services.MarketData;

namespace StoicTrade.Api.Services.Strategies
{
    public class OptionSelectionEngine
    {
        private readonly MarketDataCache _cache;
        private readonly ILogger<OptionSelectionEngine> _logger;

        public OptionSelectionEngine(MarketDataCache cache, ILogger<OptionSelectionEngine> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        /// <summary>
        /// Selects the optimal option contract based on underlying bias (BULLISH/BEARISH).
        /// Supports target expiry index (e.g. 0 = current week, 1 = next week, 2 = 2 weeks after current week expiry) and ITM distance (e.g. 1 = 1 strike ITM).
        /// </summary>
        public string? GetOptimalContract(string underlyingSymbol, string bias, int itmDistance = 1, int expiryIndex = 2)
        {
            var rawJson = _cache.GetOptionChainData(underlyingSymbol);
            var spotData = _cache.GetSpotData(underlyingSymbol);

            if (string.IsNullOrEmpty(rawJson) || spotData == null)
            {
                _logger.LogWarning("OptionSelectionEngine: Missing market data for {Underlying}", underlyingSymbol);
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("records", out var records) && 
                    records.TryGetProperty("data", out var dataArray))
                {
                    decimal spotPrice = spotData.Price;
                    int atmStrike = (int)Math.Round(spotPrice / 50.0m) * 50;
                    
                    // ITM logic:
                    // CE (BULLISH): Strike is lower than ATM (In-The-Money)
                    // PE (BEARISH): Strike is higher than ATM (In-The-Money)
                    string optionType = bias == "BULLISH" ? "CE" : "PE";
                    int targetStrike = optionType == "CE" 
                        ? atmStrike - (itmDistance * 50) 
                        : atmStrike + (itmDistance * 50);

                    // Current IST date to filter out any past expired contracts
                    var ist = TimeZoneHelper.GetIstTimeZone();
                    DateTime today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ist).Date;

                    // Collect all distinct expiries, filter out expired dates, group by parsed date, and sort strictly chronologically
                    var distinctExpiries = dataArray.EnumerateArray()
                        .Select(x => x.TryGetProperty("expiryDate", out var e) ? e.GetString() : null)
                        .Where(x => !string.IsNullOrEmpty(x))
                        .Distinct()
                        .GroupBy(e => ParseExpiryToDate(e))
                        .Where(g => g.Key.HasValue && g.Key.Value.Date >= today)
                        .OrderBy(g => g.Key!.Value)
                        .Select(g => g.OrderByDescending(x => x!.Length == 5 && char.IsLetter(x[2])).First()!)
                        .ToList();

                    if (!distinctExpiries.Any()) return null;

                    // Select the requested expiry (e.g. expiryIndex = 2 for 2 weeks after current week expiry)
                    string chosenExpiry = (expiryIndex >= 0 && expiryIndex < distinctExpiries.Count)
                        ? (distinctExpiries[expiryIndex] ?? distinctExpiries.Last() ?? "")
                        : (distinctExpiries.Count > 1 ? (distinctExpiries.ElementAtOrDefault(1) ?? distinctExpiries.First() ?? "") : (distinctExpiries.First() ?? ""));

                    return $"NSE:{underlyingSymbol}{chosenExpiry}{targetStrike}{optionType}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error selecting optimal option contract");
            }

            return null;
        }

        public static DateTime? ParseExpiryToDate(string? expiry)
        {
            if (string.IsNullOrWhiteSpace(expiry)) return null;
            string s = expiry.Trim().ToUpper();

            // 1. Weekly format: 5 chars: yy(2) + m(1) + dd(2), e.g. "26915", "26O08", "26N12", "26D24"
            if (s.Length == 5 && char.IsDigit(s[0]) && char.IsDigit(s[1]) && char.IsDigit(s[3]) && char.IsDigit(s[4]))
            {
                if (int.TryParse(s.Substring(0, 2), out int yy))
                {
                    char mChar = s[2];
                    int month = mChar switch
                    {
                        'O' => 10,
                        'N' => 11,
                        'D' => 12,
                        _ => char.IsDigit(mChar) ? (mChar - '0') : 0
                    };
                    if (int.TryParse(s.Substring(3, 2), out int day) && month >= 1 && month <= 12 && day >= 1 && day <= 31)
                    {
                        try { return new DateTime(2000 + yy, month, day); } catch {}
                    }
                }
            }

            // 2. Monthly format: 5 chars: yy(2) + mon(3 letters), e.g. "26SEP", "26OCT", "26NOV"
            // In NSE NIFTY derivatives, monthly contracts expire on the last Tuesday of the month
            if (s.Length == 5 && char.IsDigit(s[0]) && char.IsDigit(s[1]))
            {
                if (int.TryParse(s.Substring(0, 2), out int yy))
                {
                    string mon = s.Substring(2, 3);
                    string[] months = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };
                    int monthIdx = Array.IndexOf(months, mon);
                    if (monthIdx >= 0)
                    {
                        int month = monthIdx + 1;
                        int year = 2000 + yy;
                        // Return the last Tuesday of that month
                        int daysInMonth = DateTime.DaysInMonth(year, month);
                        var lastDay = new DateTime(year, month, daysInMonth);
                        int daysBack = ((int)lastDay.DayOfWeek - (int)DayOfWeek.Tuesday + 7) % 7;
                        return lastDay.AddDays(-daysBack);
                    }
                }
            }

            // 3. Fallback to standard DateTime parser if standard ISO format
            if (DateTime.TryParse(s, out var dt)) return dt;

            return null;
        }

        public decimal? ResolveOptionLtp(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return null;
            string canonical = symbol.Trim();
            if (canonical.StartsWith("NSE:", StringComparison.OrdinalIgnoreCase)) canonical = canonical.Substring(4);
            if (canonical.StartsWith("NIFTYNIFTY", StringComparison.OrdinalIgnoreCase)) canonical = canonical.Substring(5);
            canonical = canonical.Replace(" ", "");

            return _cache.GetOptionPrice(canonical)
                ?? _cache.GetOptionPrice(symbol)
                ?? _cache.GetSpotData(canonical)?.Price;
        }
    }
}
