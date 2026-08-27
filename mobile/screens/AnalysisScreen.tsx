import React, { useState, useEffect, useCallback } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, RefreshControl, Alert } from 'react-native';
import { Layers, Activity, ShieldCheck, Cpu, Trash2, Calendar } from 'lucide-react-native';
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
              <Text style={styles.sectionTitle}>Multi-Strategy Confluence Squads</Text>
              <Text style={styles.sectionDesc}>High-probability consensus units combining momentum, volume, and price action</Text>
            </View>

            {squads.map((sq) => (
              <SquadCard
                key={sq.id}
                squad={sq}
                allStrategies={strategies}
                onToggle={handleToggleSquad}
              />
            ))}
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
});
