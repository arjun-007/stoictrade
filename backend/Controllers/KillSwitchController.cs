using Microsoft.AspNetCore.Mvc;
using StoicTrade.Api.Services;

namespace StoicTrade.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class KillSwitchController : ControllerBase
    {
        private readonly KillSwitchService _killSwitchService;

        public KillSwitchController(KillSwitchService killSwitchService)
        {
            _killSwitchService = killSwitchService;
        }

        [HttpPost("trigger")]
        public async Task<IActionResult> Trigger([FromHeader(Name = "X-Account-Id")] string? accountIdHeader, [FromBody] TriggerRequest req)
        {
            var accountId = accountIdHeader ?? "default_account";
            await _killSwitchService.TriggerMasterKillSwitchAsync(accountId, req.Reason ?? "Manual Trigger");
            return Ok(new { Message = "Master kill switch triggered successfully." });
        }

        [HttpPost("square-off")]
        public async Task<IActionResult> SquareOff([FromHeader(Name = "X-Account-Id")] string? accountIdHeader)
        {
            var accountId = accountIdHeader ?? "default_account";
            await _killSwitchService.EmergencySquareOffAsync(accountId);
            return Ok(new { Message = "Emergency square-off initiated." });
        }

        [HttpGet("status")]
        public async Task<IActionResult> Status([FromHeader(Name = "X-Account-Id")] string? accountIdHeader)
        {
            var accountId = accountIdHeader ?? "default_account";
            bool isActive = await _killSwitchService.IsKillSwitchActiveAsync(accountId);
            return Ok(new { IsActive = isActive });
        }
    }

    public class TriggerRequest
    {
        public string? Reason { get; set; }
    }
}
