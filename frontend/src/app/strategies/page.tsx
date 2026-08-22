"use client";

import { useState, useEffect } from "react";
import { Settings, Play, Square, Save } from "lucide-react";
import { fetchWithAuth } from "@/lib/api";

interface StrategyConfig {
  id: number;
  strategyName: string;
  isEnabled: boolean;
  perTradeStopLossPoint: number;
  perTradeGainPoint: number;
  timeframeMinutes: number;
  trailingStopLossPoint: number;
  operatingMode: string;
  additionalParamsJson: string; // JSON string from backend
}

export default function StrategiesPage() {
  const [strategies, setStrategies] = useState<StrategyConfig[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetchWithAuth("/api/strategyconfig")
      .then(res => res.json())
      .then(data => {
        setStrategies(data);
        setLoading(false);
      })
      .catch(err => {
        console.error(err);
        setLoading(false);
      });
  }, []);

  const toggleStrategy = async (id: number) => {
    const strategy = strategies.find(s => s.id === id);
    if (!strategy) return;
    
    const updated = { ...strategy, isEnabled: !strategy.isEnabled };
    setStrategies(strategies.map(s => s.id === id ? updated : s));

    try {
      await fetchWithAuth(`/api/strategyconfig/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(updated)
      });
    } catch (err) {
      console.error(err);
    }
  };

  const updateParam = (id: number, field: keyof StrategyConfig, value: number) => {
    setStrategies(strategies.map(s => s.id === id ? { ...s, [field]: value } : s));
  };

  const updateStringParam = (id: number, field: keyof StrategyConfig, value: string) => {
    setStrategies(strategies.map(s => s.id === id ? { ...s, [field]: value } : s));
  };

  const updateAdditionalParam = (id: number, key: string, value: any) => {
    setStrategies(strategies.map(s => {
      if (s.id !== id) return s;
      
      let params = {};
      try {
        params = s.additionalParamsJson ? JSON.parse(s.additionalParamsJson) : {};
      } catch (e) {
        params = {};
      }
      
      const updatedParams = { ...params, [key]: value };
      return { ...s, additionalParamsJson: JSON.stringify(updatedParams) };
    }));
  };

  const saveConfig = async (id: number) => {
    const strategy = strategies.find(s => s.id === id);
    if (!strategy) return;
    try {
      await fetchWithAuth(`/api/strategyconfig/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(strategy)
      });
      alert(`Config for ${strategy.strategyName} saved!`);
    } catch (err) {
      console.error(err);
    }
  };

  const renderSpecificParams = (strategy: StrategyConfig) => {
    let params: Record<string, any> = {};
    try {
      params = strategy.additionalParamsJson ? JSON.parse(strategy.additionalParamsJson) : {};
    } catch (e) {}

    const name = strategy.strategyName;

    if (name.includes("Supertrend")) {
      const atr = params.atrPeriod ?? 14;
      const mult = params.multiplier ?? 3;
      const useOptionGate = params.useOptionGate ?? true;
      return (
        <div className="space-y-4 mt-4 pt-4 border-t border-slate-100 dark:border-slate-800">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-semibold text-primary uppercase tracking-wider mb-1">ATR Period</label>
              <input type="number" value={atr} onChange={e => updateAdditionalParam(strategy.id, 'atrPeriod', Number(e.target.value))} className="w-full px-3 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none" />
            </div>
            <div>
              <label className="block text-xs font-semibold text-primary uppercase tracking-wider mb-1">Multiplier</label>
              <input type="number" value={mult} onChange={e => updateAdditionalParam(strategy.id, 'multiplier', Number(e.target.value))} className="w-full px-3 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none" />
            </div>
          </div>
          <label className="flex items-center gap-2 cursor-pointer text-xs font-medium text-slate-700 dark:text-slate-300">
            <input type="checkbox" checked={useOptionGate} onChange={e => updateAdditionalParam(strategy.id, 'useOptionGate', e.target.checked)} className="w-4 h-4 text-primary rounded" />
            <span>Institutional Option Chain Floor & PCR Gate</span>
          </label>
        </div>
      );
    }
    
    if (name.includes("EMA Pullback")) {
      const fast = params.fastEma ?? 9;
      const slow = params.slowEma ?? 21;
      const checkFvg = params.checkFvg ?? false;
      return (
        <div className="space-y-4 mt-4 pt-4 border-t border-slate-100 dark:border-slate-800">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-semibold text-primary uppercase tracking-wider mb-1">Fast EMA</label>
              <input type="number" value={fast} onChange={e => updateAdditionalParam(strategy.id, 'fastEma', Number(e.target.value))} className="w-full px-3 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none" />
            </div>
            <div>
              <label className="block text-xs font-semibold text-primary uppercase tracking-wider mb-1">Slow EMA</label>
              <input type="number" value={slow} onChange={e => updateAdditionalParam(strategy.id, 'slowEma', Number(e.target.value))} className="w-full px-3 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none" />
            </div>
          </div>
          <label className="flex items-center gap-2 cursor-pointer text-xs font-medium text-slate-700 dark:text-slate-300">
            <input type="checkbox" checked={checkFvg} onChange={e => updateAdditionalParam(strategy.id, 'checkFvg', e.target.checked)} className="w-4 h-4 text-primary rounded" />
            <span>Confluence: Low must tap Bullish FVG (Fair Value Gap)</span>
          </label>
        </div>
      );
    }
    
    if (name.includes("MACD")) {
      const fast = params.macdFast ?? 12;
      const slow = params.macdSlow ?? 26;
      const sig = params.macdSignal ?? 9;
      return (
        <div className="grid grid-cols-3 gap-4 mt-4 pt-4 border-t border-slate-100 dark:border-slate-800">
          <div>
            <label className="block text-xs font-semibold text-primary uppercase tracking-wider mb-1">Fast</label>
            <input type="number" value={fast} onChange={e => updateAdditionalParam(strategy.id, 'macdFast', Number(e.target.value))} className="w-full px-3 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none" />
          </div>
          <div>
            <label className="block text-xs font-semibold text-primary uppercase tracking-wider mb-1">Slow</label>
            <input type="number" value={slow} onChange={e => updateAdditionalParam(strategy.id, 'macdSlow', Number(e.target.value))} className="w-full px-3 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none" />
          </div>
          <div>
            <label className="block text-xs font-semibold text-primary uppercase tracking-wider mb-1">Signal</label>
            <input type="number" value={sig} onChange={e => updateAdditionalParam(strategy.id, 'macdSignal', Number(e.target.value))} className="w-full px-3 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none" />
          </div>
        </div>
      );
    }
    
    if (name.includes("Bollinger")) {
      const period = params.bbPeriod ?? 20;
      const dev = params.bbStdDev ?? 2;
      const usePocGate = params.usePocGate ?? true;
      return (
        <div className="space-y-4 mt-4 pt-4 border-t border-slate-100 dark:border-slate-800">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-semibold text-primary uppercase tracking-wider mb-1">Period</label>
              <input type="number" value={period} onChange={e => updateAdditionalParam(strategy.id, 'bbPeriod', Number(e.target.value))} className="w-full px-3 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none" />
            </div>
            <div>
              <label className="block text-xs font-semibold text-primary uppercase tracking-wider mb-1">Std Dev</label>
              <input type="number" value={dev} onChange={e => updateAdditionalParam(strategy.id, 'bbStdDev', Number(e.target.value))} className="w-full px-3 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none" />
            </div>
          </div>
          <label className="flex items-center gap-2 cursor-pointer text-xs font-medium text-slate-700 dark:text-slate-300">
            <input type="checkbox" checked={usePocGate} onChange={e => updateAdditionalParam(strategy.id, 'usePocGate', e.target.checked)} className="w-4 h-4 text-primary rounded" />
            <span>Volume Profile POC Acceptance Filter</span>
          </label>
        </div>
      );
    }

    if (name.includes("ORB")) {
      const useVwap = params.useVwap ?? true;
      const useOptionGate = params.useOptionGate ?? true;
      return (
        <div className="space-y-3 mt-4 pt-4 border-t border-slate-100 dark:border-slate-800">
          <label className="flex items-center gap-2 cursor-pointer text-xs font-medium text-slate-700 dark:text-slate-300">
            <input type="checkbox" checked={useVwap} onChange={e => updateAdditionalParam(strategy.id, 'useVwap', e.target.checked)} className="w-4 h-4 text-primary rounded" />
            <span>Require VWAP Confirmation</span>
          </label>
          <label className="flex items-center gap-2 cursor-pointer text-xs font-medium text-slate-700 dark:text-slate-300">
            <input type="checkbox" checked={useOptionGate} onChange={e => updateAdditionalParam(strategy.id, 'useOptionGate', e.target.checked)} className="w-4 h-4 text-primary rounded" />
            <span>Option Chain Floor Defense Confirmation</span>
          </label>
        </div>
      );
    }

    if (name.includes("Wyckoff")) {
      const lookback = params.lookback ?? 20;
      const minRvol = params.minRvol ?? 1.8;
      return (
        <div className="grid grid-cols-2 gap-4 mt-4 pt-4 border-t border-slate-100 dark:border-slate-800">
          <div>
            <label className="block text-xs font-semibold text-primary uppercase tracking-wider mb-1">Support Lookback (Bars)</label>
            <input type="number" value={lookback} onChange={e => updateAdditionalParam(strategy.id, 'lookback', Number(e.target.value))} className="w-full px-3 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none" />
          </div>
          <div>
            <label className="block text-xs font-semibold text-primary uppercase tracking-wider mb-1">Min RVOL Sweep</label>
            <input type="number" step="0.1" value={minRvol} onChange={e => updateAdditionalParam(strategy.id, 'minRvol', Number(e.target.value))} className="w-full px-3 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none" />
          </div>
        </div>
      );
    }

    if (name.includes("Fair Value Gap") || name.includes("FVG")) {
      const minGap = params.minGapPoints ?? 8;
      return (
        <div className="grid grid-cols-1 gap-4 mt-4 pt-4 border-t border-slate-100 dark:border-slate-800">
          <div>
            <label className="block text-xs font-semibold text-primary uppercase tracking-wider mb-1">Min Imbalance Gap (Points)</label>
            <input type="number" value={minGap} onChange={e => updateAdditionalParam(strategy.id, 'minGapPoints', Number(e.target.value))} className="w-full px-3 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none" />
          </div>
        </div>
      );
    }
    
    return null;
  };

  if (loading) return <div className="p-10">Loading strategies...</div>;

  return (
    <div className="p-6 md:p-10 max-w-7xl mx-auto space-y-8">
      <header>
        <h1 className="text-3xl font-bold text-slate-900 dark:text-white flex items-center gap-3">
          <Settings className="w-8 h-8 text-primary" />
          Strategy Configuration
        </h1>
        <p className="text-slate-500 mt-1">Enable and tune algorithmic trading strategies</p>
      </header>

      <div className="grid grid-cols-1 xl:grid-cols-2 gap-6">
        {strategies.map((strategy) => (
          <div key={strategy.id} className="bg-surface p-6 rounded-2xl shadow-sm border border-slate-100 dark:border-slate-800 flex flex-col justify-between">
            <div>
              <div className="flex flex-col md:flex-row md:items-center justify-between mb-6 gap-4">
                <div className="flex items-center gap-3">
                  <h2 className="text-xl font-bold">{strategy.strategyName}</h2>
                  <span className={`px-3 py-1 text-xs font-bold uppercase tracking-wider rounded-full ${
                    strategy.isEnabled 
                      ? "bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-400 border border-green-200 dark:border-green-800" 
                      : "bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400 border border-slate-200 dark:border-slate-700"
                  }`}>
                    {strategy.isEnabled ? "Active" : "Inactive"}
                  </span>
                </div>
                <button
                  onClick={() => toggleStrategy(strategy.id)}
                  className={`flex items-center gap-2 px-4 py-2 rounded-lg font-medium transition-colors ${
                    strategy.isEnabled 
                      ? "bg-red-100 text-red-700 hover:bg-red-200 dark:bg-red-900/30 dark:text-red-400" 
                      : "bg-primary/10 text-primary hover:bg-primary/20"
                  }`}
                >
                  {strategy.isEnabled ? (
                    <><Square className="w-4 h-4" /> Disable</>
                  ) : (
                    <><Play className="w-4 h-4" /> Enable</>
                  )}
                </button>
              </div>

              <div className="mb-4">
                <label className="block text-xs font-semibold text-slate-500 uppercase tracking-wider mb-1">Operating Mode</label>
                <select 
                  value={strategy.operatingMode || "ApprovalRequired"} 
                  onChange={e => updateStringParam(strategy.id, 'operatingMode', e.target.value)}
                  className="w-full px-3 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
                >
                  <option value="Automatic">Fully Automatic</option>
                  <option value="ApprovalRequired">Approval Required</option>
                  <option value="SignalOnly">Signal Only</option>
                </select>
              </div>

              <div className="grid grid-cols-2 lg:grid-cols-4 gap-4 mb-2">
                <div>
                  <label className="block text-xs font-semibold text-slate-500 uppercase tracking-wider mb-1">Time (Min)</label>
                  <input 
                    type="number" 
                    value={strategy.timeframeMinutes}
                    onChange={e => updateParam(strategy.id, 'timeframeMinutes', Number(e.target.value))}
                    className="w-full px-3 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
                  />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-slate-500 uppercase tracking-wider mb-1">Target</label>
                  <input 
                    type="number" 
                    value={strategy.perTradeGainPoint}
                    onChange={e => updateParam(strategy.id, 'perTradeGainPoint', Number(e.target.value))}
                    className="w-full px-3 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
                  />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-slate-500 uppercase tracking-wider mb-1">Stoploss</label>
                  <input 
                    type="number" 
                    value={strategy.perTradeStopLossPoint}
                    onChange={e => updateParam(strategy.id, 'perTradeStopLossPoint', Number(e.target.value))}
                    className="w-full px-3 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
                  />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-slate-500 uppercase tracking-wider mb-1">Trail SL</label>
                  <input 
                    type="number" 
                    value={strategy.trailingStopLossPoint}
                    onChange={e => updateParam(strategy.id, 'trailingStopLossPoint', Number(e.target.value))}
                    className="w-full px-3 py-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
                  />
                </div>
              </div>
              
              {/* Dynamic Algorithm-Specific Parameters */}
              {renderSpecificParams(strategy)}
              
            </div>

            <button 
              onClick={() => saveConfig(strategy.id)}
              className="w-full mt-6 flex items-center justify-center gap-2 py-3 border border-primary text-primary hover:bg-primary/5 rounded-lg font-bold transition-colors"
            >
              <Save className="w-4 h-4" />
              Save Configuration
            </button>
          </div>
        ))}
      </div>
    </div>
  );
}
