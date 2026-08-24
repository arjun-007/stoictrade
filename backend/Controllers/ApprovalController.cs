using Microsoft.AspNetCore.Mvc;
using StoicTrade.Api.Models;
using StoicTrade.Api.Services;
using System.Text.Json;
using System.Threading.Tasks;

namespace StoicTrade.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ApprovalController : ControllerBase
    {
        private readonly RedisService _redisService;
        private readonly OrderManagementService _orderManager;
        private readonly Microsoft.Extensions.Logging.ILogger<ApprovalController> _logger;

        public ApprovalController(RedisService redisService, OrderManagementService orderManager, Microsoft.Extensions.Logging.ILogger<ApprovalController> logger)
        {
            _redisService = redisService;
            _orderManager = orderManager;
            _logger = logger;
        }

        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingApprovals([FromHeader(Name = "X-Account-Id")] string? accountIdHeader)
        {
            var accountId = accountIdHeader ?? "default_account";
            
            try
            {
                var pendingKeys = await _redisService.GetKeysByPrefixAsync($"pending_approval:{accountId}:");
                var pendingSignals = new System.Collections.Generic.List<object>();

                foreach (var key in pendingKeys)
                {
                    var val = await _redisService.GetValueAsync(key);
                    if (!string.IsNullOrEmpty(val))
                    {
                        var signal = JsonSerializer.Deserialize<Signal>(val);
                        pendingSignals.Add(new { Id = key.Split(':').Last(), Signal = signal });
                    }
                }

                return Ok(pendingSignals);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error fetching pending approvals");
                return Ok(new System.Collections.Generic.List<object>());
            }
        }

        [HttpPost("approve/{signalId}")]
        public async Task<IActionResult> ApproveSignal(string signalId, [FromHeader(Name = "X-Account-Id")] string? accountIdHeader)
        {
            var accountId = accountIdHeader ?? "default_account";
            var key = $"pending_approval:{accountId}:{signalId}";
            
            try
            {
                var val = await _redisService.GetValueAsync(key);
                if (string.IsNullOrEmpty(val)) return NotFound(new { Message = "Signal not found or expired." });

                var signal = JsonSerializer.Deserialize<Signal>(val);
                if (signal == null) return BadRequest(new { Message = "Invalid signal payload." });
                
                // Execute the order
                await _orderManager.ExecuteOrderAsync(signal);
                
                // Remove from pending
                await _redisService.DeleteKeyAsync(key);

                _logger.LogInformation("Signal {SignalId} successfully approved and executed.", signalId);
                return Ok(new { Message = "Signal approved and order executed." });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Failed to approve signal {SignalId}", signalId);
                return StatusCode(500, new { Message = "Execution failed", Details = ex.Message });
            }
        }

        [HttpPost("deny/{signalId}")]
        public async Task<IActionResult> DenySignal(string signalId, [FromHeader(Name = "X-Account-Id")] string? accountIdHeader)
        {
            var accountId = accountIdHeader ?? "default_account";
            var key = $"pending_approval:{accountId}:{signalId}";
            
            try
            {
                await _redisService.DeleteKeyAsync(key);
                _logger.LogInformation("Signal {SignalId} denied and removed.", signalId);
                return Ok(new { Message = "Signal denied and removed." });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Failed to deny signal {SignalId}", signalId);
                return StatusCode(500, new { Message = "Deny failed", Details = ex.Message });
            }
        }
    }
}
