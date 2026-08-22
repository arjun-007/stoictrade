"use client";

import { useState, useEffect } from "react";
import { 
  Layers, 
  Plus, 
  Play, 
  Square, 
  Trash2, 
  Edit3, 
  ShieldCheck, 
  Sparkles, 
  CheckSquare, 
  Square as SquareIcon,
  X,
  Sliders,
  CheckCircle2
} from "lucide-react";
import { fetchWithAuth } from "@/lib/api";

interface StrategyItem {
  id: number;
  strategyName: string;
  isEnabled: boolean;
}

interface StrategyGroup {
  id: number;
  name: string;
  description: string;
  isEnabled: boolean;
  strategyIdsJson: string; // e.g. "[1, 2, 7]"
  consensusRule: string; // "Majority" | "Unanimous" | "Any"
  minAgreeingStrategies: number;
  operatingMode: string; // "Automatic" | "ApprovalRequired" | "SignalOnly"
  perTradeStopLossPoint: number;
  perTradeGainPoint: number;
  trailingStopLossPoint: number;
  timeframeMinutes: number;
}

export default function StrategyGroupsPage() {
  const [groups, setGroups] = useState<StrategyGroup[]>([]);
  const [strategies, setStrategies] = useState<StrategyItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingGroup, setEditingGroup] = useState<StrategyGroup | null>(null);

  // Form state
  const [formName, setFormName] = useState("");
  const [formDescription, setFormDescription] = useState("");
  const [formStrategyIds, setFormStrategyIds] = useState<number[]>([]);
  const [formConsensusRule, setFormConsensusRule] = useState("Majority");
  const [formMinAgreeing, setFormMinAgreeing] = useState(2);
  const [formOperatingMode, setFormOperatingMode] = useState("ApprovalRequired");
  const [formStopLoss, setFormStopLoss] = useState(12);
  const [formGain, setFormGain] = useState(35);
  const [formTrailingSl, setFormTrailingSl] = useState(8);
  const [formTimeframe, setFormTimeframe] = useState(5);

  const loadData = async () => {
    try {
      setLoading(true);
      const [groupsRes, stratsRes] = await Promise.all([
        fetchWithAuth("/api/strategygroups"),
        fetchWithAuth("/api/strategyconfig")
      ]);

      if (groupsRes.ok) {
        const data = await groupsRes.json();
        setGroups(data);
      }
      if (stratsRes.ok) {
        const data = await stratsRes.json();
        setStrategies(data);
      }
    } catch (err) {
      console.error("Error loading strategy groups data:", err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadData();
  }, []);

  const openCreateModal = () => {
    setEditingGroup(null);
    setFormName("");
    setFormDescription("");
    setFormStrategyIds(strategies.slice(0, 2).map(s => s.id));
    setFormConsensusRule("Majority");
    setFormMinAgreeing(2);
    setFormOperatingMode("ApprovalRequired");
    setFormStopLoss(12);
    setFormGain(35);
    setFormTrailingSl(8);
    setFormTimeframe(5);
    setIsModalOpen(true);
  };

  const openEditModal = (group: StrategyGroup) => {
    setEditingGroup(group);
    setFormName(group.name);
    setFormDescription(group.description);
    let ids: number[] = [];
    try {
      ids = JSON.parse(group.strategyIdsJson) || [];
    } catch {
      ids = [];
    }
    setFormStrategyIds(ids);
    setFormConsensusRule(group.consensusRule);
    setFormMinAgreeing(group.minAgreeingStrategies);
    setFormOperatingMode(group.operatingMode);
    setFormStopLoss(group.perTradeStopLossPoint);
    setFormGain(group.perTradeGainPoint);
    setFormTrailingSl(group.trailingStopLossPoint);
    setFormTimeframe(group.timeframeMinutes);
    setIsModalOpen(true);
  };

  const toggleStrategySelection = (id: number) => {
    if (formStrategyIds.includes(id)) {
      setFormStrategyIds(formStrategyIds.filter(x => x !== id));
    } else {
      setFormStrategyIds([...formStrategyIds, id]);
    }
  };

  const saveGroup = async () => {
    if (!formName.trim()) {
      alert("Please provide a group name.");
      return;
    }
    if (formStrategyIds.length === 0) {
      alert("Please select at least one strategy for this group.");
      return;
    }

    const payload = {
      name: formName.trim(),
      description: formDescription.trim(),
      isEnabled: editingGroup ? editingGroup.isEnabled : false,
      strategyIdsJson: JSON.stringify(formStrategyIds),
      consensusRule: formConsensusRule,
      minAgreeingStrategies: formMinAgreeing,
      operatingMode: formOperatingMode,
      perTradeStopLossPoint: Number(formStopLoss),
      perTradeGainPoint: Number(formGain),
      trailingStopLossPoint: Number(formTrailingSl),
      timeframeMinutes: Number(formTimeframe)
    };

    try {
      if (editingGroup) {
        await fetchWithAuth(`/api/strategygroups/${editingGroup.id}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload)
        });
      } else {
        await fetchWithAuth("/api/strategygroups", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload)
        });
      }
      setIsModalOpen(false);
      loadData();
    } catch (err) {
      console.error("Failed to save strategy group:", err);
    }
  };

  const toggleGroupActive = async (id: number) => {
    try {
      await fetchWithAuth(`/api/strategygroups/${id}/toggle`, { method: "POST" });
      setGroups(groups.map(g => g.id === id ? { ...g, isEnabled: !g.isEnabled } : g));
    } catch (err) {
      console.error("Failed to toggle group:", err);
    }
  };

  const deleteGroup = async (id: number, name: string) => {
    if (!confirm(`Are you sure you want to delete the strategy group "${name}"?`)) return;
    try {
      await fetchWithAuth(`/api/strategygroups/${id}`, { method: "DELETE" });
      setGroups(groups.filter(g => g.id !== id));
    } catch (err) {
      console.error("Failed to delete group:", err);
    }
  };

  const applyPreset = async (presetName: string, stratNames: string[], rule: string, desc: string) => {
    const matchedIds = strategies
      .filter(s => stratNames.some(name => s.strategyName.toLowerCase().includes(name.toLowerCase())))
      .map(s => s.id);

    if (matchedIds.length === 0) {
      alert("Could not map preset strategies.");
      return;
    }

    const payload = {
      name: presetName,
      description: desc,
      isEnabled: false,
      strategyIdsJson: JSON.stringify(matchedIds),
      consensusRule: rule,
      minAgreeingStrategies: Math.min(2, matchedIds.length),
      operatingMode: "ApprovalRequired",
      perTradeStopLossPoint: 12,
      perTradeGainPoint: 35,
      trailingStopLossPoint: 8,
      timeframeMinutes: 5
    };

    try {
      await fetchWithAuth("/api/strategygroups", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      });
      loadData();
    } catch (err) {
      console.error("Failed to apply preset:", err);
    }
  };

  const getMemberStrategyNames = (json: string): string[] => {
    try {
      const ids: number[] = JSON.parse(json) || [];
      return ids
        .map(id => strategies.find(s => s.id === id)?.strategyName)
        .filter((n): n is string => !!n);
    } catch {
      return [];
    }
  };

  if (loading) {
    return (
      <div className="p-10 flex items-center justify-center text-slate-500">
        <div className="animate-spin mr-3"><Layers className="w-6 h-6 text-primary" /></div>
        Loading Strategy Groups...
      </div>
    );
  }

  return (
    <div className="p-6 md:p-10 max-w-7xl mx-auto space-y-8">
      {/* Header */}
      <div className="flex flex-col lg:flex-row lg:items-center justify-between gap-4">
        <div>
          <h1 className="text-3xl font-bold text-slate-900 dark:text-white flex items-center gap-3">
            <Layers className="w-8 h-8 text-primary" />
            Strategy Groups & Squads
          </h1>
          <p className="text-slate-500 mt-1">
            Group strategies into teams that require multi-strategy consensus before executing high-confidence trades.
          </p>
        </div>
        <button
          onClick={openCreateModal}
          className="flex items-center gap-2 px-5 py-2.5 bg-primary text-white rounded-xl font-semibold shadow-md shadow-primary/20 hover:bg-primary/90 transition-all self-start"
        >
          <Plus className="w-5 h-5" /> Create Strategy Group
        </button>
      </div>

      {/* Preset Templates Bar */}
      <div className="bg-gradient-to-r from-primary/5 via-primary/10 to-transparent p-5 rounded-2xl border border-primary/15">
        <div className="flex items-center gap-2 mb-3">
          <Sparkles className="w-4 h-4 text-primary" />
          <h3 className="text-xs font-bold uppercase tracking-wider text-primary">1-Click High-Confluence Presets</h3>
        </div>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
          <button
            onClick={() => applyPreset(
              "Morning Momentum Trap Squad",
              ["Opening Range Breakout", "Wyckoff Spring"],
              "Majority",
              "Captures opening breakouts or immediate fakeout absorption sweeps with tight stop loss."
            )}
            className="text-left p-3.5 rounded-xl bg-surface hover:border-primary/50 border border-slate-200 dark:border-slate-800 transition-all shadow-sm group"
          >
            <div className="font-bold text-sm text-slate-900 dark:text-white group-hover:text-primary transition-colors">
              🌅 Morning Momentum Trap
            </div>
            <div className="text-xs text-slate-500 mt-1">ORB + Wyckoff Spring (Majority Consensus)</div>
          </button>

          <button
            onClick={() => applyPreset(
              "Institutional Trend & Mitigation",
              ["Supertrend Rider", "EMA Pullback", "Fair Value Gap"],
              "Majority",
              "Rides established intraday trends and enters when price taps unfilled Fair Value Gaps."
            )}
            className="text-left p-3.5 rounded-xl bg-surface hover:border-primary/50 border border-slate-200 dark:border-slate-800 transition-all shadow-sm group"
          >
            <div className="font-bold text-sm text-slate-900 dark:text-white group-hover:text-primary transition-colors">
              🏛️ Trend & Mitigation Syndicate
            </div>
            <div className="text-xs text-slate-500 mt-1">Supertrend + EMA Pullback + FVG (2+ Agree)</div>
          </button>

          <button
            onClick={() => applyPreset(
              "Volatility Explosion Unit",
              ["Bollinger Volatility Squeeze", "NR7 Breakout"],
              "Unanimous",
              "Requires extreme volatility compression and price range contraction before a massive breakout."
            )}
            className="text-left p-3.5 rounded-xl bg-surface hover:border-primary/50 border border-slate-200 dark:border-slate-800 transition-all shadow-sm group"
          >
            <div className="font-bold text-sm text-slate-900 dark:text-white group-hover:text-primary transition-colors">
              💥 Volatility Explosion Unit
            </div>
            <div className="text-xs text-slate-500 mt-1">Bollinger Squeeze + NR7 (Unanimous 100%)</div>
          </button>
        </div>
      </div>

      {/* Groups Grid */}
      <div className="grid grid-cols-1 xl:grid-cols-2 gap-6">
        {groups.map((group) => {
          const memberNames = getMemberStrategyNames(group.strategyIdsJson);
          return (
            <div 
              key={group.id} 
              className={`bg-surface p-6 rounded-2xl shadow-sm border transition-all flex flex-col justify-between ${
                group.isEnabled 
                  ? "border-primary/40 ring-1 ring-primary/20" 
                  : "border-slate-200 dark:border-slate-800"
              }`}
            >
              <div>
                {/* Header info */}
                <div className="flex items-start justify-between gap-3 mb-3">
                  <div>
                    <div className="flex items-center gap-3">
                      <h2 className="text-xl font-bold text-slate-900 dark:text-white">{group.name}</h2>
                      <span className={`px-3 py-0.5 text-xs font-bold uppercase tracking-wider rounded-full ${
                        group.isEnabled 
                          ? "bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-400 border border-green-200 dark:border-green-800" 
                          : "bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400 border border-slate-200 dark:border-slate-700"
                      }`}>
                        {group.isEnabled ? "Active Squad" : "Standby"}
                      </span>
                    </div>
                    {group.description && (
                      <p className="text-xs text-slate-500 mt-1 line-clamp-2">{group.description}</p>
                    )}
                  </div>
                  
                  {/* Toggle active */}
                  <button
                    onClick={() => toggleGroupActive(group.id)}
                    className={`flex items-center gap-2 px-3.5 py-1.5 rounded-lg text-xs font-semibold transition-all ${
                      group.isEnabled 
                        ? "bg-red-100 text-red-700 hover:bg-red-200 dark:bg-red-900/30 dark:text-red-400" 
                        : "bg-primary/10 text-primary hover:bg-primary/20"
                    }`}
                  >
                    {group.isEnabled ? <><Square className="w-3.5 h-3.5" /> Disable</> : <><Play className="w-3.5 h-3.5" /> Enable</>}
                  </button>
                </div>

                {/* Consensus & Operating Badge */}
                <div className="flex flex-wrap items-center gap-2 my-4">
                  <div className="px-2.5 py-1 rounded-lg text-xs font-semibold bg-primary/10 text-primary border border-primary/20 flex items-center gap-1.5">
                    <ShieldCheck className="w-3.5 h-3.5" />
                    Consensus: {group.consensusRule === "Majority" ? `Majority (${group.minAgreeingStrategies}+ Agree)` : group.consensusRule}
                  </div>
                  <div className="px-2.5 py-1 rounded-lg text-xs font-medium bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300">
                    Mode: {group.operatingMode}
                  </div>
                  <div className="px-2.5 py-1 rounded-lg text-xs font-medium bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-300">
                    {group.timeframeMinutes}m Timeframe
                  </div>
                </div>

                {/* Member Strategies Chips */}
                <div className="space-y-2 mt-4 pt-4 border-t border-slate-100 dark:border-slate-800">
                  <label className="block text-[11px] font-bold text-slate-400 uppercase tracking-wider">
                    Member Strategies ({memberNames.length})
                  </label>
                  <div className="flex flex-wrap gap-2">
                    {memberNames.length > 0 ? (
                      memberNames.map((name, idx) => (
                        <span 
                          key={idx} 
                          className="px-2.5 py-1 rounded-lg text-xs font-medium bg-slate-100 dark:bg-slate-800/80 text-slate-800 dark:text-slate-200 border border-slate-200 dark:border-slate-700 flex items-center gap-1.5"
                        >
                          <CheckCircle2 className="w-3.5 h-3.5 text-primary" />
                          {name}
                        </span>
                      ))
                    ) : (
                      <span className="text-xs text-red-500 italic">No member strategies selected</span>
                    )}
                  </div>
                </div>

                {/* Target & Stop Loss Summary */}
                <div className="grid grid-cols-3 gap-2 mt-4 p-3 rounded-xl bg-slate-50 dark:bg-slate-900/50 text-center text-xs">
                  <div>
                    <div className="text-slate-400">Stop Loss</div>
                    <div className="font-bold text-slate-700 dark:text-slate-200 mt-0.5">{group.perTradeStopLossPoint} pts</div>
                  </div>
                  <div>
                    <div className="text-slate-400">Target</div>
                    <div className="font-bold text-emerald-600 dark:text-emerald-400 mt-0.5">{group.perTradeGainPoint} pts</div>
                  </div>
                  <div>
                    <div className="text-slate-400">Trailing SL</div>
                    <div className="font-bold text-primary mt-0.5">{group.trailingStopLossPoint} pts</div>
                  </div>
                </div>
              </div>

              {/* Action Buttons */}
              <div className="flex items-center justify-end gap-2 mt-6 pt-4 border-t border-slate-100 dark:border-slate-800">
                <button
                  onClick={() => openEditModal(group)}
                  className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
                >
                  <Edit3 className="w-3.5 h-3.5" /> Edit Squad
                </button>
                <button
                  onClick={() => deleteGroup(group.id, group.name)}
                  className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium text-red-600 hover:bg-red-50 dark:hover:bg-red-950/30 transition-colors"
                >
                  <Trash2 className="w-3.5 h-3.5" /> Delete
                </button>
              </div>
            </div>
          );
        })}
      </div>

      {/* Modal for Creating / Editing Strategy Group */}
      {isModalOpen && (
        <div className="fixed inset-0 z-50 bg-black/60 backdrop-blur-sm flex items-center justify-center p-4">
          <div className="bg-surface border border-slate-200 dark:border-slate-800 w-full max-w-2xl rounded-3xl p-6 md:p-8 shadow-2xl space-y-6 max-h-[90vh] overflow-y-auto">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2.5">
                <Layers className="w-6 h-6 text-primary" />
                <h2 className="text-xl font-bold">{editingGroup ? "Edit Strategy Squad" : "Create New Strategy Squad"}</h2>
              </div>
              <button 
                onClick={() => setIsModalOpen(false)}
                className="p-2 text-slate-400 hover:text-slate-600 dark:hover:text-white rounded-lg"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            <div className="space-y-4">
              <div>
                <label className="block text-xs font-bold text-slate-500 uppercase tracking-wider mb-1">Squad Name</label>
                <input
                  type="text"
                  placeholder="e.g. Morning Alpha Squad"
                  value={formName}
                  onChange={e => setFormName(e.target.value)}
                  className="w-full px-4 py-2.5 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none text-sm font-medium"
                />
              </div>

              <div>
                <label className="block text-xs font-bold text-slate-500 uppercase tracking-wider mb-1">Description</label>
                <input
                  type="text"
                  placeholder="Brief description of the squad's edge..."
                  value={formDescription}
                  onChange={e => setFormDescription(e.target.value)}
                  className="w-full px-4 py-2.5 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none text-sm"
                />
              </div>

              {/* Strategy Multi-Select Checklist */}
              <div>
                <div className="flex items-center justify-between mb-2">
                  <label className="block text-xs font-bold text-slate-500 uppercase tracking-wider">
                    Select Member Strategies ({formStrategyIds.length} selected)
                  </label>
                </div>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-2 p-3 rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-50/50 dark:bg-slate-900/30 max-h-48 overflow-y-auto">
                  {strategies.map((strat) => {
                    const isSelected = formStrategyIds.includes(strat.id);
                    return (
                      <div
                        key={strat.id}
                        onClick={() => toggleStrategySelection(strat.id)}
                        className={`flex items-center gap-3 p-2.5 rounded-xl cursor-pointer transition-all border text-xs font-medium select-none ${
                          isSelected
                            ? "bg-primary/10 border-primary text-primary font-semibold"
                            : "bg-surface border-slate-200 dark:border-slate-700 hover:border-slate-300 dark:hover:border-slate-600 text-slate-700 dark:text-slate-300"
                        }`}
                      >
                        {isSelected ? <CheckSquare className="w-4 h-4 text-primary shrink-0" /> : <SquareIcon className="w-4 h-4 text-slate-400 shrink-0" />}
                        <span className="truncate">{strat.strategyName}</span>
                      </div>
                    );
                  })}
                </div>
              </div>

              {/* Consensus Rule & Settings */}
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <label className="block text-xs font-bold text-slate-500 uppercase tracking-wider mb-1">Consensus Rule</label>
                  <select
                    value={formConsensusRule}
                    onChange={e => setFormConsensusRule(e.target.value)}
                    className="w-full px-3 py-2.5 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none text-sm"
                  >
                    <option value="Majority">Majority Vote (At least N agree)</option>
                    <option value="Unanimous">Unanimous (100% of members must agree)</option>
                    <option value="Any">Any Strategy (First or highest priority)</option>
                  </select>
                </div>

                {formConsensusRule === "Majority" && (
                  <div>
                    <label className="block text-xs font-bold text-slate-500 uppercase tracking-wider mb-1">Min Strategies Agreeing</label>
                    <input
                      type="number"
                      min={1}
                      max={Math.max(1, formStrategyIds.length)}
                      value={formMinAgreeing}
                      onChange={e => setFormMinAgreeing(Number(e.target.value))}
                      className="w-full px-3 py-2.5 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none text-sm"
                    />
                  </div>
                )}

                <div>
                  <label className="block text-xs font-bold text-slate-500 uppercase tracking-wider mb-1">Operating Mode</label>
                  <select
                    value={formOperatingMode}
                    onChange={e => setFormOperatingMode(e.target.value)}
                    className="w-full px-3 py-2.5 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none text-sm"
                  >
                    <option value="ApprovalRequired">Approval Required</option>
                    <option value="Automatic">Fully Automatic</option>
                    <option value="SignalOnly">Signal Only</option>
                  </select>
                </div>

                <div>
                  <label className="block text-xs font-bold text-slate-500 uppercase tracking-wider mb-1">Timeframe (Minutes)</label>
                  <input
                    type="number"
                    value={formTimeframe}
                    onChange={e => setFormTimeframe(Number(e.target.value))}
                    className="w-full px-3 py-2.5 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none text-sm"
                  />
                </div>
              </div>

              {/* Risk Settings */}
              <div className="grid grid-cols-3 gap-3 pt-2">
                <div>
                  <label className="block text-xs font-bold text-slate-500 uppercase tracking-wider mb-1">Stop Loss (pts)</label>
                  <input
                    type="number"
                    value={formStopLoss}
                    onChange={e => setFormStopLoss(Number(e.target.value))}
                    className="w-full px-3 py-2.5 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none text-sm"
                  />
                </div>
                <div>
                  <label className="block text-xs font-bold text-slate-500 uppercase tracking-wider mb-1">Target Gain (pts)</label>
                  <input
                    type="number"
                    value={formGain}
                    onChange={e => setFormGain(Number(e.target.value))}
                    className="w-full px-3 py-2.5 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none text-sm"
                  />
                </div>
                <div>
                  <label className="block text-xs font-bold text-slate-500 uppercase tracking-wider mb-1">Trailing SL (pts)</label>
                  <input
                    type="number"
                    value={formTrailingSl}
                    onChange={e => setFormTrailingSl(Number(e.target.value))}
                    className="w-full px-3 py-2.5 rounded-xl border border-slate-200 dark:border-slate-700 bg-transparent focus:ring-2 focus:ring-primary outline-none text-sm"
                  />
                </div>
              </div>
            </div>

            {/* Modal Actions */}
            <div className="flex items-center justify-end gap-3 pt-4 border-t border-slate-100 dark:border-slate-800">
              <button
                onClick={() => setIsModalOpen(false)}
                className="px-5 py-2.5 rounded-xl font-semibold text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors text-sm"
              >
                Cancel
              </button>
              <button
                onClick={saveGroup}
                className="px-6 py-2.5 bg-primary text-white rounded-xl font-semibold shadow-md shadow-primary/20 hover:bg-primary/90 transition-all text-sm"
              >
                {editingGroup ? "Save Squad Changes" : "Create Squad"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
