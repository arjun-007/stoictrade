"use client";

import { useState, useEffect } from "react";
import { ShieldAlert, TrendingUp, DollarSign, Briefcase, Key } from "lucide-react";
import { fetchWithAuth } from "@/lib/api";

export default function Dashboard() {
  const [isLocked, setIsLocked] = useState(false);
  const [isEngineRunning, setIsEngineRunning] = useState(false);
  const [shutdownMinutes, setShutdownMinutes] = useState(720);
  const [totpCode, setTotpCode] = useState<string | null>(null);
  const [pin, setPin] = useState("");
  const [error, setError] = useState<string | null>(null);

  // Kill Switch Modal state
  const [showKillModal, setShowKillModal] = useState(false);
  const [openPositions, setOpenPositions] = useState([
    { id: '1', symbol: 'NIFTY24MAY22000CE', qty: 50, pnl: 2500, selected: true },
    { id: '2', symbol: 'HDFCBANK-EQ', qty: 100, pnl: -450, selected: true }
  ]);

  useEffect(() => {
    // Check initial kill switch status
    fetchWithAuth("http://localhost:5000/api/killswitch/status")
      .then((res) => res.json())
      .then((data) => setIsLocked(data.isActive))
      .catch((err) => console.error("Failed to check status", err));

    fetchWithAuth("http://localhost:5000/api/engine/status")
      .then((res) => res.json())
      .then((data) => setIsEngineRunning(data.isRunning))
      .catch((err) => console.error("Failed to check engine status", err));

    fetchWithAuth("http://localhost:5000/api/globalsettings")
      .then((res) => res.json())
      .then((data) => setShutdownMinutes(data.killSwitchShutdownMinutes || 720))
      .catch((err) => console.error("Failed to fetch settings", err));
  }, []);

  const handleKillSwitch = () => {
    setShowKillModal(true);
  };

  const confirmKillSwitch = async () => {
    try {
      // In a real app, we would pass the selected positions to the backend to exit them
      const selectedPositions = openPositions.filter(p => p.selected).map(p => p.id);
      
      const res = await fetchWithAuth("http://localhost:5000/api/killswitch/trigger", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ reason: "Manual UI Trigger", closePositions: selectedPositions })
      });
      
      if (res.ok) {
        setIsLocked(true);
        setShowKillModal(false);
        alert("Account has been locked and selected positions have been scheduled for exit.");
      }
    } catch (err) {
      console.error(err);
    }
  };

  const requestTotp = async () => {
    try {
      const res = await fetchWithAuth("http://localhost:5000/api/totp/request", { method: "POST" });
      const data = await res.json();
      alert(data.message || data.error);
    } catch (err) {
      console.error(err);
    }
  };

  const generateTotp = async () => {
    setError(null);
    try {
      const msgBuffer = new TextEncoder().encode(pin);
      const hashBuffer = await crypto.subtle.digest('SHA-256', msgBuffer);
      const hashArray = Array.from(new Uint8Array(hashBuffer));
      const hashHex = hashArray.map(b => b.toString(16).padStart(2, '0')).join('');

      const res = await fetchWithAuth("http://localhost:5000/api/totp/generate", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ pin: hashHex })
      });
      const data = await res.json();
      if (res.ok) {
        setTotpCode(data.totpCode);
      } else {
        setError(data.error);
      }
    } catch (err) {
      setError("Failed to connect to server");
    }
  };

  const toggleEngine = async () => {
    try {
      const endpoint = isEngineRunning ? "stop" : "start";
      const res = await fetchWithAuth(`http://localhost:5000/api/engine/${endpoint}`, { method: "POST" });
      if (res.ok) {
        setIsEngineRunning(!isEngineRunning);
      } else {
        const data = await res.json();
        alert(data.error || "Failed to toggle engine");
      }
    } catch (err) {
      console.error(err);
      alert("Network error toggling engine");
    }
  };

  return (
    <div className="p-6 md:p-10 max-w-7xl mx-auto space-y-8">
      <header className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="text-3xl font-bold text-slate-900 dark:text-white">Master Dashboard</h1>
          <p className="text-slate-500 mt-1">Consolidated view of your trading activity</p>
        </div>
        <div className="flex gap-4">
          <button 
            onClick={toggleEngine}
            className={`flex items-center gap-2 px-6 py-3 rounded-lg font-bold text-white shadow-lg transition-all ${
              isEngineRunning ? "bg-amber-500 hover:bg-amber-600" : "bg-green-500 hover:bg-green-600"
            }`}
          >
            {isEngineRunning ? "Stop Engine" : "Start Engine"}
          </button>
          
          <button 
            onClick={handleKillSwitch}
            disabled={isLocked}
            className={`flex items-center gap-2 px-6 py-3 rounded-lg font-bold text-white shadow-lg transition-all ${
              isLocked ? "bg-slate-400 cursor-not-allowed" : "bg-danger hover:bg-danger-hover active:scale-95"
            }`}
          >
            <ShieldAlert className="w-5 h-5" />
            {isLocked ? "ACCOUNT LOCKED" : "MASTER KILL SWITCH"}
          </button>
        </div>
      </header>

      {/* Stats Grid */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        <div className="bg-surface p-6 rounded-2xl shadow-sm border border-slate-100 dark:border-slate-800">
          <div className="flex items-center justify-between">
            <h3 className="text-slate-500 font-medium">Daily P&L</h3>
            <div className="p-2 bg-green-100 dark:bg-green-900/30 text-green-600 rounded-lg">
              <TrendingUp className="w-5 h-5" />
            </div>
          </div>
          <p className="text-3xl font-bold mt-4 text-green-500">+₹ 14,250</p>
        </div>
        
        <div className="bg-surface p-6 rounded-2xl shadow-sm border border-slate-100 dark:border-slate-800">
          <div className="flex items-center justify-between">
            <h3 className="text-slate-500 font-medium">Available Margin</h3>
            <div className="p-2 bg-blue-100 dark:bg-blue-900/30 text-primary rounded-lg">
              <DollarSign className="w-5 h-5" />
            </div>
          </div>
          <p className="text-3xl font-bold mt-4 text-slate-900 dark:text-white">₹ 4,50,000</p>
        </div>

        <div className="bg-surface p-6 rounded-2xl shadow-sm border border-slate-100 dark:border-slate-800">
          <div className="flex items-center justify-between">
            <h3 className="text-slate-500 font-medium">Active Positions</h3>
            <div className="p-2 bg-purple-100 dark:bg-purple-900/30 text-purple-600 rounded-lg">
              <Briefcase className="w-5 h-5" />
            </div>
          </div>
          <p className="text-3xl font-bold mt-4 text-slate-900 dark:text-white">2</p>
        </div>
      </div>

      {/* Manual Access Gate */}
      <div className="bg-surface p-8 rounded-2xl shadow-sm border border-slate-100 dark:border-slate-800">
        <div className="flex items-center gap-3 mb-6">
          <Key className="w-6 h-6 text-primary" />
          <h2 className="text-xl font-bold">Manual Access Gate (Fyers TOTP)</h2>
        </div>
        
        <div className="grid md:grid-cols-2 gap-8">
          <div className="space-y-4">
            <p className="text-slate-600 dark:text-slate-400">
              1. Request access. If the Kill Switch is active, you will face a 20-minute behavioral cooling-off delay.
            </p>
            <button 
              onClick={requestTotp}
              className="px-4 py-2 bg-slate-200 hover:bg-slate-300 dark:bg-slate-700 dark:hover:bg-slate-600 text-slate-800 dark:text-slate-100 rounded-lg font-medium transition-colors"
            >
              Request Manual Access
            </button>
          </div>
          
          <div className="space-y-4 border-l border-slate-200 dark:border-slate-700 pl-8">
            <p className="text-slate-600 dark:text-slate-400">
              2. Enter your PIN to generate the TOTP code (after the delay).
            </p>
            <div className="flex gap-2">
              <input 
                type="password" 
                placeholder="Enter PIN" 
                value={pin}
                onChange={e => setPin(e.target.value)}
                className="flex-1 px-4 py-2 rounded-lg border border-slate-300 dark:border-slate-600 bg-transparent focus:outline-none focus:ring-2 focus:ring-primary"
              />
              <button 
                onClick={generateTotp}
                className="px-6 py-2 bg-primary hover:bg-primary-hover text-white rounded-lg font-medium transition-colors"
              >
                Generate
              </button>
            </div>
            
            {error && <p className="text-danger text-sm">{error}</p>}
            
            {totpCode && (
              <div className="mt-4 p-4 bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-800 rounded-lg text-center">
                <p className="text-sm text-green-600 dark:text-green-400 font-medium mb-1">Your TOTP Code</p>
                <p className="text-4xl font-mono font-bold tracking-widest text-slate-900 dark:text-white">{totpCode}</p>
              </div>
            )}
          </div>
        </div>
      </div>

      {showKillModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
          <div className="bg-surface rounded-2xl w-full max-w-lg shadow-xl overflow-hidden border border-slate-200 dark:border-slate-800">
            <div className="bg-danger p-6 text-white text-center">
              <ShieldAlert className="w-12 h-12 mx-auto mb-3" />
              <h2 className="text-2xl font-bold">ENGAGE KILL SWITCH</h2>
              <p className="text-red-100 mt-1">This will lock the account for {shutdownMinutes} minutes!</p>
            </div>
            
            <div className="p-6">
              <h3 className="font-bold text-slate-900 dark:text-white mb-4">Open Positions to Close</h3>
              
              {openPositions.length === 0 ? (
                <p className="text-slate-500 mb-6">No active positions open.</p>
              ) : (
                <div className="space-y-3 mb-6 max-h-48 overflow-y-auto">
                  {openPositions.map(pos => (
                    <label key={pos.id} className="flex items-center gap-3 p-3 rounded-lg border border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-800/50 cursor-pointer transition-colors">
                      <input 
                        type="checkbox" 
                        checked={pos.selected}
                        onChange={(e) => {
                          setOpenPositions(openPositions.map(p => p.id === pos.id ? { ...p, selected: e.target.checked } : p))
                        }}
                        className="w-5 h-5 text-danger rounded border-slate-300 focus:ring-danger"
                      />
                      <div className="flex-1 flex justify-between items-center">
                        <div>
                          <p className="font-bold">{pos.symbol}</p>
                          <p className="text-xs text-slate-500">Qty: {pos.qty}</p>
                        </div>
                        <p className={`font-bold ${pos.pnl >= 0 ? "text-green-500" : "text-danger"}`}>
                          {pos.pnl >= 0 ? "+" : ""}₹{Math.abs(pos.pnl)}
                        </p>
                      </div>
                    </label>
                  ))}
                </div>
              )}
              
              <div className="flex gap-4">
                <button 
                  onClick={() => setShowKillModal(false)}
                  className="flex-1 py-3 px-4 rounded-xl font-bold bg-slate-100 hover:bg-slate-200 dark:bg-slate-800 dark:hover:bg-slate-700 text-slate-700 dark:text-slate-300 transition-colors"
                >
                  Cancel
                </button>
                <button 
                  onClick={confirmKillSwitch}
                  className="flex-1 py-3 px-4 rounded-xl font-bold bg-danger hover:bg-danger-hover text-white transition-colors"
                >
                  CONFIRM LOCKDOWN
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

    </div>
  );
}
