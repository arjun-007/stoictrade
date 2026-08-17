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
                var buySignals = group.Where(s => s.Action == "BUY").ToList();
                var sellSignals = group.Where(s => s.Action == "SELL").ToList();

                if (buySignals.Any() && sellSignals.Any())
                {
                    _logger.LogWarning("SignalAggregator: Conflicting signals for {Instrument}. Rejecting both.", group.Key);
                    continue;
                }

                // If multiple strategies say BUY, just take the first one (or we could sum quantity, but for now we take the highest priority)
                // We'll just take the first one to avoid doubling risk
                if (buySignals.Any())
                {
                    aggregatedSignals.Add(buySignals.First());
                }
                else if (sellSignals.Any())
                {
                    aggregatedSignals.Add(sellSignals.First());
                }
            }

            return aggregatedSignals;
        }
    }
}
