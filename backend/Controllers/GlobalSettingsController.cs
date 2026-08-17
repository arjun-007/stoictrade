using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoicTrade.Api.Data;
using StoicTrade.Api.Models;

namespace StoicTrade.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GlobalSettingsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public GlobalSettingsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<GlobalSettings>> Get()
        {
            var settings = await _context.GlobalSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                // Return default if DB is somehow empty, though seed should prevent this
                settings = new GlobalSettings();
                _context.GlobalSettings.Add(settings);
                await _context.SaveChangesAsync();
            }
            return settings;
        }

        [HttpPut]
        public async Task<IActionResult> Update(GlobalSettings updatedSettings)
        {
            var settings = await _context.GlobalSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new GlobalSettings();
                _context.GlobalSettings.Add(settings);
            }

            settings.MaxLossPerTrade = updatedSettings.MaxLossPerTrade;
            settings.MaxDailyLoss = updatedSettings.MaxDailyLoss;
            settings.MaxTradesPerDay = updatedSettings.MaxTradesPerDay;
            settings.MaxFailedTrades = updatedSettings.MaxFailedTrades;
            settings.VixMinLimit = updatedSettings.VixMinLimit;
            settings.VixMaxLimit = updatedSettings.VixMaxLimit;
            settings.PerTradeStopLossPoint = updatedSettings.PerTradeStopLossPoint;
            settings.PerTradeGainPoint = updatedSettings.PerTradeGainPoint;
            settings.TradeMode = updatedSettings.TradeMode;
            settings.TradingWindowStart = updatedSettings.TradingWindowStart;
            settings.TradingWindowEnd = updatedSettings.TradingWindowEnd;
            settings.KillSwitchShutdownMinutes = updatedSettings.KillSwitchShutdownMinutes;

            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
