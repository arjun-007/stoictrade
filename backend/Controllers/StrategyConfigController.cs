using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoicTrade.Api.Data;
using StoicTrade.Api.Models;

namespace StoicTrade.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StrategyConfigController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public StrategyConfigController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var configs = await _dbContext.StrategyConfigs.ToListAsync();
            return Ok(configs);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] StrategyConfig config)
        {
            var existing = await _dbContext.StrategyConfigs.FindAsync(id);
            if (existing == null) return NotFound(new { error = "Strategy not found." });

            existing.IsEnabled = config.IsEnabled;
            if (!string.IsNullOrWhiteSpace(config.OperatingMode))
                existing.OperatingMode = config.OperatingMode;
            if (config.PerTradeGainPoint > 0)
                existing.PerTradeGainPoint = config.PerTradeGainPoint;
            if (config.PerTradeStopLossPoint > 0)
                existing.PerTradeStopLossPoint = config.PerTradeStopLossPoint;
            if (config.TimeframeMinutes > 0)
                existing.TimeframeMinutes = config.TimeframeMinutes;
            if (config.TrailingStopLossPoint > 0)
                existing.TrailingStopLossPoint = config.TrailingStopLossPoint;
            if (config.TrailingActivationPoint > 0)
                existing.TrailingActivationPoint = config.TrailingActivationPoint;
            if (!string.IsNullOrWhiteSpace(config.AdditionalParamsJson))
                existing.AdditionalParamsJson = config.AdditionalParamsJson;

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to save strategy changes: " + ex.Message });
            }

            return Ok(existing);
        }

        [HttpPost("{id}/toggle")]
        public async Task<IActionResult> Toggle(int id)
        {
            var existing = await _dbContext.StrategyConfigs.FindAsync(id);
            if (existing == null) return NotFound(new { error = "Strategy not found." });

            existing.IsEnabled = !existing.IsEnabled;
            await _dbContext.SaveChangesAsync();

            return Ok(new { id = existing.Id, isEnabled = existing.IsEnabled });
        }
    }
}
