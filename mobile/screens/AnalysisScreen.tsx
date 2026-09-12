import React, { useState, useEffect, useCallback } from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  TouchableOpacity,
  RefreshControl,
  Alert,
  Modal,
  TextInput,
  ActivityIndicator,
} from 'react-native';
import {
  Layers,
  Activity,
  ShieldCheck,
  Cpu,
  Trash2,
  Calendar,
  Plus,
  Sparkles,
  X,
  CheckSquare,
  Square as SquareIcon,
  Sliders,
} from 'lucide-react-native';
import * as Haptics from 'expo-haptics';
import { COLORS } from '../lib/theme';
import { apiClient } from '../lib/api';
import { SquadCard, StrategyGroupData } from '../components/SquadCard';
import { StrategyCard, StrategyItemData } from '../components/StrategyCard';
import { SignalLogCard, SignalLogData } from '../components/SignalLogCard';
import { ApprovalQueueCard, PendingSignal } from '../components/ApprovalQueueCard';

type AnalysisTab = 'approvals' | 'squads' | 'strategies' | 'signals';

export const AnalysisScreen: React.FC = () => {
  const [activeTab, setActiveTab] = useState<AnalysisTab>('approvals');
  const [squads, setSquads] = useState<StrategyGroupData[]>([]);
  const [strategies, setStrategies] = useState<StrategyItemData[]>([]);
  const [pendingApprovals, setPendingApprovals] = useState<PendingSignal[]>([]);
  const [signals, setSignals] = useState<SignalLogData[]>([]);
  const [dateFilter, setDateFilter] = useState<'today' | 'yesterday' | 'all'>('today');
  const [refreshing, setRefreshing] = useState(false);

  // Squad Modal & Editing state
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingSquad, setEditingSquad] = useState<StrategyGroupData | null>(null);
  const [formName, setFormName] = useState('');
  const [formDescription, setFormDescription] = useState('');
  const [formStrategyIds, setFormStrategyIds] = useState<number[]>([]);
  const [formConsensusRule, setFormConsensusRule] = useState<'Majority' | 'Unanimous' | 'Any'>('Majority');
  const [formMinAgreeing, setFormMinAgreeing] = useState('2');
  const [formOperatingMode, setFormOperatingMode] = useState<'Automatic' | 'ApprovalRequired' | 'SignalOnly'>('ApprovalRequired');
  const [formStopLoss, setFormStopLoss] = useState('12');
  const [formGain, setFormGain] = useState('35');
  const [formTrailingSl, setFormTrailingSl] = useState('8');
  const [formTimeframe, setFormTimeframe] = useState('5');
  const [seeding, setSeeding] = useState(false);
  const [savingSquad, setSavingSquad] = useState(false);

  const fetchData = useCallback(async () => {
    try {
      const [squadsRes, stratRes, signalsRes, approvalsRes] = await Promise.allSettled([
        apiClient.get('/api/strategygroups'),
        apiClient.get('/api/strategyconfig'),
        apiClient.get('/api/engine/signals'),
        apiClient.get('/api/approval/pending'),
      ]);

      if (squadsRes.status === 'fulfilled') {
        setSquads(squadsRes.value.data);
      }
      if (stratRes.status === 'fulfilled') {
        setStrategies(stratRes.value.data);
      }
      if (signalsRes.status === 'fulfilled' && Array.isArray(signalsRes.value.data)) {
        setSignals(signalsRes.value.data);
      }
      if (approvalsRes.status === 'fulfilled' && Array.isArray(approvalsRes.value.data)) {
        setPendingApprovals(approvalsRes.value.data);
      }
    } catch (err) {
      console.error('Error fetching analysis data:', err);
    }
  }, []);

  useEffect(() => {
    fetchData();
    const interval = setInterval(fetchData, 3000);
    return () => clearInterval(interval);
  }, [fetchData]);

  const onRefresh = async () => {
    setRefreshing(true);
    await fetchData();
    setRefreshing(false);
  };

  const handleApproveSignal = async (id: string) => {
    try {
      await apiClient.post(`/api/approval/approve/${id}`);
      Alert.alert('Trade Approved', 'Signal approved and executed.');
      fetchData();
    } catch (err: any) {
      Alert.alert('Error', err.response?.data?.message || 'Failed to approve signal');
    }
  };

  const handleDenySignal = async (id: string) => {
    try {
      await apiClient.post(`/api/approval/deny/${id}`);
      Alert.alert('Trade Denied', 'Signal removed from approval queue.');
      fetchData();
    } catch (err: any) {
      Alert.alert('Error', err.response?.data?.message || 'Failed to deny signal');
    }
  };

  const handleToggleSquad = async (id: number, currentValue: boolean) => {
    Haptics.selectionAsync();
    try {
      await apiClient.post(`/api/strategygroups/${id}/toggle`);
      setSquads((prev) =>
        prev.map((sq) => (sq.id === id ? { ...sq, isEnabled: !currentValue } : sq))
      );
    } catch (err: any) {
      Alert.alert('Error', err.response?.data?.error || 'Failed to toggle squad');
    }
  };

  const handleSeedDefaults = async () => {
    setSeeding(true);
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
    try {
      const res = await apiClient.post('/api/strategygroups/seed-defaults');
      Alert.alert('Squads Seeded', res.data?.message || 'Seeded 5 predefined high-confluence squads.');
      fetchData();
    } catch (err: any) {
      Alert.alert('Error', err.response?.data?.error || 'Failed to seed default squads');
    } finally {
      setSeeding(false);
    }
  };

  const handleOpenCreateSquad = () => {
    setEditingSquad(null);
    setFormName('');
    setFormDescription('');
    setFormStrategyIds(strategies.slice(0, 2).map((s) => s.id));
    setFormConsensusRule('Majority');
    setFormMinAgreeing('2');
    setFormOperatingMode('ApprovalRequired');
    setFormStopLoss('12');
    setFormGain('35');
    setFormTrailingSl('8');
    setFormTimeframe('5');
    setIsModalOpen(true);
  };

  const handleOpenEditSquad = (sq: StrategyGroupData) => {
    setEditingSquad(sq);
    setFormName(sq.name);
    setFormDescription(sq.description);
    let ids: number[] = [];
    try {
      ids = JSON.parse(sq.strategyIdsJson) || [];
    } catch {}
    setFormStrategyIds(ids);
    setFormConsensusRule((sq.consensusRule as any) || 'Majority');
    setFormMinAgreeing((sq.minAgreeingStrategies || 2).toString());
    setFormOperatingMode((sq.operatingMode as any) || 'ApprovalRequired');
    setFormStopLoss((sq.perTradeStopLossPoint || 12).toString());
    setFormGain((sq.perTradeGainPoint || 35).toString());
    setFormTrailingSl((sq.trailingStopLossPoint || 8).toString());
    setFormTimeframe((sq.timeframeMinutes || 5).toString());
    setIsModalOpen(true);
  };

  const toggleStrategySelection = (id: number) => {
    Haptics.selectionAsync();
    setFormStrategyIds((prev) =>
      prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]
    );
  };

  const handleSaveSquad = async () => {
    if (!formName.trim()) {
      Alert.alert('Required', 'Please enter a squad name');
      return;
    }
    if (formStrategyIds.length === 0) {
      Alert.alert('Required', 'Please select at least one participating strategy');
      return;
    }

    setSavingSquad(true);
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);

    const payload = {
      name: formName.trim(),
      description: formDescription.trim(),
      isEnabled: editingSquad ? editingSquad.isEnabled : false,
      strategyIdsJson: JSON.stringify(formStrategyIds),
      consensusRule: formConsensusRule,
      minAgreeingStrategies: parseInt(formMinAgreeing, 10) || 2,
      operatingMode: formOperatingMode,
      perTradeStopLossPoint: parseFloat(formStopLoss) || 12,
      perTradeGainPoint: parseFloat(formGain) || 35,
      trailingStopLossPoint: parseFloat(formTrailingSl) || 8,
      timeframeMinutes: parseInt(formTimeframe, 10) || 5,
    };

    try {
      if (editingSquad) {
        await apiClient.put(`/api/strategygroups/${editingSquad.id}`, payload);
        Alert.alert('Squad Updated', `Successfully updated "${formName}".`);
      } else {
        await apiClient.post('/api/strategygroups', payload);
        Alert.alert('Squad Created', `Successfully created squad "${formName}".`);
      }
      setIsModalOpen(false);
      fetchData();
    } catch (err: any) {
      Alert.alert('Error', err.response?.data?.error || 'Failed to save squad');
    } finally {
      setSavingSquad(false);
    }
  };

  const handleDeleteSquad = () => {
    if (!editingSquad) return;
    Alert.alert(
      'Delete Squad',
      `Are you sure you want to delete "${editingSquad.name}"?`,
      [
        { text: 'Cancel', style: 'cancel' },
        {
          text: 'Delete',
          style: 'destructive',
          onPress: async () => {
            try {
              await apiClient.delete(`/api/strategygroups/${editingSquad.id}`);
              setIsModalOpen(false);
              fetchData();
            } catch {
              Alert.alert('Error', 'Failed to delete squad');
            }
          },
        },
      ]
    );
  };

  const handleToggleStrategy = async (id: number, currentValue: boolean) => {
    Haptics.selectionAsync();
    try {
      await apiClient.post(`/api/strategyconfig/${id}/toggle`);
      setStrategies((prev) =>
        prev.map((st) => (st.id === id ? { ...st, isEnabled: !currentValue } : st))
      );
    } catch (err: any) {
      Alert.alert('Error', err.response?.data?.error || 'Failed to toggle strategy');
    }
  };

  const handleClearSignals = () => {
    Alert.alert(
      'Clear Live Signal Log',
      'Are you sure you want to clear all logged signals?',
      [
        { text: 'Cancel', style: 'cancel' },
        {
          text: 'Clear Log',
          style: 'destructive',
          onPress: async () => {
            Haptics.notificationAsync(Haptics.NotificationFeedbackType.Warning);
            try {
              await apiClient.post('/api/engine/signals/clear');
              setSignals([]);
            } catch (err: any) {
              Alert.alert('Error', 'Failed to clear signals');
            }
          },
        },
      ]
    );
  };

  const filteredSignals = signals.filter((s) => {
    const signalDate = new Date(s.generatedAt);
    const today = new Date();
    const isToday = signalDate.toDateString() === today.toDateString();

    const yesterday = new Date();
    yesterday.setDate(today.getDate() - 1);
    const isYesterday = signalDate.toDateString() === yesterday.toDateString();

    if (dateFilter === 'today') return isToday;
    if (dateFilter === 'yesterday') return isYesterday;
    return true;
  });

  return (
    <View style={styles.container}>
      {/* Segmented Horizontal Tabs */}
      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        style={styles.tabsScrollView}
        contentContainerStyle={styles.tabsContainer}
      >
        <TouchableOpacity
          style={[styles.tabBtn, activeTab === 'approvals' && styles.tabActive]}
          onPress={() => { setActiveTab('approvals'); Haptics.selectionAsync(); }}
        >
          <ShieldCheck size={15} color={activeTab === 'approvals' ? '#ffffff' : COLORS.textMuted} />
          <Text style={[styles.tabText, activeTab === 'approvals' && styles.textWhite]}>
            Approvals ({pendingApprovals.length})
          </Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={[styles.tabBtn, activeTab === 'squads' && styles.tabActive]}
          onPress={() => { setActiveTab('squads'); Haptics.selectionAsync(); }}
        >
          <Layers size={15} color={activeTab === 'squads' ? '#ffffff' : COLORS.textMuted} />
          <Text style={[styles.tabText, activeTab === 'squads' && styles.textWhite]}>
            Squads ({squads.length})
          </Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={[styles.tabBtn, activeTab === 'strategies' && styles.tabActive]}
          onPress={() => { setActiveTab('strategies'); Haptics.selectionAsync(); }}
        >
          <Cpu size={15} color={activeTab === 'strategies' ? '#ffffff' : COLORS.textMuted} />
          <Text style={[styles.tabText, activeTab === 'strategies' && styles.textWhite]}>
            Strategies ({strategies.length})
          </Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={[styles.tabBtn, activeTab === 'signals' && styles.tabActive]}
          onPress={() => { setActiveTab('signals'); Haptics.selectionAsync(); }}
        >
          <Activity size={15} color={activeTab === 'signals' ? '#ffffff' : COLORS.textMuted} />
          <Text style={[styles.tabText, activeTab === 'signals' && styles.textWhite]}>
            Signal Log ({signals.length})
          </Text>
        </TouchableOpacity>
      </ScrollView>

      {/* Main Tab Content Area */}
      <ScrollView
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={COLORS.primary} />}
        contentContainerStyle={{ paddingBottom: 40 }}
      >
        {/* TAB 1: APPROVAL QUEUE */}
        {activeTab === 'approvals' && (
          <View>
            <View style={styles.sectionHeader}>
              <Text style={styles.sectionTitle}>Squad & Strategy Approval Queue</Text>
              <Text style={styles.sectionDesc}>High-confluence setups awaiting your manual authorization before execution</Text>
            </View>

            {pendingApprovals.length === 0 ? (
              <View style={styles.emptyBox}>
                <ShieldCheck size={36} color={COLORS.profit} />
                <Text style={styles.emptyTitle}>Approval Queue is Clear</Text>
                <Text style={styles.emptyDesc}>New squad consensus setups requiring manual approval will appear here with instant trade buttons.</Text>
              </View>
            ) : (
              pendingApprovals.map((item) => (
                <ApprovalQueueCard
                  key={item.id}
                  item={item}
                  onApprove={handleApproveSignal}
                  onDeny={handleDenySignal}
                />
              ))
            )}
          </View>
        )}

        {/* TAB 2: STRATEGY SQUADS */}
        {activeTab === 'squads' && (
          <View>
            <View style={styles.sectionHeader}>
              <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' }}>
                <Text style={styles.sectionTitle}>Multi-Strategy Confluence Squads</Text>
                <View style={{ flexDirection: 'row', gap: 6 }}>
                  <TouchableOpacity
                    style={styles.headerActionBtn}
                    onPress={handleSeedDefaults}
                    disabled={seeding}
                  >
                    <Sparkles size={12} color={COLORS.primary} />
                    <Text style={styles.headerActionText}>{seeding ? 'Seeding...' : 'Seed 5'}</Text>
                  </TouchableOpacity>

                  <TouchableOpacity
                    style={[styles.headerActionBtn, { backgroundColor: COLORS.primary }]}
                    onPress={handleOpenCreateSquad}
                  >
                    <Plus size={12} color="#ffffff" />
                    <Text style={[styles.headerActionText, { color: '#ffffff' }]}>New</Text>
                  </TouchableOpacity>
                </View>
              </View>
              <Text style={styles.sectionDesc}>High-probability consensus units combining momentum, volume, and price action</Text>
            </View>

            {squads.length === 0 ? (
              <View style={styles.emptyBox}>
                <Layers size={36} color={COLORS.primary} />
                <Text style={styles.emptyTitle}>No Active Strategy Squads</Text>
                <Text style={styles.emptyDesc}>
                  Seed the 5 predefined high-confluence squads to start consensus scanning across indicators.
                </Text>
                <TouchableOpacity
                  style={styles.seedEmptyBtn}
                  onPress={handleSeedDefaults}
                  disabled={seeding}
                >
                  <Sparkles size={16} color="#ffffff" />
                  <Text style={styles.seedEmptyBtnText}>
                    {seeding ? 'Seeding Squads...' : 'Seed 5 Predefined Squads'}
                  </Text>
                </TouchableOpacity>
              </View>
            ) : (
              squads.map((sq) => (
                <SquadCard
                  key={sq.id}
                  squad={sq}
                  allStrategies={strategies}
                  onToggle={handleToggleSquad}
                  onEdit={handleOpenEditSquad}
                />
              ))
            )}
          </View>
        )}

        {/* TAB 3: STANDALONE STRATEGIES */}
        {activeTab === 'strategies' && (
          <View>
            <View style={styles.sectionHeader}>
              <Text style={styles.sectionTitle}>Individual Systematic Strategies</Text>
              <Text style={styles.sectionDesc}>Manage operating modes and enable/disable individual quantitative engines</Text>
            </View>

            {strategies.map((st) => (
              <StrategyCard
                key={st.id}
                strategy={st}
                onToggle={handleToggleStrategy}
              />
            ))}
          </View>
        )}

        {/* TAB 4: LIVE SIGNAL LOG */}
        {activeTab === 'signals' && (
          <View>
            {/* Filter Pills and Clear Log */}
            <View style={styles.filterRow}>
              <View style={styles.pillsContainer}>
                <TouchableOpacity
                  style={[styles.pill, dateFilter === 'today' && styles.pillActive]}
                  onPress={() => setDateFilter('today')}
                >
                  <Text style={[styles.pillText, dateFilter === 'today' && styles.pillTextActive]}>Today</Text>
                </TouchableOpacity>
                <TouchableOpacity
                  style={[styles.pill, dateFilter === 'yesterday' && styles.pillActive]}
                  onPress={() => setDateFilter('yesterday')}
                >
                  <Text style={[styles.pillText, dateFilter === 'yesterday' && styles.pillTextActive]}>Yesterday</Text>
                </TouchableOpacity>
                <TouchableOpacity
                  style={[styles.pill, dateFilter === 'all' && styles.pillActive]}
                  onPress={() => setDateFilter('all')}
                >
                  <Text style={[styles.pillText, dateFilter === 'all' && styles.pillTextActive]}>All</Text>
                </TouchableOpacity>
              </View>

              {signals.length > 0 && (
                <TouchableOpacity style={styles.clearBtn} onPress={handleClearSignals}>
                  <Trash2 size={14} color={COLORS.loss} />
                  <Text style={styles.clearBtnText}>Clear</Text>
                </TouchableOpacity>
              )}
            </View>

            {filteredSignals.length === 0 ? (
              <View style={styles.emptyBox}>
                <Activity size={32} color={COLORS.textSubtle} />
                <Text style={styles.emptyTitle}>No signals recorded</Text>
                <Text style={styles.emptyDesc}>Signals appear here live during market hours (09:15 AM - 03:30 PM IST)</Text>
              </View>
            ) : (
              filteredSignals.map((sig) => (
                <SignalLogCard key={sig.id} signal={sig} />
              ))
            )}
          </View>
        )}
      </ScrollView>

      {/* Squad Edit / Create Modal */}
      <Modal
        visible={isModalOpen}
        animationType="slide"
        transparent={true}
        onRequestClose={() => setIsModalOpen(false)}
      >
        <View style={styles.modalOverlay}>
          <View style={styles.modalContent}>
            <View style={styles.modalHeader}>
              <View style={{ flexDirection: 'row', alignItems: 'center', gap: 6 }}>
                <Layers size={18} color={COLORS.primary} />
                <Text style={styles.modalTitle}>
                  {editingSquad ? 'Edit Strategy Squad' : 'Create Strategy Squad'}
                </Text>
              </View>
              <TouchableOpacity onPress={() => setIsModalOpen(false)} style={styles.closeBtn}>
                <X size={18} color={COLORS.textMuted} />
              </TouchableOpacity>
            </View>

            <ScrollView style={styles.modalBody} showsVerticalScrollIndicator={false}>
              <Text style={styles.formLabel}>Squad Name</Text>
              <TextInput
                style={styles.modalInput}
                value={formName}
                onChangeText={setFormName}
                placeholder="e.g. Trend Confluence Alpha"
                placeholderTextColor={COLORS.textSubtle}
              />

              <Text style={styles.formLabel}>Description</Text>
              <TextInput
                style={[styles.modalInput, { height: 60 }]}
                value={formDescription}
                onChangeText={setFormDescription}
                placeholder="High-probability multi-indicator setup"
                placeholderTextColor={COLORS.textSubtle}
                multiline
              />

              {/* Operating Mode */}
              <Text style={styles.formLabel}>Operating Mode</Text>
              <View style={styles.modeSelectorRow}>
                {(['ApprovalRequired', 'Automatic', 'SignalOnly'] as const).map((mode) => (
                  <TouchableOpacity
                    key={mode}
                    style={[styles.modeSelectorBtn, formOperatingMode === mode && styles.modeSelectorActive]}
                    onPress={() => setFormOperatingMode(mode)}
                  >
                    <Text style={[styles.modeSelectorText, formOperatingMode === mode && styles.modeSelectorTextActive]}>
                      {mode === 'ApprovalRequired' ? 'Approval' : mode === 'Automatic' ? 'Auto-Trade' : 'Signal'}
                    </Text>
                  </TouchableOpacity>
                ))}
              </View>

              {/* Consensus Rule */}
              <Text style={styles.formLabel}>Consensus Rule</Text>
              <View style={styles.modeSelectorRow}>
                {(['Majority', 'Unanimous', 'Any'] as const).map((rule) => (
                  <TouchableOpacity
                    key={rule}
                    style={[styles.modeSelectorBtn, formConsensusRule === rule && styles.modeSelectorActive]}
                    onPress={() => setFormConsensusRule(rule)}
                  >
                    <Text style={[styles.modeSelectorText, formConsensusRule === rule && styles.modeSelectorTextActive]}>
                      {rule}
                    </Text>
                  </TouchableOpacity>
                ))}
              </View>

              {formConsensusRule === 'Majority' && (
                <View style={{ marginTop: 8 }}>
                  <Text style={styles.formLabel}>Min Agreeing Strategies</Text>
                  <TextInput
                    style={styles.modalInput}
                    value={formMinAgreeing}
                    onChangeText={setFormMinAgreeing}
                    keyboardType="numeric"
                  />
                </View>
              )}

              {/* Grid: Gain, SL, Trailing SL, Timeframe */}
              <View style={styles.formGrid}>
                <View style={styles.formCol}>
                  <Text style={styles.formLabel}>Target (pts)</Text>
                  <TextInput
                    style={styles.modalInput}
                    value={formGain}
                    onChangeText={setFormGain}
                    keyboardType="numeric"
                  />
                </View>
                <View style={styles.formCol}>
                  <Text style={styles.formLabel}>Stop Loss (pts)</Text>
                  <TextInput
                    style={styles.modalInput}
                    value={formStopLoss}
                    onChangeText={setFormStopLoss}
                    keyboardType="numeric"
                  />
                </View>
              </View>

              <View style={styles.formGrid}>
                <View style={styles.formCol}>
                  <Text style={styles.formLabel}>Trailing SL (pts)</Text>
                  <TextInput
                    style={styles.modalInput}
                    value={formTrailingSl}
                    onChangeText={setFormTrailingSl}
                    keyboardType="numeric"
                  />
                </View>
                <View style={styles.formCol}>
                  <Text style={styles.formLabel}>Timeframe (min)</Text>
                  <TextInput
                    style={styles.modalInput}
                    value={formTimeframe}
                    onChangeText={setFormTimeframe}
                    keyboardType="numeric"
                  />
                </View>
              </View>

              {/* Participating Strategies Selection */}
              <Text style={[styles.formLabel, { marginTop: 12 }]}>
                Participating Strategies ({formStrategyIds.length} Selected)
              </Text>
              <View style={styles.strategiesList}>
                {strategies.map((st) => {
                  const isSelected = formStrategyIds.includes(st.id);
                  return (
                    <TouchableOpacity
                      key={st.id}
                      style={[styles.strategySelectRow, isSelected && styles.strategySelectRowActive]}
                      onPress={() => toggleStrategySelection(st.id)}
                    >
                      {isSelected ? (
                        <CheckSquare size={16} color={COLORS.primary} />
                      ) : (
                        <SquareIcon size={16} color={COLORS.textSubtle} />
                      )}
                      <Text style={[styles.strategySelectText, isSelected && styles.strategySelectTextActive]}>
                        {st.strategyName}
                      </Text>
                    </TouchableOpacity>
                  );
                })}
              </View>
            </ScrollView>

            <View style={styles.modalFooter}>
              {editingSquad && (
                <TouchableOpacity style={styles.deleteSquadBtn} onPress={handleDeleteSquad}>
                  <Trash2 size={16} color={COLORS.loss} />
                </TouchableOpacity>
              )}

              <TouchableOpacity
                style={styles.cancelModalBtn}
                onPress={() => setIsModalOpen(false)}
              >
                <Text style={styles.cancelModalText}>Cancel</Text>
              </TouchableOpacity>

              <TouchableOpacity
                style={styles.saveSquadBtn}
                onPress={handleSaveSquad}
                disabled={savingSquad}
              >
                {savingSquad ? (
                  <ActivityIndicator size="small" color="#ffffff" />
                ) : (
                  <Text style={styles.saveSquadText}>
                    {editingSquad ? 'Save Changes' : 'Create Squad'}
                  </Text>
                )}
              </TouchableOpacity>
            </View>
          </View>
        </View>
      </Modal>
    </View>
  );
};

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: COLORS.bg,
  },
  tabsScrollView: {
    maxHeight: 56,
    borderBottomWidth: 1,
    borderBottomColor: COLORS.surfaceBorder,
    backgroundColor: COLORS.surface,
  },
  tabsContainer: {
    flexDirection: 'row',
    paddingHorizontal: 12,
    paddingVertical: 8,
    gap: 8,
    alignItems: 'center',
  },
  tabBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    paddingHorizontal: 14,
    paddingVertical: 8,
    borderRadius: 10,
    backgroundColor: COLORS.bg,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  tabActive: {
    backgroundColor: COLORS.primary,
    borderColor: COLORS.primary,
  },
  tabText: {
    fontSize: 12,
    fontWeight: '800',
    color: COLORS.textMuted,
  },
  textWhite: {
    color: '#ffffff',
  },
  sectionHeader: {
    paddingHorizontal: 16,
    paddingTop: 12,
    paddingBottom: 6,
  },
  sectionTitle: {
    fontSize: 15,
    fontWeight: '800',
    color: COLORS.text,
  },
  sectionDesc: {
    fontSize: 11,
    color: COLORS.textMuted,
    marginTop: 2,
  },
  filterRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 16,
    paddingVertical: 8,
  },
  pillsContainer: {
    flexDirection: 'row',
    gap: 6,
  },
  pill: {
    backgroundColor: COLORS.surface,
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: 8,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  pillActive: {
    backgroundColor: COLORS.primaryLight,
    borderColor: COLORS.primary,
  },
  pillText: {
    fontSize: 12,
    fontWeight: '700',
    color: COLORS.textMuted,
  },
  pillTextActive: {
    color: COLORS.primary,
  },
  clearBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    backgroundColor: COLORS.lossLight,
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: 8,
  },
  clearBtnText: {
    fontSize: 12,
    fontWeight: '700',
    color: COLORS.loss,
  },
  emptyBox: {
    backgroundColor: COLORS.surface,
    borderRadius: 16,
    padding: 30,
    marginHorizontal: 16,
    marginTop: 16,
    alignItems: 'center',
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  emptyTitle: {
    fontSize: 15,
    fontWeight: '800',
    color: COLORS.text,
    marginTop: 10,
  },
  emptyDesc: {
    fontSize: 12,
    color: COLORS.textMuted,
    textAlign: 'center',
    marginTop: 4,
    lineHeight: 16,
  },
  headerActionBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    backgroundColor: COLORS.surface,
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: 8,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  headerActionText: {
    fontSize: 11,
    fontWeight: '800',
    color: COLORS.primary,
  },
  seedEmptyBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    backgroundColor: COLORS.primary,
    paddingHorizontal: 18,
    paddingVertical: 12,
    borderRadius: 12,
    marginTop: 16,
  },
  seedEmptyBtnText: {
    fontSize: 13,
    fontWeight: '800',
    color: '#ffffff',
  },
  modalOverlay: {
    flex: 1,
    backgroundColor: 'rgba(0, 0, 0, 0.7)',
    justifyContent: 'flex-end',
  },
  modalContent: {
    backgroundColor: COLORS.surface,
    borderTopLeftRadius: 20,
    borderTopRightRadius: 20,
    maxHeight: '90%',
    paddingBottom: 24,
  },
  modalHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 20,
    paddingVertical: 16,
    borderBottomWidth: 1,
    borderBottomColor: COLORS.surfaceBorder,
  },
  modalTitle: {
    fontSize: 16,
    fontWeight: '800',
    color: COLORS.text,
  },
  closeBtn: {
    padding: 6,
    borderRadius: 6,
    backgroundColor: COLORS.bg,
  },
  modalBody: {
    paddingHorizontal: 20,
    paddingVertical: 12,
  },
  formLabel: {
    fontSize: 12,
    fontWeight: '700',
    color: COLORS.textMuted,
    marginBottom: 6,
    marginTop: 10,
  },
  modalInput: {
    backgroundColor: COLORS.bg,
    borderRadius: 10,
    paddingHorizontal: 12,
    paddingVertical: 10,
    color: COLORS.text,
    fontSize: 13,
    fontWeight: '700',
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  modeSelectorRow: {
    flexDirection: 'row',
    gap: 8,
  },
  modeSelectorBtn: {
    flex: 1,
    paddingVertical: 8,
    alignItems: 'center',
    borderRadius: 8,
    backgroundColor: COLORS.bg,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  modeSelectorActive: {
    backgroundColor: COLORS.primaryLight,
    borderColor: COLORS.primary,
  },
  modeSelectorText: {
    fontSize: 11,
    fontWeight: '700',
    color: COLORS.textMuted,
  },
  modeSelectorTextActive: {
    color: COLORS.primary,
  },
  formGrid: {
    flexDirection: 'row',
    gap: 10,
  },
  formCol: {
    flex: 1,
  },
  strategiesList: {
    gap: 6,
    marginBottom: 20,
  },
  strategySelectRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    backgroundColor: COLORS.bg,
    paddingHorizontal: 12,
    paddingVertical: 10,
    borderRadius: 8,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  strategySelectRowActive: {
    borderColor: 'rgba(99, 102, 241, 0.4)',
    backgroundColor: 'rgba(99, 102, 241, 0.08)',
  },
  strategySelectText: {
    fontSize: 12,
    fontWeight: '600',
    color: COLORS.textMuted,
    flex: 1,
  },
  strategySelectTextActive: {
    color: COLORS.text,
    fontWeight: '700',
  },
  modalFooter: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'flex-end',
    gap: 10,
    paddingHorizontal: 20,
    paddingTop: 12,
    borderTopWidth: 1,
    borderTopColor: COLORS.surfaceBorder,
  },
  deleteSquadBtn: {
    padding: 12,
    borderRadius: 10,
    backgroundColor: COLORS.lossLight,
    marginRight: 'auto',
  },
  cancelModalBtn: {
    paddingHorizontal: 16,
    paddingVertical: 12,
    borderRadius: 10,
    backgroundColor: COLORS.bg,
  },
  cancelModalText: {
    fontSize: 13,
    fontWeight: '700',
    color: COLORS.textMuted,
  },
  saveSquadBtn: {
    paddingHorizontal: 20,
    paddingVertical: 12,
    borderRadius: 10,
    backgroundColor: COLORS.primary,
    minWidth: 120,
    alignItems: 'center',
  },
  saveSquadText: {
    fontSize: 13,
    fontWeight: '800',
    color: '#ffffff',
  },
});
