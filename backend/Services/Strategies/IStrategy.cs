using System.Threading.Tasks;
using StoicTrade.Api.Models;

namespace StoicTrade.Api.Services.Strategies
{
    public interface IStrategy
    {
        string Name { get; }
        Task ExecuteAsync(StrategyConfig config, string marketData);
    }
}
