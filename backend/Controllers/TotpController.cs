using Microsoft.AspNetCore.Mvc;
using StoicTrade.Api.Services;

namespace StoicTrade.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TotpController : ControllerBase
    {
        private readonly RedisService _redisService;
        private readonly FyersApiService _fyersApi;
        private readonly IConfiguration _configuration;

        public TotpController(RedisService redisService, FyersApiService fyersApi, IConfiguration configuration)
        {
            _redisService = redisService;
            _fyersApi = fyersApi;
            _configuration = configuration;
        }

        [HttpPost("request")]
        public async Task<IActionResult> RequestTotp([FromHeader(Name = "X-Account-Id")] string? accountIdHeader)
        {
            var accountId = accountIdHeader ?? "default_account";
            
            // Set request time in Redis
            var requestTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            await _redisService.SetValueAsync($"totp_req:{accountId}", requestTime, TimeSpan.FromMinutes(30)); // Expire after 30 mins
            
            return Ok(new { Message = "TOTP request initiated. If Kill Switch is active, please wait 20 minutes." });
        }

        [HttpPost("generate")]
        public async Task<IActionResult> GenerateTotp([FromHeader(Name = "X-Account-Id")] string? accountIdHeader, [FromBody] GenerateTotpDto request)
        {
            var accountId = accountIdHeader ?? "default_account";

            bool bypassTimeLock = _configuration.GetValue<bool>("TEST_MODE_BYPASS_TIME_LOCK");

            if (!bypassTimeLock)
            {
                var istZone = StoicTrade.Api.Services.TimeZoneHelper.GetIstTimeZone();
                var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, istZone);
                
                // Rule 1: Hard Time Lock (Weekdays, 06:00-09:30 and 15:10-16:00 IST)
                if (nowIst.DayOfWeek != DayOfWeek.Saturday && nowIst.DayOfWeek != DayOfWeek.Sunday)
                {
                    var time = nowIst.TimeOfDay;
                    bool inMorningBlock = time >= new TimeSpan(6, 0, 0) && time < new TimeSpan(9, 30, 0);
                    bool inEveningBlock = time >= new TimeSpan(15, 10, 0) && time < new TimeSpan(16, 0, 0);

                    if (inMorningBlock || inEveningBlock)
                    {
                        return BadRequest(new { Error = "TOTP generation is blocked during pre-market and market-close hours." });
                    }
                }
            }

            // Rule 2: Kill Switch Delay
            if (await _redisService.IsLockedAsync($"kill_switch:{accountId}"))
            {
                var reqTimeStr = await _redisService.GetValueAsync($"totp_req:{accountId}");
                if (string.IsNullOrEmpty(reqTimeStr) || !long.TryParse(reqTimeStr, out long reqTimeUnix))
                {
                    return BadRequest(new { Error = "Kill Switch is ACTIVE on this account. Please click 'Request Manual Access' first to start the 20-minute behavioral cooling period." });
                }

                var reqTime = DateTimeOffset.FromUnixTimeSeconds(reqTimeUnix).UtcDateTime;
                var elapsedMinutes = (DateTime.UtcNow - reqTime).TotalMinutes;
                if (elapsedMinutes < 20)
                {
                    int remainingMinutes = (int)Math.Ceiling(20 - elapsedMinutes);
                    return BadRequest(new { Error = $"Cooling period active. Please wait {remainingMinutes} more minute(s) before generating TOTP." });
                }
            }

            // Validate PIN (frontend sends SHA256 hash)
            // Expected hash for 'bPnvKkn@007'
            var expectedHash = "73575068bb4b3b7f4ccc6f6eada01a7e0bf61afea3d0ce77d64cb7d7284e11a8";
            if (request.Pin.ToLowerInvariant() != expectedHash)
            {
                return Unauthorized(new { Error = "Invalid credentials." });
            }

            // Decrypt and generate TOTP
            var totpSecretEnc = _configuration["TOTP_SECRET_ENC"] ?? "";
            var masterKey = _configuration["MASTER_KEY"] ?? "";
            
            var code = await _fyersApi.GenerateTotpPinAsync(totpSecretEnc, masterKey);

            return Ok(new { TotpCode = code });
        }
    }

    public class GenerateTotpDto
    {
        public string Pin { get; set; } = string.Empty;
    }
}
