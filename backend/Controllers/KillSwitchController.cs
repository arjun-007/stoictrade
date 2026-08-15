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
            await _killSwitchService.TriggerKillSequenceAsync(accountId, req.Reason ?? "Manual Trigger");
            return Ok(new { Message = "Kill switch triggered successfully." });
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
