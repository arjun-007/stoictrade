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

        public string GetAuthUrl()
        {
            var appId = _config["FYERS_APP_ID"];
            var redirectUri = _config["FYERS_REDIRECT_URI"] ?? "https://localhost:5000";
            return $"https://api-t1.fyers.in/api/v3/generate-authcode?client_id={appId}&redirect_uri={redirectUri}&response_type=code&state=None";
        }

        public async Task<string> ValidateAuthCodeAsync(string authCode)
        {
            _logger.LogInformation("Fyers API: Validating auth code...");
            
            var appId = _config["FYERS_APP_ID"];
            var secretId = _config["FYERS_SECRET_ID"];
            
            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(secretId))
            {
                _logger.LogError("Fyers credentials missing in configuration.");
                throw new Exception("Fyers credentials missing.");
            }

            var appIdHash = GenerateSHA256Hash($"{appId}:{secretId}");

            var tokenRes = await _httpClient.PostAsJsonAsync("https://api-t1.fyers.in/api/v3/validate-authcode", new
            {
                grant_type = "authorization_code",
                appIdHash = appIdHash,
                code = authCode
            });

            if (!tokenRes.IsSuccessStatusCode) 
            {
                var err = await tokenRes.Content.ReadAsStringAsync();
                throw new Exception($"Fyers Token Validation Error: {tokenRes.StatusCode} - {err}");
            }

            var tokenData = await tokenRes.Content.ReadFromJsonAsync<JsonElement>();
            
            _accessToken = tokenData.GetProperty("access_token").GetString();
            _tokenExpiry = DateTime.UtcNow.AddHours(8); // Cache for 8 hours
            
            _logger.LogInformation("Fyers API: Successfully generated new access token!");
            IsEngineRunning = true;
            return _accessToken ?? string.Empty;
        }

        public string? GetAccessToken() => _accessToken;

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

        public async Task<System.Collections.Generic.List<object>> GetPositionsAsync()
        {
            // In reality, you would send a GET request to Fyers positions API here
            await Task.Delay(50);
            return new System.Collections.Generic.List<object>(); // return empty dummy list
        }

        public async Task PlaceOrderAsync(string instrument, string action, int quantity, decimal expectedPrice)
        {
            _logger.LogInformation("Fyers API: Placing {Action} order for {Quantity} of {Instrument} at {ExpectedPrice}", 
                action, quantity, instrument, expectedPrice);
            // In reality, you would send a POST request to Fyers order placement API here
            await Task.Delay(100);
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
