using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoicTrade.Api.Data;
using StoicTrade.Api.Models;

namespace StoicTrade.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StrategyGroupsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public StrategyGroupsController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var groups = await _dbContext.StrategyGroups.OrderBy(g => g.Id).ToListAsync();
            return Ok(groups);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var group = await _dbContext.StrategyGroups.FindAsync(id);
            if (group == null) return NotFound();
            return Ok(group);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] StrategyGroup group)
        {
            if (string.IsNullOrWhiteSpace(group.Name))
                return BadRequest("Group name is required.");

            group.CreatedAt = DateTime.UtcNow;
            group.UpdatedAt = DateTime.UtcNow;
            _dbContext.StrategyGroups.Add(group);
            await _dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = group.Id }, group);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] StrategyGroup updated)
        {
            var existing = await _dbContext.StrategyGroups.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Name = updated.Name;
            existing.Description = updated.Description;
            existing.IsEnabled = updated.IsEnabled;
            existing.StrategyIdsJson = updated.StrategyIdsJson;
            existing.ConsensusRule = updated.ConsensusRule;
            existing.MinAgreeingStrategies = updated.MinAgreeingStrategies;
            existing.OperatingMode = updated.OperatingMode;
            existing.PerTradeStopLossPoint = updated.PerTradeStopLossPoint;
            existing.PerTradeGainPoint = updated.PerTradeGainPoint;
            existing.TrailingStopLossPoint = updated.TrailingStopLossPoint;
            existing.TimeframeMinutes = updated.TimeframeMinutes;
            existing.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            return Ok(existing);
        }

        [HttpPost("{id}/toggle")]
        public async Task<IActionResult> Toggle(int id)
        {
            var group = await _dbContext.StrategyGroups.FindAsync(id);
            if (group == null) return NotFound();

            group.IsEnabled = !group.IsEnabled;
            group.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            return Ok(group);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var group = await _dbContext.StrategyGroups.FindAsync(id);
            if (group == null) return NotFound();

            _dbContext.StrategyGroups.Remove(group);
            await _dbContext.SaveChangesAsync();
            return Ok(new { message = $"Group {group.Name} deleted successfully." });
        }
    }
}
