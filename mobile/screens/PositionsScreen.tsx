import React, { useState, useEffect, useCallback } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, RefreshControl, Alert } from 'react-native';
import { Briefcase, RotateCcw, TrendingUp, AlertCircle, RefreshCw, Trash2 } from 'lucide-react-native';
import * as Haptics from 'expo-haptics';
import { COLORS } from '../lib/theme';
import { apiClient } from '../lib/api';
import { PositionCard, PositionData } from '../components/PositionCard';
import MorningConditionBanner from '../components/MorningConditionBanner';

type PositionCategory = 'DAY' | 'HOLDING';
type PositionFilter = 'ALL' | 'ACTIVE' | 'EXITED';

export const PositionsScreen: React.FC = () => {
  const [positions, setPositions] = useState<PositionData[]>([]);
  const [tradeMode, setTradeMode] = useState<'Paper' | 'Live'>('Paper');
  const [activeCategory, setActiveCategory] = useState<PositionCategory>('DAY');
  const [activeFilter, setActiveFilter] = useState<PositionFilter>('ALL');
  const [refreshing, setRefreshing] = useState(false);

  const fetchPositions = useCallback(async () => {
    try {
      const [posRes, holdRes, settingsRes] = await Promise.allSettled([
        apiClient.get('/api/portfolio/positions'),
        apiClient.get('/api/portfolio/holdings'),
        apiClient.get('/api/globalsettings'),
      ]);

      let allPositions: PositionData[] = [];

      if (posRes.status === 'fulfilled') {
        const data = posRes.value.data;
        if (data?.error) {
          console.error('Portfolio positions API error:', data.error);
        }
        const netList = Array.isArray(data) ? data : (data?.netPositions || []);
        netList.forEach((p: any) => {
          const qty = Math.abs(p.netQty ?? 0);
          const rawSymbol: string = p.symbol ?? '-';
          const symbol = rawSymbol.startsWith('NIFTYNIFTY') ? rawSymbol.substring(5) : rawSymbol;
          const buyAvg = p.buyAvg ?? 0;
          const sellAvg = p.sellAvg ?? 0;
          const ltp = p.ltp ?? p.buyAvg ?? 0;
          const realizedProfit = p.realized_profit ?? p.realizedProfit ?? 0;
          const unrealizedPnL = p.unrealized_profit ?? p.unrealizedPnL ?? (qty > 0 ? (ltp - buyAvg) * (p.netQty ?? 0) : 0);

          allPositions.push({
            symbol,
            netQty: p.netQty ?? 0,
            buyAvg,
            sellAvg,
            ltp,
            unrealizedPnL,
            realizedProfit,
            targetPrice: p.targetPrice && p.targetPrice > 0 ? p.targetPrice : (buyAvg > 0 ? buyAvg * 1.25 : undefined),
            stopLossPrice: p.stopLossPrice && p.stopLossPrice > 0 ? p.stopLossPrice : (buyAvg > 0 ? Math.max(5, buyAvg * 0.85) : undefined),
            trailingStopLossPoint: p.trailingStopLossPoint,
            strategyName: p.strategyName,
            status: qty === 0 ? 'EXITED' : 'ACTIVE',
            category: p.isCarryForward ? 'HOLDING' : 'DAY',
          });
        });
      }

      if (holdRes.status === 'fulfilled') {
        const holdData = holdRes.value.data;
        const holdList = Array.isArray(holdData) ? holdData : (holdData?.holdings || []);
        holdList.forEach((h: any) => {
          allPositions.push({
            symbol: h.symbol ?? '-',
            netQty: h.quantity ?? 0,
            buyAvg: h.costPrice ?? 0,
            sellAvg: 0,
            ltp: h.ltp ?? h.costPrice ?? 0,
            unrealizedPnL: ((h.ltp ?? h.costPrice ?? 0) - (h.costPrice ?? 0)) * (h.quantity ?? 0),
            realizedProfit: 0,
            status: 'ACTIVE',
            category: 'HOLDING',
          });
        });
      }

      setPositions(allPositions);

      if (settingsRes.status === 'fulfilled') {
        setTradeMode(settingsRes.value.data.tradeMode || 'Paper');
      }
    } catch (err) {
      console.error('Error fetching positions:', err);
    }
  }, []);

  useEffect(() => {
    fetchPositions();
    const interval = setInterval(fetchPositions, 3000);
    return () => clearInterval(interval);
  }, [fetchPositions]);

  const onRefresh = async () => {
    setRefreshing(true);
    await fetchPositions();
    setRefreshing(false);
  };

  const handleClosePosition = (symbol: string, qty: number) => {
    Alert.alert(
      'Exit Position',
      `Are you sure you want to close ${qty} qty of ${symbol} at market price?`,
      [
        { text: 'Cancel', style: 'cancel' },
        {
          text: 'Exit Now',
          style: 'destructive',
          onPress: async () => {
            Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
            try {
              await apiClient.post('/api/orders', {
                instrument: symbol,
                action: 'SELL',
                quantity: qty,
                price: 0,
                orderType: 'MARKET',
              });
              Alert.alert('Position Closed', `Sent exit order for ${symbol}`);
              fetchPositions();
            } catch (err: any) {
              Alert.alert('Error', err.response?.data?.message || 'Failed to exit position');
            }
          },
        },
      ]
    );
  };

  const handleEmergencySquareOff = () => {
    Alert.alert(
      'EMERGENCY SQUARE-OFF',
      'This will instantly close ALL open positions at market price. Are you sure?',
      [
        { text: 'Cancel', style: 'cancel' },
        {
          text: 'SQUARE-OFF ALL',
          style: 'destructive',
          onPress: async () => {
            Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
            try {
              await apiClient.post('/api/portfolio/emergency-squareoff');
              Alert.alert('Emergency Stop Triggered', 'All positions squared off.');
              fetchPositions();
            } catch {
              Alert.alert('Error', 'Failed to execute emergency square-off.');
            }
          },
        },
      ]
    );
  };

  const handleResetPaper = () => {
    Alert.alert(
      'Reset Paper Portfolio',
      'This will clear all paper trading positions and reset your simulated trade history.',
      [
        { text: 'Cancel', style: 'cancel' },
        {
          text: 'Reset Portfolio',
          style: 'destructive',
          onPress: async () => {
            try {
              await apiClient.post('/api/portfolio/reset-paper');
              Alert.alert('Success', 'Paper trading positions and logs reset.');
              fetchPositions();
            } catch {
              Alert.alert('Error', 'Failed to reset paper balance.');
            }
          },
        },
      ]
    );
  };

  // Filter positions by Category and Status
  const filteredPositions = positions.filter((p) => {
    if (p.category !== activeCategory) return false;
    if (activeFilter === 'ACTIVE' && p.status !== 'ACTIVE') return false;
    if (activeFilter === 'EXITED' && p.status !== 'EXITED') return false;
    return true;
  });

  const activeCount = positions.filter((p) => p.category === activeCategory && p.status === 'ACTIVE').length;
  const exitedCount = positions.filter((p) => p.category === activeCategory && p.status === 'EXITED').length;

  const totalUnrealized = positions
    .filter((p) => p.category === activeCategory && p.status === 'ACTIVE')
    .reduce((acc, p) => acc + (p.unrealizedPnL || 0), 0);

  const totalRealized = positions
    .filter((p) => p.category === activeCategory)
    .reduce((acc, p) => acc + (p.realizedProfit || 0), 0);

  const netDayPnL = totalRealized + totalUnrealized;
  const isProfit = netDayPnL >= 0;

  return (
    <ScrollView
      style={styles.container}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={COLORS.primary} />}
      contentContainerStyle={{ paddingBottom: 40 }}
    >
      <MorningConditionBanner />

      {/* Mode & PnL Summary Header */}
      <View style={styles.summaryCard}>
        <View style={styles.summaryTop}>
          <View style={[styles.modePill, tradeMode === 'Live' ? styles.liveMode : styles.paperMode]}>
            <Text style={styles.modePillText}>{tradeMode.toUpperCase()} TRADING</Text>
          </View>

          <View style={{ flexDirection: 'row', alignItems: 'center', gap: 6 }}>
            <TouchableOpacity style={styles.refreshBtn} onPress={onRefresh}>
              <RotateCcw size={13} color={COLORS.textMuted} />
            </TouchableOpacity>

            {tradeMode === 'Paper' && (
              <TouchableOpacity style={styles.resetBtn} onPress={handleResetPaper}>
                <Trash2 size={13} color="#ef4444" />
                <Text style={styles.resetBtnText}>Reset</Text>
              </TouchableOpacity>
            )}
          </View>
        </View>

        <Text style={styles.pnlTitle}>Day P&L ({activeCategory === 'DAY' ? 'Intraday' : 'Holdings'})</Text>
        <Text style={[styles.pnlAmount, isProfit ? styles.textProfit : styles.textLoss]}>
          {isProfit ? '+' : ''}₹{netDayPnL.toFixed(2)}
        </Text>

        <View style={styles.realizedRow}>
          <View>
            <Text style={styles.breakdownLabel}>Realized P&L</Text>
            <Text style={[styles.breakdownValue, totalRealized >= 0 ? styles.textProfit : styles.textLoss]}>
              {totalRealized >= 0 ? '+' : ''}₹{totalRealized.toFixed(2)}
            </Text>
          </View>
          <View style={{ alignItems: 'flex-end' }}>
            <Text style={styles.breakdownLabel}>Unrealized P&L</Text>
            <Text style={[styles.breakdownValue, totalUnrealized >= 0 ? styles.textProfit : styles.textLoss]}>
              {totalUnrealized >= 0 ? '+' : ''}₹{totalUnrealized.toFixed(2)}
            </Text>
          </View>
        </View>

        {activeCount > 0 && (
          <TouchableOpacity 
            style={{ marginTop: 12, backgroundColor: '#fee2e2', padding: 10, borderRadius: 8, alignItems: 'center', borderWidth: 1, borderColor: '#f87171' }}
            onPress={handleEmergencySquareOff}
          >
            <Text style={{ color: '#b91c1c', fontWeight: 'bold', fontSize: 13 }}>🚨 EMERGENCY SQUARE-OFF ALL</Text>
          </TouchableOpacity>
        )}
      </View>

      {/* Category Tabs (DAY vs HOLDING) */}
      <View style={styles.categoryRow}>
        <TouchableOpacity
          style={[styles.categoryTab, activeCategory === 'DAY' && styles.categoryTabActive]}
          onPress={() => {
            Haptics.selectionAsync();
            setActiveCategory('DAY');
          }}
        >
          <Text style={[styles.categoryTabText, activeCategory === 'DAY' && styles.categoryTabTextActive]}>
            Day Positions ({positions.filter((p) => p.category === 'DAY').length})
          </Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={[styles.categoryTab, activeCategory === 'HOLDING' && styles.categoryTabActive]}
          onPress={() => {
            Haptics.selectionAsync();
            setActiveCategory('HOLDING');
          }}
        >
          <Text style={[styles.categoryTabText, activeCategory === 'HOLDING' && styles.categoryTabTextActive]}>
            Holdings ({positions.filter((p) => p.category === 'HOLDING').length})
          </Text>
        </TouchableOpacity>
      </View>

      {/* Status Filter Chips (ALL, ACTIVE, EXITED) */}
      <View style={styles.filterRow}>
        {(['ALL', 'ACTIVE', 'EXITED'] as PositionFilter[]).map((f) => (
          <TouchableOpacity
            key={f}
            style={[styles.filterChip, activeFilter === f && styles.filterChipActive]}
            onPress={() => {
              Haptics.selectionAsync();
              setActiveFilter(f);
            }}
          >
            <Text style={[styles.filterChipText, activeFilter === f && styles.filterChipTextActive]}>
              {f === 'ALL' ? `All (${positions.filter((p) => p.category === activeCategory).length})` : 
               f === 'ACTIVE' ? `Active (${activeCount})` : `Exited (${exitedCount})`}
            </Text>
          </TouchableOpacity>
        ))}
      </View>

      {/* Positions List */}
      {filteredPositions.length === 0 ? (
        <View style={styles.emptyCard}>
          <Briefcase size={32} color={COLORS.textSubtle} />
          <Text style={styles.emptyTitle}>No {activeFilter.toLowerCase()} positions</Text>
          <Text style={styles.emptyDesc}>
            {activeFilter === 'ACTIVE' 
              ? 'New trades taken by strategy squads or manual orders will appear here' 
              : 'Positions matching your filters will be listed here'}
          </Text>
        </View>
      ) : (
        filteredPositions.map((pos, idx) => (
          <PositionCard key={idx} position={pos} onClose={handleClosePosition} />
        ))
      )}
    </ScrollView>
  );
};

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: COLORS.bg,
  },
  summaryCard: {
    backgroundColor: COLORS.surface,
    borderRadius: 16,
    padding: 16,
    marginHorizontal: 16,
    marginVertical: 10,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  summaryTop: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: 12,
  },
  modePill: {
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 6,
  },
  paperMode: {
    backgroundColor: COLORS.purpleLight,
  },
  liveMode: {
    backgroundColor: COLORS.profitLight,
  },
  modePillText: {
    fontSize: 10,
    fontWeight: '800',
    color: COLORS.text,
    letterSpacing: 0.5,
  },
  resetBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    backgroundColor: COLORS.bg,
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: 6,
  },
  resetBtnText: {
    fontSize: 11,
    fontWeight: '700',
    color: COLORS.textMuted,
  },
  refreshBtn: {
    backgroundColor: COLORS.bg,
    padding: 6,
    borderRadius: 6,
  },
  pnlTitle: {
    fontSize: 12,
    color: COLORS.textMuted,
    marginBottom: 2,
  },
  pnlAmount: {
    fontSize: 28,
    fontWeight: '900',
    letterSpacing: -0.5,
    marginBottom: 10,
  },
  realizedRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    borderTopWidth: 1,
    borderTopColor: COLORS.surfaceBorder,
    paddingTop: 10,
  },
  breakdownLabel: {
    fontSize: 11,
    color: COLORS.textMuted,
    marginBottom: 2,
  },
  breakdownValue: {
    fontSize: 13,
    fontWeight: '800',
  },
  textProfit: {
    color: COLORS.profit,
  },
  textLoss: {
    color: COLORS.loss,
  },
  categoryRow: {
    flexDirection: 'row',
    marginHorizontal: 16,
    marginBottom: 10,
    backgroundColor: COLORS.surface,
    padding: 4,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  categoryTab: {
    flex: 1,
    paddingVertical: 8,
    alignItems: 'center',
    borderRadius: 8,
  },
  categoryTabActive: {
    backgroundColor: COLORS.primary,
  },
  categoryTabText: {
    fontSize: 12,
    fontWeight: '700',
    color: COLORS.textMuted,
  },
  categoryTabTextActive: {
    color: '#ffffff',
  },
  filterRow: {
    flexDirection: 'row',
    marginHorizontal: 16,
    marginBottom: 12,
    gap: 8,
  },
  filterChip: {
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: 8,
    backgroundColor: COLORS.surface,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  filterChipActive: {
    backgroundColor: 'rgba(59, 130, 246, 0.15)',
    borderColor: COLORS.primary,
  },
  filterChipText: {
    fontSize: 11,
    fontWeight: '700',
    color: COLORS.textMuted,
  },
  filterChipTextActive: {
    color: COLORS.primary,
  },
  emptyCard: {
    backgroundColor: COLORS.surface,
    borderRadius: 16,
    padding: 30,
    marginHorizontal: 16,
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
