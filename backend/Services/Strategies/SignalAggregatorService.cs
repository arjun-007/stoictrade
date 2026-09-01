using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using StoicTrade.Api.Models;

namespace StoicTrade.Api.Services.Strategies
{
    public class SignalAggregatorService
    {
        private readonly ILogger<SignalAggregatorService> _logger;

        public SignalAggregatorService(ILogger<SignalAggregatorService> logger)
        {
            _logger = logger;
        }

        public IEnumerable<Signal> Aggregate(IEnumerable<Signal> signals)
        {
            if (!signals.Any()) return Enumerable.Empty<Signal>();

            _logger.LogInformation("SignalAggregator: Processing {Count} signals from current tick.", signals.Count());

            // Simple conflict resolution:
            // If there's a BUY and a SELL for the same instrument, we cancel them out (reject both).
            // This prevents the bot from entering conflicting positions due to strategy overlaps.
            var aggregatedSignals = new List<Signal>();

            var groupedByInstrument = signals.GroupBy(s => s.Instrument);

            foreach (var group in groupedByInstrument)
            {
                var buySignals = group.Where(s => s.Action == "BUY" || s.Action == "BUY_PE").ToList();
                var sellSignals = group.Where(s => s.Action == "SELL").ToList();
                var exitSignals = group.Where(s => s.Action == "EXIT").ToList();

                if (buySignals.Any() && sellSignals.Any())
                {
                    var buyNames = string.Join(", ", buySignals.Select(s => s.StrategyName));
                    var sellNames = string.Join(", ", sellSignals.Select(s => s.StrategyName));
                    _logger.LogWarning("SignalAggregator: Conflicting signals for {Instrument}. Rejecting both. BUY from: [{BuyStrategies}] vs SELL from: [{SellStrategies}]", 
                        group.Key, buyNames, sellNames);
                    continue;
                }

                // Pass through ALL exit signals as they are meant to close specific strategy positions
                if (exitSignals.Any())
                {
                    _logger.LogInformation("SignalAggregator: Passing {Count} EXIT signals for {Instrument}", exitSignals.Count, group.Key);
                    aggregatedSignals.AddRange(exitSignals);
                }

                // If multiple strategies say BUY, take the one with highest priority (lower number = higher priority)
                if (buySignals.Any())
                {
                    var bestBuy = buySignals.OrderBy(s => s.Priority).First();
                    _logger.LogInformation("SignalAggregator: Selected BUY signal from {Strategy} due to priority {Priority}", bestBuy.StrategyName, bestBuy.Priority);
                    aggregatedSignals.Add(bestBuy);
                }
                else if (sellSignals.Any())
                {
                    var bestSell = sellSignals.OrderBy(s => s.Priority).First();
                    _logger.LogInformation("SignalAggregator: Selected SELL signal from {Strategy} due to priority {Priority}", bestSell.StrategyName, bestSell.Priority);
                    aggregatedSignals.Add(bestSell);
                }
            }

            return aggregatedSignals;
        }
    }
}
