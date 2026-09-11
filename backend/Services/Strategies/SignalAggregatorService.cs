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

            // Always pass through EXIT signals first so positions can be squared off cleanly
            var exitSignals = signals.Where(s => s.Action == "EXIT").ToList();
            if (exitSignals.Any())
            {
                _logger.LogInformation("SignalAggregator: Passing {Count} EXIT signals.", exitSignals.Count);
                aggregatedSignals.AddRange(exitSignals);
            }

            // Group entry signals by underlying asset (NIFTY index vs Equity symbols)
            var entrySignals = signals.Where(s => s.Action != "EXIT").ToList();
            var groupedByAsset = entrySignals.GroupBy(s => GetUnderlyingAsset(s.Instrument));

            foreach (var assetGroup in groupedByAsset)
            {
                string asset = assetGroup.Key;

                if (asset == "NIFTY")
                {
                    // Classify CE (Bullish) vs PE (Bearish)
                    var ceSignals = assetGroup.Where(s => IsCall(s)).ToList();
                    var peSignals = assetGroup.Where(s => IsPut(s)).ToList();

                    if (ceSignals.Any() && peSignals.Any())
                    {
                        var bestCe = ceSignals.OrderBy(s => s.Priority).First();
                        var bestPe = peSignals.OrderBy(s => s.Priority).First();

                        if (bestCe.Priority < bestPe.Priority)
                        {
                            _logger.LogInformation("SignalAggregator: NIFTY directional conflict resolved. CE from {CeStrategy} (Priority {CePri}) wins over PE from {PeStrategy} (Priority {PePri}).",
                                bestCe.StrategyName, bestCe.Priority, bestPe.StrategyName, bestPe.Priority);
                            aggregatedSignals.Add(bestCe);
                        }
                        else if (bestPe.Priority < bestCe.Priority)
                        {
                            _logger.LogInformation("SignalAggregator: NIFTY directional conflict resolved. PE from {PeStrategy} (Priority {PePri}) wins over CE from {CeStrategy} (Priority {CePri}).",
                                bestPe.StrategyName, bestPe.Priority, bestCe.StrategyName, bestCe.Priority);
                            aggregatedSignals.Add(bestPe);
                        }
                        else
                        {
                            var ceNames = string.Join(", ", ceSignals.Select(s => s.StrategyName));
                            var peNames = string.Join(", ", peSignals.Select(s => s.StrategyName));
                            _logger.LogWarning("SignalAggregator: Conflicting directional signals for NIFTY with equal priority. Rejecting both to prevent chop whipsaw. CE from: [{CeStrats}] vs PE from: [{PeStrats}]",
                                ceNames, peNames);
                        }
                    }
                    else if (ceSignals.Any())
                    {
                        var bestCe = ceSignals.OrderBy(s => s.Priority).First();
                        _logger.LogInformation("SignalAggregator: Selected CE entry from {Strategy} (Priority {Priority})", bestCe.StrategyName, bestCe.Priority);
                        aggregatedSignals.Add(bestCe);
                    }
                    else if (peSignals.Any())
                    {
                        var bestPe = peSignals.OrderBy(s => s.Priority).First();
                        _logger.LogInformation("SignalAggregator: Selected PE entry from {Strategy} (Priority {Priority})", bestPe.StrategyName, bestPe.Priority);
                        aggregatedSignals.Add(bestPe);
                    }
                }
                else
                {
                    // Non-NIFTY equities or individual instruments
                    var buySignals = assetGroup.Where(s => s.Action == "BUY").ToList();
                    var sellSignals = assetGroup.Where(s => s.Action == "SELL").ToList();

                    if (buySignals.Any() && sellSignals.Any())
                    {
                        _logger.LogWarning("SignalAggregator: Conflicting BUY/SELL signals for {Asset}. Rejecting both.", asset);
                        continue;
                    }

                    if (buySignals.Any())
                    {
                        aggregatedSignals.Add(buySignals.OrderBy(s => s.Priority).First());
                    }
                    else if (sellSignals.Any())
                    {
                        aggregatedSignals.Add(sellSignals.OrderBy(s => s.Priority).First());
                    }
                }
            }

            return aggregatedSignals;
        }

        private static string GetUnderlyingAsset(string instrument)
        {
            if (string.IsNullOrWhiteSpace(instrument)) return "UNKNOWN";
            var upper = instrument.ToUpperInvariant();
            if (upper.StartsWith("NIFTY")) return "NIFTY";
            return upper;
        }

        private static bool IsCall(Signal signal)
        {
            if (signal.Action == "BUY_PE") return false;
            if (signal.Instrument.Contains("PE", System.StringComparison.OrdinalIgnoreCase)) return false;
            if (signal.Instrument.Contains("CE", System.StringComparison.OrdinalIgnoreCase)) return true;
            return signal.Action == "BUY";
        }

        private static bool IsPut(Signal signal)
        {
            if (signal.Action == "BUY_PE") return true;
            if (signal.Instrument.Contains("PE", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (signal.Instrument.Contains("CE", System.StringComparison.OrdinalIgnoreCase)) return false;
            return signal.Action == "SELL";
        }
    }
}
