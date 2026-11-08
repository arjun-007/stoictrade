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

            // During market hours (09:15 - 15:30 IST), ensure the option chain and spot data were fetched AFTER 09:15:00 AM IST today
            var ist = TimeZoneHelper.GetIstTimeZone();
            var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ist);
            var marketOpenTodayIst = nowIst.Date.Add(new TimeSpan(9, 15, 0));
            var marketCloseTodayIst = nowIst.Date.Add(new TimeSpan(15, 30, 0));

            if (nowIst >= marketOpenTodayIst && nowIst <= marketCloseTodayIst)
            {
                var chainUpdatedUtc = _cache.GetOptionChainUpdatedAt(underlyingSymbol);
                var spotUpdatedIst = TimeZoneInfo.ConvertTimeFromUtc(spotData.Timestamp, ist);
                if (!chainUpdatedUtc.HasValue || TimeZoneInfo.ConvertTimeFromUtc(chainUpdatedUtc.Value, ist) < marketOpenTodayIst || spotUpdatedIst < marketOpenTodayIst)
                {
                    _logger.LogWarning("OptionSelectionEngine: Rejecting stale pre-market option chain/spot snapshot for {Underlying}. Waiting for live post-09:15 AM tick.", underlyingSymbol);
                    return null;
                }
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
                    DateTime today = nowIst.Date;

                    // Collect all distinct expiries, filter out expired dates, group by parsed date, and sort strictly chronologically
                    var distinctExpiries = dataArray.EnumerateArray()
                        .Select(x => x.TryGetProperty("expiryDate", out var e) ? e.GetString() : null)
                        .Where(x => !string.IsNullOrEmpty(x))
                        .Distinct()
                        .GroupBy(e => ParseExpiryToDate(e))
                        .Where(g => g.Key.HasValue && g.Key.Value.Date >= today)
                        .OrderBy(g => g.Key!.Value)
                        .Select(g => g.OrderByDescending(x => x!.Length == 5 && char.IsLetter(x[2]) && char.IsLetter(x[3])).First()!)
                        .ToList();

                    if (!distinctExpiries.Any()) return null;

                    // Select the requested expiry (e.g. expiryIndex: 0 = CurrentWeek, 1 = NextWeek e.g. Oct 13, 2 = TwoWeeksOut e.g. Oct 19/20)
                    string chosenExpiry = "";
                    if (expiryIndex >= 0 && expiryIndex < distinctExpiries.Count && !string.IsNullOrEmpty(distinctExpiries[expiryIndex]))
                    {
                        chosenExpiry = distinctExpiries[expiryIndex]!;
                    }
                    else if (distinctExpiries.Count > 1 && !string.IsNullOrEmpty(distinctExpiries[1]))
                    {
                        // Fall back to Next Week (Index 1) instead of jumping to monthly
                        chosenExpiry = distinctExpiries[1]!;
                    }
                    else
                    {
                        chosenExpiry = distinctExpiries.FirstOrDefault() ?? "";
                    }

                    if (string.IsNullOrEmpty(chosenExpiry)) return null;

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

        public decimal? ResolveOptionLtp(string symbol, bool requireLiveTodayQuote = true)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return null;
            string canonical = symbol.Trim();
            if (canonical.StartsWith("NSE:", StringComparison.OrdinalIgnoreCase)) canonical = canonical.Substring(4);
            if (canonical.StartsWith("NIFTYNIFTY", StringComparison.OrdinalIgnoreCase)) canonical = canonical.Substring(5);
            canonical = canonical.Replace(" ", "");

            var ist = TimeZoneHelper.GetIstTimeZone();
            var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ist).TimeOfDay;
            bool isMarketHours = nowIst >= new TimeSpan(9, 15, 0) && nowIst <= new TimeSpan(15, 30, 0);
            bool enforceLiveSession = requireLiveTodayQuote && isMarketHours;

            var optPrice = _cache.GetOptionPrice(canonical, enforceLiveSession)
                ?? _cache.GetOptionPrice(symbol, enforceLiveSession);

            if (optPrice.HasValue && optPrice.Value > 0)
                return optPrice.Value;

            // Only fall back to spot data if the symbol is NOT an option contract
            if (!canonical.EndsWith("CE", StringComparison.OrdinalIgnoreCase) &&
                !canonical.EndsWith("PE", StringComparison.OrdinalIgnoreCase))
            {
                return _cache.GetSpotData(canonical)?.Price;
            }

            return null;
        }
    }
}
