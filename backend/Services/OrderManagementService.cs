using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StoicTrade.Api.Models;

namespace StoicTrade.Api.Services
{
    public class OrderManagementService
    {
        private readonly ILogger<OrderManagementService> _logger;
        private readonly FyersApiService _fyersApiService;

        public OrderManagementService(ILogger<OrderManagementService> logger, FyersApiService fyersApiService)
        {
            _logger = logger;
            _fyersApiService = fyersApiService;
        }

        public async Task ExecuteOrderAsync(Signal signal)
        {
            _logger.LogInformation("OrderManagementService: Executing order for {Action} {Quantity} {Instrument}", 
                signal.Action, signal.Quantity, signal.Instrument);

            await _fyersApiService.PlaceOrderAsync(signal.Instrument, signal.Action, signal.Quantity, signal.ExpectedPrice);
        }
    }
}
