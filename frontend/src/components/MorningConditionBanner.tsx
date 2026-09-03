"use client";

import { useState, useEffect } from "react";
import { AlertTriangle, TrendingUp, TrendingDown, Activity, ShieldAlert, ChevronDown, ChevronUp, AlertOctagon, Flame } from "lucide-react";
import { fetchWithAuth } from "@/lib/api";

interface BuyerTrapAlert {
  trapId: string;
  name: string;
  severity: string;
  description: string;
  footprintEvidence: string;
  buyerDirective: string;
}

interface MorningMarketCondition {
  spotPrice: number;
  vwap: number;
  isPriorDayCompressed: boolean;
  compressionType: string;
  priorDayRange: number;
  liquidityRejection: string;
  openPrice0915: number;
  sessionHigh: number;
  sessionLow: number;
  maxRejectionWickRatio: number;
  vwapStatus: string;
  vwapSlope: number;
  optionOiBias: string;
  pcr: number;
  institutionalFloorStrike: number;
  institutionalCeilingStrike: number;
  marketRegime: string;
  regimeLabel: string;
  actionDirective: string;
  overtradingShieldActive: boolean;
  warningLevel: string;
  detectedTraps?: BuyerTrapAlert[];
  trapCount?: number;
}

export default function MorningConditionBanner() {
  const [data, setData] = useState<MorningMarketCondition | null>(null);
  const [expanded, setExpanded] = useState<boolean>(false);

  const fetchCondition = async () => {
    try {
      const res = await fetchWithAuth("/api/marketdata/morning-condition");
      if (res.ok) {
        const json = await res.json();
        setData(json);
      }
    } catch {
      // Ignore background poll errors
    }
  };

  useEffect(() => {
    fetchCondition();
    const interval = setInterval(fetchCondition, 10000);
    return () => clearInterval(interval);
  }, []);

  if (!data) return null;

  const hasTraps = data.detectedTraps && data.detectedTraps.length > 0;
  const isChoppy = data.overtradingShieldActive || data.marketRegime === "CHOPPY_RANGE_BOUND" || hasTraps;
  const isBullish = data.marketRegime === "BULLISH_TREND_DAY";
  const isBearish = data.marketRegime === "BEARISH_TREND_DAY";

  const themeStyles = isChoppy
    ? {
        border: "border-rose-500/40 dark:border-rose-500/30",
        bg: "bg-rose-50/95 dark:bg-rose-950/40",
        badgeBg: "bg-rose-600 text-white animate-pulse",
        textPrimary: "text-rose-900 dark:text-rose-200",
        textSecondary: "text-rose-800 dark:text-rose-300",
        icon: <ShieldAlert className="w-5 h-5 text-rose-600 dark:text-rose-400 shrink-0" />
      }
    : isBullish
    ? {
        border: "border-emerald-500/40 dark:border-emerald-500/30",
        bg: "bg-emerald-50/90 dark:bg-emerald-950/30",
        badgeBg: "bg-emerald-600 text-white",
        textPrimary: "text-emerald-950 dark:text-emerald-100",
        textSecondary: "text-emerald-700 dark:text-emerald-300",
        icon: <TrendingUp className="w-5 h-5 text-emerald-600 dark:text-emerald-400 shrink-0" />
      }
    : isBearish
    ? {
        border: "border-amber-500/40 dark:border-amber-500/30",
        bg: "bg-amber-50/90 dark:bg-amber-950/30",
        badgeBg: "bg-amber-600 text-white",
        textPrimary: "text-amber-950 dark:text-amber-100",
        textSecondary: "text-amber-800 dark:text-amber-300",
        icon: <TrendingDown className="w-5 h-5 text-amber-600 dark:text-amber-400 shrink-0" />
      }
    : {
        border: "border-blue-500/30 dark:border-blue-500/20",
        bg: "bg-blue-50/80 dark:bg-blue-950/20",
        badgeBg: "bg-blue-600 text-white",
        textPrimary: "text-blue-950 dark:text-blue-100",
        textSecondary: "text-blue-700 dark:text-blue-300",
        icon: <Activity className="w-5 h-5 text-blue-600 dark:text-blue-400 shrink-0" />
      };

  return (
    <aside aria-label="Morning Market Condition Scanner" className={`w-full border-b backdrop-blur-md ${themeStyles.bg} ${themeStyles.border} transition-colors duration-300`}>
      <div className="max-w-7xl mx-auto px-4 py-2.5 sm:px-6 lg:px-8">
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-3">
          {/* Main Alert Info */}
          <div className="flex items-center gap-3 min-w-0">
            {themeStyles.icon}
            <div className="min-w-0">
              <div className="flex items-center gap-2 flex-wrap">
                <span className={`text-[11px] font-black tracking-wider uppercase px-2 py-0.5 rounded-full shadow-sm ${themeStyles.badgeBg}`}>
                  {hasTraps ? `⚠️ ${data.detectedTraps?.length} TRAP(S) ACTIVE` : isChoppy ? "OVERTRADING SHIELD ACTIVE" : "09:15–10:00 AM SCANNER"}
                </span>
                <span className={`text-xs sm:text-sm font-bold truncate ${themeStyles.textPrimary}`}>
                  {data.regimeLabel}
                </span>
                <span className="text-[11px] font-mono text-slate-500 dark:text-slate-400">
                  Spot: ₹{data.spotPrice.toFixed(1)} | VWAP: ₹{data.vwap.toFixed(1)}
                </span>
              </div>
              <p className={`text-xs font-semibold mt-0.5 ${themeStyles.textSecondary}`}>
                👉 {data.actionDirective}
              </p>
            </div>
          </div>

          {/* Quick Metrics & Toggle Button */}
          <div className="flex items-center gap-2 shrink-0 self-end md:self-center">
            <div className="hidden sm:flex items-center gap-3 text-[11px] font-mono bg-white/60 dark:bg-slate-900/60 px-2.5 py-1 rounded-lg border border-slate-200/50 dark:border-slate-800/50">
              <span>PCR: <b>{data.pcr.toFixed(2)}</b></span>
              <span className="text-slate-300 dark:text-slate-700">|</span>
              <span>Wick: <b>{(data.maxRejectionWickRatio * 100).toFixed(0)}%</b></span>
              <span className="text-slate-300 dark:text-slate-700">|</span>
              <span>{data.isPriorDayCompressed ? "⚡ Compressed" : "Normal Vol"}</span>
            </div>

            <button
              onClick={() => setExpanded(!expanded)}
              className="flex items-center gap-1 text-xs font-semibold px-2.5 py-1 rounded-lg bg-white/80 dark:bg-slate-900/80 hover:bg-white dark:hover:bg-slate-800 border border-slate-200 dark:border-slate-700 text-slate-700 dark:text-slate-200 transition-colors shadow-sm"
              title="View 4-Step Checklist & Trap Details"
            >
              <span>{hasTraps ? "View Traps & Structure" : "4-Step Footprint"}</span>
              {expanded ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />}
            </button>
          </div>
        </div>

        {/* Expanded Drawer */}
        {expanded && (
          <div className="mt-3 pt-3 border-t border-slate-200/60 dark:border-slate-800/60 space-y-3">
            {/* Active Traps Section (if any detected) */}
            {hasTraps && (
              <div className="space-y-2">
                <div className="text-xs font-black uppercase tracking-wider text-rose-600 dark:text-rose-400 flex items-center gap-1.5">
                  <AlertOctagon className="w-4 h-4" />
                  <span>Identified Option Buyer Traps</span>
                </div>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-2.5">
                  {data.detectedTraps?.map((trap, idx) => (
                    <div key={idx} className="p-3 rounded-xl bg-rose-100/70 dark:bg-rose-950/60 border border-rose-300 dark:border-rose-800/60 text-xs space-y-1">
                      <div className="flex items-center justify-between">
                        <span className="font-bold text-rose-950 dark:text-rose-200">{trap.name}</span>
                        <span className={`text-[10px] font-extrabold px-2 py-0.5 rounded-md ${
                          trap.severity === "Critical"
                            ? "bg-rose-600 text-white"
                            : trap.severity === "High"
                            ? "bg-orange-500 text-white"
                            : "bg-amber-500 text-white"
                        }`}>
                          {trap.severity}
                        </span>
                      </div>
                      <p className="text-rose-900/90 dark:text-rose-300 font-normal">
                        {trap.description}
                      </p>
                      <div className="text-[11px] font-mono text-slate-700 dark:text-slate-300 bg-white/50 dark:bg-slate-900/50 p-1.5 rounded border border-rose-200/50 dark:border-rose-900/50">
                        🔍 <b>Footprint:</b> {trap.footprintEvidence}
                      </div>
                      <div className="text-[11px] font-bold text-rose-900 dark:text-rose-200 pt-0.5">
                        🛡️ <b>Shield Directive:</b> {trap.buyerDirective}
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {/* 4-Step Checklist Grid */}
            <div>
              <div className="text-xs font-bold uppercase tracking-wider text-slate-500 dark:text-slate-400 mb-2">
                4-Step Institutional Footprint Breakdown
              </div>
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 text-xs">
                {/* Step 1: Pre-market compression */}
                <div className="p-2.5 rounded-lg bg-white/70 dark:bg-slate-900/70 border border-slate-200/60 dark:border-slate-800/60">
                  <div className="font-bold text-slate-800 dark:text-slate-200 flex items-center justify-between">
                    <span>1. Daily Volatility</span>
                    <span className={`text-[10px] px-1.5 py-0.2 rounded font-bold ${data.isPriorDayCompressed ? "bg-amber-100 text-amber-800 dark:bg-amber-900/40 dark:text-amber-300" : "bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-400"}`}>
                      {data.isPriorDayCompressed ? "High Trend Prob" : "Standard"}
                    </span>
                  </div>
                  <p className="text-slate-600 dark:text-slate-400 mt-1">
                    {data.compressionType} (Prior Range: {data.priorDayRange > 0 ? `${data.priorDayRange.toFixed(1)} pts` : "N/A"})
                  </p>
                </div>

                {/* Step 2: 15m Wick Rejection */}
                <div className="p-2.5 rounded-lg bg-white/70 dark:bg-slate-900/70 border border-slate-200/60 dark:border-slate-800/60">
                  <div className="font-bold text-slate-800 dark:text-slate-200 flex items-center justify-between">
                    <span>2. 15m Wick Rejection</span>
                    <span className={`text-[10px] px-1.5 py-0.2 rounded font-bold ${
                      data.liquidityRejection === "BULLISH_SWEEP" 
                        ? "bg-green-100 text-green-800 dark:bg-green-900/40 dark:text-green-300"
                        : data.liquidityRejection === "BEARISH_TRAP"
                        ? "bg-rose-100 text-rose-800 dark:bg-rose-900/40 dark:text-rose-300"
                        : "bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-400"
                    }`}>
                      {data.liquidityRejection}
                    </span>
                  </div>
                  <p className="text-slate-600 dark:text-slate-400 mt-1">
                    Open: ₹{data.openPrice0915.toFixed(1)} | Max Wick: {(data.maxRejectionWickRatio * 100).toFixed(0)}%
                  </p>
                </div>

                {/* Step 3: 09:45 VWAP Anchor */}
                <div className="p-2.5 rounded-lg bg-white/70 dark:bg-slate-900/70 border border-slate-200/60 dark:border-slate-800/60">
                  <div className="font-bold text-slate-800 dark:text-slate-200 flex items-center justify-between">
                    <span>3. VWAP Anchor</span>
                    <span className={`text-[10px] px-1.5 py-0.2 rounded font-bold ${
                      data.vwapStatus.includes("Rising")
                        ? "bg-green-100 text-green-800 dark:bg-green-900/40 dark:text-green-300"
                        : data.vwapStatus.includes("Falling")
                        ? "bg-rose-100 text-rose-800 dark:bg-rose-900/40 dark:text-rose-300"
                        : "bg-amber-100 text-amber-800 dark:bg-amber-900/40 dark:text-amber-300"
                    }`}>
                      {data.vwapStatus}
                    </span>
                  </div>
                  <p className="text-slate-600 dark:text-slate-400 mt-1">
                    VWAP: ₹{data.vwap.toFixed(1)} (Slope: {data.vwapSlope > 0 ? "+" : ""}{data.vwapSlope.toFixed(1)})
                  </p>
                </div>

                {/* Step 4: Option Strike Skew */}
                <div className="p-2.5 rounded-lg bg-white/70 dark:bg-slate-900/70 border border-slate-200/60 dark:border-slate-800/60">
                  <div className="font-bold text-slate-800 dark:text-slate-200 flex items-center justify-between">
                    <span>4. Option Chain Skew</span>
                    <span className="text-[10px] px-1.5 py-0.2 rounded font-bold bg-purple-100 text-purple-800 dark:bg-purple-900/40 dark:text-purple-300">
                      PCR {data.pcr.toFixed(2)}
                    </span>
                  </div>
                  <p className="text-slate-600 dark:text-slate-400 mt-1 truncate">
                    Floor: {data.institutionalFloorStrike} | Ceiling: {data.institutionalCeilingStrike}
                  </p>
                </div>
              </div>
            </div>
          </div>
        )}
      </div>
    </aside>
  );
}
