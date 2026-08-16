using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using OtpNet;
using System.Text.Json.Serialization;

namespace StoicTrade.Api.Services
{
    public class FyersApiService
    {
        private readonly ILogger<FyersApiService> _logger;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        
        public bool IsEngineRunning { get; private set; }
        private string? _accessToken;
        private DateTime _tokenExpiry;

        public FyersApiService(ILogger<FyersApiService> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
            _httpClient = new HttpClient();
            IsEngineRunning = false;
        }

        public async Task<string> GetDailyAccessTokenAsync()
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
            {
                return _accessToken;
            }

            _logger.LogInformation("Fyers API: Fetching new daily access token via headless automation...");
            
            var appId = _config["FYERS_APP_ID"];
            var secretId = _config["FYERS_SECRET_ID"];
            var redirectUri = _config["FYERS_REDIRECT_URI"] ?? "https://localhost:5000";
            var fyersId = _config["FYERS_USER_ID"];
            var totpSecret = _config["FYERS_TOTP_SECRET"];
            var pin = _config["FYERS_PIN"];

            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(secretId) || string.IsNullOrEmpty(fyersId) || string.IsNullOrEmpty(totpSecret))
            {
                _logger.LogError("Fyers credentials missing in configuration.");
                throw new Exception("Fyers credentials missing.");
            }

            var appIdHash = GenerateSHA256Hash($"{appId}:{secretId}");

            // Step 1: Send Login OTP Request (Vagator v2)
            var sendOtpRes = await _httpClient.PostAsJsonAsync("https://api-t2.fyers.in/vagator/v2/send_login_otp_v2", new { fy_id = fyersId, app_id = 2 });
            if (!sendOtpRes.IsSuccessStatusCode) 
            {
                var err = await sendOtpRes.Content.ReadAsStringAsync();
                throw new Exception($"Fyers Login OTP Error: {sendOtpRes.StatusCode} - {err}");
            }
            var sendOtpData = await sendOtpRes.Content.ReadFromJsonAsync<JsonElement>();
            var requestKey = sendOtpData.GetProperty("request_key").GetString();

            // Step 2: Verify TOTP
            var totpPin = GenerateTotpPin(totpSecret);
            var verifyTotpRes = await _httpClient.PostAsJsonAsync("https://api-t2.fyers.in/vagator/v2/verify_totp", new { request_key = requestKey, totp = totpPin });
            var verifyTotpData = await verifyTotpRes.Content.ReadFromJsonAsync<JsonElement>();
            requestKey = verifyTotpData.GetProperty("request_key").GetString();

            // Step 3: Verify PIN
            var verifyPinRes = await _httpClient.PostAsJsonAsync("https://api-t2.fyers.in/vagator/v2/verify_pin_v2", new { request_key = requestKey, identity_type = "pin", identifier = pin });
            var verifyPinData = await verifyPinRes.Content.ReadFromJsonAsync<JsonElement>();
            var vagatorToken = verifyPinData.GetProperty("data").GetProperty("access_token").GetString();

            // Step 4: Get Auth Code
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", vagatorToken);
            var authCodeRes = await _httpClient.PostAsJsonAsync("https://api-t1.fyers.in/api/v3/token", new
            {
                fyers_id = fyersId,
                app_id = appId,
                redirect_uri = redirectUri,
                appType = "100",
                code_challenge = "",
                state = "None",
                scope = "",
                nonce = "",
                response_type = "code",
                create_cookie = true
            });
            var authCodeData = await authCodeRes.Content.ReadFromJsonAsync<JsonElement>();
            var authCode = authCodeData.GetProperty("Url").GetString()?.Split("auth_code=")[1].Split("&")[0];

            // Step 5: Get Final Access Token
            var tokenRes = await _httpClient.PostAsJsonAsync("https://api-t1.fyers.in/api/v3/validate-authcode", new
            {
                grant_type = "authorization_code",
                appIdHash = appIdHash,
                code = authCode
            });
            var tokenData = await tokenRes.Content.ReadFromJsonAsync<JsonElement>();
            
            _accessToken = tokenData.GetProperty("access_token").GetString();
            _tokenExpiry = DateTime.UtcNow.AddHours(8); // Cache for 8 hours
            
            _logger.LogInformation("Fyers API: Successfully generated new access token!");
            IsEngineRunning = true;
            return _accessToken;
        }

        private string GenerateTotpPin(string secret)
        {
            try
            {
                var bytes = Base32Encoding.ToBytes(secret);
                var totp = new Totp(bytes);
                return totp.ComputeTotp();
            }
            catch (FormatException)
            {
                throw new Exception($"The FYERS_TOTP_SECRET '{secret}' is not a valid Base32 string. Ensure you copy the raw TOTP secret from Fyers without any special characters.");
            }
        }

        private string GenerateSHA256Hash(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            var builder = new StringBuilder();
            foreach (var b in bytes) builder.Append(b.ToString("x2"));
            return builder.ToString();
        }

        public void Disconnect()
        {
            _logger.LogInformation("Fyers API: Disconnecting engine...");
            IsEngineRunning = false;
        }

        public async Task CancelAllPendingOrdersAsync(string accountId)
        {
            _logger.LogInformation("Fyers API: Cancelling all pending orders for {AccountId}", accountId);
            await Task.Delay(100); 
        }

        public async Task SquareOffAllPositionsAsync(string accountId)
        {
            _logger.LogInformation("Fyers API: Squaring off all active positions for {AccountId}", accountId);
            await Task.Delay(100);
        }

        public async Task<string> GenerateTotpPinAsync(string totpSecretEnc, string masterKey)
        {
            // Note: Currently returns a dummy pin for the generic frontend TOTP verifier
            _logger.LogInformation("Fyers API: Generating TOTP pin for UI");
            await Task.Delay(50);
            return new Random().Next(100000, 999999).ToString();
        }
    }
}
