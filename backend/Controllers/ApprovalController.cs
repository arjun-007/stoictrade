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

        public ApprovalController(RedisService redisService, OrderManagementService orderManager)
        {
            _redisService = redisService;
            _orderManager = orderManager;
        }

        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingApprovals([FromHeader(Name = "X-Account-Id")] string? accountIdHeader)
        {
            var accountId = accountIdHeader ?? "default_account";
            
            // In a real Redis implementation you'd use SCAN to find keys. 
            // For now, since RedisService exposes basic KV, we might need a small workaround.
            // Let's assume we maintain a list of pending IDs, or we can just fetch it if we extend RedisService.
            // But since this is just an example backend, we'll return an empty list if we can't scan.
            
            // Let's try to get all pending signals (requires adding a Scan method to RedisService)
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

        [HttpPost("approve/{signalId}")]
        public async Task<IActionResult> ApproveSignal(string signalId, [FromHeader(Name = "X-Account-Id")] string? accountIdHeader)
        {
            var accountId = accountIdHeader ?? "default_account";
            var key = $"pending_approval:{accountId}:{signalId}";
            
            var val = await _redisService.GetValueAsync(key);
            if (string.IsNullOrEmpty(val)) return NotFound(new { Message = "Signal not found or expired." });

            var signal = JsonSerializer.Deserialize<Signal>(val);
            
            // Execute the order
            await _orderManager.ExecuteOrderAsync(signal!);
            
            // Remove from pending
            await _redisService.DeleteKeyAsync(key);

            return Ok(new { Message = "Signal approved and order executed." });
        }

        [HttpPost("deny/{signalId}")]
        public async Task<IActionResult> DenySignal(string signalId, [FromHeader(Name = "X-Account-Id")] string? accountIdHeader)
        {
            var accountId = accountIdHeader ?? "default_account";
            var key = $"pending_approval:{accountId}:{signalId}";
            
            await _redisService.DeleteKeyAsync(key);

            return Ok(new { Message = "Signal denied and removed." });
        }
    }
}
