"use client";

import { useState } from "react";
import { X, TrendingUp, TrendingDown } from "lucide-react";
import { fetchWithAuth } from "@/lib/api";

interface OrderModalProps {
  instrument: string;
  price?: number;
  change?: number;
  onClose: () => void;
}

export default function OrderModal({ instrument, price = 0, change = 0, onClose }: OrderModalProps) {
  const [quantity, setQuantity] = useState(50);
  const [orderType, setOrderType] = useState<"BUY" | "SELL">("BUY");
  const [orderMode, setOrderMode] = useState<"MARKET" | "LIMIT">("MARKET");
  const [entryPrice, setEntryPrice] = useState<string>("");
  const [stoploss, setStoploss] = useState<string>("");
  const [target, setTarget] = useState<string>("");
  
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const calculationPrice = orderMode === "LIMIT" && entryPrice ? Number(entryPrice) : price;
  const estimatedMargin = (quantity * calculationPrice).toFixed(2);

  const submitOrder = async () => {
    setLoading(true);
    setError(null);
    try {
      const payload = {
        instrument,
        quantity,
        orderType,
        orderMode,
        entryPrice: orderMode === "LIMIT" ? Number(entryPrice) : null,
        stoploss: stoploss ? Number(stoploss) : null,
        target: target ? Number(target) : null
      };

      const res = await fetchWithAuth("/api/orders", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      const data = await res.json();
      
      if (res.ok) {
        alert(data.message || "Order placed successfully");
        onClose();
      } else {
        setError(data.error || "Failed to place order");
      }
    } catch (err) {
      setError("Failed to connect to backend");
    } finally {
      setLoading(false);
    }
  };

  return (
    <>
      <div 
        className="fixed inset-0 bg-black/40 backdrop-blur-sm z-40 transition-opacity"
        onClick={onClose}
      />
      
      {/* Desktop Modal / Mobile Bottom Sheet */}
      <div className="fixed z-50 bg-surface w-full md:w-[450px] max-w-full bottom-0 md:bottom-auto md:top-1/2 left-1/2 -translate-x-1/2 md:-translate-y-1/2 rounded-t-3xl md:rounded-2xl shadow-2xl p-6 transition-transform transform max-h-[90vh] overflow-y-auto">
        <div className="flex justify-between items-start mb-6">
          <div>
            <h2 className="text-xl font-bold">{instrument}</h2>
            <p className="text-sm text-slate-500 mb-2">Place Order</p>
            <div className="flex items-center gap-3">
              <span className="text-2xl font-bold text-slate-900 dark:text-white">₹ {price.toFixed(2)}</span>
              <span className={`text-sm font-semibold flex items-center gap-1 px-2 py-1 rounded-md ${change >= 0 ? "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400" : "bg-red-100 text-danger dark:bg-red-900/30 dark:text-red-400"}`}>
                {change >= 0 ? <TrendingUp className="w-3 h-3" /> : <TrendingDown className="w-3 h-3" />}
                {Math.abs(change)}%
              </span>
            </div>
          </div>
          <button onClick={onClose} className="p-2 hover:bg-slate-100 dark:hover:bg-slate-800 rounded-full transition-colors">
            <X className="w-5 h-5" />
          </button>
        </div>

        {error && (
          <div className="mb-6 p-4 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg text-danger text-sm">
            {error}
          </div>
        )}

        <div className="space-y-6">
          {/* Buy / Sell Toggle */}
          <div className="flex bg-slate-100 dark:bg-slate-800 p-1 rounded-xl">
            <button 
              className={`flex-1 py-2 font-bold rounded-lg transition-all ${orderType === 'BUY' ? 'bg-white dark:bg-slate-700 text-blue-600 shadow-sm' : 'text-slate-500'}`}
              onClick={() => setOrderType('BUY')}
            >
              BUY
            </button>
            <button 
              className={`flex-1 py-2 font-bold rounded-lg transition-all ${orderType === 'SELL' ? 'bg-white dark:bg-slate-700 text-danger shadow-sm' : 'text-slate-500'}`}
              onClick={() => setOrderType('SELL')}
            >
              SELL
            </button>
          </div>

          {/* Quantity */}
          <div>
            <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">Quantity</label>
            <div className="flex items-center">
              <button 
                onClick={() => setQuantity(Math.max(1, quantity - 50))}
                className="w-12 h-12 flex items-center justify-center bg-slate-100 dark:bg-slate-800 rounded-l-xl text-xl font-bold hover:bg-slate-200 dark:hover:bg-slate-700 transition-colors"
              >-</button>
              <input 
                type="number" 
                value={quantity}
                onChange={e => setQuantity(Number(e.target.value))}
                className="w-full h-12 text-center bg-slate-50 dark:bg-slate-900 border-y border-slate-100 dark:border-slate-800 font-bold text-lg focus:outline-none"
              />
              <button 
                onClick={() => setQuantity(quantity + 50)}
                className="w-12 h-12 flex items-center justify-center bg-slate-100 dark:bg-slate-800 rounded-r-xl text-xl font-bold hover:bg-slate-200 dark:hover:bg-slate-700 transition-colors"
              >+</button>
            </div>
          </div>

          {/* Market / Limit Toggle */}
          <div className="flex gap-4">
            <label className="flex items-center gap-2 cursor-pointer">
              <input 
                type="radio" 
                name="orderMode" 
                checked={orderMode === "MARKET"} 
                onChange={() => setOrderMode("MARKET")}
                className="w-4 h-4 text-primary focus:ring-primary"
              />
              <span className="font-medium text-slate-700 dark:text-slate-300">Market</span>
            </label>
            <label className="flex items-center gap-2 cursor-pointer">
              <input 
                type="radio" 
                name="orderMode" 
                checked={orderMode === "LIMIT"} 
                onChange={() => setOrderMode("LIMIT")}
                className="w-4 h-4 text-primary focus:ring-primary"
              />
              <span className="font-medium text-slate-700 dark:text-slate-300">Limit</span>
            </label>
          </div>

          {/* Price Inputs */}
          <div className="grid grid-cols-2 gap-4">
            <div className="col-span-2">
              <label className="block text-xs font-semibold text-slate-500 uppercase tracking-wider mb-1">Entry Price</label>
              <input 
                type="number" 
                value={entryPrice}
                onChange={e => setEntryPrice(e.target.value)}
                disabled={orderMode === "MARKET"}
                placeholder={orderMode === "MARKET" ? "Market Price" : "0.00"}
                className={`w-full px-4 py-3 rounded-xl border font-medium focus:outline-none focus:ring-2 focus:ring-primary ${
                  orderMode === "MARKET" 
                    ? "bg-slate-100 dark:bg-slate-800 border-transparent text-slate-400 cursor-not-allowed" 
                    : "bg-surface border-slate-200 dark:border-slate-700"
                }`}
              />
            </div>
            
            <div>
              <label className="block text-xs font-semibold text-slate-500 uppercase tracking-wider mb-1">Stoploss (SL)</label>
              <input 
                type="number" 
                value={stoploss}
                onChange={e => setStoploss(e.target.value)}
                placeholder="Optional"
                className="w-full px-4 py-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-surface font-medium focus:outline-none focus:ring-2 focus:ring-primary"
              />
            </div>
            <div>
              <label className="block text-xs font-semibold text-slate-500 uppercase tracking-wider mb-1">Target</label>
              <input 
                type="number" 
                value={target}
                onChange={e => setTarget(e.target.value)}
                placeholder="Optional"
                className="w-full px-4 py-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-surface font-medium focus:outline-none focus:ring-2 focus:ring-primary"
              />
            </div>
          </div>

          <div className="flex justify-between items-center py-4 border-t border-slate-100 dark:border-slate-800">
            <span className="text-slate-500 font-medium">Req. Margin</span>
            <span className="text-lg font-bold">₹ {estimatedMargin}</span>
          </div>

          <button 
            onClick={submitOrder}
            disabled={loading}
            className={`w-full py-4 rounded-xl font-bold text-white text-lg transition-colors shadow-lg ${
              orderType === 'BUY' 
                ? "bg-blue-600 hover:bg-blue-700 shadow-blue-600/30" 
                : "bg-danger hover:bg-danger-hover shadow-danger/30"
            }`}
          >
            {loading ? "PROCESSING..." : `CONFIRM ${orderType}`}
          </button>
        </div>
      </div>
    </>
  );
}
