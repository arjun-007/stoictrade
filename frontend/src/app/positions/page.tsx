"use client";

import { useState, useEffect } from "react";
import { Briefcase, TrendingUp, TrendingDown, RefreshCcw, Filter } from "lucide-react";
import { fetchWithAuth } from "@/lib/api";

type PositionStatus = "ACTIVE" | "EXITED";
type PositionType = "LONG" | "SHORT";
type PositionCategory = "DAY" | "HOLDING";

interface Position {
  id: number;
  symbol: string;
  qty: number;
  buyPrice: number;
  sellPrice?: number;
  ltp: number;
  type: PositionType;
  status: PositionStatus;
  category: PositionCategory;
}

export default function PositionsPage() {
  const [activeTab, setActiveTab] = useState<PositionCategory>("DAY");
  
  const [filterStatus, setFilterStatus] = useState<PositionStatus | "ALL">("ALL");
  const [filterType, setFilterType] = useState<PositionType | "ALL">("ALL");

  const [positionsData, setPositionsData] = useState<Position[]>([]);

  useEffect(() => {
    const fetchPositions = async () => {
      try {
        const [posRes, holdRes] = await Promise.all([
          fetchWithAuth("/api/portfolio/positions"),
          fetchWithAuth("/api/portfolio/holdings")
        ]);

        let mapped: Position[] = [];

        if (posRes.ok) {
          const data = await posRes.json();
          if (data.netPositions) {
            data.netPositions.forEach((p: any) => {
              const qty = Math.abs(p.netQty ?? 0);
              // Normalise symbol: collapse old "NIFTYNIFTY…" entries stored in DB
              const rawSymbol: string = p.symbol ?? "-";
              const symbol = rawSymbol.startsWith("NIFTYNIFTY") ? rawSymbol.substring(5) : rawSymbol;
              mapped.push({
                id: mapped.length + 1,
                symbol,
                qty: qty,
                buyPrice: p.buyAvg ?? 0,
                sellPrice: p.sellAvg ?? 0,
                // ltp comes from the backend option price cache; fall back to avg only if null
                ltp: p.ltp ?? p.buyAvg ?? 0,
                type: (p.netQty ?? 0) >= 0 ? "LONG" : "SHORT",
                status: qty === 0 ? "EXITED" : "ACTIVE",
                category: "DAY"
              });
            });
          }
        }

        if (holdRes.ok) {
          const data = await holdRes.json();
          if (data.holdings) {
            data.holdings.forEach((h: any) => {
              mapped.push({
                id: mapped.length + 1,
                symbol: h.symbol ?? "-",
                qty: h.quantity ?? 0,
                buyPrice: h.costPrice ?? 0,
                ltp: h.ltp ?? h.costPrice ?? 0,
                type: "LONG",
                status: "ACTIVE",
                category: "HOLDING"
              });
            });
          }
        }
        
        setPositionsData(mapped);
      } catch (err) {
        console.error("Failed to fetch positions", err);
      }
    };
    
    fetchPositions();
    const interval = setInterval(fetchPositions, 5000);
    return () => clearInterval(interval);
  }, []);

  const filteredData = positionsData.filter(item => {
    if (item.category !== activeTab) return false;
    if (filterStatus !== "ALL" && item.status !== filterStatus) return false;
    if (filterType !== "ALL" && item.type !== filterType) return false;
    return true;
  });

  const totalPnL = filteredData.reduce((acc, pos) => {
    const currentPrice = pos.status === "EXITED" && pos.sellPrice ? pos.sellPrice : pos.ltp;
    const pnl = (currentPrice - pos.buyPrice) * pos.qty;
    return acc + (pos.type === "LONG" ? pnl : -pnl);
  }, 0);

  return (
    <div className="p-6 md:p-10 max-w-7xl mx-auto space-y-8">
      <header className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="text-3xl font-bold text-slate-900 dark:text-white flex items-center gap-3">
            <Briefcase className="w-8 h-8 text-primary" />
            Portfolio
          </h1>
          <p className="text-slate-500 mt-1">Manage day positions and long-term holdings</p>
        </div>
        
        <div className="flex items-center gap-4">
          <div className="text-right">
            <p className="text-sm font-semibold text-slate-500 uppercase tracking-wider">{activeTab} P&L</p>
            <p className={`text-2xl font-bold flex items-center gap-2 ${totalPnL >= 0 ? 'text-green-500' : 'text-danger'}`}>
              {totalPnL >= 0 ? <TrendingUp className="w-5 h-5" /> : <TrendingDown className="w-5 h-5" />}
              ₹ {totalPnL.toFixed(2)}
            </p>
          </div>
          <button className="p-3 bg-surface border border-slate-200 dark:border-slate-700 rounded-xl hover:bg-slate-50 dark:hover:bg-slate-800 transition-colors shadow-sm">
            <RefreshCcw className="w-5 h-5 text-slate-600 dark:text-slate-400" />
          </button>
        </div>
      </header>

      {/* Tabs */}
      <div className="flex border-b border-slate-200 dark:border-slate-800">
        <button 
          onClick={() => setActiveTab("DAY")}
          className={`pb-4 px-6 font-bold text-lg transition-colors border-b-2 ${activeTab === "DAY" ? "border-primary text-primary" : "border-transparent text-slate-500 hover:text-slate-700 dark:hover:text-slate-300"}`}
        >
          Day Positions
        </button>
        <button 
          onClick={() => setActiveTab("HOLDING")}
          className={`pb-4 px-6 font-bold text-lg transition-colors border-b-2 ${activeTab === "HOLDING" ? "border-primary text-primary" : "border-transparent text-slate-500 hover:text-slate-700 dark:hover:text-slate-300"}`}
        >
          Holdings
        </button>
      </div>

      {/* Filters */}
      <div className="flex flex-wrap items-center gap-4 p-4 bg-surface rounded-2xl border border-slate-200 dark:border-slate-800 shadow-sm">
        <div className="flex items-center gap-2 text-slate-500 mr-2">
          <Filter className="w-4 h-4" />
          <span className="text-sm font-semibold uppercase tracking-wider">Filters:</span>
        </div>
        
        <div className="flex bg-slate-100 dark:bg-slate-800 p-1 rounded-lg">
          <button onClick={() => setFilterStatus("ALL")} className={`px-4 py-1.5 text-sm font-bold rounded-md transition-all ${filterStatus === "ALL" ? "bg-white dark:bg-slate-700 shadow-sm text-slate-900 dark:text-white" : "text-slate-500"}`}>All</button>
          <button onClick={() => setFilterStatus("ACTIVE")} className={`px-4 py-1.5 text-sm font-bold rounded-md transition-all ${filterStatus === "ACTIVE" ? "bg-white dark:bg-slate-700 shadow-sm text-slate-900 dark:text-white" : "text-slate-500"}`}>Active</button>
          <button onClick={() => setFilterStatus("EXITED")} className={`px-4 py-1.5 text-sm font-bold rounded-md transition-all ${filterStatus === "EXITED" ? "bg-white dark:bg-slate-700 shadow-sm text-slate-900 dark:text-white" : "text-slate-500"}`}>Exited</button>
        </div>

        <div className="flex bg-slate-100 dark:bg-slate-800 p-1 rounded-lg">
          <button onClick={() => setFilterType("ALL")} className={`px-4 py-1.5 text-sm font-bold rounded-md transition-all ${filterType === "ALL" ? "bg-white dark:bg-slate-700 shadow-sm text-slate-900 dark:text-white" : "text-slate-500"}`}>All Types</button>
          <button onClick={() => setFilterType("LONG")} className={`px-4 py-1.5 text-sm font-bold rounded-md transition-all ${filterType === "LONG" ? "bg-white dark:bg-slate-700 shadow-sm text-slate-900 dark:text-white" : "text-slate-500"}`}>Long</button>
          <button onClick={() => setFilterType("SHORT")} className={`px-4 py-1.5 text-sm font-bold rounded-md transition-all ${filterType === "SHORT" ? "bg-white dark:bg-slate-700 shadow-sm text-slate-900 dark:text-white" : "text-slate-500"}`}>Short</button>
        </div>
      </div>

      <div className="bg-surface border border-slate-200 dark:border-slate-800 rounded-2xl overflow-hidden shadow-sm">
        <div className="overflow-x-auto">
          <table className="w-full text-left border-collapse">
            <thead>
              <tr className="bg-slate-50 dark:bg-slate-800/50 text-slate-500 text-sm font-semibold tracking-wide uppercase border-b border-slate-200 dark:border-slate-800">
                <th className="p-4">Instrument</th>
                <th className="p-4">Type</th>
                <th className="p-4">Status</th>
                <th className="p-4 text-right">Qty</th>
                <th className="p-4 text-right">Avg. Price</th>
                <th className="p-4 text-right">LTP</th>
                <th className="p-4 text-right">P&L</th>
                <th className="p-4 text-center">Action</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 dark:divide-slate-800/50">
              {filteredData.length === 0 ? (
                <tr>
                  <td colSpan={8} className="p-8 text-center text-slate-500">
                    No positions found matching your filters.
                  </td>
                </tr>
              ) : (
                filteredData.map((pos) => {
                  const currentPrice = pos.status === "EXITED" && pos.sellPrice ? pos.sellPrice : pos.ltp;
                  const rawPnl = (currentPrice - pos.buyPrice) * pos.qty;
                  const pnl = pos.type === "LONG" ? rawPnl : -rawPnl;
                  const isProfit = pnl >= 0;
                  
                  return (
                    <tr key={pos.id} className="hover:bg-slate-50 dark:hover:bg-slate-800/20 transition-colors">
                      <td className="p-4 font-bold text-slate-900 dark:text-white">{pos.symbol}</td>
                      <td className="p-4">
                        <span className={`px-2.5 py-1 text-xs font-bold rounded-md ${
                          pos.type === "LONG" 
                            ? "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400" 
                            : "bg-orange-100 text-orange-700 dark:bg-orange-900/30 dark:text-orange-400"
                        }`}>
                          {pos.type}
                        </span>
                      </td>
                      <td className="p-4">
                        <span className={`px-2.5 py-1 text-xs font-bold rounded-md ${
                          pos.status === "ACTIVE"
                            ? "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400"
                            : "bg-slate-200 text-slate-600 dark:bg-slate-800 dark:text-slate-400"
                        }`}>
                          {pos.status}
                        </span>
                      </td>
                      <td className="p-4 text-right font-medium">{pos.qty}</td>
                      <td className="p-4 text-right text-slate-600 dark:text-slate-400">₹{pos.buyPrice.toFixed(2)}</td>
                      <td className="p-4 text-right font-medium">₹{currentPrice.toFixed(2)}</td>
                      <td className={`p-4 text-right font-bold ${isProfit ? 'text-green-500' : 'text-danger'}`}>
                        {isProfit ? '+' : ''}₹{pnl.toFixed(2)}
                      </td>
                      <td className="p-4 text-center">
                        {pos.status === "ACTIVE" ? (
                          <button className="px-4 py-1.5 bg-danger/10 text-danger hover:bg-danger hover:text-white rounded-lg font-bold transition-colors text-sm">
                            EXIT
                          </button>
                        ) : (
                          <span className="text-slate-400 font-medium text-sm">Closed</span>
                        )}
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

