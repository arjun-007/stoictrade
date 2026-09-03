"use client";

import { useState, useEffect } from "react";
import { ShieldAlert, CheckCircle2, XCircle, Clock } from "lucide-react";
import { fetchWithAuth } from "@/lib/api";

function formatOptionSymbol(raw: string): string {
  if (!raw) return "";
  let s = raw.replace("NSE:", "").replace(" ", "").trim();
  if (s.startsWith("NIFTYNIFTY")) s = s.substring(5);

  // If matches canonical NIFTY{EXPIRY}{STRIKE}{TYPE}
  if (s.startsWith("NIFTY") && !s.startsWith("NIFTYBANK")) {
    const withoutPrefix = s.substring(5);
    const isCe = withoutPrefix.endsWith("CE");
    const isPe = withoutPrefix.endsWith("PE");
    if (isCe || isPe) {
      const type = isCe ? "CE" : "PE";
      const noSuffix = withoutPrefix.substring(0, withoutPrefix.length - 2);

      let strikeLen = 0;
      for (let i = noSuffix.length - 1; i >= 0; i--) {
        if (noSuffix[i] >= "0" && noSuffix[i] <= "9") strikeLen++;
        else break;
      }

      if (strikeLen > 0) {
        const strike = noSuffix.substring(noSuffix.length - strikeLen);
        const expiry = noSuffix.substring(0, noSuffix.length - strikeLen);
        return `NIFTY ${expiry} ${strike} ${type}`;
      }
    }
  }
  return s;
}

export default function GlobalPendingApprovalsBanner() {
  const [approvals, setApprovals] = useState<any[]>([]);
  const [loadingAction, setLoadingAction] = useState<string | null>(null);

  const fetchApprovals = async () => {
    try {
      const res = await fetchWithAuth("/api/approval/pending");
      if (res.ok) {
        const data = await res.json();
        setApprovals(Array.isArray(data) ? data : []);
      }
    } catch {
      // Ignore background poll errors
    }
  };

  useEffect(() => {
    fetchApprovals();
    const interval = setInterval(fetchApprovals, 4000);
    return () => clearInterval(interval);
  }, []);

  const handleAction = async (id: string, action: "approve" | "deny") => {
    setLoadingAction(`${action}_${id}`);
    try {
      const res = await fetchWithAuth(`/api/approval/${action}/${id}`, { method: "POST" });
      if (res.ok) {
        setApprovals(prev => prev.filter(a => a.id !== id));
      }
    } catch (err) {
      console.error(`Failed to ${action} signal`, err);
    } finally {
      setLoadingAction(null);
    }
  };

  if (approvals.length === 0) return null;

  return (
    <div className="w-full bg-amber-500/10 border-b border-amber-500/30 dark:bg-amber-950/40 dark:border-amber-700/50 backdrop-blur-md sticky top-0 z-40 px-4 py-3 sm:px-6 shadow-md transition-all">
      <div className="max-w-7xl mx-auto space-y-3">
        {approvals.map((app) => {
          const sig = app.signal || {};
          const isGroup = sig.strategyName?.startsWith("Group:");
          const displayInstrument = formatOptionSymbol(sig.instrument || "NIFTY");

          return (
            <div
              key={app.id}
              className="bg-white/95 dark:bg-slate-900/95 border border-amber-300/80 dark:border-amber-600/60 rounded-xl p-3.5 sm:p-4 shadow-sm flex flex-col md:flex-row md:items-center justify-between gap-3 animate-in fade-in slide-in-from-top-2 duration-300"
            >
              <div className="flex items-start gap-3 min-w-0">
                <div className="p-2 rounded-lg bg-amber-500/15 text-amber-600 dark:text-amber-400 shrink-0 mt-0.5 animate-pulse">
                  <ShieldAlert className="w-5 h-5" />
                </div>
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2 mb-1">
                    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-[11px] font-bold uppercase tracking-wider bg-amber-500/15 text-amber-700 dark:text-amber-300 border border-amber-500/20">
                      <Clock className="w-3 h-3" /> Action Required
                    </span>
                    {isGroup ? (
                      <span className="px-2 py-0.5 rounded text-[11px] font-bold bg-primary/15 text-primary border border-primary/25">
                        🛡️ Squad Consensus
                      </span>
                    ) : (
                      <span className="px-2 py-0.5 rounded text-[11px] font-semibold bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300">
                        Standalone Strategy
                      </span>
                    )}
                    <span className="text-xs font-semibold text-slate-500 dark:text-slate-400 truncate max-w-xs sm:max-w-md">
                      {sig.strategyName}
                    </span>
                  </div>

                  <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
                    <span className="text-base sm:text-lg font-black text-slate-900 dark:text-white">
                      <span className="text-emerald-600 dark:text-emerald-400">{sig.action || "BUY"}</span>{" "}
                      <span className="font-mono text-primary">{sig.quantity || 65} Qty</span>{" "}
                      <span className="font-mono text-slate-800 dark:text-slate-100">{displayInstrument}</span>
                    </span>
                    {sig.price > 0 && (
                      <span className="text-xs font-semibold text-slate-500 dark:text-slate-400">
                        @ ~₹{sig.price.toFixed(2)}
                      </span>
                    )}
                  </div>

                  {/* Target and Exit / StopLoss Badges */}
                  <div className="flex flex-wrap items-center gap-2 mt-1.5 text-xs font-semibold">
                    <span className="px-2 py-0.5 rounded bg-emerald-100/70 text-emerald-800 dark:bg-emerald-950/50 dark:text-emerald-300 border border-emerald-500/20 flex items-center gap-1">
                      🎯 Target: ₹{sig.targetPrice > 0 ? sig.targetPrice.toFixed(2) : (sig.price > 0 ? (sig.price * 1.25).toFixed(2) : "—")}
                    </span>
                    <span className="px-2 py-0.5 rounded bg-rose-100/70 text-rose-800 dark:bg-rose-950/50 dark:text-rose-300 border border-rose-500/20 flex items-center gap-1">
                      🛑 Exit / SL: ₹{sig.stopLossPrice > 0 ? sig.stopLossPrice.toFixed(2) : (sig.price > 0 ? Math.max(5, sig.price * 0.85).toFixed(2) : "—")}
                    </span>
                  </div>
                </div>
              </div>

              <div className="flex items-center gap-2 self-end md:self-center shrink-0">
                <button
                  onClick={() => handleAction(app.id, "deny")}
                  disabled={loadingAction === `deny_${app.id}`}
                  className="px-4 py-2 text-xs sm:text-sm font-bold rounded-lg bg-slate-100 hover:bg-slate-200 dark:bg-slate-800 dark:hover:bg-slate-700 text-slate-700 dark:text-slate-300 transition-colors flex items-center gap-1.5 disabled:opacity-50"
                >
                  <XCircle className="w-4 h-4 text-slate-500" />
                  DENY
                </button>
                <button
                  onClick={() => handleAction(app.id, "approve")}
                  disabled={loadingAction === `approve_${app.id}`}
                  className="px-5 py-2 text-xs sm:text-sm font-bold rounded-lg bg-emerald-600 hover:bg-emerald-700 text-white shadow-sm transition-all flex items-center gap-1.5 active:scale-95 disabled:opacity-50"
                >
                  <CheckCircle2 className="w-4 h-4" />
                  APPROVE & BUY
                </button>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
