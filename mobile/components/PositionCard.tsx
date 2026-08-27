import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity } from 'react-native';
import { Briefcase, TrendingUp, TrendingDown, XCircle } from 'lucide-react-native';
import * as Haptics from 'expo-haptics';
import { COLORS } from '../lib/theme';

export interface PositionData {
  symbol: string;
  netQty: number;
  buyAvg: number;
  sellAvg: number;
  ltp: number;
  unrealizedPnL: number;
  realizedProfit: number;
  targetPrice?: number;
  stopLossPrice?: number;
  strategyName?: string;
}

interface PositionCardProps {
  position: PositionData;
  onClose: (symbol: string, qty: number) => void;
}

export const PositionCard: React.FC<PositionCardProps> = ({ position, onClose }) => {
  const isLong = position.netQty > 0;
  const isProfit = position.unrealizedPnL >= 0;
  const currentLtp = position.ltp > 0 ? position.ltp : (position.buyAvg || 150);

  const handleClose = () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
    onClose(position.symbol, Math.abs(position.netQty));
  };

  return (
    <View style={styles.card}>
      {/* Top Header */}
      <View style={styles.topRow}>
        <View style={styles.symbolArea}>
          <Text style={styles.symbol}>{position.symbol}</Text>
          {position.strategyName ? (
            <Text style={styles.strategyTag} numberOfLines={1}>{position.strategyName}</Text>
          ) : null}
        </View>

        <View style={[styles.qtyBadge, isLong ? styles.badgeLong : styles.badgeShort]}>
          <Text style={[styles.qtyText, isLong ? styles.textLong : styles.textShort]}>
            {isLong ? 'LONG' : 'SHORT'} {position.netQty}
          </Text>
        </View>
      </View>

      {/* Main P&L Callout */}
      <View style={styles.pnlRow}>
        <View>
          <Text style={styles.pnlLabel}>Unrealized P&L</Text>
          <Text style={[styles.pnlValue, isProfit ? styles.textProfit : styles.textLoss]}>
            {isProfit ? '+' : ''}₹{position.unrealizedPnL.toFixed(2)}
          </Text>
        </View>

        <TouchableOpacity style={styles.exitBtn} onPress={handleClose} activeOpacity={0.8}>
          <XCircle size={15} color="#ffffff" />
          <Text style={styles.exitBtnText}>Exit</Text>
        </TouchableOpacity>
      </View>

      {/* Stats Breakdown */}
      <View style={styles.statsGrid}>
        <View style={styles.statItem}>
          <Text style={styles.statLabel}>Avg Buy</Text>
          <Text style={styles.statValue}>₹{position.buyAvg.toFixed(2)}</Text>
        </View>
        <View style={styles.statItem}>
          <Text style={styles.statLabel}>Current LTP</Text>
          <Text style={styles.statValue}>₹{currentLtp.toFixed(2)}</Text>
        </View>
        <View style={styles.statItem}>
          <Text style={styles.statLabel}>Target</Text>
          <Text style={[styles.statValue, { color: COLORS.profit }]}>
            {position.targetPrice ? `₹${position.targetPrice.toFixed(2)}` : '—'}
          </Text>
        </View>
        <View style={styles.statItem}>
          <Text style={styles.statLabel}>Exit / SL</Text>
          <Text style={[styles.statValue, { color: COLORS.loss }]}>
            {position.stopLossPrice ? `₹${position.stopLossPrice.toFixed(2)}` : '—'}
          </Text>
        </View>
      </View>
    </View>
  );
};

const styles = StyleSheet.create({
  card: {
    backgroundColor: COLORS.surface,
    borderRadius: 16,
    padding: 16,
    marginVertical: 6,
    marginHorizontal: 16,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  topRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: 12,
  },
  symbolArea: {
    flex: 1,
  },
  symbol: {
    fontSize: 16,
    fontWeight: '800',
    color: COLORS.text,
  },
  strategyTag: {
    fontSize: 11,
    color: COLORS.textMuted,
    marginTop: 1,
  },
  qtyBadge: {
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 6,
  },
  badgeLong: {
    backgroundColor: COLORS.profitLight,
  },
  badgeShort: {
    backgroundColor: COLORS.lossLight,
  },
  qtyText: {
    fontSize: 11,
    fontWeight: '800',
  },
  textLong: {
    color: COLORS.profit,
  },
  textShort: {
    color: COLORS.loss,
  },
  pnlRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    backgroundColor: COLORS.bg,
    padding: 12,
    borderRadius: 12,
    marginBottom: 12,
  },
  pnlLabel: {
    fontSize: 11,
    color: COLORS.textMuted,
    marginBottom: 2,
  },
  pnlValue: {
    fontSize: 20,
    fontWeight: '900',
    letterSpacing: -0.5,
  },
  textProfit: {
    color: COLORS.profit,
  },
  textLoss: {
    color: COLORS.loss,
  },
  exitBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    backgroundColor: COLORS.loss,
    paddingHorizontal: 12,
    paddingVertical: 8,
    borderRadius: 8,
  },
  exitBtnText: {
    fontSize: 12,
    fontWeight: '800',
    color: '#ffffff',
  },
  statsGrid: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    paddingTop: 4,
  },
  statItem: {
    alignItems: 'center',
  },
  statLabel: {
    fontSize: 10,
    color: COLORS.textMuted,
    marginBottom: 2,
  },
  statValue: {
    fontSize: 12,
    fontWeight: '700',
    color: COLORS.text,
  },
});
