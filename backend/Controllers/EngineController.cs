using Microsoft.AspNetCore.Mvc;
using StoicTrade.Api.Services;
using System.Threading.Tasks;

namespace StoicTrade.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EngineController : ControllerBase
    {
        private readonly FyersApiService _fyersApi;

        public EngineController(FyersApiService fyersApi)
        {
            _fyersApi = fyersApi;
        }

        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            return Ok(new { IsRunning = _fyersApi.IsEngineRunning });
        }

        [HttpPost("start")]
        public IActionResult StartEngine()
        {
            var authUrl = _fyersApi.GetAuthUrl();
            return Ok(new { Message = "Please authenticate with Fyers to start the engine.", AuthUrl = authUrl });
        }

        [HttpPost("callback")]
        public async Task<IActionResult> AuthCallback([FromBody] AuthCallbackRequest request)
        {
            try
            {
                var token = await _fyersApi.ValidateAuthCodeAsync(request.AuthCode);
                return Ok(new { Message = "Engine started successfully. Connected to Fyers API.", Token = token });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { Error = "Failed to start engine: " + ex.Message });
            }
        }

        [HttpPost("stop")]
        public IActionResult StopEngine()
        {
            _fyersApi.Disconnect();
            return Ok(new { Message = "Engine stopped successfully." });
        }
    }

    public class AuthCallbackRequest
    {
        public string AuthCode { get; set; } = string.Empty;
    }
}
