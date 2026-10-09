import React, { useState, useEffect, useCallback } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, RefreshControl, Alert } from 'react-native';
import { Briefcase, RotateCcw, TrendingUp, TrendingDown, AlertCircle, RefreshCw, Trash2, Calendar, ChevronDown, ChevronUp, Award, Clock } from 'lucide-react-native';
import * as Haptics from 'expo-haptics';
import { COLORS } from '../lib/theme';
import { apiClient } from '../lib/api';
import { PositionCard, PositionData } from '../components/PositionCard';
import MorningConditionBanner from '../components/MorningConditionBanner';

type PositionCategory = 'DAY' | 'HOLDING' | 'PNL_30D';
type PositionFilter = 'ALL' | 'ACTIVE' | 'EXITED';

interface DailyPnLRecord {
  id: number;
  date: string;
  realizedPnL: number;
  unrealizedPnL: number;
  netPnL: number;
  totalTrades: number;
  winningTrades: number;
  losingTrades: number;
  tradeMode: string;
}

interface ClosedTradeItem {
  id: string;
  symbol: string;
  strategyName: string;
  tradeMode: string;
  entryTime: string;
  exitTime: string;
  buyPrice: number;
  sellPrice: number;
  quantity: number;
  realizedProfit: number;
  exitReason: string;
  dateIst: string;
}

interface PnLHistoryData {
  daysRequested: number;
  totalNetPnL: number;
  totalRealizedPnL: number;
  totalTrades: number;
  winningTrades: number;
  losingTrades: number;
  winRatePercent: number;
  profitableDays: number;
  lossDays: number;
  breakEvenDays: number;
  averageDailyPnL: number;
  bestDay: { date: string; pnl: number } | null;
  worstDay: { date: string; pnl: number } | null;
  dailyRecords: DailyPnLRecord[];
  closedTrades: ClosedTradeItem[];
}

export const PositionsScreen: React.FC = () => {
  const [positions, setPositions] = useState<PositionData[]>([]);
  const [tradeMode, setTradeMode] = useState<'Paper' | 'Live'>('Paper');
  const [activeCategory, setActiveCategory] = useState<PositionCategory>('DAY');
  const [activeFilter, setActiveFilter] = useState<PositionFilter>('ALL');
  const [refreshing, setRefreshing] = useState(false);

  // 30-Day P&L History State
  const [pnlData, setPnlData] = useState<PnLHistoryData | null>(null);
  const [pnlRange, setPnlRange] = useState<number>(30);
  const [expandedDay, setExpandedDay] = useState<string | null>(null);

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
          const netQty = p.netQty ?? 0;
          const tradeQty = p.tradeQty ?? p.qty ?? Math.abs(netQty);
          const rawSymbol: string = p.symbol ?? '-';
          const symbol = rawSymbol.startsWith('NIFTYNIFTY') ? rawSymbol.substring(5) : rawSymbol;
          const buyAvg = p.buyAvg ?? 0;
          const sellAvg = p.sellAvg ?? 0;
          const ltp = p.ltp ?? p.buyAvg ?? 0;
          const rawRealized = p.realized_profit ?? p.realizedProfit ?? 0;
          const realizedProfit = (rawRealized !== 0) ? rawRealized : (netQty === 0 && sellAvg > 0 && buyAvg > 0 ? (sellAvg - buyAvg) * (tradeQty || 65) : 0);
          const unrealizedPnL = p.unrealized_profit ?? p.unrealizedPnL ?? (Math.abs(netQty) > 0 ? (ltp - buyAvg) * netQty : 0);

          allPositions.push({
            symbol,
            netQty,
            tradeQty,
            buyAvg,
            sellAvg,
            ltp,
            unrealizedPnL,
            realizedProfit,
            targetPrice: p.targetPrice && p.targetPrice > 0 ? p.targetPrice : (buyAvg > 0 ? buyAvg * 1.25 : undefined),
            stopLossPrice: p.stopLossPrice && p.stopLossPrice > 0 ? p.stopLossPrice : (buyAvg > 0 ? Math.max(5, buyAvg * 0.85) : undefined),
            trailingStopLossPoint: p.trailingStopLossPoint,
            peakLtp: p.peakLtp,
            strategyName: p.strategyName,
            status: netQty === 0 ? 'EXITED' : 'ACTIVE',
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

  const fetchPnLHistory = useCallback(async () => {
    try {
      const res = await apiClient.get(`/api/portfolio/pnl-history?days=${pnlRange}`);
      if (res.data) {
        setPnlData(res.data);
      }
    } catch (err) {
      console.error('Error fetching PnL history:', err);
    }
  }, [pnlRange]);

  useEffect(() => {
    fetchPositions();
    const interval = setInterval(fetchPositions, 3000);
    return () => clearInterval(interval);
  }, [fetchPositions]);

  useEffect(() => {
    if (activeCategory === 'PNL_30D') {
      fetchPnLHistory();
    }
  }, [activeCategory, pnlRange, fetchPnLHistory]);

  const onRefresh = async () => {
    setRefreshing(true);
    await Promise.all([fetchPositions(), fetchPnLHistory()]);
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
    .filter((p) => p.category === activeCategory && p.status === 'EXITED')
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

      {/* Category Tabs (DAY vs HOLDING vs 30D P&L) */}
      <View style={styles.categoryRow}>
        <TouchableOpacity
          style={[styles.categoryTab, activeCategory === 'DAY' && styles.categoryTabActive]}
          onPress={() => {
            Haptics.selectionAsync();
            setActiveCategory('DAY');
          }}
        >
          <Text style={[styles.categoryTabText, activeCategory === 'DAY' && styles.categoryTabTextActive]}>
            Day ({positions.filter((p) => p.category === 'DAY').length})
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

        <TouchableOpacity
          style={[styles.categoryTab, activeCategory === 'PNL_30D' && styles.categoryTabActive]}
          onPress={() => {
            Haptics.selectionAsync();
            setActiveCategory('PNL_30D');
            fetchPnLHistory();
          }}
        >
          <Text style={[styles.categoryTabText, activeCategory === 'PNL_30D' && styles.categoryTabTextActive]}>
            30D P&L
          </Text>
        </TouchableOpacity>
      </View>

      {/* ─── 30-DAY P&L ANALYTICS VIEW ─── */}
      {activeCategory === 'PNL_30D' ? (
        <View style={styles.pnlContainer}>
          {/* Range Selector Pills */}
          <View style={styles.rangeRow}>
            {[7, 15, 30].map((days) => (
              <TouchableOpacity
                key={days}
                style={[styles.rangePill, pnlRange === days && styles.rangePillActive]}
                onPress={() => {
                  Haptics.selectionAsync();
                  setPnlRange(days);
                }}
              >
                <Text style={[styles.rangePillText, pnlRange === days && styles.rangePillTextActive]}>
                  {days} Days
                </Text>
              </TouchableOpacity>
            ))}
          </View>

          {/* 30D Net P&L Summary Card */}
          <View style={[styles.pnlSummaryCard, (pnlData?.totalNetPnL || 0) >= 0 ? styles.borderProfit : styles.borderLoss]}>
            <View style={styles.summaryTop}>
              <Text style={styles.pnlCardTitle}>{pnlRange}-DAY NET P&L</Text>
              <View style={[styles.badgePill, (pnlData?.totalNetPnL || 0) >= 0 ? styles.badgeProfit : styles.badgeLoss]}>
                <Text style={[styles.badgeText, (pnlData?.totalNetPnL || 0) >= 0 ? styles.textProfit : styles.textLoss]}>
                  {(pnlData?.totalNetPnL || 0) >= 0 ? 'PROFITABLE' : 'DRAWDOWN'}
                </Text>
              </View>
            </View>

            <Text style={[styles.pnlTotalAmount, (pnlData?.totalNetPnL || 0) >= 0 ? styles.textProfit : styles.textLoss]}>
              {(pnlData?.totalNetPnL || 0) >= 0 ? '+' : ''}₹{Math.abs(pnlData?.totalNetPnL || 0).toFixed(2)}
            </Text>

            <View style={styles.pnlStatsRow}>
              <View style={styles.pnlStatCol}>
                <Text style={styles.pnlStatLabel}>Win Rate</Text>
                <Text style={styles.pnlStatVal}>{pnlData?.winRatePercent || 0}%</Text>
                <Text style={styles.pnlStatSub}>({pnlData?.winningTrades || 0}W / {pnlData?.losingTrades || 0}L)</Text>
              </View>
              <View style={styles.pnlStatCol}>
                <Text style={styles.pnlStatLabel}>Profitable Days</Text>
                <Text style={[styles.pnlStatVal, styles.textProfit]}>{pnlData?.profitableDays || 0} Green</Text>
                <Text style={styles.pnlStatSub}>{pnlData?.lossDays || 0} Red Days</Text>
              </View>
              <View style={styles.pnlStatCol}>
                <Text style={styles.pnlStatLabel}>Avg Daily P&L</Text>
                <Text style={[styles.pnlStatVal, (pnlData?.averageDailyPnL || 0) >= 0 ? styles.textProfit : styles.textLoss]}>
                  {(pnlData?.averageDailyPnL || 0) >= 0 ? '+' : ''}₹{(pnlData?.averageDailyPnL || 0).toFixed(0)}
                </Text>
                <Text style={styles.pnlStatSub}>{pnlData?.dailyRecords?.length || 0} Sessions</Text>
              </View>
            </View>
          </View>

          {/* Daily Records List */}
          <View style={styles.historySectionHeader}>
            <Text style={styles.historySectionTitle}>Daily Performance Journal</Text>
            <Text style={styles.historySectionSub}>Tap session to view trade tickets</Text>
          </View>

          {(!pnlData?.dailyRecords || pnlData.dailyRecords.length === 0) ? (
            <View style={styles.emptyCard}>
              <Calendar size={28} color={COLORS.textSubtle} />
              <Text style={styles.emptyTitle}>No P&L history records yet</Text>
              <Text style={styles.emptyDesc}>Closed trades over the trading session will be recorded here.</Text>
            </View>
          ) : (
            pnlData.dailyRecords.map((item) => {
              const isItemProfit = item.netPnL >= 0;
              const isExpanded = expandedDay === item.date;
              const matchingTrades = pnlData.closedTrades?.filter((t) => t.dateIst === item.date) || [];

              return (
                <View key={item.id || item.date} style={styles.dailyCard}>
                  <TouchableOpacity
                    style={styles.dailyCardHeader}
                    onPress={() => {
                      Haptics.selectionAsync();
                      setExpandedDay(isExpanded ? null : item.date);
                    }}
                    activeOpacity={0.7}
                  >
                    <View style={styles.dailyDateCol}>
                      <View style={{ flexDirection: 'row', alignItems: 'center', gap: 6 }}>
                        <Calendar size={14} color={COLORS.textMuted} />
                        <Text style={styles.dailyDateText}>{item.date}</Text>
                      </View>
                      <Text style={styles.dailyTradesCount}>
                        {item.totalTrades} trade{item.totalTrades !== 1 ? 's' : ''} ({item.winningTrades}W / {item.losingTrades}L)
                      </Text>
                    </View>

                    <View style={{ flexDirection: 'row', alignItems: 'center', gap: 10 }}>
                      <Text style={[styles.dailyPnLText, isItemProfit ? styles.textProfit : styles.textLoss]}>
                        {isItemProfit ? '+' : ''}₹{Math.abs(item.netPnL).toFixed(2)}
                      </Text>
                      {isExpanded ? (
                        <ChevronUp size={16} color={COLORS.textMuted} />
                      ) : (
                        <ChevronDown size={16} color={COLORS.textMuted} />
                      )}
                    </View>
                  </TouchableOpacity>

                  {/* Expanded Closed Trades for this Day */}
                  {isExpanded && (
                    <View style={styles.expandedTradeList}>
                      {matchingTrades.length === 0 ? (
                        <Text style={styles.noTicketsText}>
                          Aggregate daily record preserved. No separate intra-session tickets logged.
                        </Text>
                      ) : (
                        matchingTrades.map((t) => (
                          <View key={t.id} style={styles.tradeTicketRow}>
                            <View style={{ flex: 1 }}>
                              <Text style={styles.tradeSymbol}>{t.symbol}</Text>
                              <Text style={styles.tradeMeta}>
                                {t.strategyName} • Buy: ₹{t.buyPrice.toFixed(1)} / Sell: ₹{t.sellPrice.toFixed(1)}
                              </Text>
                            </View>
                            <Text style={[styles.tradeTicketPnL, t.realizedProfit >= 0 ? styles.textProfit : styles.textLoss]}>
                              {t.realizedProfit >= 0 ? '+' : ''}₹{t.realizedProfit.toFixed(2)}
                            </Text>
                          </View>
                        ))
                      )}
                    </View>
                  )}
                </View>
              );
            })
          )}
        </View>
      ) : (
        /* ─── ACTIVE POSITIONS / HOLDINGS VIEW ─── */
        <>
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
        </>
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
  // 30D P&L Styles
  pnlContainer: {
    marginHorizontal: 16,
    paddingBottom: 20,
  },
  rangeRow: {
    flexDirection: 'row',
    justifyContent: 'center',
    gap: 8,
    marginBottom: 12,
  },
  rangePill: {
    paddingHorizontal: 16,
    paddingVertical: 6,
    borderRadius: 8,
    backgroundColor: COLORS.surface,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  rangePillActive: {
    backgroundColor: COLORS.primary,
    borderColor: COLORS.primary,
  },
  rangePillText: {
    fontSize: 12,
    fontWeight: '700',
    color: COLORS.textMuted,
  },
  rangePillTextActive: {
    color: '#ffffff',
  },
  pnlSummaryCard: {
    backgroundColor: COLORS.surface,
    borderRadius: 16,
    padding: 18,
    borderWidth: 1.5,
    marginBottom: 16,
  },
  borderProfit: {
    borderColor: 'rgba(34, 197, 94, 0.4)',
  },
  borderLoss: {
    borderColor: 'rgba(239, 68, 68, 0.4)',
  },
  pnlCardTitle: {
    fontSize: 11,
    fontWeight: '800',
    color: COLORS.textMuted,
    letterSpacing: 0.5,
  },
  badgePill: {
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 6,
  },
  badgeProfit: {
    backgroundColor: 'rgba(34, 197, 94, 0.15)',
  },
  badgeLoss: {
    backgroundColor: 'rgba(239, 68, 68, 0.15)',
  },
  badgeText: {
    fontSize: 10,
    fontWeight: '800',
  },
  pnlTotalAmount: {
    fontSize: 32,
    fontWeight: '900',
    letterSpacing: -0.5,
    marginVertical: 6,
  },
  pnlStatsRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    borderTopWidth: 1,
    borderTopColor: COLORS.surfaceBorder,
    paddingTop: 12,
    marginTop: 6,
  },
  pnlStatCol: {
    alignItems: 'center',
  },
  pnlStatLabel: {
    fontSize: 10,
    fontWeight: '700',
    color: COLORS.textMuted,
    marginBottom: 2,
  },
  pnlStatVal: {
    fontSize: 13,
    fontWeight: '800',
    color: COLORS.text,
  },
  pnlStatSub: {
    fontSize: 10,
    color: COLORS.textMuted,
    marginTop: 1,
  },
  historySectionHeader: {
    marginBottom: 10,
  },
  historySectionTitle: {
    fontSize: 14,
    fontWeight: '800',
    color: COLORS.text,
  },
  historySectionSub: {
    fontSize: 11,
    color: COLORS.textMuted,
    marginTop: 1,
  },
  dailyCard: {
    backgroundColor: COLORS.surface,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
    marginBottom: 10,
    overflow: 'hidden',
  },
  dailyCardHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: 14,
  },
  dailyDateCol: {
    gap: 3,
  },
  dailyDateText: {
    fontSize: 13,
    fontWeight: '700',
    color: COLORS.text,
  },
  dailyTradesCount: {
    fontSize: 11,
    color: COLORS.textMuted,
  },
  dailyPnLText: {
    fontSize: 16,
    fontWeight: '900',
  },
  expandedTradeList: {
    borderTopWidth: 1,
    borderTopColor: COLORS.surfaceBorder,
    backgroundColor: 'rgba(0, 0, 0, 0.15)',
    padding: 12,
    gap: 10,
  },
  noTicketsText: {
    fontSize: 11,
    color: COLORS.textMuted,
    textAlign: 'center',
    fontStyle: 'italic',
    paddingVertical: 4,
  },
  tradeTicketRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingVertical: 4,
    borderBottomWidth: 1,
    borderBottomColor: 'rgba(255, 255, 255, 0.05)',
  },
  tradeSymbol: {
    fontSize: 12,
    fontWeight: '700',
    color: COLORS.text,
  },
  tradeMeta: {
    fontSize: 10,
    color: COLORS.textMuted,
    marginTop: 2,
  },
  tradeTicketPnL: {
    fontSize: 13,
    fontWeight: '800',
  },
});
