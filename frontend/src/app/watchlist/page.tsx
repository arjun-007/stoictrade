"use client";

import { useState } from "react";
import { Search, TrendingUp, TrendingDown, Info, Plus, Trash2, FolderPlus } from "lucide-react";
import OrderModal from "./OrderModal";

interface WatchlistItem {
  id: string;
  symbol: string;
  price: number;
  change: number;
}

interface Watchlist {
  id: string;
  name: string;
  items: WatchlistItem[];
}

const INITIAL_WATCHLISTS: Watchlist[] = [
  {
    id: "wl_1",
    name: "Nifty Options",
    items: [
      { id: "1", symbol: "NIFTY24MAY22000CE", price: 345.20, change: 15.4 },
      { id: "2", symbol: "NIFTY24MAY22000PE", price: 120.10, change: -10.2 },
    ]
  },
  {
    id: "wl_2",
    name: "Core Equities",
    items: [
      { id: "3", symbol: "NIFTY-EQ", price: 22150.45, change: 1.2 },
      { id: "4", symbol: "HDFCBANK-EQ", price: 1520.00, change: 0.5 },
    ]
  }
];

// Mock database of all available instruments to search and add
const ALL_INSTRUMENTS = [
  { symbol: "NIFTY24MAY22000CE", price: 345.20, change: 15.4 },
  { symbol: "NIFTY24MAY22000PE", price: 120.10, change: -10.2 },
  { symbol: "NIFTY24MAY22100CE", price: 280.50, change: 12.1 },
  { symbol: "NIFTY24MAY22100PE", price: 150.00, change: -8.5 },
  { symbol: "NIFTY-EQ", price: 22150.45, change: 1.2 },
  { symbol: "HDFCBANK-EQ", price: 1520.00, change: 0.5 },
  { symbol: "RELIANCE-EQ", price: 2950.00, change: 2.1 },
  { symbol: "BANKNIFTY-EQ", price: 47500.00, change: 0.8 },
];

export default function WatchlistPage() {
  const [watchlists, setWatchlists] = useState<Watchlist[]>(INITIAL_WATCHLISTS);
  const [activeWatchlistId, setActiveWatchlistId] = useState<string>(INITIAL_WATCHLISTS[0].id);
  
  const [search, setSearch] = useState("");
  const [selectedInstrument, setSelectedInstrument] = useState<WatchlistItem | null>(null);

  const activeWatchlist = watchlists.find(w => w.id === activeWatchlistId);

  // Filter available instruments for the search dropdown
  const searchResults = search.trim() === "" ? [] : ALL_INSTRUMENTS.filter(inst => 
    inst.symbol.toLowerCase().includes(search.toLowerCase()) && 
    !activeWatchlist?.items.find(item => item.symbol === inst.symbol)
  );

  const createWatchlist = () => {
    const name = prompt("Enter new watchlist name:");
    if (name && name.trim()) {
      const newWl: Watchlist = {
        id: `wl_${Date.now()}`,
        name: name.trim(),
        items: []
      };
      setWatchlists([...watchlists, newWl]);
      setActiveWatchlistId(newWl.id);
    }
  };

  const deleteWatchlist = (id: string) => {
    if (watchlists.length === 1) {
      alert("You must have at least one watchlist.");
      return;
    }
    if (confirm("Are you sure you want to delete this watchlist?")) {
      const newLists = watchlists.filter(w => w.id !== id);
      setWatchlists(newLists);
      if (activeWatchlistId === id) {
        setActiveWatchlistId(newLists[0].id);
      }
    }
  };

  const addInstrumentToWatchlist = (symbol: string) => {
    if (!activeWatchlist) return;
    
    const instrumentTemplate = ALL_INSTRUMENTS.find(i => i.symbol === symbol);
    if (!instrumentTemplate) return;

    const newItem: WatchlistItem = {
      id: `item_${Date.now()}`,
      ...instrumentTemplate
    };

    setWatchlists(watchlists.map(wl => 
      wl.id === activeWatchlistId 
        ? { ...wl, items: [...wl.items, newItem] } 
        : wl
    ));
    setSearch(""); // Clear search after adding
  };

  const removeInstrument = (e: React.MouseEvent, itemId: string) => {
    e.stopPropagation(); // Prevent opening the order modal
    setWatchlists(watchlists.map(wl => 
      wl.id === activeWatchlistId 
        ? { ...wl, items: wl.items.filter(i => i.id !== itemId) } 
        : wl
    ));
  };

  return (
    <div className="p-6 md:p-10 max-w-5xl mx-auto space-y-6">
      <header className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
        <div>
          <h1 className="text-3xl font-bold text-slate-900 dark:text-white">Watchlists</h1>
          <p className="text-slate-500 mt-1">Organize and monitor your favorite instruments</p>
        </div>
        <button 
          onClick={createWatchlist}
          className="flex items-center gap-2 px-4 py-2 bg-primary hover:bg-primary-hover text-white rounded-lg font-medium transition-colors shadow-sm"
        >
          <FolderPlus className="w-5 h-5" />
          New Watchlist
        </button>
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
      <div className="relative z-10">
        <Search className="w-5 h-5 absolute left-4 top-1/2 -translate-y-1/2 text-slate-400" />
        <input 
          type="text" 
          placeholder={`Search to add instruments to "${activeWatchlist?.name}"...`} 
          value={search}
          onChange={e => setSearch(e.target.value)}
          className="w-full pl-12 pr-4 py-3 rounded-xl border border-slate-200 dark:border-slate-700 bg-surface focus:outline-none focus:ring-2 focus:ring-primary shadow-sm"
        />
        
        {/* Search Results Dropdown */}
        {searchResults.length > 0 && (
          <div className="absolute top-full left-0 right-0 mt-2 bg-surface border border-slate-200 dark:border-slate-800 rounded-xl shadow-xl overflow-hidden max-h-60 overflow-y-auto">
            {searchResults.map(inst => (
              <div key={inst.symbol} className="flex items-center justify-between p-3 hover:bg-slate-50 dark:hover:bg-slate-800/50 border-b border-slate-100 dark:border-slate-800 last:border-0">
                <span className="font-bold">{inst.symbol}</span>
                <button 
                  onClick={() => addInstrumentToWatchlist(inst.symbol)}
                  className="p-2 text-primary hover:bg-blue-50 dark:hover:bg-blue-900/30 rounded-lg transition-colors flex items-center gap-1 font-semibold text-sm"
                >
                  <Plus className="w-4 h-4" /> Add
                </button>
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
            <p className="text-sm mt-1">Search above to add instruments.</p>
          </div>
        ) : (
          activeWatchlist?.items.map(item => (
            <div 
              key={item.id}
              onClick={() => setSelectedInstrument(item)}
              className="group flex items-center justify-between p-4 border-b border-slate-100 dark:border-slate-800 hover:bg-slate-50 dark:hover:bg-slate-800/50 cursor-pointer transition-colors last:border-0 relative"
            >
              <div>
                <h3 className="font-bold text-slate-900 dark:text-white">{item.symbol}</h3>
                <p className="text-xs text-slate-500">NSE</p>
              </div>
              <div className="flex items-center gap-6">
                <div className="text-right transition-transform group-hover:-translate-x-8">
                  <p className="font-medium text-slate-900 dark:text-white">₹ {item.price.toFixed(2)}</p>
                  <p className={`text-sm flex items-center justify-end gap-1 ${item.change >= 0 ? "text-green-500" : "text-danger"}`}>
                    {item.change >= 0 ? <TrendingUp className="w-3 h-3" /> : <TrendingDown className="w-3 h-3" />}
                    {Math.abs(item.change)}%
                  </p>
                </div>
                {/* Delete button (hidden until hover) */}
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
          price={selectedInstrument.price}
          change={selectedInstrument.change}
          onClose={() => setSelectedInstrument(null)} 
        />
      )}
    </div>
  );
}
