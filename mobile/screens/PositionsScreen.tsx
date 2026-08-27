import React, { useState, useEffect, useCallback } from 'react';
import { View, Text, StyleSheet, ScrollView, TouchableOpacity, RefreshControl, Alert } from 'react-native';
import { Briefcase, RotateCcw, TrendingUp, AlertCircle } from 'lucide-react-native';
import * as Haptics from 'expo-haptics';
import { COLORS } from '../lib/theme';
import { apiClient } from '../lib/api';
import { PositionCard, PositionData } from '../components/PositionCard';

export const PositionsScreen: React.FC = () => {
  const [positions, setPositions] = useState<PositionData[]>([]);
  const [tradeMode, setTradeMode] = useState<'Paper' | 'Live'>('Paper');
  const [refreshing, setRefreshing] = useState(false);

  const fetchPositions = useCallback(async () => {
    try {
      const [posRes, settingsRes] = await Promise.allSettled([
        apiClient.get('/api/portfolio/positions'),
        apiClient.get('/api/globalsettings'),
      ]);

      if (posRes.status === 'fulfilled' && Array.isArray(posRes.value.data)) {
        setPositions(posRes.value.data);
      }
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

  const handleResetPaper = () => {
    Alert.alert(
      'Reset Paper Portfolio',
      'This will reset your paper trading balance back to ₹1,00,000 and clear simulated positions.',
      [
        { text: 'Cancel', style: 'cancel' },
        {
          text: 'Reset Portfolio',
          style: 'destructive',
          onPress: async () => {
            try {
              await apiClient.post('/api/portfolio/reset-paper');
              Alert.alert('Success', 'Paper trading balance reset to ₹1,00,000.');
              fetchPositions();
            } catch {
              Alert.alert('Error', 'Failed to reset paper balance.');
            }
          },
        },
      ]
    );
  };

  const totalUnrealized = positions.reduce((acc, p) => acc + (p.unrealizedPnL || 0), 0);
  const totalRealized = positions.reduce((acc, p) => acc + (p.realizedProfit || 0), 0);
  const isProfit = totalUnrealized >= 0;

  return (
    <ScrollView
      style={styles.container}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={COLORS.primary} />}
      contentContainerStyle={{ paddingBottom: 40 }}
    >
      {/* Mode & PnL Summary Header */}
      <View style={styles.summaryCard}>
        <View style={styles.summaryTop}>
          <View style={[styles.modePill, tradeMode === 'Live' ? styles.liveMode : styles.paperMode]}>
            <Text style={styles.modePillText}>{tradeMode.toUpperCase()} TRADING</Text>
          </View>

          {tradeMode === 'Paper' && (
            <TouchableOpacity style={styles.resetBtn} onPress={handleResetPaper}>
              <RotateCcw size={13} color={COLORS.textMuted} />
              <Text style={styles.resetBtnText}>Reset Paper</Text>
            </TouchableOpacity>
          )}
        </View>

        <Text style={styles.pnlTitle}>Total Open P&L</Text>
        <Text style={[styles.pnlAmount, isProfit ? styles.textProfit : styles.textLoss]}>
          {isProfit ? '+' : ''}₹{totalUnrealized.toFixed(2)}
        </Text>

        <View style={styles.realizedRow}>
          <Text style={styles.realizedLabel}>Realized P&L Today:</Text>
          <Text style={[styles.realizedValue, totalRealized >= 0 ? styles.textProfit : styles.textLoss]}>
            {totalRealized >= 0 ? '+' : ''}₹{totalRealized.toFixed(2)}
          </Text>
        </View>
      </View>

      {/* Positions List */}
      <View style={styles.sectionHeader}>
        <Text style={styles.sectionTitle}>Open Positions ({positions.length})</Text>
      </View>

      {positions.length === 0 ? (
        <View style={styles.emptyCard}>
          <Briefcase size={32} color={COLORS.textSubtle} />
          <Text style={styles.emptyTitle}>No active positions</Text>
          <Text style={styles.emptyDesc}>New trades taken by strategy squads or manual orders will appear here</Text>
        </View>
      ) : (
        positions.map((pos, idx) => (
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
  realizedLabel: {
    fontSize: 12,
    color: COLORS.textMuted,
  },
  realizedValue: {
    fontSize: 13,
    fontWeight: '800',
  },
  textProfit: {
    color: COLORS.profit,
  },
  textLoss: {
    color: COLORS.loss,
  },
  sectionHeader: {
    paddingHorizontal: 16,
    paddingVertical: 8,
  },
  sectionTitle: {
    fontSize: 14,
    fontWeight: '800',
    color: COLORS.textMuted,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
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
