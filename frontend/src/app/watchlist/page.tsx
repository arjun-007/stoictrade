"use client";

import { useState, useEffect, useRef } from "react";
import { Search, TrendingUp, TrendingDown, Info, Plus, Trash2, FolderPlus, Loader2 } from "lucide-react";
import OrderModal from "./OrderModal";
import { fetchWithAuth } from "@/lib/api";

interface WatchlistItem {
  id: string;
  symbol: string;        // canonical key, e.g. "NIFTY26AUG23850CE"
  displayName: string;   // formatted, e.g. "NIFTY 26 AUG 26 23850 CE"
  price: number;
  change: number;
}

interface Watchlist {
  id: string;
  name: string;
  items: WatchlistItem[];
}

interface ParsedInstrument {
  symbol: string;       // raw/canonical key
  displayName: string;  // formatted for UI
  price: number;
  change: number;
  expiry: string;
  strike: number;
  optionType: string;
}

// ─── Expiry format helpers ────────────────────────────────────────────────────
const MONTH_NAMES = ["JAN","FEB","MAR","APR","MAY","JUN","JUL","AUG","SEP","OCT","NOV","DEC"];

/**
 * Parse a Fyers-style expiry string into { day, monthName, year }
 * Supports: "26AUG26" (monthly) or "2682601" (weekly-style yydmmdd) 
 * Actually from FyersDataPollingService:
 *   weekly format: yy + monthChar + dd  e.g. "2682521" → yy=26, month=8(AUG), dd=21
 *   monthly format: yyMMM.toUpper()    e.g. "26AUG"
 */
function getLastThursdayOfMonth(year: number, month: number): number {
  const lastDay = new Date(year, month, 0).getDate();
  const date = new Date(year, month - 1, lastDay);
  const dayOfWeek = date.getDay(); // 0 = Sun, 4 = Thu
  const diff = (dayOfWeek - 4 + 7) % 7;
  return lastDay - diff;
}

function parseExpiry(expiryStr: string): { day: string; month: string; year: string } {
  // Strip any accidental 'NIFTY' prefix that slips through from old cached data
  let s = expiryStr;
  if (s.startsWith("NIFTY")) s = s.substring(5);

  // Monthly format: 5 chars like "26AUG" (yy + MMM)
  if (/^\d{2}[A-Z]{3}$/.test(s)) {
    const yearShort = s.substring(0, 2);
    const mon = s.substring(2, 5);
    const monthIdx = MONTH_NAMES.indexOf(mon);
    const fullYear = 2000 + parseInt(yearShort, 10);
    const lastThurs = monthIdx >= 0 ? getLastThursdayOfMonth(fullYear, monthIdx + 1) : 0;
    const day = lastThurs > 0 ? String(lastThurs).padStart(2, "0") : "";
    return { day, month: mon, year: yearShort };
  }
  // Weekly format: 5 chars like "26821" (yy + monthChar + dd)
  if (/^\d{2}[0-9ONDond]\d{2}$/.test(s)) {
    const year = s.substring(0, 2);
    const mChar = s.substring(2, 3).toUpperCase();
    const day = s.substring(3, 5);
    let monthIdx = parseInt(mChar, 10);
    if (mChar === "O") monthIdx = 10;
    else if (mChar === "N") monthIdx = 11;
    else if (mChar === "D") monthIdx = 12;
    const monthName = MONTH_NAMES[(monthIdx - 1) % 12] || mChar;
    return { day, month: monthName, year };
  }
  // Fallback: return raw (minus any NIFTY prefix stripped above)
  return { day: "", month: s, year: "" };
}

/**
 * Build a human-readable display name for an option strike.
 * Format: "NIFTY {DD} {MMM} {YY} {Strike} {CE/PE}"
 * e.g.   "NIFTY 21 AUG 26 23850 CE"
 */
function buildDisplayName(expiry: string, strike: number, type: string): string {
  const { day, month, year } = parseExpiry(expiry);
  const parts = ["NIFTY", day, month, year, String(strike), type].filter(Boolean);
  return parts.join(" ");
}

// ─── Parse full option chain into flat instrument list ────────────────────────
function parseOptionChain(apiData: any): ParsedInstrument[] {
  const instruments: ParsedInstrument[] = [];
  
  // The backend returns: { records: { underlyingValue, data: [...] } }
  // So apiData (which is data.options from /api/marketdata/all) has .records.data
  const records: any[] = apiData?.records?.data || apiData?.data || [];

  for (const row of records) {
    const strike: number = row.strikePrice ?? 0;
    const rawExpiry: string = row.expiryDate ?? "";
    if (!strike || !rawExpiry) continue;

    // Strip any NIFTY prefix that leaked into the expiry field from old parser
    const expiry = rawExpiry.startsWith("NIFTY") ? rawExpiry.substring(5) : rawExpiry;

    if (row.CE) {
      const symbol = `NIFTY${expiry}${strike}CE`;
      instruments.push({
        symbol,
        displayName: buildDisplayName(expiry, strike, "CE"),
        price: row.CE.lastPrice ?? 0,
        change: row.CE.change ?? 0,
        expiry,
        strike,
        optionType: "CE",
      });
    }
    if (row.PE) {
      const symbol = `NIFTY${expiry}${strike}PE`;
      instruments.push({
        symbol,
        displayName: buildDisplayName(expiry, strike, "PE"),
        price: row.PE.lastPrice ?? 0,
        change: row.PE.change ?? 0,
        expiry,
        strike,
        optionType: "PE",
      });
    }
  }

  return instruments;
}

// ─── Component ────────────────────────────────────────────────────────────────
export default function WatchlistPage() {
  const [watchlists, setWatchlists] = useState<Watchlist[]>([
    { id: "wl_1", name: "Nifty Options", items: [] }
  ]);
  const [activeWatchlistId, setActiveWatchlistId] = useState<string>("wl_1");
  const [isLoaded, setIsLoaded] = useState(false);

  const [search, setSearch] = useState("");
  const [searchFocused, setSearchFocused] = useState(false);
  const [liveInstruments, setLiveInstruments] = useState<ParsedInstrument[]>([]);
  const [niftySpot, setNiftySpot] = useState<number | null>(null);
  const [dataLoading, setDataLoading] = useState(true);
  const [selectedInstrument, setSelectedInstrument] = useState<WatchlistItem | null>(null);
  const searchRef = useRef<HTMLDivElement>(null);

  // ── Persist watchlists ─────────────────────────────────────────────────────
  useEffect(() => {
    const savedLists = localStorage.getItem("stoictrade_watchlists_v2");
    const savedActiveId = localStorage.getItem("stoictrade_active_watchlist");
    if (savedLists) {
      try { setWatchlists(JSON.parse(savedLists)); } catch {}
    }
    if (savedActiveId) setActiveWatchlistId(savedActiveId);
    setIsLoaded(true);
  }, []);

  useEffect(() => {
    if (isLoaded) {
      localStorage.setItem("stoictrade_watchlists_v2", JSON.stringify(watchlists));
      localStorage.setItem("stoictrade_active_watchlist", activeWatchlistId);
    }
  }, [watchlists, activeWatchlistId, isLoaded]);

  // ── Click outside to close search dropdown ─────────────────────────────────
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (searchRef.current && !searchRef.current.contains(e.target as Node)) {
        setSearchFocused(false);
      }
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  // ── Poll market data every 3s ──────────────────────────────────────────────
  useEffect(() => {
    const fetchData = async () => {
      try {
        const res = await fetchWithAuth("/api/marketdata/all");
        if (!res.ok) return;

        const data = await res.json();

        // Parse spot price
        const spot = data.options?.records?.underlyingValue
          ?? data.options?.underlyingValue
          ?? (data.spots?.NIFTY?.lastPrice || null);
        setNiftySpot(spot);

        // Parse ALL option chain records (not just ATM ±5)
        const optionsPayload = data.options;
        const instruments = parseOptionChain(optionsPayload);
        setLiveInstruments(instruments);
        setDataLoading(false);

        // Update prices of existing watchlist items
        setWatchlists(prev => prev.map(wl => ({
          ...wl,
          items: wl.items.map(item => {
            const live = instruments.find(i => i.symbol === item.symbol);
            return live ? { ...item, price: live.price, change: live.change } : item;
          })
        })));
      } catch (e) {
        console.error("Watchlist: failed to fetch live data", e);
        setDataLoading(false);
      }
    };

    fetchData();
    const interval = setInterval(fetchData, 3000);
    return () => clearInterval(interval);
  }, []);

  const activeWatchlist = watchlists.find(w => w.id === activeWatchlistId);
  const alreadyAdded = new Set(activeWatchlist?.items.map(i => i.symbol) || []);

  // ── Search / suggestion logic ──────────────────────────────────────────────
  const query = search.trim().toLowerCase();

  // If search is empty and bar is focused, show ATM ± 3 strikes as suggestions
  const atmStrike = niftySpot ? Math.round(niftySpot / 50) * 50 : 0;

    const searchResults: ParsedInstrument[] = (() => {
    if (!searchFocused) return [];
    if (query === "") {
      // Show ATM ± 6 suggestions if data loaded
      if (!atmStrike || liveInstruments.length === 0) return [];
      return liveInstruments
        .filter(i => Math.abs(i.strike - atmStrike) <= 300 && !alreadyAdded.has(i.symbol))
        .sort((a, b) => {
          const da = Math.abs(a.strike - atmStrike);
          const db = Math.abs(b.strike - atmStrike);
          if (da !== db) return da - db;
          return a.optionType.localeCompare(b.optionType);
        })
        .slice(0, 24);
    }
    // Text search: match against displayName or symbol
    return liveInstruments
      .filter(i =>
        (i.displayName.toLowerCase().includes(query) || i.symbol.toLowerCase().includes(query)) &&
        !alreadyAdded.has(i.symbol)
      )
      .slice(0, 50);
  })();

  // ── Actions ────────────────────────────────────────────────────────────────
  const createWatchlist = () => {
    const name = prompt("Enter new watchlist name:");
    if (name?.trim()) {
      const newWl: Watchlist = { id: `wl_${Date.now()}`, name: name.trim(), items: [] };
      setWatchlists([...watchlists, newWl]);
      setActiveWatchlistId(newWl.id);
    }
  };

  const deleteWatchlist = (id: string) => {
    if (watchlists.length === 1) { alert("You must have at least one watchlist."); return; }
    if (confirm("Delete this watchlist?")) {
      const newLists = watchlists.filter(w => w.id !== id);
      setWatchlists(newLists);
      if (activeWatchlistId === id) setActiveWatchlistId(newLists[0].id);
    }
  };

  const addInstrument = (inst: ParsedInstrument) => {
    if (!activeWatchlist) return;
    const newItem: WatchlistItem = {
      id: `item_${Date.now()}`,
      symbol: inst.symbol,
      displayName: inst.displayName,
      price: inst.price,
      change: inst.change,
    };
    setWatchlists(watchlists.map(wl =>
      wl.id === activeWatchlistId ? { ...wl, items: [...wl.items, newItem] } : wl
    ));
    setSearch("");
    setSearchFocused(false);
  };

  const removeInstrument = (e: React.MouseEvent, itemId: string) => {
    e.stopPropagation();
    setWatchlists(watchlists.map(wl =>
      wl.id === activeWatchlistId ? { ...wl, items: wl.items.filter(i => i.id !== itemId) } : wl
    ));
  };

  const showDropdown = searchResults.length > 0;

  return (
    <div className="p-6 md:p-10 max-w-5xl mx-auto space-y-6">
      {/* Header */}
      <header className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
        <div>
          <h1 className="text-3xl font-bold text-slate-900 dark:text-white">Watchlists</h1>
          <p className="text-slate-500 mt-1">Organize and monitor your favorite instruments</p>
        </div>
        <div className="flex items-center gap-3">
          {niftySpot && (
            <div className="text-right px-3 py-2 bg-slate-50 dark:bg-slate-800 rounded-xl border border-slate-200 dark:border-slate-700">
              <p className="text-xs text-slate-500 font-semibold">NIFTY</p>
              <p className="font-bold text-slate-900 dark:text-white">₹ {niftySpot.toFixed(2)}</p>
            </div>
          )}
          <button
            onClick={createWatchlist}
            className="flex items-center gap-2 px-4 py-2 bg-primary hover:bg-primary-hover text-white rounded-lg font-medium transition-colors shadow-sm"
          >
            <FolderPlus className="w-5 h-5" />
            New Watchlist
          </button>
        </div>
      </header>

      {/* Watchlist Tabs */}
      <div className="flex overflow-x-auto hide-scrollbar gap-2 pb-2">
        {watchlists.map(wl => (
          <div key={wl.id} className="flex items-center">
            <button
              onClick={() => setActiveWatchlistId(wl.id)}
              className={`px-5 py-2.5 whitespace-nowrap rounded-l-lg font-bold transition-colors ${
                activeWatchlistId === wl.id
                  ? "bg-slate-800 text-white dark:bg-white dark:text-slate-900 shadow-md"
                  : "bg-surface text-slate-600 dark:text-slate-400 border border-slate-200 dark:border-slate-800 hover:bg-slate-50 dark:hover:bg-slate-800/50"
              }`}
            >
              {wl.name}
            </button>
            <button
              onClick={() => deleteWatchlist(wl.id)}
              className={`px-3 py-2.5 rounded-r-lg transition-colors border-y border-r border-slate-200 dark:border-slate-800 ${
                activeWatchlistId === wl.id
                  ? "bg-slate-800 text-slate-400 hover:text-danger dark:bg-white dark:hover:bg-slate-100"
                  : "bg-surface text-slate-400 hover:text-danger hover:bg-slate-50 dark:hover:bg-slate-800/50"
              }`}
            >
              <Trash2 className="w-4 h-4" />
            </button>
          </div>
        ))}
      </div>

      {/* Search and Add */}
      <div className="relative z-10" ref={searchRef}>
        <Search className="w-5 h-5 absolute left-4 top-1/2 -translate-y-1/2 text-slate-400 pointer-events-none" />
        {dataLoading && (
          <Loader2 className="w-4 h-4 absolute right-4 top-1/2 -translate-y-1/2 text-slate-400 animate-spin" />
        )}
        <input
          type="text"
          placeholder={`Search NIFTY options to add to "${activeWatchlist?.name}"…`}
          value={search}
          onChange={e => setSearch(e.target.value)}
          onFocus={() => setSearchFocused(true)}
          className="w-full pl-12 pr-10 py-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-surface focus:outline-none focus:ring-2 focus:ring-primary shadow-sm"
        />

        {/* Dropdown */}
        {showDropdown && (
          <div className="absolute top-full left-0 right-0 mt-2 bg-surface border border-slate-200 dark:border-slate-800 rounded-xl shadow-xl overflow-hidden max-h-72 overflow-y-auto z-20">
            {query === "" && (
              <div className="px-4 py-2 text-xs text-slate-500 font-semibold uppercase tracking-wider border-b border-slate-100 dark:border-slate-800 bg-slate-50 dark:bg-slate-900/50">
                ATM Strikes (NIFTY {atmStrike}) — click to add
              </div>
            )}
            {searchResults.map(inst => (
              <div
                key={inst.symbol}
                className="flex items-center justify-between px-4 py-3 hover:bg-slate-50 dark:hover:bg-slate-800/50 border-b border-slate-100 dark:border-slate-800 last:border-0 cursor-pointer"
                onClick={() => addInstrument(inst)}
              >
                <div>
                  <span className="font-bold text-slate-900 dark:text-white text-sm">{inst.displayName}</span>
                  <span className={`ml-2 text-xs font-semibold ${inst.optionType === "CE" ? "text-green-500" : "text-red-500"}`}>
                    {inst.optionType}
                  </span>
                </div>
                <div className="flex items-center gap-4">
                  <div className="text-right">
                    <p className="font-medium text-sm text-slate-800 dark:text-white">₹{inst.price.toFixed(2)}</p>
                    <p className={`text-xs ${inst.change >= 0 ? "text-green-500" : "text-red-500"}`}>
                      {inst.change >= 0 ? "+" : ""}{inst.change.toFixed(2)}%
                    </p>
                  </div>
                  <button
                    className="p-1.5 text-primary hover:bg-blue-50 dark:hover:bg-blue-900/30 rounded-lg transition-colors"
                    onClick={e => { e.stopPropagation(); addInstrument(inst); }}
                  >
                    <Plus className="w-4 h-4" />
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Disclaimer */}
      <div className="flex gap-2 items-start p-4 bg-blue-50 dark:bg-blue-900/20 text-blue-700 dark:text-blue-300 rounded-xl text-sm">
        <Info className="w-5 h-5 shrink-0 mt-0.5" />
        <p>
          <strong>Risk Management Active:</strong> Only NIFTY Index Options and Equity Stocks (EQ) are permitted to trade.
        </p>
      </div>

      {/* Watchlist Items */}
      <div className="bg-surface border border-slate-200 dark:border-slate-800 rounded-2xl overflow-hidden shadow-sm min-h-[300px]">
        {activeWatchlist?.items.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-[300px] text-slate-500">
            <FolderPlus className="w-12 h-12 mb-3 text-slate-300 dark:text-slate-700" />
            <p>This watchlist is empty.</p>
            <p className="text-sm mt-1">Search above to add NIFTY option strikes.</p>
          </div>
        ) : (
          activeWatchlist?.items.map(item => (
            <div
              key={item.id}
              onClick={() => setSelectedInstrument(item)}
              className="group flex items-center justify-between p-4 border-b border-slate-100 dark:border-slate-800 hover:bg-slate-50 dark:hover:bg-slate-800/50 cursor-pointer transition-colors last:border-0 relative"
            >
              <div>
                <h3 className="font-bold text-slate-900 dark:text-white">{item.displayName || item.symbol}</h3>
                <p className="text-xs text-slate-500">NSE · Option</p>
              </div>
              <div className="flex items-center gap-6">
                <div className="text-right transition-transform group-hover:-translate-x-8">
                  <p className="font-medium text-slate-900 dark:text-white">₹ {item.price.toFixed(2)}</p>
                  <p className={`text-sm flex items-center justify-end gap-1 ${item.change >= 0 ? "text-green-500" : "text-danger"}`}>
                    {item.change >= 0 ? <TrendingUp className="w-3 h-3" /> : <TrendingDown className="w-3 h-3" />}
                    {Math.abs(item.change).toFixed(2)}%
                  </p>
                </div>
                <button
                  onClick={(e) => removeInstrument(e, item.id)}
                  className="absolute right-4 p-2 text-slate-400 hover:text-danger hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg opacity-0 group-hover:opacity-100 transition-all"
                  title="Remove from watchlist"
                >
                  <Trash2 className="w-5 h-5" />
                </button>
              </div>
            </div>
          ))
        )}
      </div>

      {selectedInstrument && (
        <OrderModal
          instrument={selectedInstrument.symbol}
          displayName={selectedInstrument.displayName}
          price={selectedInstrument.price}
          change={selectedInstrument.change}
          onClose={() => setSelectedInstrument(null)}
        />
      )}
    </div>
  );
}
