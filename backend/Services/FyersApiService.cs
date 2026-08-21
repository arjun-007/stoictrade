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

        public void StartPaperEngine()
        {
            _logger.LogInformation("Fyers API: Starting engine in Paper Trading mode...");
            IsEngineRunning = true;
        }

        public void Disconnect()
        {
            _logger.LogInformation("Fyers API: Disconnecting engine and clearing active session...");
            IsEngineRunning = false;
            _accessToken = null;
        }

        public async Task<JsonElement> GetFundsAsync()
        {
            if (string.IsNullOrEmpty(_accessToken)) return default;
            
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api-t1.fyers.in/api/v3/funds");
            request.Headers.TryAddWithoutValidation("Authorization", $"{_config["FYERS_APP_ID"]}:{_accessToken}");
            
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonDocument.Parse(content).RootElement;
            }
            return default;
        }

        public async Task<JsonElement> GetPositionsAsync()
        {
            if (string.IsNullOrEmpty(_accessToken)) return default;
            
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api-t1.fyers.in/api/v3/positions");
            request.Headers.TryAddWithoutValidation("Authorization", $"{_config["FYERS_APP_ID"]}:{_accessToken}");
            
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonDocument.Parse(content).RootElement;
            }
            return default;
        }

        public async Task<JsonElement> GetHoldingsAsync()
        {
            if (string.IsNullOrEmpty(_accessToken)) return default;
            
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api-t1.fyers.in/api/v3/holdings");
            request.Headers.TryAddWithoutValidation("Authorization", $"{_config["FYERS_APP_ID"]}:{_accessToken}");
            
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonDocument.Parse(content).RootElement;
            }
            return default;
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
            _logger.LogInformation("Fyers API: Generating REAL TOTP pin from environment config");
            await Task.Delay(50);
            
            var secret = _config["FYERS_TOTP_SECRET"];
            if (string.IsNullOrEmpty(secret)) 
            {
                throw new Exception("FYERS_TOTP_SECRET is not set in the environment (.env) file.");
            }

            return GenerateTotpPin(secret);
        }

        public async Task<System.Collections.Generic.List<StoicTrade.Api.Models.Candle>> GetHistoricalCandlesAsync(string symbol, string resolution, DateTime from, DateTime to)
        {
            if (string.IsNullOrEmpty(_accessToken))
            {
                _logger.LogWarning("Fyers API: Cannot fetch history without access token.");
                return new System.Collections.Generic.List<StoicTrade.Api.Models.Candle>();
            }

            long fromEpoch = new DateTimeOffset(from).ToUnixTimeSeconds();
            long toEpoch = new DateTimeOffset(to).ToUnixTimeSeconds();
            
            var url = $"https://api-t1.fyers.in/api/v3/history/?symbol={symbol}&resolution={resolution}&date_format=0&range_from={fromEpoch}&range_to={toEpoch}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", $"{_config["FYERS_APP_ID"]}:{_accessToken}"); // Fyers Auth format: AppId:AccessToken

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                _logger.LogError("History API error: {Error}", err);
                return new System.Collections.Generic.List<StoicTrade.Api.Models.Candle>();
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var candles = new System.Collections.Generic.List<StoicTrade.Api.Models.Candle>();

            if (json.TryGetProperty("candles", out var candlesArray))
            {
                foreach (var c in candlesArray.EnumerateArray())
                {
                    long epoch = c[0].GetInt64();
                    var date = DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;

                    candles.Add(new StoicTrade.Api.Models.Candle
                    {
                        Date = date,
                        Open = c[1].GetDecimal(),
                        High = c[2].GetDecimal(),
                        Low = c[3].GetDecimal(),
                        Close = c[4].GetDecimal(),
                        Volume = c[5].GetDecimal()
                    });
                }
            }

            return candles;
        }
    }
}
