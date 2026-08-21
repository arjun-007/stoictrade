using System;

namespace StoicTrade.Api.Services
{
    public static class TimeZoneHelper
    {
        public static TimeZoneInfo GetIstTimeZone()
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById("India Standard Time", out var ist))
                return ist;
            if (TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Kolkata", out var kolkata))
                return kolkata;
            if (TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Calcutta", out var calcutta))
                return calcutta;
            return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "India Standard Time", "India Standard Time");
        }
    }
}
