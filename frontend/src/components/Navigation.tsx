"use client";

import { useState, useEffect } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { 
  ShieldAlert, 
  LogOut, 
  Activity, 
  List, 
  Briefcase, 
  Cpu, 
  Layers,
  Settings, 
  Menu, 
  X, 
  PanelLeftClose, 
  PanelLeftOpen 
} from "lucide-react";
import clsx from "clsx";
import { fetchWithAuth } from "@/lib/api";

const navItems = [
  { name: 'Dashboard', href: '/', icon: Activity },
  { name: 'Watchlist', href: '/watchlist', icon: List },
  { name: 'Positions', href: '/positions', icon: Briefcase },
  { name: 'Strategies', href: '/strategies', icon: Cpu },
  { name: 'Strategy Groups', href: '/strategy-groups', icon: Layers },
  { name: 'Strategy Analysis', href: '/strategy-analysis', icon: Activity },
  { name: 'Settings', href: '/settings', icon: Settings }
];

interface SpotInfo {
  price: number;
  change?: number;
  changePercent?: number;
}

export default function Navigation() {
  const pathname = usePathname();
  const [niftySpot, setNiftySpot] = useState<SpotInfo | null>(null);
  const [isCollapsed, setIsCollapsed] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);

  // Restore collapsed preference
  useEffect(() => {
    const saved = localStorage.getItem("stoictrade_sidebar_collapsed");
    if (saved !== null) {
      setIsCollapsed(saved === "true");
    }
  }, []);

  const toggleCollapse = () => {
    setIsCollapsed(prev => {
      const next = !prev;
      localStorage.setItem("stoictrade_sidebar_collapsed", String(next));
      return next;
    });
  };

  // Close mobile drawer on route change
  useEffect(() => {
    setMobileOpen(false);
  }, [pathname]);

  useEffect(() => {
    let isRunning = false;

    const checkStatusAndSpot = async () => {
      try {
        const statusRes = await fetchWithAuth("/api/engine/status");
        if (statusRes.ok) {
          const statusData = await statusRes.json();
          isRunning = statusData.isRunning === true || statusData.IsRunning === true;
        }

        if (isRunning) {
          const res = await fetchWithAuth("/api/marketdata/spot?symbol=NIFTY");
          if (res.ok) {
            const data = await res.json();
            const p = data.price ?? data.lastPrice;
            if (p !== undefined && p > 0) {
              setNiftySpot({
                price: p,
                change: data.change,
                changePercent: data.changePercent
              });
            }
          }
        }
      } catch {}
    };

    checkStatusAndSpot();
    const interval = setInterval(checkStatusAndSpot, 6000);
    return () => clearInterval(interval);
  }, []);

  const handleLogout = () => {
    localStorage.removeItem("jwt_token");
    window.location.href = "/login";
  };

  return (
    <>
      {/* ── Mobile Top Header with Hamburger ── */}
      <header className="md:hidden flex items-center justify-between px-4 py-3 bg-surface border-b border-slate-200 dark:border-slate-800 sticky top-0 z-30 shadow-sm">
        <div className="flex items-center gap-3">
          <button
            onClick={() => setMobileOpen(prev => !prev)}
            aria-label="Toggle navigation menu"
            className="p-2 rounded-lg text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors focus:outline-none focus:ring-2 focus:ring-primary"
          >
            {mobileOpen ? <X className="w-6 h-6 text-primary" /> : <Menu className="w-6 h-6" />}
          </button>
          <div className="flex items-center gap-2 font-bold text-primary text-lg">
            <ShieldAlert className="w-6 h-6" />
            <span>StoicTrade</span>
          </div>
        </div>

        {/* Live Spot Pill in Mobile Header */}
        {niftySpot && (
          <div className="flex items-center gap-1.5 px-2.5 py-1 bg-slate-50 dark:bg-slate-800/80 border border-slate-200/80 dark:border-slate-700/60 rounded-lg text-xs font-semibold">
            <span className="w-2 h-2 rounded-full bg-green-500 animate-pulse"></span>
            <span className="text-slate-900 dark:text-white">₹{niftySpot.price.toFixed(0)}</span>
            {niftySpot.change !== undefined && (
              <span className={`text-[11px] ${niftySpot.change >= 0 ? "text-green-500" : "text-rose-500"}`}>
                ({niftySpot.change >= 0 ? `+${niftySpot.change.toFixed(1)}` : niftySpot.change.toFixed(1)})
              </span>
            )}
          </div>
        )}
      </header>

      {/* ── Mobile Backdrop Drawer ── */}
      {mobileOpen && (
        <div 
          className="md:hidden fixed inset-0 z-40 bg-slate-900/60 backdrop-blur-sm transition-opacity"
          onClick={() => setMobileOpen(false)}
        />
      )}

      {/* ── Mobile Drawer Sidebar ── */}
      <aside
        className={clsx(
          "md:hidden fixed top-0 bottom-0 left-0 z-50 w-72 bg-surface border-r border-slate-200 dark:border-slate-800 shadow-2xl flex flex-col transform transition-transform duration-300 ease-in-out",
          mobileOpen ? "translate-x-0" : "-translate-x-full"
        )}
      >
        <div className="p-5 flex items-center justify-between border-b border-slate-200 dark:border-slate-800">
          <div className="flex items-center gap-2 font-bold text-primary text-xl">
            <ShieldAlert className="w-6 h-6" />
            <span>StoicTrade</span>
          </div>
          <button
            onClick={() => setMobileOpen(false)}
            aria-label="Close menu"
            className="p-1.5 rounded-lg text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Mobile Live NIFTY Spot Card */}
        <div className="p-4">
          <div className="p-3 bg-slate-50 dark:bg-slate-800/60 border border-slate-200/80 dark:border-slate-700/60 rounded-xl">
            <div className="flex items-center justify-between text-xs text-slate-500 font-medium">
              <span className="font-semibold uppercase tracking-wider text-[11px]">NIFTY SPOT</span>
              <span className="flex items-center gap-1 text-green-500 text-[11px]">
                <span className="w-2 h-2 rounded-full bg-green-500 animate-pulse"></span>
                Live
              </span>
            </div>
            <div className="flex items-baseline flex-wrap gap-1.5 mt-0.5">
              <span className="text-lg font-bold text-slate-900 dark:text-white">
                {niftySpot !== null ? `₹ ${niftySpot.price.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` : "Loading..."}
              </span>
              {niftySpot !== null && niftySpot.change !== undefined && (
                <span className={`text-xs font-semibold ${niftySpot.change >= 0 ? "text-green-500" : "text-rose-500"}`}>
                  ({niftySpot.change >= 0 ? `+${niftySpot.change.toFixed(2)}` : niftySpot.change.toFixed(2)})
                </span>
              )}
            </div>
          </div>
        </div>

        <nav className="flex-1 px-4 space-y-1 overflow-y-auto">
          {navItems.map((item) => {
            const isActive = pathname === item.href;
            const Icon = item.icon;
            return (
              <Link
                key={item.name}
                href={item.href}
                className={clsx(
                  "flex items-center gap-3 px-4 py-3 rounded-xl transition-colors font-medium text-sm",
                  isActive
                    ? "bg-primary text-white shadow-md shadow-primary/20"
                    : "text-slate-600 hover:bg-slate-100 dark:text-slate-400 dark:hover:bg-slate-800"
                )}
              >
                <Icon className="w-5 h-5" />
                {item.name}
              </Link>
            );
          })}
        </nav>

        <div className="p-4 border-t border-slate-200 dark:border-slate-800">
          <button
            onClick={handleLogout}
            className="flex items-center gap-3 w-full px-4 py-3 rounded-xl transition-colors font-medium text-sm text-slate-600 hover:bg-red-50 hover:text-red-600 dark:text-slate-400 dark:hover:bg-red-900/20 dark:hover:text-red-400"
          >
            <LogOut className="w-5 h-5" />
            Log Out
          </button>
        </div>
      </aside>

      {/* ── Desktop Sidebar (Collapsible with Hamburger Toggle) ── */}
      <aside 
        className={clsx(
          "hidden md:flex flex-col h-screen bg-surface border-r border-slate-200 dark:border-slate-800 shadow-sm sticky top-0 transition-all duration-300 ease-in-out",
          isCollapsed ? "w-20" : "w-64"
        )}
      >
        {/* Header with Title & Hamburger Toggle Button */}
        <div className={clsx("p-5 pb-2 flex items-center", isCollapsed ? "justify-center" : "justify-between")}>
          {!isCollapsed && (
            <h1 className="text-2xl font-bold text-primary flex items-center gap-2 overflow-hidden whitespace-nowrap">
              <ShieldAlert className="w-6 h-6 shrink-0" />
              <span>StoicTrade</span>
            </h1>
          )}
          {isCollapsed && (
            <ShieldAlert className="w-7 h-7 text-primary shrink-0 mb-1" />
          )}

          {/* Desktop Hamburger / Toggle Button */}
          <button
            onClick={toggleCollapse}
            aria-label={isCollapsed ? "Expand sidebar" : "Collapse sidebar"}
            title={isCollapsed ? "Expand sidebar" : "Collapse sidebar"}
            className={clsx(
              "p-2 rounded-lg text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800 hover:text-primary transition-colors focus:outline-none",
              isCollapsed && "mt-2"
            )}
          >
            {isCollapsed ? (
              <PanelLeftOpen className="w-5 h-5" />
            ) : (
              <PanelLeftClose className="w-5 h-5" />
            )}
          </button>
        </div>

        {/* Live NIFTY Spot Card (Desktop) */}
        {!isCollapsed ? (
          <div className="px-5 mt-3">
            <div className="p-3 bg-slate-50 dark:bg-slate-800/60 border border-slate-200/80 dark:border-slate-700/60 rounded-xl transition-all">
              <div className="flex items-center justify-between text-xs text-slate-500 font-medium">
                <span className="font-semibold uppercase tracking-wider text-[11px]">NIFTY SPOT</span>
                <span className="flex items-center gap-1 text-green-500 text-[11px]">
                  <span className="w-2 h-2 rounded-full bg-green-500 animate-pulse"></span>
                  Live
                </span>
              </div>
              <div className="flex items-baseline flex-wrap gap-1.5 mt-0.5">
                <span className="text-lg font-bold text-slate-900 dark:text-white">
                  {niftySpot !== null ? `₹ ${niftySpot.price.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` : "Loading..."}
                </span>
                {niftySpot !== null && niftySpot.change !== undefined && (
                  <span className={`text-xs font-semibold ${niftySpot.change >= 0 ? "text-green-500" : "text-rose-500"}`}>
                    ({niftySpot.change >= 0 ? `+${niftySpot.change.toFixed(2)}` : niftySpot.change.toFixed(2)})
                  </span>
                )}
              </div>
            </div>
          </div>
        ) : (
          <div className="px-2 mt-2 flex flex-col items-center" title={`NIFTY SPOT: ₹${niftySpot?.price ?? '—'}`}>
            <div className="w-12 py-2 flex flex-col items-center bg-slate-50 dark:bg-slate-800/60 border border-slate-200/80 dark:border-slate-700/60 rounded-xl">
              <span className="w-2 h-2 rounded-full bg-green-500 animate-pulse mb-1"></span>
              <span className="text-[10px] font-bold text-slate-900 dark:text-white">
                {niftySpot ? `${(niftySpot.price / 1000).toFixed(1)}k` : "—"}
              </span>
            </div>
          </div>
        )}

        {/* Navigation Items */}
        <nav className="flex-1 px-3 space-y-1.5 mt-4">
          {navItems.map((item) => {
            const isActive = pathname === item.href;
            const Icon = item.icon;
            return (
              <Link
                key={item.name}
                href={item.href}
                title={isCollapsed ? item.name : undefined}
                className={clsx(
                  "flex items-center gap-3 px-3.5 py-3 rounded-xl transition-all font-medium text-sm",
                  isCollapsed ? "justify-center" : "",
                  isActive
                    ? "bg-primary text-white shadow-md shadow-primary/20"
                    : "text-slate-600 hover:bg-slate-100 dark:text-slate-400 dark:hover:bg-slate-800"
                )}
              >
                <Icon className="w-5 h-5 shrink-0" />
                {!isCollapsed && <span className="truncate">{item.name}</span>}
              </Link>
            );
          })}
        </nav>

        {/* Logout Button */}
        <div className="p-3 mt-auto border-t border-slate-200 dark:border-slate-800">
          <button
            onClick={handleLogout}
            title={isCollapsed ? "Log Out" : undefined}
            className={clsx(
              "flex items-center gap-3 w-full px-3.5 py-3 rounded-xl transition-colors font-medium text-sm text-slate-600 hover:bg-red-50 hover:text-red-600 dark:text-slate-400 dark:hover:bg-red-900/20 dark:hover:text-red-400",
              isCollapsed ? "justify-center" : ""
            )}
          >
            <LogOut className="w-5 h-5 shrink-0" />
            {!isCollapsed && <span>Log Out</span>}
          </button>
        </div>
      </aside>

      {/* ── Mobile Bottom Navigation Bar ── */}
      <nav className="md:hidden fixed bottom-0 left-0 right-0 bg-surface border-t border-slate-200 dark:border-slate-800 flex justify-around p-2 pb-safe z-30 shadow-[0_-4px_6px_-1px_rgba(0,0,0,0.1)]">
        {navItems.slice(0, 4).map((item) => {
          const isActive = pathname === item.href;
          const Icon = item.icon;
          return (
            <Link
              key={item.name}
              href={item.href}
              className={clsx(
                "flex flex-col items-center justify-center p-1.5 rounded-xl min-w-[56px] transition-colors",
                isActive
                  ? "text-primary bg-blue-50 dark:bg-blue-900/20"
                  : "text-slate-500 hover:text-slate-900 dark:hover:text-slate-300"
              )}
            >
              <Icon className={clsx("w-5 h-5 mb-0.5", isActive && "fill-primary/20")} />
              <span className="text-[10px] font-semibold">{item.name}</span>
            </Link>
          );
        })}
        <button
          onClick={() => setMobileOpen(true)}
          className={clsx(
            "flex flex-col items-center justify-center p-1.5 rounded-xl min-w-[56px] transition-colors",
            mobileOpen ? "text-primary bg-blue-50 dark:bg-blue-900/20" : "text-slate-500 hover:text-slate-900 dark:hover:text-slate-300"
          )}
        >
          <Menu className="w-5 h-5 mb-0.5" />
          <span className="text-[10px] font-semibold">More</span>
        </button>
      </nav>
    </>
  );
}
