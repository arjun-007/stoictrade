using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using StoicTrade.Api.Models;
using System.Linq;

namespace StoicTrade.Api.Services
{
    public class OrderManagementService
    {
        private readonly ILogger<OrderManagementService> _logger;
        private readonly FyersApiService _fyersApiService;
        private readonly IServiceProvider _serviceProvider;

        public OrderManagementService(ILogger<OrderManagementService> logger, FyersApiService fyersApiService, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _fyersApiService = fyersApiService;
            _serviceProvider = serviceProvider;
        }

        public async Task ExecuteOrderAsync(Signal signal)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<StoicTrade.Api.Data.AppDbContext>();
            var globalSettings = dbContext.GlobalSettings.FirstOrDefault();
            
            if (globalSettings != null && globalSettings.TradeMode == "Paper")
            {
                _logger.LogInformation("OrderManagementService [PAPER]: Executing mock order for {Action} {Quantity} {Instrument} at {ExpectedPrice}", 
                    signal.Action, signal.Quantity, signal.Instrument, signal.ExpectedPrice);
                    
                // In paper mode, we could save the execution directly to SQLite here
                return;
            }

            _logger.LogInformation("OrderManagementService [LIVE]: Executing order for {Action} {Quantity} {Instrument}", 
                signal.Action, signal.Quantity, signal.Instrument);

            await _fyersApiService.PlaceOrderAsync(signal.Instrument, signal.Action, signal.Quantity, signal.ExpectedPrice);
        }
    }
}
