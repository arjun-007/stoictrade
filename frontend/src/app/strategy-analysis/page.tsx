"use client";

import { useState, useEffect } from "react";
import { Activity, Play, Pause, Zap, Shield, Radar, BarChart2 } from "lucide-react";
import { fetchWithAuth } from "@/lib/api";

interface StrategyConfig {
  id: number;
  strategyName: string;
  isEnabled: boolean;
  operatingMode: string;
  perTradeStopLossPoint: number;
  perTradeGainPoint: number;
  timeframeMinutes: number;
  trailingStopLossPoint: number;
}

export default function StrategyAnalysisPage() {
  const [strategies, setStrategies] = useState<StrategyConfig[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetchWithAuth("/api/strategyconfig")
      .then(res => res.json())
      .then(data => {
        setStrategies(data.filter((s: StrategyConfig) => s.isEnabled));
        setLoading(false);
      })
      .catch(err => {
        console.error(err);
        setLoading(false);
      });
  }, []);

  const getStatusDetails = (strategy: StrategyConfig) => {
    switch (strategy.operatingMode) {
      case "Automatic":
        return {
          icon: <Zap className="w-5 h-5 text-amber-500" />,
          color: "text-amber-500",
          bgColor: "bg-amber-50 dark:bg-amber-900/20",
          statusText: "Active & Auto-Trading",
          description: "System is continuously analyzing the market data and will automatically place orders when entry conditions are met. No manual intervention is required."
        };
      case "ApprovalRequired":
        return {
          icon: <Shield className="w-5 h-5 text-blue-500" />,
          color: "text-blue-500",
          bgColor: "bg-blue-50 dark:bg-blue-900/20",
          statusText: "Active (Awaiting Approval)",
          description: "System is analyzing market conditions. It will generate a signal and wait for your manual approval before placing any trades."
        };
      case "SignalOnly":
      default:
        return {
          icon: <Radar className="w-5 h-5 text-purple-500" />,
          color: "text-purple-500",
          bgColor: "bg-purple-50 dark:bg-purple-900/20",
          statusText: "Scanning (Signal Only)",
          description: "Monitoring for setups. Will alert you visually upon a signal, but will not queue or execute any trades automatically."
        };
    }
  };

  if (loading) {
    return <div className="p-10 flex items-center justify-center">Loading strategy data...</div>;
  }

  return (
    <div className="p-6 md:p-10 max-w-6xl mx-auto space-y-8">
      <header className="flex items-center gap-4 border-b border-slate-100 dark:border-slate-800 pb-6">
        <div className="w-12 h-12 bg-indigo-50 dark:bg-indigo-900/20 text-indigo-600 rounded-xl flex items-center justify-center shrink-0">
          <Activity className="w-6 h-6" />
        </div>
        <div>
          <h1 className="text-3xl font-bold text-slate-900 dark:text-white">Strategy Analysis</h1>
          <p className="text-slate-500 mt-1">Real-time status and operational details of your enabled strategies</p>
        </div>
      </header>

      {strategies.length === 0 ? (
        <div className="flex flex-col items-center justify-center h-64 bg-surface rounded-2xl border border-slate-100 dark:border-slate-800 shadow-sm">
          <BarChart2 className="w-16 h-16 text-slate-300 dark:text-slate-700 mb-4" />
          <p className="text-xl font-bold text-slate-700 dark:text-slate-300">No active strategies</p>
          <p className="text-slate-500 mt-2">Go to the Strategies page to enable them.</p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
          {strategies.map(strategy => {
            const details = getStatusDetails(strategy);
            
            return (
              <div 
                key={strategy.id} 
                className="bg-surface border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-sm flex flex-col h-full transition-transform hover:-translate-y-1 hover:shadow-md"
              >
                <div className="flex items-center justify-between mb-4">
                  <h2 className="text-xl font-bold text-slate-900 dark:text-white truncate" title={strategy.strategyName}>
                    {strategy.strategyName}
                  </h2>
                  <div className={`p-2 rounded-lg ${details.bgColor}`}>
                    {details.icon}
                  </div>
                </div>

                <div className="flex items-center gap-2 mb-4">
                  <div className="flex relative w-3 h-3">
                    <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-green-400 opacity-75"></span>
                    <span className="relative inline-flex rounded-full h-3 w-3 bg-green-500"></span>
                  </div>
                  <span className={`font-semibold ${details.color}`}>
                    {details.statusText}
                  </span>
                </div>

                <div className="mb-6 flex-grow">
                  <h3 className="text-xs font-semibold text-slate-500 uppercase tracking-wider mb-2">What to expect next</h3>
                  <p className="text-sm text-slate-700 dark:text-slate-300 leading-relaxed bg-slate-50 dark:bg-slate-900/50 p-3 rounded-lg border border-slate-100 dark:border-slate-800">
                    {details.description}
                  </p>
                </div>

                <div className="grid grid-cols-2 gap-4 border-t border-slate-100 dark:border-slate-800 pt-4">
                  <div>
                    <p className="text-xs text-slate-500 font-medium">Timeframe</p>
                    <p className="font-bold text-slate-900 dark:text-white">{strategy.timeframeMinutes} Min</p>
                  </div>
                  <div>
                    <p className="text-xs text-slate-500 font-medium">Target / SL</p>
                    <p className="font-bold text-slate-900 dark:text-white">{strategy.perTradeGainPoint} / {strategy.perTradeStopLossPoint}</p>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
