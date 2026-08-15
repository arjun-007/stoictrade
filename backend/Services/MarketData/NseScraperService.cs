using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StoicTrade.Api.Services.MarketData
{
    public class NseScraperService : BackgroundService
    {
        private readonly ILogger<NseScraperService> _logger;
        private readonly MarketDataCache _cache;
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl = "https://www.nseindia.com";
        private readonly string _apiEndpoint = "/api/option-chain-indices?symbol=NIFTY";
        
        public NseScraperService(ILogger<NseScraperService> logger, MarketDataCache cache)
        {
            _logger = logger;
            _cache = cache;

            var handler = new SocketsHttpHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };

            _httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(_baseUrl),
                Timeout = TimeSpan.FromSeconds(10)
            };

            // Set standard browser headers
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            _httpClient.DefaultRequestHeaders.Add("Referer", $"{_baseUrl}/get-quotes/derivatives?symbol=NIFTY");
        }

        private async Task EnsureCookiesAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("NSE Scraper: Initializing session and fetching cookies...");
                var response = await _httpClient.GetAsync("/", stoppingToken);
                response.EnsureSuccessStatusCode();
                _logger.LogInformation("NSE Scraper: Session initialized successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "NSE Scraper: Failed to initialize session.");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("NSE Scraper Background Service is starting.");
            
            await EnsureCookiesAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var response = await _httpClient.GetAsync(_apiEndpoint, stoppingToken);

                    if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        _logger.LogWarning($"NSE Scraper: Access Denied ({response.StatusCode}). Re-initializing session...");
                        await EnsureCookiesAsync(stoppingToken);
                        await Task.Delay(2000, stoppingToken);
                        continue;
                    }

                    response.EnsureSuccessStatusCode();
                    
                    var json = await response.Content.ReadAsStringAsync(stoppingToken);
                    
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    if (root.TryGetProperty("records", out var records) && 
                        records.TryGetProperty("underlyingValue", out var underlyingValueElement))
                    {
                        if (underlyingValueElement.TryGetDecimal(out var underlyingValue))
                        {
                            // Update cache with both spot price and raw option chain
                            _cache.UpdateSpotData("NIFTY", underlyingValue, DateTime.UtcNow);
                            _cache.UpdateOptionChainData("NIFTY", json);
                            _logger.LogDebug($"NSE Scraper: Updated NIFTY Spot Price: {underlyingValue}");
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning($"NSE Scraper Network Error: {ex.Message}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "NSE Scraper Unexpected Error");
                }

                // Poll every 3 seconds to avoid heavy throttling
                await Task.Delay(3000, stoppingToken);
            }

            _logger.LogInformation("NSE Scraper Background Service is stopping.");
        }
    }
}
