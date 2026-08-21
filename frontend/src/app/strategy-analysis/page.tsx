"use client";

import { useState, useEffect, useCallback, useRef } from "react";
import {
  Activity, Zap, Shield, Radar, BarChart2, CheckCircle2, XCircle,
  TrendingUp, TrendingDown, Clock, RefreshCcw, ChevronDown, ChevronUp
} from "lucide-react";
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

interface SignalLogEntry {
  id: string;
  strategyName: string;
  action: string;
  instrument: string;
  price: number;
  quantity: number;
  status: string; // AutoExecuted | AwaitingApproval | SignalOnly | Blocked
  generatedAt: string;
}

interface PendingApproval {
  id: string;
  signal: {
    strategyName: string;
    action: string;
    instrument: string;
    price: number;
    quantity: number;
    generatedAt: string;
  };
}

interface SpotData {
  price?: number;
  lastPrice?: number;
  change?: number;
  changePercent?: number;
}

// ─── Status helpers ───────────────────────────────────────────────────────────
const MODE_META: Record<string, { icon: React.ReactNode; color: string; bgColor: string; label: string; desc: string }> = {
  Automatic: {
    icon: <Zap className="w-5 h-5 text-amber-500" />,
    color: "text-amber-500",
    bgColor: "bg-amber-50 dark:bg-amber-900/20",
    label: "Active · Auto-Trading",
    desc: "Engine is continuously scanning. Orders are placed automatically when entry conditions are met. No manual approval required."
  },
  ApprovalRequired: {
    icon: <Shield className="w-5 h-5 text-blue-500" />,
    color: "text-blue-500",
    bgColor: "bg-blue-50 dark:bg-blue-900/20",
    label: "Active · Awaiting Approval",
    desc: "Strategy generates signals and queues them. You must approve each signal before an order is placed."
  },
  SignalOnly: {
    icon: <Radar className="w-5 h-5 text-purple-500" />,
    color: "text-purple-500",
    bgColor: "bg-purple-50 dark:bg-purple-900/20",
    label: "Scanning · Signal Only",
    desc: "Monitoring for setups. Signals are logged but no orders are queued or executed automatically."
  }
};

const STATUS_META: Record<string, { label: string; class: string }> = {
  AutoExecuted:     { label: "Auto-Executed",     class: "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400" },
  AwaitingApproval: { label: "Awaiting Approval", class: "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400" },
  SignalOnly:       { label: "Signal Only",        class: "bg-purple-100 text-purple-700 dark:bg-purple-900/30 dark:text-purple-400" },
  Blocked:          { label: "Blocked",            class: "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400" },
};

// ─── Component ────────────────────────────────────────────────────────────────
export default function StrategyAnalysisPage() {
  const [strategies, setStrategies]     = useState<StrategyConfig[]>([]);
  const [signalLog, setSignalLog]       = useState<SignalLogEntry[]>([]);
  const [pending, setPending]           = useState<PendingApproval[]>([]);
  const [niftySpot, setNiftySpot]       = useState<SpotData | null>(null);
  const [loading, setLoading]           = useState(true);
  const [lastRefresh, setLastRefresh]   = useState<Date>(new Date());
  const [newSignalIds, setNewSignalIds] = useState<Set<string>>(new Set());
  const prevSignalIdsRef                = useRef<Set<string>>(new Set());
  const [showLog, setShowLog]           = useState(true);
  const [approvingId, setApprovingId]   = useState<string | null>(null);

  // ── Fetch strategies once ──────────────────────────────────────────────────
  useEffect(() => {
    fetchWithAuth("/api/strategyconfig")
      .then(r => r.json())
      .then(data => {
        setStrategies(data.filter((s: StrategyConfig) => s.isEnabled));
        setLoading(false);
      })
      .catch(() => setLoading(false));
  }, []);

  // ── Poll live data every 2 seconds ─────────────────────────────────────────
  const poll = useCallback(async () => {
    try {
      const [signalsRes, pendingRes, spotRes] = await Promise.all([
        fetchWithAuth("/api/engine/signals"),
        fetchWithAuth("/api/approval/pending"),
        fetchWithAuth("/api/marketdata/spot?symbol=NIFTY"),
      ]);

      if (signalsRes.ok) {
        const logs: SignalLogEntry[] = await signalsRes.json();
        // Detect genuinely new signals for flash animation
        const incoming = new Set(logs.map(s => s.id));
        const fresh = new Set<string>();
        incoming.forEach(id => {
          if (!prevSignalIdsRef.current.has(id)) fresh.add(id);
        });
        if (fresh.size > 0) {
          setNewSignalIds(fresh);
          setTimeout(() => setNewSignalIds(new Set()), 2000);
        }
        prevSignalIdsRef.current = incoming;
        setSignalLog(logs);
      }

      if (pendingRes.ok) {
        const data = await pendingRes.json();
        setPending(data);
      }

      if (spotRes.ok) {
        const spot: SpotData = await spotRes.json();
        setNiftySpot(spot);
      }

      setLastRefresh(new Date());
    } catch { /* silent */ }
  }, []);

  useEffect(() => {
    poll();
    const interval = setInterval(poll, 2000);
    return () => clearInterval(interval);
  }, [poll]);

  // ── Approve / Deny handlers ────────────────────────────────────────────────
  const handleApprove = async (id: string) => {
    setApprovingId(id);
    try {
      await fetchWithAuth(`/api/approval/approve/${id}`, { method: "POST" });
      setPending(p => p.filter(s => s.id !== id));
    } catch { /* silent */ } finally {
      setApprovingId(null);
    }
  };

  const handleDeny = async (id: string) => {
    setApprovingId(id);
    try {
      await fetchWithAuth(`/api/approval/deny/${id}`, { method: "POST" });
      setPending(p => p.filter(s => s.id !== id));
    } catch { /* silent */ } finally {
      setApprovingId(null);
    }
  };

  const fmtTime = (iso: string) => {
    const d = new Date(iso);
    return d.toLocaleTimeString("en-IN", { hour: "2-digit", minute: "2-digit", second: "2-digit" });
  };

  if (loading) {
    return (
      <div className="p-10 flex items-center justify-center gap-3 text-slate-500">
        <RefreshCcw className="w-5 h-5 animate-spin" />
        Loading strategy data...
      </div>
    );
  }

  return (
    <div className="p-6 md:p-10 max-w-7xl mx-auto space-y-8">
      {/* ── Header ── */}
      <header className="flex flex-col md:flex-row md:items-center justify-between gap-4 border-b border-slate-100 dark:border-slate-800 pb-6">
        <div className="flex items-center gap-4">
          <div className="w-12 h-12 bg-indigo-50 dark:bg-indigo-900/20 text-indigo-600 rounded-xl flex items-center justify-center shrink-0">
            <Activity className="w-6 h-6" />
          </div>
          <div>
            <h1 className="text-3xl font-bold text-slate-900 dark:text-white">Strategy Analysis</h1>
            <p className="text-slate-500 mt-1">Live status, signal log &amp; approval queue</p>
          </div>
        </div>
        <div className="flex items-center gap-4">
          {niftySpot !== null && (
            <div className="text-right">
              <p className="text-xs text-slate-500 font-semibold uppercase tracking-wider">NIFTY Spot</p>
              <div className="flex items-baseline justify-end gap-1.5 mt-0.5">
                <span className="text-2xl font-bold text-slate-900 dark:text-white">
                  ₹ {(niftySpot.price ?? niftySpot.lastPrice ?? 0).toFixed(2)}
                </span>
                {niftySpot.change !== undefined && (
                  <span className={`text-xs font-semibold ${niftySpot.change >= 0 ? "text-green-500" : "text-rose-500"}`}>
                    ({niftySpot.change >= 0 ? `+${niftySpot.change.toFixed(2)}` : niftySpot.change.toFixed(2)})
                  </span>
                )}
              </div>
            </div>
          )}
          <div className="text-right text-xs text-slate-400">
            <div className="flex items-center gap-1">
              <span className="inline-block w-2 h-2 rounded-full bg-green-500 animate-pulse" />
              Live
            </div>
            <span>Updated {lastRefresh.toLocaleTimeString("en-IN")}</span>
          </div>
        </div>
      </header>

      {/* ── Strategy Cards ── */}
      {strategies.length === 0 ? (
        <div className="flex flex-col items-center justify-center h-64 bg-surface rounded-2xl border border-slate-100 dark:border-slate-800 shadow-sm">
          <BarChart2 className="w-16 h-16 text-slate-300 dark:text-slate-700 mb-4" />
          <p className="text-xl font-bold text-slate-700 dark:text-slate-300">No active strategies</p>
          <p className="text-slate-500 mt-2">Go to the Strategies page to enable them.</p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
          {strategies.map(strategy => {
            const meta = MODE_META[strategy.operatingMode] ?? MODE_META.SignalOnly;
            const recentSignals = signalLog.filter(s => s.strategyName === strategy.strategyName);
            const lastSignal = recentSignals[0];

            return (
              <div
                key={strategy.id}
                className="bg-surface border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-sm flex flex-col h-full transition-all hover:-translate-y-1 hover:shadow-md"
              >
                {/* Card header */}
                <div className="flex items-start justify-between mb-4">
                  <h2 className="text-lg font-bold text-slate-900 dark:text-white leading-tight pr-2" title={strategy.strategyName}>
                    {strategy.strategyName}
                  </h2>
                  <div className={`p-2 rounded-lg shrink-0 ${meta.bgColor}`}>{meta.icon}</div>
                </div>

                {/* Live status indicator */}
                <div className="flex items-center gap-2 mb-4">
                  <span className="relative flex w-3 h-3">
                    <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-green-400 opacity-75" />
                    <span className="relative inline-flex rounded-full h-3 w-3 bg-green-500" />
                  </span>
                  <span className={`font-semibold text-sm ${meta.color}`}>{meta.label}</span>
                </div>

                {/* Description */}
                <p className="text-sm text-slate-600 dark:text-slate-400 leading-relaxed bg-slate-50 dark:bg-slate-900/50 p-3 rounded-lg border border-slate-100 dark:border-slate-800 mb-4 flex-grow">
                  {meta.desc}
                </p>

                {/* Last signal badge */}
                {lastSignal && (
                  <div className={`mb-4 px-3 py-2 rounded-lg text-sm flex items-center justify-between ${
                    lastSignal.action === "BUY"
                      ? "bg-green-50 dark:bg-green-900/20 text-green-700 dark:text-green-400"
                      : "bg-red-50 dark:bg-red-900/20 text-red-700 dark:text-red-400"
                  }`}>
                    <span className="flex items-center gap-1.5 font-bold">
                      {lastSignal.action === "BUY" ? <TrendingUp className="w-4 h-4" /> : <TrendingDown className="w-4 h-4" />}
                      Last: {lastSignal.action} @ ₹{lastSignal.price.toFixed(2)}
                    </span>
                    <span className="text-xs opacity-75">{fmtTime(lastSignal.generatedAt)}</span>
                  </div>
                )}

                {/* Config stats */}
                <div className="grid grid-cols-2 gap-3 border-t border-slate-100 dark:border-slate-800 pt-4">
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

      {/* ── Approval Queue ── */}
      <section>
        <div className="flex items-center gap-3 mb-4">
          <Shield className="w-5 h-5 text-blue-500" />
          <h2 className="text-xl font-bold text-slate-900 dark:text-white">Approval Queue</h2>
          {pending.length > 0 && (
            <span className="px-2.5 py-0.5 rounded-full text-xs font-bold bg-blue-500 text-white animate-pulse">
              {pending.length}
            </span>
          )}
        </div>

        {pending.length === 0 ? (
          <div className="bg-surface border border-slate-200 dark:border-slate-800 rounded-2xl p-8 text-center text-slate-500">
            <Shield className="w-10 h-10 mx-auto mb-3 text-slate-300 dark:text-slate-700" />
            <p className="font-semibold">No signals awaiting approval</p>
            <p className="text-sm mt-1">Signals from strategies in <em>Approval Required</em> mode will appear here.</p>
          </div>
        ) : (
          <div className="bg-surface border border-slate-200 dark:border-slate-800 rounded-2xl overflow-hidden shadow-sm">
            <div className="overflow-x-auto">
              <table className="w-full text-left border-collapse text-sm">
                <thead>
                  <tr className="bg-slate-50 dark:bg-slate-800/50 text-slate-500 text-xs font-semibold tracking-wider uppercase border-b border-slate-200 dark:border-slate-800">
                    <th className="p-4">Strategy</th>
                    <th className="p-4">Action</th>
                    <th className="p-4">Instrument</th>
                    <th className="p-4 text-right">Price</th>
                    <th className="p-4 text-right">Qty</th>
                    <th className="p-4">Generated</th>
                    <th className="p-4 text-center">Decision</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100 dark:divide-slate-800/50">
                  {pending.map(item => (
                    <tr key={item.id} className="hover:bg-blue-50/50 dark:hover:bg-blue-900/10 transition-colors">
                      <td className="p-4 font-semibold text-slate-900 dark:text-white">{item.signal.strategyName}</td>
                      <td className="p-4">
                        <span className={`px-2.5 py-1 text-xs font-bold rounded-md ${
                          item.signal.action === "BUY"
                            ? "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400"
                            : "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400"
                        }`}>
                          {item.signal.action}
                        </span>
                      </td>
                      <td className="p-4 font-medium text-slate-700 dark:text-slate-300">{item.signal.instrument}</td>
                      <td className="p-4 text-right font-bold text-slate-900 dark:text-white">₹{item.signal.price.toFixed(2)}</td>
                      <td className="p-4 text-right text-slate-600 dark:text-slate-400">{item.signal.quantity}</td>
                      <td className="p-4 text-slate-500 text-xs">{fmtTime(item.signal.generatedAt)}</td>
                      <td className="p-4">
                        <div className="flex items-center justify-center gap-2">
                          <button
                            onClick={() => handleApprove(item.id)}
                            disabled={approvingId === item.id}
                            className="flex items-center gap-1.5 px-3 py-1.5 bg-green-500 hover:bg-green-600 text-white rounded-lg font-bold text-xs transition-colors disabled:opacity-50"
                          >
                            <CheckCircle2 className="w-4 h-4" />
                            Approve
                          </button>
                          <button
                            onClick={() => handleDeny(item.id)}
                            disabled={approvingId === item.id}
                            className="flex items-center gap-1.5 px-3 py-1.5 bg-red-100 hover:bg-red-500 text-red-600 hover:text-white rounded-lg font-bold text-xs transition-colors disabled:opacity-50"
                          >
                            <XCircle className="w-4 h-4" />
                            Deny
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </section>

      {/* ── Signal Log ── */}
      <section>
        <button
          onClick={() => setShowLog(v => !v)}
          className="w-full flex items-center justify-between gap-3 mb-4 group"
        >
          <div className="flex items-center gap-3">
            <Activity className="w-5 h-5 text-indigo-500" />
            <h2 className="text-xl font-bold text-slate-900 dark:text-white">Live Signal Log</h2>
            <span className="px-2.5 py-0.5 rounded-full text-xs font-bold bg-indigo-100 dark:bg-indigo-900/30 text-indigo-700 dark:text-indigo-300">
              {signalLog.length}
            </span>
          </div>
          {showLog ? <ChevronUp className="w-5 h-5 text-slate-400" /> : <ChevronDown className="w-5 h-5 text-slate-400" />}
        </button>

        {showLog && (
          signalLog.length === 0 ? (
            <div className="bg-surface border border-slate-200 dark:border-slate-800 rounded-2xl p-8 text-center text-slate-500">
              <Activity className="w-10 h-10 mx-auto mb-3 text-slate-300 dark:text-slate-700" />
              <p className="font-semibold">No signals generated yet</p>
              <p className="text-sm mt-1">Signals will appear here as the strategy engine scans the market.</p>
            </div>
          ) : (
            <div className="bg-surface border border-slate-200 dark:border-slate-800 rounded-2xl overflow-hidden shadow-sm">
              <div className="overflow-x-auto max-h-96 overflow-y-auto">
                <table className="w-full text-left border-collapse text-sm">
                  <thead className="sticky top-0 z-10">
                    <tr className="bg-slate-50 dark:bg-slate-800/90 text-slate-500 text-xs font-semibold tracking-wider uppercase border-b border-slate-200 dark:border-slate-800">
                      <th className="p-4">Time</th>
                      <th className="p-4">Strategy</th>
                      <th className="p-4">Action</th>
                      <th className="p-4">Instrument</th>
                      <th className="p-4 text-right">Price</th>
                      <th className="p-4 text-right">Qty</th>
                      <th className="p-4">Status</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100 dark:divide-slate-800/50">
                    {signalLog.map(entry => {
                      const isNew = newSignalIds.has(entry.id);
                      const statusMeta = STATUS_META[entry.status] ?? STATUS_META.SignalOnly;
                      return (
                        <tr
                          key={entry.id}
                          className={`transition-colors ${
                            isNew
                              ? "bg-indigo-50 dark:bg-indigo-900/20 animate-pulse"
                              : "hover:bg-slate-50 dark:hover:bg-slate-800/30"
                          }`}
                        >
                          <td className="p-4 text-slate-500 text-xs font-mono">{fmtTime(entry.generatedAt)}</td>
                          <td className="p-4 font-medium text-slate-700 dark:text-slate-300 max-w-[160px] truncate" title={entry.strategyName}>
                            {entry.strategyName}
                          </td>
                          <td className="p-4">
                            <span className={`px-2.5 py-1 text-xs font-bold rounded-md flex items-center gap-1 w-fit ${
                              entry.action === "BUY"
                                ? "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400"
                                : "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400"
                            }`}>
                              {entry.action === "BUY" ? <TrendingUp className="w-3 h-3" /> : <TrendingDown className="w-3 h-3" />}
                              {entry.action}
                            </span>
                          </td>
                          <td className="p-4 font-medium text-slate-700 dark:text-slate-300">{entry.instrument}</td>
                          <td className="p-4 text-right font-bold text-slate-900 dark:text-white">₹{entry.price.toFixed(2)}</td>
                          <td className="p-4 text-right text-slate-600 dark:text-slate-400">{entry.quantity}</td>
                          <td className="p-4">
                            <span className={`px-2.5 py-1 text-xs font-bold rounded-md ${statusMeta.class}`}>
                              {statusMeta.label}
                            </span>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </div>
          )
        )}
      </section>
    </div>
  );
}
