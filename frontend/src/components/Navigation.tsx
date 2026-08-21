"use client";

import { useState, useEffect } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { LayoutDashboard, TrendingUp, Settings, ListOrdered, ShieldAlert, BarChart2, Settings2, LogOut, Activity, List, Briefcase, Cpu } from "lucide-react";
import clsx from "clsx";
import { fetchWithAuth } from "@/lib/api";

const navItems = [
  { name: 'Dashboard', href: '/', icon: Activity },
  { name: 'Watchlist', href: '/watchlist', icon: List },
  { name: 'Positions', href: '/positions', icon: Briefcase },
  { name: 'Strategies', href: '/strategies', icon: Cpu },
  { name: 'Strategy Analysis', href: '/strategy-analysis', icon: Activity },
  { name: 'Settings', href: '/settings', icon: Settings }
];

export default function Navigation() {
  const pathname = usePathname();
  const [niftySpot, setNiftySpot] = useState<number | null>(null);

  useEffect(() => {
    const fetchSpot = async () => {
      try {
        const res = await fetchWithAuth("/api/marketdata/spot?symbol=NIFTY");
        if (res.ok) {
          const data = await res.json();
          setNiftySpot(data.price ?? data.lastPrice ?? null);
        }
      } catch {}
    };

    fetchSpot();
    const interval = setInterval(fetchSpot, 3000);
    return () => clearInterval(interval);
  }, []);

  const handleLogout = () => {
    localStorage.removeItem("jwt_token");
    window.location.href = "/login";
  };

  return (
    <>
      {/* Desktop Sidebar */}
      <aside className="hidden md:flex flex-col w-64 h-screen bg-surface border-r border-slate-200 dark:border-slate-800 shadow-sm sticky top-0">
        <div className="p-6 pb-2">
          <h1 className="text-2xl font-bold text-primary flex items-center gap-2">
            <ShieldAlert className="w-6 h-6" />
            StoicTrade
          </h1>

          {/* Live NIFTY Spot Card */}
          <div className="mt-4 p-3 bg-slate-50 dark:bg-slate-800/60 border border-slate-200/80 dark:border-slate-700/60 rounded-xl">
            <div className="flex items-center justify-between text-xs text-slate-500 font-medium">
              <span className="font-semibold uppercase tracking-wider text-[11px]">NIFTY SPOT</span>
              <span className="flex items-center gap-1 text-green-500 text-[11px]">
                <span className="w-2 h-2 rounded-full bg-green-500 animate-pulse"></span>
                Live
              </span>
            </div>
            <div className="text-lg font-bold text-slate-900 dark:text-white mt-0.5">
              {niftySpot !== null ? `₹ ${niftySpot.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` : "Loading..."}
            </div>
          </div>
        </div>
        <nav className="flex-1 px-4 space-y-2 mt-4">
          {navItems.map((item) => {
            const isActive = pathname === item.href;
            const Icon = item.icon;
            return (
              <Link
                key={item.name}
                href={item.href}
                className={clsx(
                  "flex items-center gap-3 px-4 py-3 rounded-lg transition-colors font-medium",
                  isActive
                    ? "bg-primary text-white shadow-md"
                    : "text-slate-600 hover:bg-slate-100 dark:text-slate-400 dark:hover:bg-slate-800"
                )}
              >
                <Icon className="w-5 h-5" />
                {item.name}
              </Link>
            );
          })}
        </nav>
        <div className="p-4 mt-auto border-t border-slate-200 dark:border-slate-800">
          <button
            onClick={handleLogout}
            className="flex items-center gap-3 w-full px-4 py-3 rounded-lg transition-colors font-medium text-slate-600 hover:bg-red-50 hover:text-red-600 dark:text-slate-400 dark:hover:bg-red-900/20 dark:hover:text-red-400"
          >
            <LogOut className="w-5 h-5" />
            Log Out
          </button>
        </div>
      </aside>

      {/* Mobile Bottom Nav */}
      <nav className="md:hidden fixed bottom-0 left-0 right-0 bg-surface border-t border-slate-200 dark:border-slate-800 flex justify-around p-3 pb-safe z-50 shadow-[0_-4px_6px_-1px_rgba(0,0,0,0.1)]">
        {navItems.map((item) => {
          const isActive = pathname === item.href;
          const Icon = item.icon;
          return (
            <Link
              key={item.name}
              href={item.href}
              className={clsx(
                "flex flex-col items-center justify-center p-2 rounded-xl min-w-[64px] transition-colors",
                isActive
                  ? "text-primary bg-blue-50 dark:bg-blue-900/20"
                  : "text-slate-500 hover:text-slate-900 dark:hover:text-slate-300"
              )}
            >
              <Icon className={clsx("w-6 h-6 mb-1", isActive && "fill-primary/20")} />
              <span className="text-[10px] font-semibold">{item.name}</span>
            </Link>
          );
        })}
        <button
          onClick={handleLogout}
          className="flex flex-col items-center justify-center p-2 rounded-xl min-w-[64px] transition-colors text-slate-500 hover:text-red-600 dark:hover:text-red-400"
        >
          <LogOut className="w-6 h-6 mb-1" />
          <span className="text-[10px] font-semibold">Log Out</span>
        </button>
      </nav>
    </>
  );
}
