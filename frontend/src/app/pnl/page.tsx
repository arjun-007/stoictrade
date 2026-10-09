"use client";

import { useState, useEffect, useCallback } from "react";
import { 
  TrendingUp, 
  TrendingDown, 
  Calendar, 
  RefreshCw, 
  Award, 
  AlertCircle, 
  ChevronRight, 
  CheckCircle2, 
  XCircle, 
  Activity, 
  BarChart2, 
  SlidersHorizontal,
  ChevronDown,
  ChevronUp,
  Clock
} from "lucide-react";
import { fetchWithAuth } from "@/lib/api";
import { formatInstrumentName } from "@/app/positions/page";

interface DailyRecord {
  id: number;
  date: string;
  realizedPnL: number;
  unrealizedPnL: number;
  netPnL: number;
  totalTrades: number;
  winningTrades: number;
  losingTrades: number;
  tradeMode: string;
  updatedAt: string;
}

interface ClosedTrade {
  id: string;
  symbol: string;
  strategyName: string;
  tradeMode: string;
  entryTime: string;
  exitTime: string;
  buyPrice: number;
  sellPrice: number;
  quantity: number;
  realizedProfit: number;
  exitReason: string;
  dateIst: string;
}

interface PnLHistoryResponse {
  daysRequested: number;
  totalNetPnL: number;
  totalRealizedPnL: number;
  totalTrades: number;
  winningTrades: number;
  losingTrades: number;
  winRatePercent: number;
  profitableDays: number;
  lossDays: number;
  breakEvenDays: number;
  averageDailyPnL: number;
  bestDay: { date: string; pnl: number } | null;
  worstDay: { date: string; pnl: number } | null;
  dailyRecords: DailyRecord[];
  closedTrades: ClosedTrade[];
}

export default function PnLAnalyticsPage() {
  const [selectedRange, setSelectedRange] = useState<number>(30);
  const [data, setData] = useState<PnLHistoryResponse | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedDayTrades, setSelectedDayTrades] = useState<string | null>(null);
  const [hoveredBarIndex, setHoveredBarIndex] = useState<number | null>(null);

  const fetchHistory = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const res = await fetchWithAuth(`/api/portfolio/pnl-history?days=${selectedRange}`);
      if (res.ok) {
        const json = await res.json();
        setData(json);
      } else {
        const err = await res.json();
        setError(err.message || "Failed to load P&L history");
      }
    } catch (err: any) {
      setError(err?.message || "Network error loading data");
    } finally {
      setIsLoading(false);
    }
  }, [selectedRange]);

  useEffect(() => {
    fetchHistory();
  }, [fetchHistory]);

  const formatDateLabel = (dateStr: string) => {
    try {
      const d = new Date(dateStr);
      return d.toLocaleDateString("en-IN", { month: "short", day: "numeric" });
    } catch {
      return dateStr;
    }
  };

  const formatCurrency = (val: number) => {
    return (val < 0 ? "-₹ " : "+₹ ") + Math.abs(val).toLocaleString("en-IN", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  };

  // Find max absolute PnL for scaling the bar chart
  const maxAbsPnL = data?.dailyRecords?.reduce((max, r) => Math.max(max, Math.abs(r.netPnL)), 1000) || 1000;
  
  // Chronological order for chart from left to right (oldest to newest)
  const chartRecords = data ? [...data.dailyRecords].reverse() : [];

  return (
    <div className="p-6 md:p-10 max-w-7xl mx-auto space-y-8">
      {/* Header */}
      <header className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2">
            <h1 className="text-3xl font-extrabold text-slate-900 dark:text-white tracking-tight">
              P&L Analytics & Journal
            </h1>
            <span className="px-2.5 py-0.5 rounded-full text-xs font-semibold bg-primary/10 text-primary border border-primary/20">
              Live Tracker
            </span>
          </div>
          <p className="text-slate-500 dark:text-slate-400 mt-1">
            Performance analytics, win rates, and daily trade journal over your trading journey.
          </p>
        </div>

        {/* Range Selector & Refresh */}
        <div className="flex items-center gap-3">
          <div className="flex bg-slate-100 dark:bg-slate-800/80 p-1 rounded-xl border border-slate-200 dark:border-slate-700/60 shadow-sm">
            {[7, 15, 30].map((days) => (
              <button
                key={days}
                onClick={() => setSelectedRange(days)}
                className={`px-4 py-2 rounded-lg text-sm font-semibold transition-all ${
                  selectedRange === days
                    ? "bg-white dark:bg-slate-900 text-primary shadow-sm"
                    : "text-slate-600 dark:text-slate-400 hover:text-slate-900 dark:hover:text-white"
                }`}
              >
                {days} Days
              </button>
            ))}
          </div>

          <button
            onClick={fetchHistory}
            disabled={isLoading}
            className="p-2.5 bg-slate-100 hover:bg-slate-200 dark:bg-slate-800 dark:hover:bg-slate-700 text-slate-700 dark:text-slate-300 rounded-xl transition-all disabled:opacity-50 shadow-sm border border-slate-200 dark:border-slate-700"
            title="Refresh P&L Data"
          >
            <RefreshCw className={`w-5 h-5 ${isLoading ? "animate-spin text-primary" : ""}`} />
          </button>
        </div>
      </header>

      {error && (
        <div className="p-4 bg-red-50 dark:bg-red-950/30 border border-red-200 dark:border-red-800 rounded-xl flex items-center gap-3 text-red-600 dark:text-red-400">
          <AlertCircle className="w-5 h-5 flex-shrink-0" />
          <p className="text-sm font-medium">{error}</p>
        </div>
      )}

      {/* Main Stats Grid */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6">
        {/* Net 30D P&L */}
        <div className={`p-6 rounded-2xl border shadow-sm transition-all relative overflow-hidden ${
          (data?.totalNetPnL || 0) >= 0 
            ? "bg-gradient-to-br from-green-500/10 via-surface to-surface border-green-500/30" 
            : "bg-gradient-to-br from-red-500/10 via-surface to-surface border-red-500/30"
        }`}>
          <div className="flex items-center justify-between">
            <span className="text-sm font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wider">
              {selectedRange}-Day Net P&L
            </span>
            <div className={`p-2 rounded-xl ${
              (data?.totalNetPnL || 0) >= 0 
                ? "bg-green-100 dark:bg-green-950/50 text-green-600 dark:text-green-400" 
                : "bg-red-100 dark:bg-red-950/50 text-red-600 dark:text-red-400"
            }`}>
              {(data?.totalNetPnL || 0) >= 0 ? <TrendingUp className="w-5 h-5" /> : <TrendingDown className="w-5 h-5" />}
            </div>
          </div>
          <p className={`text-3xl font-extrabold mt-4 tracking-tight ${
            (data?.totalNetPnL || 0) >= 0 ? "text-green-600 dark:text-green-400" : "text-red-600 dark:text-red-400"
          }`}>
            {data ? formatCurrency(data.totalNetPnL) : "₹ 0.00"}
          </p>
          <div className="mt-3 flex items-center justify-between text-xs text-slate-500">
            <span>Realized: {data ? formatCurrency(data.totalRealizedPnL) : "₹ 0.00"}</span>
            <span>Avg/Day: {data ? formatCurrency(data.averageDailyPnL) : "₹ 0.00"}</span>
          </div>
        </div>

        {/* Win Rate */}
        <div className="bg-surface p-6 rounded-2xl border border-slate-200 dark:border-slate-800 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-sm font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wider">
              Win Rate
            </span>
            <div className="p-2 bg-blue-100 dark:bg-blue-950/50 text-primary rounded-xl">
              <Award className="w-5 h-5" />
            </div>
          </div>
          <div className="flex items-baseline gap-2 mt-4">
            <p className="text-3xl font-extrabold text-slate-900 dark:text-white">
              {data?.winRatePercent || 0}%
            </p>
            <span className="text-xs font-semibold text-slate-400">
              ({data?.winningTrades || 0}W / {data?.losingTrades || 0}L)
            </span>
          </div>
          <div className="mt-3 w-full bg-slate-100 dark:bg-slate-800 h-2 rounded-full overflow-hidden flex">
            <div 
              className="bg-green-500 h-full transition-all duration-500" 
              style={{ width: `${data?.winRatePercent || 0}%` }}
            />
            <div 
              className="bg-red-500 h-full transition-all duration-500" 
              style={{ width: `${100 - (data?.winRatePercent || 0)}%` }}
            />
          </div>
        </div>

        {/* Day Breakdown */}
        <div className="bg-surface p-6 rounded-2xl border border-slate-200 dark:border-slate-800 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-sm font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wider">
              Profitable Days
            </span>
            <div className="p-2 bg-emerald-100 dark:bg-emerald-950/50 text-emerald-600 rounded-xl">
              <CheckCircle2 className="w-5 h-5" />
            </div>
          </div>
          <p className="text-3xl font-extrabold text-slate-900 dark:text-white mt-4">
            {data?.profitableDays || 0} <span className="text-sm font-normal text-slate-400">/ {data?.dailyRecords.length || 0} Days</span>
          </p>
          <div className="mt-3 flex items-center justify-between text-xs text-slate-500">
            <span className="text-emerald-500 font-semibold">{data?.profitableDays || 0} Green</span>
            <span className="text-red-500 font-semibold">{data?.lossDays || 0} Red</span>
            <span className="text-slate-400">{data?.breakEvenDays || 0} Neutral</span>
          </div>
        </div>

        {/* Best & Worst Day */}
        <div className="bg-surface p-6 rounded-2xl border border-slate-200 dark:border-slate-800 shadow-sm">
          <div className="flex items-center justify-between">
            <span className="text-sm font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wider">
              Peaks & Drawdowns
            </span>
            <div className="p-2 bg-purple-100 dark:bg-purple-950/50 text-purple-600 rounded-xl">
              <Activity className="w-5 h-5" />
            </div>
          </div>
          <div className="mt-4 space-y-2">
            <div className="flex items-center justify-between text-sm">
              <span className="text-slate-500 text-xs flex items-center gap-1">
                <span className="w-2 h-2 rounded-full bg-emerald-500 inline-block" /> Best:
              </span>
              <span className="font-bold text-emerald-500">
                {data?.bestDay ? formatCurrency(data.bestDay.pnl) : "₹ 0"}
              </span>
            </div>
            <div className="flex items-center justify-between text-sm">
              <span className="text-slate-500 text-xs flex items-center gap-1">
                <span className="w-2 h-2 rounded-full bg-red-500 inline-block" /> Worst:
              </span>
              <span className="font-bold text-red-500">
                {data?.worstDay ? formatCurrency(data.worstDay.pnl) : "₹ 0"}
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* Daily P&L Visualizer (Bar Chart) */}
      <div className="bg-surface p-6 md:p-8 rounded-2xl border border-slate-200 dark:border-slate-800 shadow-sm space-y-6">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
          <div>
            <h2 className="text-lg font-bold text-slate-900 dark:text-white flex items-center gap-2">
              <BarChart2 className="w-5 h-5 text-primary" />
              Daily P&L Timeline
            </h2>
            <p className="text-xs text-slate-500 mt-0.5">
              Chronological daily profit (green) and loss (red) distribution
            </p>
          </div>
          <div className="flex items-center gap-4 text-xs font-medium">
            <div className="flex items-center gap-1.5">
              <span className="w-3 h-3 rounded bg-emerald-500" />
              <span className="text-slate-600 dark:text-slate-400">Profit</span>
            </div>
            <div className="flex items-center gap-1.5">
              <span className="w-3 h-3 rounded bg-red-500" />
              <span className="text-slate-600 dark:text-slate-400">Loss</span>
            </div>
          </div>
        </div>

        {/* Visual Bar Chart */}
        <div className="pt-8 pb-4">
          <div className="relative h-48 flex items-end gap-1.5 sm:gap-2 justify-between border-b border-slate-200 dark:border-slate-700/80 px-2">
            {/* Zero Axis Guideline */}
            <div className="absolute inset-x-0 top-1/2 border-t border-dashed border-slate-300 dark:border-slate-700 pointer-events-none" />

            {chartRecords.map((record, idx) => {
              const isProfit = record.netPnL >= 0;
              const absVal = Math.abs(record.netPnL);
              // Scale height relative to half of 192px (96px max for either direction)
              const heightPercent = Math.min(100, Math.max(12, Math.round((absVal / maxAbsPnL) * 90)));
              const isHovered = hoveredBarIndex === idx;

              return (
                <div
                  key={record.date}
                  className="flex-1 flex flex-col items-center justify-center h-full relative group cursor-pointer"
                  onMouseEnter={() => setHoveredBarIndex(idx)}
                  onMouseLeave={() => setHoveredBarIndex(null)}
                  onClick={() => setSelectedDayTrades(selectedDayTrades === record.date ? null : record.date)}
                >
                  {/* Tooltip on hover */}
                  {isHovered && (
                    <div className="absolute -top-16 z-30 bg-slate-900 text-white dark:bg-slate-800 text-xs rounded-lg py-1.5 px-3 shadow-xl whitespace-nowrap pointer-events-none border border-slate-700 flex flex-col items-center">
                      <span className="font-semibold text-slate-300">{record.date}</span>
                      <span className={`font-bold ${isProfit ? "text-emerald-400" : "text-red-400"}`}>
                        {formatCurrency(record.netPnL)}
                      </span>
                      <span className="text-[10px] text-slate-400">
                        {record.totalTrades} trade{record.totalTrades !== 1 ? "s" : ""} ({record.winningTrades}W/{record.losingTrades}L)
                      </span>
                    </div>
                  )}

                  {/* Top half (Profits) */}
                  <div className="h-1/2 w-full flex items-end justify-center">
                    {isProfit && (
                      <div
                        className="w-full max-w-[28px] rounded-t-md bg-emerald-500 hover:bg-emerald-400 transition-all duration-300 shadow-sm"
                        style={{ height: `${heightPercent}%` }}
                      />
                    )}
                  </div>

                  {/* Bottom half (Losses) */}
                  <div className="h-1/2 w-full flex items-start justify-center">
                    {!isProfit && (
                      <div
                        className="w-full max-w-[28px] rounded-b-md bg-red-500 hover:bg-red-400 transition-all duration-300 shadow-sm"
                        style={{ height: `${heightPercent}%` }}
                      />
                    )}
                  </div>
                </div>
              );
            })}
          </div>

          {/* Date labels under the chart */}
          <div className="flex justify-between px-2 pt-2 text-[10px] sm:text-xs text-slate-400 font-medium">
            {chartRecords.length > 0 && (
              <>
                <span>{formatDateLabel(chartRecords[0].date)}</span>
                {chartRecords.length > 10 && (
                  <span>{formatDateLabel(chartRecords[Math.floor(chartRecords.length / 2)].date)}</span>
                )}
                <span>{formatDateLabel(chartRecords[chartRecords.length - 1].date)}</span>
              </>
            )}
          </div>
        </div>
      </div>

      {/* Daily Records Journal Table */}
      <div className="bg-surface rounded-2xl border border-slate-200 dark:border-slate-800 shadow-sm overflow-hidden">
        <div className="p-6 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between">
          <div>
            <h2 className="text-lg font-bold text-slate-900 dark:text-white">
              Daily Journal & History
            </h2>
            <p className="text-xs text-slate-500 mt-0.5">
              Click on any row to view individual closed trades and execution details.
            </p>
          </div>
          <span className="text-xs font-semibold text-slate-500 px-3 py-1 bg-slate-100 dark:bg-slate-800 rounded-full">
            {data?.dailyRecords?.length || 0} Sessions
          </span>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 dark:bg-slate-800/50 text-slate-600 dark:text-slate-400 font-semibold text-xs uppercase tracking-wider border-b border-slate-200 dark:border-slate-800">
              <tr>
                <th className="py-3.5 px-6">Date</th>
                <th className="py-3.5 px-6">Mode</th>
                <th className="py-3.5 px-6">Trades</th>
                <th className="py-3.5 px-6">Win / Loss</th>
                <th className="py-3.5 px-6">Realized P&L</th>
                <th className="py-3.5 px-6 text-right">Net P&L</th>
                <th className="py-3.5 px-6 text-center">Action</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 dark:divide-slate-800">
              {data?.dailyRecords?.map((rec) => {
                const isSelected = selectedDayTrades === rec.date;
                const isProfit = rec.netPnL >= 0;
                const matchingTrades = data.closedTrades.filter((t) => t.dateIst === rec.date);

                return (
                  <tr
                    key={rec.id}
                    onClick={() => setSelectedDayTrades(isSelected ? null : rec.date)}
                    className={`cursor-pointer transition-colors ${
                      isSelected 
                        ? "bg-primary/5 dark:bg-primary/10" 
                        : "hover:bg-slate-50/80 dark:hover:bg-slate-800/40"
                    }`}
                  >
                    <td className="py-4 px-6 font-semibold text-slate-900 dark:text-white flex items-center gap-2">
                      <Calendar className="w-4 h-4 text-slate-400" />
                      {rec.date}
                    </td>
                    <td className="py-4 px-6">
                      <span className={`px-2.5 py-0.5 rounded-full text-xs font-semibold ${
                        rec.tradeMode === "Live"
                          ? "bg-emerald-100 dark:bg-emerald-950/40 text-emerald-600 border border-emerald-500/20"
                          : "bg-blue-100 dark:bg-blue-950/40 text-blue-600 border border-blue-500/20"
                      }`}>
                        {rec.tradeMode}
                      </span>
                    </td>
                    <td className="py-4 px-6 text-slate-600 dark:text-slate-300 font-medium">
                      {rec.totalTrades}
                    </td>
                    <td className="py-4 px-6">
                      <span className="text-emerald-500 font-semibold">{rec.winningTrades}W</span>
                      {" / "}
                      <span className="text-red-500 font-semibold">{rec.losingTrades}L</span>
                    </td>
                    <td className="py-4 px-6 font-semibold text-slate-700 dark:text-slate-300">
                      {formatCurrency(rec.realizedPnL)}
                    </td>
                    <td className={`py-4 px-6 text-right font-bold text-base ${
                      isProfit ? "text-emerald-500" : "text-red-500"
                    }`}>
                      {formatCurrency(rec.netPnL)}
                    </td>
                    <td className="py-4 px-6 text-center">
                      <button className="text-slate-400 hover:text-primary transition-colors p-1">
                        {isSelected ? <ChevronUp className="w-4 h-4" /> : <ChevronDown className="w-4 h-4" />}
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>

      {/* Selected Day Closed Trades Details (if tapped) */}
      {selectedDayTrades && (
        <div className="bg-surface p-6 rounded-2xl border border-primary/30 shadow-md space-y-4 animate-in fade-in slide-in-from-top-2 duration-300">
          <div className="flex items-center justify-between">
            <h3 className="text-base font-bold text-slate-900 dark:text-white flex items-center gap-2">
              <Clock className="w-4 h-4 text-primary" />
              Closed Trades for {selectedDayTrades}
            </h3>
            <button
              onClick={() => setSelectedDayTrades(null)}
              className="text-xs text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
            >
              Close Details ✕
            </button>
          </div>

          {(() => {
            const trades = data?.closedTrades.filter((t) => t.dateIst === selectedDayTrades) || [];
            if (trades.length === 0) {
              return (
                <p className="text-sm text-slate-500 py-4 text-center">
                  No individual trade tickets found for this session. Daily aggregate was recorded.
                </p>
              );
            }

            return (
              <div className="overflow-x-auto">
                <table className="w-full text-left text-xs">
                  <thead className="bg-slate-100 dark:bg-slate-800/60 text-slate-500 uppercase tracking-wider">
                    <tr>
                      <th className="py-2.5 px-4">Instrument</th>
                      <th className="py-2.5 px-4">Strategy</th>
                      <th className="py-2.5 px-4">Qty</th>
                      <th className="py-2.5 px-4">Buy Avg</th>
                      <th className="py-2.5 px-4">Sell Avg</th>
                      <th className="py-2.5 px-4">P&L</th>
                      <th className="py-2.5 px-4">Exit Reason</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100 dark:divide-slate-800">
                    {trades.map((t) => (
                      <tr key={t.id} className="hover:bg-slate-50 dark:hover:bg-slate-800/30">
                        <td className="py-2.5 px-4 font-semibold text-slate-900 dark:text-white">
                          {formatInstrumentName(t.symbol)}
                        </td>
                        <td className="py-2.5 px-4 text-slate-600 dark:text-slate-400">
                          {t.strategyName}
                        </td>
                        <td className="py-2.5 px-4 font-medium">{t.quantity}</td>
                        <td className="py-2.5 px-4">₹{t.buyPrice.toFixed(2)}</td>
                        <td className="py-2.5 px-4">₹{t.sellPrice.toFixed(2)}</td>
                        <td className={`py-2.5 px-4 font-bold ${
                          t.realizedProfit >= 0 ? "text-emerald-500" : "text-red-500"
                        }`}>
                          {formatCurrency(t.realizedProfit)}
                        </td>
                        <td className="py-2.5 px-4 text-slate-500">{t.exitReason}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            );
          })()}
        </div>
      )}
    </div>
  );
}
