import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity } from 'react-native';
import { Briefcase, TrendingUp, TrendingDown, XCircle, CheckCircle2 } from 'lucide-react-native';
import * as Haptics from 'expo-haptics';
import { COLORS } from '../lib/theme';
import { formatInstrumentName } from '../lib/formatters';

export interface PositionData {
  symbol: string;
  netQty: number;
  tradeQty?: number;
  buyAvg: number;
  sellAvg: number;
  ltp: number;
  unrealizedPnL: number;
  realizedProfit: number;
  targetPrice?: number;
  stopLossPrice?: number;
  trailingStopLossPoint?: number;
  peakLtp?: number;
  strategyName?: string;
  status?: 'ACTIVE' | 'EXITED';
  category?: 'DAY' | 'HOLDING';
}

interface PositionCardProps {
  position: PositionData;
  onClose: (symbol: string, qty: number) => void;
}

export const PositionCard: React.FC<PositionCardProps> = ({ position, onClose }) => {
  const isExited = position.netQty === 0 || position.status === 'EXITED';
  const isLong = position.netQty > 0;
  const effectiveRealized = (position.realizedProfit !== undefined && position.realizedProfit !== 0)
    ? position.realizedProfit
    : (position.sellAvg && position.buyAvg ? (position.sellAvg - position.buyAvg) * (position.tradeQty || 65) : 0);
  const isProfit = isExited ? (effectiveRealized >= 0) : (position.unrealizedPnL >= 0);
  const displayPnL = isExited ? effectiveRealized : position.unrealizedPnL;
  const currentLtp = position.ltp > 0 ? position.ltp : (position.buyAvg || 150);
  const formattedName = formatInstrumentName(position.symbol);

  const handleClose = () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
    onClose(position.symbol, Math.abs(position.netQty));
  };

  return (
    <View style={[styles.card, isExited && styles.cardExited]}>
      {/* Top Header */}
      <View style={styles.topRow}>
        <View style={styles.symbolArea}>
          <Text style={styles.symbol}>{formattedName}</Text>
          {formattedName !== position.symbol ? (
            <Text style={styles.rawSymbolTag}>{position.symbol}</Text>
          ) : null}
          {position.strategyName ? (
            <Text style={styles.strategyTag} numberOfLines={1}>{position.strategyName}</Text>
          ) : null}
        </View>

        <View style={[styles.qtyBadge, isExited ? styles.badgeExited : isLong ? styles.badgeLong : styles.badgeShort]}>
          <Text style={[styles.qtyText, isExited ? styles.textExited : isLong ? styles.textLong : styles.textShort]}>
            {isExited ? (position.tradeQty ? `EXITED (${position.tradeQty})` : 'EXITED') : `${isLong ? 'LONG' : 'SHORT'} ${position.netQty}`}
          </Text>
        </View>
      </View>

      {/* Main P&L Callout */}
      <View style={styles.pnlRow}>
        <View>
          <Text style={styles.pnlLabel}>{isExited ? 'Realized P&L' : 'Unrealized P&L'}</Text>
          <Text style={[styles.pnlValue, isProfit ? styles.textProfit : styles.textLoss]}>
            {isProfit ? '+' : ''}₹{displayPnL.toFixed(2)}
          </Text>
        </View>

        {!isExited ? (
          <TouchableOpacity style={styles.exitBtn} onPress={handleClose} activeOpacity={0.8}>
            <XCircle size={15} color="#ffffff" />
            <Text style={styles.exitBtnText}>Exit</Text>
          </TouchableOpacity>
        ) : (
          <View style={styles.closedBadge}>
            <CheckCircle2 size={14} color={COLORS.textMuted} />
            <Text style={styles.closedText}>Closed</Text>
          </View>
        )}
      </View>

      {/* Stats Breakdown */}
      <View style={styles.statsGrid}>
        <View style={styles.statItem}>
          <Text style={styles.statLabel}>Avg Buy</Text>
          <Text style={styles.statValue}>₹{position.buyAvg.toFixed(2)}</Text>
        </View>
        <View style={styles.statItem}>
          <Text style={styles.statLabel}>{isExited ? 'Exit Avg' : 'Current LTP'}</Text>
          <Text style={styles.statValue}>₹{(isExited && position.sellAvg > 0 ? position.sellAvg : currentLtp).toFixed(2)}</Text>
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
      
      {!isExited && position.trailingStopLossPoint !== undefined && position.trailingStopLossPoint > 0 && (
        <View style={{ marginTop: 8, paddingTop: 8, borderTopWidth: 1, borderTopColor: COLORS.surfaceBorder, flexDirection: 'row', justifyContent: 'space-between' }}>
          <Text style={{ fontSize: 11, color: COLORS.textMuted, fontWeight: '600' }}>Trailing SL Active:</Text>
          <Text style={{ fontSize: 11, color: COLORS.text, fontWeight: '800' }}>Trail by ₹{position.trailingStopLossPoint.toFixed(1)}</Text>
        </View>
      )}
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
  cardExited: {
    opacity: 0.85,
    backgroundColor: 'rgba(30, 41, 59, 0.4)',
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
  rawSymbolTag: {
    fontSize: 11,
    color: COLORS.textSubtle,
    fontFamily: 'monospace',
    marginTop: 1,
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
  badgeExited: {
    backgroundColor: 'rgba(100, 116, 139, 0.2)',
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
  textExited: {
    color: COLORS.textMuted,
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
  closedBadge: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    backgroundColor: 'rgba(100, 116, 139, 0.15)',
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: 8,
  },
  closedText: {
    fontSize: 12,
    fontWeight: '700',
    color: COLORS.textMuted,
  },
});
