import React, { useState, useEffect, useCallback } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, RefreshControl, Alert } from 'react-native';
import { Layers, Activity, Trash2, Calendar } from 'lucide-react-native';
import * as Haptics from 'expo-haptics';
import { COLORS } from '../lib/theme';
import { apiClient } from '../lib/api';
import { SquadCard, StrategyGroupData } from '../components/SquadCard';
import { SignalLogCard, SignalLogData } from '../components/SignalLogCard';

export const AnalysisScreen: React.FC = () => {
  const [activeTab, setActiveTab] = useState<'squads' | 'signals'>('squads');
  const [squads, setSquads] = useState<StrategyGroupData[]>([]);
  const [allStrategies, setAllStrategies] = useState<{ id: number; strategyName: string }[]>([]);
  const [signals, setSignals] = useState<SignalLogData[]>([]);
  const [dateFilter, setDateFilter] = useState<'today' | 'yesterday' | 'all'>('today');
  const [refreshing, setRefreshing] = useState(false);

  const fetchData = useCallback(async () => {
    try {
      const [squadsRes, stratRes, signalsRes] = await Promise.allSettled([
        apiClient.get('/api/strategygroups'),
        apiClient.get('/api/strategyconfig'),
        apiClient.get('/api/engine/signals'),
      ]);

      if (squadsRes.status === 'fulfilled') {
        setSquads(squadsRes.value.data);
      }
      if (stratRes.status === 'fulfilled') {
        setAllStrategies(stratRes.value.data);
      }
      if (signalsRes.status === 'fulfilled' && Array.isArray(signalsRes.value.data)) {
        setSignals(signalsRes.value.data);
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
      {/* Top Segmented Tabs */}
      <View style={styles.segmentContainer}>
        <TouchableOpacity
          style={[styles.segmentBtn, activeTab === 'squads' && styles.segmentActive]}
          onPress={() => { setActiveTab('squads'); Haptics.selectionAsync(); }}
        >
          <Layers size={16} color={activeTab === 'squads' ? '#ffffff' : COLORS.textMuted} />
          <Text style={[styles.segmentText, activeTab === 'squads' && styles.textWhite]}>
            Strategy Squads ({squads.length})
          </Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={[styles.segmentBtn, activeTab === 'signals' && styles.segmentActive]}
          onPress={() => { setActiveTab('signals'); Haptics.selectionAsync(); }}
        >
          <Activity size={16} color={activeTab === 'signals' ? '#ffffff' : COLORS.textMuted} />
          <Text style={[styles.segmentText, activeTab === 'signals' && styles.textWhite]}>
            Live Signal Log ({signals.length})
          </Text>
        </TouchableOpacity>
      </View>

      <ScrollView
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={COLORS.primary} />}
        contentContainerStyle={{ paddingBottom: 40 }}
      >
        {activeTab === 'squads' ? (
          <View>
            <View style={styles.sectionHeader}>
              <Text style={styles.sectionTitle}>Multi-Strategy Confluence Squads</Text>
              <Text style={styles.sectionDesc}>High-probability consensus units executing on candle alignments</Text>
            </View>

            {squads.map((sq) => (
              <SquadCard
                key={sq.id}
                squad={sq}
                allStrategies={allStrategies}
                onToggle={handleToggleSquad}
              />
            ))}
          </View>
        ) : (
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
                <Text style={styles.emptyDesc}>Signals appear here during market hours (09:15 AM - 03:30 PM IST)</Text>
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
  segmentContainer: {
    flexDirection: 'row',
    backgroundColor: COLORS.surface,
    padding: 8,
    marginHorizontal: 16,
    marginVertical: 10,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
    gap: 6,
  },
  segmentBtn: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 6,
    paddingVertical: 10,
    borderRadius: 8,
  },
  segmentActive: {
    backgroundColor: COLORS.primary,
  },
  segmentText: {
    fontSize: 13,
    fontWeight: '800',
    color: COLORS.textMuted,
  },
  textWhite: {
    color: '#ffffff',
  },
  sectionHeader: {
    paddingHorizontal: 16,
    paddingVertical: 8,
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
    marginTop: 20,
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
  },
});
