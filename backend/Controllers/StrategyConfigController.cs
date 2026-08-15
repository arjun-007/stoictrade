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
            if (id != config.Id) return BadRequest();

            _dbContext.Entry(config).State = EntityState.Modified;
            
            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _dbContext.StrategyConfigs.AnyAsync(e => e.Id == id))
                    return NotFound();
                else
                    throw;
            }

            return NoContent();
        }
    }
}
