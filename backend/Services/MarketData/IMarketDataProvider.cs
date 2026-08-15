using System.Threading.Tasks;

namespace StoicTrade.Api.Services.MarketData
{
    public interface IMarketDataProvider
    {
        Task<string?> GetMarketDataAsync(string symbol);
    }
}
