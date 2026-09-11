"use client";

import { useState, useEffect } from "react";
import { Settings2, Save, ShieldCheck } from "lucide-react";
import { fetchWithAuth } from "@/lib/api";

interface GlobalSettings {
  maxLossPerTrade: number;
  maxDailyLoss: number;
  maxTradesPerDay: number;
  maxFailedTrades: number;
  vixMinLimit: number;
  vixMaxLimit: number;
  perTradeStopLossPoint: number;
  perTradeGainPoint: number;
  tradeMode: string;
  killSwitchShutdownMinutes: number;
  autoTradeLots: number;
  baseLotSize: number;
  trailingStopLossPoint: number;
  maxActivePositions: number;
  maxCapitalPerTrade: number;
  disallowOppositeLegs: boolean;
  allowHtfReversalOverwrite: boolean;
  preventSameStrategyPyramiding: boolean;
}

export default function SettingsPage() {
  const [settings, setSettings] = useState<GlobalSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchWithAuth("/api/globalsettings")
      .then(res => res.json())
      .then(data => {
        setSettings(data);
        setLoading(false);
      })
      .catch(err => {
        console.error(err);
        setError("Failed to load settings");
        setLoading(false);
      });
  }, []);

  const handleChange = (field: keyof GlobalSettings, value: any) => {
    if (settings) {
      setSettings({ ...settings, [field]: value });
    }
  };

  const saveSettings = async () => {
    if (!settings) return;
    setSaving(true);
    setError(null);
    try {
      const res = await fetchWithAuth("/api/globalsettings", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(settings)
      });
      if (res.ok) {
        alert("Global parameters saved successfully!");
      } else {
        setError("Failed to save settings");
      }
    } catch (err) {
      console.error(err);
      setError("Network error when saving settings");
    } finally {
      setSaving(false);
    }
  };

  if (loading) return <div className="p-10">Loading settings...</div>;
  if (!settings) return <div className="p-10">Error loading settings.</div>;

  return (
    <div className="p-6 md:p-10 max-w-4xl mx-auto space-y-8">
      <header>
        <h1 className="text-3xl font-bold text-slate-900 dark:text-white flex items-center gap-3">
          <Settings2 className="w-8 h-8 text-primary" />
          Global Parameters
        </h1>
        <p className="text-slate-500 mt-1">Configure your master risk management and system-wide settings</p>
      </header>

      {error && (
        <div className="p-4 bg-red-50 text-red-600 rounded-lg">
          {error}
        </div>
      )}

      <div className="bg-surface p-6 md:p-8 rounded-2xl shadow-sm border border-slate-100 dark:border-slate-800 space-y-8">
        
        {/* Loss Limits */}
        <section>
          <h2 className="text-xl font-bold mb-4 pb-2 border-b border-slate-100 dark:border-slate-800">Risk & Loss Limits</h2>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">Max Loss Per Trade (₹)</label>
              <input 
                type="number" 
                value={settings.maxLossPerTrade}
                onChange={e => handleChange('maxLossPerTrade', Number(e.target.value))}
                className="w-full px-4 py-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
              />
              <p className="text-xs text-slate-500 mt-1">Individual trade will be exited if loss exceeds this limit.</p>
            </div>
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">Max Loss Per Day (₹)</label>
              <input 
                type="number" 
                value={settings.maxDailyLoss}
                onChange={e => handleChange('maxDailyLoss', Number(e.target.value))}
                className="w-full px-4 py-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
              />
              <p className="text-xs text-slate-500 mt-1">Kill switch triggers instantly if overall daily MTM drops below this value.</p>
            </div>
          </div>
        </section>

        {/* Per Trade Targets */}
        <section>
          <h2 className="text-xl font-bold mb-4 pb-2 border-b border-slate-100 dark:border-slate-800">Per Trade Points (Target / SL)</h2>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">Per Trade Gain Point</label>
              <input 
                type="number" 
                value={settings.perTradeGainPoint}
                onChange={e => handleChange('perTradeGainPoint', Number(e.target.value))}
                className="w-full px-4 py-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
              />
              <p className="text-xs text-slate-500 mt-1">Global target points for a single trade.</p>
            </div>
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">Per Trade Stop Loss Point</label>
              <input 
                type="number" 
                value={settings.perTradeStopLossPoint}
                onChange={e => handleChange('perTradeStopLossPoint', Number(e.target.value))}
                className="w-full px-4 py-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
              />
              <p className="text-xs text-slate-500 mt-1">Global stop-loss points for a single trade.</p>
            </div>
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">Global Trailing Stop Loss Point</label>
              <input 
                type="number" 
                value={settings.trailingStopLossPoint ?? 8}
                onChange={e => handleChange('trailingStopLossPoint', Number(e.target.value))}
                className="w-full px-4 py-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
              />
              <p className="text-xs text-slate-500 mt-1">Points to trail stop-loss upward as option LTP rises.</p>
            </div>
          </div>
        </section>

        {/* Trade Execution Limits */}
        <section>
          <h2 className="text-xl font-bold mb-4 pb-2 border-b border-slate-100 dark:border-slate-800">Trade Execution Limits</h2>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">Auto Trade Lot(s)</label>
              <input 
                type="number" 
                value={settings.autoTradeLots}
                onChange={e => handleChange('autoTradeLots', Number(e.target.value))}
                className="w-full px-4 py-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
              />
              <p className="text-xs text-slate-500 mt-1">Number of lots to execute per trade (multiplied by Base Lot Size).</p>
            </div>
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">Base Lot Size</label>
              <input 
                type="number" 
                value={settings.baseLotSize}
                onChange={e => handleChange('baseLotSize', Number(e.target.value))}
                className="w-full px-4 py-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
              />
              <p className="text-xs text-slate-500 mt-1">Default quantity size for options (e.g. 25, 50, 65).</p>
            </div>
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">Max Trades Per Day</label>
              <input 
                type="number" 
                value={settings.maxTradesPerDay}
                onChange={e => handleChange('maxTradesPerDay', Number(e.target.value))}
                className="w-full px-4 py-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
              />
              <p className="text-xs text-slate-500 mt-1">System stops taking new entries after this many trades.</p>
            </div>
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">Max Failed Trades in a Day</label>
              <input 
                type="number" 
                value={settings.maxFailedTrades}
                onChange={e => handleChange('maxFailedTrades', Number(e.target.value))}
                className="w-full px-4 py-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
              />
              <p className="text-xs text-slate-500 mt-1">System halts if consecutive stop-losses hit this limit.</p>
            </div>
          </div>
        </section>

        {/* VIX Filters */}
        <section>
          <h2 className="text-xl font-bold mb-4 pb-2 border-b border-slate-100 dark:border-slate-800">Market VIX Filters</h2>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">Minimum VIX</label>
              <input 
                type="number" 
                value={settings.vixMinLimit}
                onChange={e => handleChange('vixMinLimit', Number(e.target.value))}
                className="w-full px-4 py-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
              />
              <p className="text-xs text-slate-500 mt-1">Option buying disabled if VIX is below this level.</p>
            </div>
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">Maximum VIX</label>
              <input 
                type="number" 
                value={settings.vixMaxLimit}
                onChange={e => handleChange('vixMaxLimit', Number(e.target.value))}
                className="w-full px-4 py-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
              />
              <p className="text-xs text-slate-500 mt-1">Option buying disabled if VIX exceeds this level.</p>
            </div>
          </div>
        </section>

        {/* Portfolio Concurrency & Capital Guards */}
        <section>
          <h2 className="text-xl font-bold mb-4 pb-2 border-b border-slate-100 dark:border-slate-800 flex items-center justify-between">
            <span className="flex items-center gap-2">
              <ShieldCheck className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />
              Portfolio Concurrency & Capital Guards
            </span>
            <span className="text-xs font-medium px-2.5 py-1 rounded-full bg-emerald-50 text-emerald-700 dark:bg-emerald-950/40 dark:text-emerald-400 border border-emerald-200 dark:border-emerald-800">
              Active Shield
            </span>
          </h2>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">Max Concurrent Active Positions</label>
              <input 
                type="number" 
                value={settings.maxActivePositions ?? 1}
                onChange={e => handleChange('maxActivePositions', Number(e.target.value))}
                className="w-full px-4 py-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
              />
              <p className="text-xs text-slate-500 mt-1">Limits simultaneous active trades across all strategies to protect capital X.</p>
            </div>
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">Max Capital Per Trade (₹)</label>
              <input 
                type="number" 
                value={settings.maxCapitalPerTrade ?? 200000}
                onChange={e => handleChange('maxCapitalPerTrade', Number(e.target.value))}
                className="w-full px-4 py-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
              />
              <p className="text-xs text-slate-500 mt-1">Blocks trade entry if total capital required (Lots × Qty × Premium) exceeds this limit.</p>
            </div>
          </div>

          <div className="mt-6 space-y-3 pt-2">
            <label className="flex items-start gap-3 p-3.5 rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-50/50 dark:bg-slate-900/30 cursor-pointer hover:bg-slate-100/50 dark:hover:bg-slate-900/50 transition-colors">
              <input 
                type="checkbox" 
                checked={settings.disallowOppositeLegs ?? true}
                onChange={e => handleChange('disallowOppositeLegs', e.target.checked)}
                className="mt-1 w-4 h-4 rounded text-primary focus:ring-primary"
              />
              <div>
                <span className="font-semibold text-sm text-slate-800 dark:text-slate-200">Prevent Simultaneous Opposite Legs (No CE + PE Conflict)</span>
                <p className="text-xs text-slate-500 mt-0.5">Prevents self-hedging positions where theta decay eats both long call and long put options during range-bound conditions.</p>
              </div>
            </label>

            <label className="flex items-start gap-3 p-3.5 rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-50/50 dark:bg-slate-900/30 cursor-pointer hover:bg-slate-100/50 dark:hover:bg-slate-900/50 transition-colors">
              <input 
                type="checkbox" 
                checked={settings.allowHtfReversalOverwrite ?? true}
                onChange={e => handleChange('allowHtfReversalOverwrite', e.target.checked)}
                className="mt-1 w-4 h-4 rounded text-primary focus:ring-primary"
              />
              <div>
                <span className="font-semibold text-sm text-slate-800 dark:text-slate-200">Allow Higher-Timeframe Reversal Overwrite (Option B)</span>
                <p className="text-xs text-slate-500 mt-0.5">If an opposite signal is confirmed by 15m EMA/VWAP higher-timeframe trend, the existing opposite trade is cleanly squared off first before the reversal trade is entered.</p>
              </div>
            </label>

            <label className="flex items-start gap-3 p-3.5 rounded-xl border border-slate-200 dark:border-slate-800 bg-slate-50/50 dark:bg-slate-900/30 cursor-pointer hover:bg-slate-100/50 dark:hover:bg-slate-900/50 transition-colors">
              <input 
                type="checkbox" 
                checked={settings.preventSameStrategyPyramiding ?? true}
                onChange={e => handleChange('preventSameStrategyPyramiding', e.target.checked)}
                className="mt-1 w-4 h-4 rounded text-primary focus:ring-primary"
              />
              <div>
                <span className="font-semibold text-sm text-slate-800 dark:text-slate-200">Prevent Same-Strategy Pyramiding / Duplicate Entries</span>
                <p className="text-xs text-slate-500 mt-0.5">Blocks duplicate entry signals for a strategy that already holds an active position in that direction.</p>
              </div>
            </label>
          </div>
        </section>

        {/* Trade Mode & App Lock */}
        <section>
          <h2 className="text-xl font-bold mb-4 pb-2 border-b border-slate-100 dark:border-slate-800">System Mode & Lock</h2>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">Trade Mode</label>
              <select 
                value={settings.tradeMode}
                onChange={e => handleChange('tradeMode', e.target.value as any)}
                className="w-full px-4 py-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
              >
                <option value="Paper">Paper Trading</option>
                <option value="Live">Live Trading</option>
              </select>
              <p className="text-xs text-slate-500 mt-1">Select whether to simulate trades or send real orders.</p>
            </div>
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">Kill Switch Lock Time (Minutes)</label>
              <input 
                type="number" 
                value={settings.killSwitchShutdownMinutes}
                onChange={e => handleChange('killSwitchShutdownMinutes', Number(e.target.value))}
                className="w-full px-4 py-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none"
              />
              <p className="text-xs text-slate-500 mt-1">Duration the app remains locked after Kill Switch is triggered.</p>
            </div>
          </div>
        </section>

        <div className="pt-4">
          <button 
            onClick={saveSettings}
            disabled={saving}
            className="w-full md:w-auto px-8 py-4 bg-primary hover:bg-primary-hover text-white rounded-xl font-bold text-lg transition-colors flex items-center justify-center gap-2"
          >
            <Save className="w-5 h-5" />
            {saving ? "Saving..." : "Save Global Parameters"}
          </button>
        </div>
      </div>
    </div>
  );
}
