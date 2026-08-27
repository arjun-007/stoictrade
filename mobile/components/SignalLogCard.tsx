import React from 'react';
import { View, Text, StyleSheet } from 'react-native';
import { TrendingUp, TrendingDown, Clock, LogOut } from 'lucide-react-native';
import { COLORS } from '../lib/theme';

export interface SignalLogData {
  id: string;
  strategyName: string;
  action: string;
  instrument: string;
  price: number;
  targetPrice?: number;
  stopLossPrice?: number;
  quantity: number;
  status: string;
  generatedAt: string;
  expiresAt?: string;
}

interface SignalLogCardProps {
  signal: SignalLogData;
}

export const SignalLogCard: React.FC<SignalLogCardProps> = ({ signal }) => {
  const isBuy = signal.action === 'BUY';
  const isExit = signal.action === 'EXIT';
  
  const isExpired = signal.expiresAt
    ? new Date(signal.expiresAt).getTime() < Date.now()
    : Date.now() - new Date(signal.generatedAt).getTime() > 15 * 60 * 1000;

  const timeStr = new Date(signal.generatedAt).toLocaleTimeString([], {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });

  const getStatusBadge = () => {
    switch (signal.status) {
      case 'AutoExecuted':
        return { label: 'Auto-Executed', bg: COLORS.profitLight, text: COLORS.profit };
      case 'AwaitingApproval':
        return { label: 'Awaiting Approval', bg: 'rgba(59, 130, 246, 0.15)', text: '#3b82f6' };
      case 'ExitSignal':
        return { label: 'Position Exit', bg: COLORS.warningLight, text: COLORS.warning };
      default:
        return { label: 'Signal Only', bg: COLORS.purpleLight, text: COLORS.purple };
    }
  };

  const status = getStatusBadge();

  return (
    <View style={[styles.card, isExpired && styles.cardExpired]}>
      {/* Top Header */}
      <View style={styles.topRow}>
        <View style={styles.timeArea}>
          <Clock size={12} color={COLORS.textSubtle} />
          <Text style={styles.timeText}>{timeStr}</Text>
        </View>

        <View style={styles.badgeGroup}>
          <View style={[styles.statusPill, { backgroundColor: status.bg }]}>
            <Text style={[styles.statusText, { color: status.text }]}>{status.label}</Text>
          </View>
          
          {!isExit && (
            <View style={[styles.validityPill, isExpired ? styles.pillExpired : styles.pillActive]}>
              <Text style={[styles.validityText, isExpired ? styles.textExpired : styles.textActive]}>
                {isExpired ? 'Expired' : 'Active'}
              </Text>
            </View>
          )}
        </View>
      </View>

      {/* Main Signal Info */}
      <Text style={styles.strategyName}>{signal.strategyName}</Text>

      <View style={styles.instrumentRow}>
        <View style={[styles.actionTag, isBuy ? styles.tagBuy : isExit ? styles.tagExit : styles.tagSell]}>
          {isBuy ? (
            <TrendingUp size={14} color={COLORS.profit} />
          ) : isExit ? (
            <LogOut size={14} color={COLORS.warning} />
          ) : (
            <TrendingDown size={14} color={COLORS.loss} />
          )}
          <Text style={[styles.actionTagText, isBuy ? styles.textBuy : isExit ? styles.textExit : styles.textSell]}>
            {signal.action}
          </Text>
        </View>

        <Text style={styles.instrumentText}>{signal.instrument}</Text>
      </View>

      {/* Price & Risk Parameters */}
      <View style={styles.metricsBox}>
        <View style={styles.metric}>
          <Text style={styles.metricLabel}>Price</Text>
          <Text style={styles.metricValue}>₹{signal.price.toFixed(2)}</Text>
        </View>

        {signal.targetPrice && signal.targetPrice > 0 ? (
          <View style={styles.metric}>
            <Text style={styles.metricLabel}>Target</Text>
            <Text style={[styles.metricValue, { color: COLORS.profit }]}>
              ₹{signal.targetPrice.toFixed(2)}
            </Text>
          </View>
        ) : null}

        {signal.stopLossPrice && signal.stopLossPrice > 0 ? (
          <View style={styles.metric}>
            <Text style={styles.metricLabel}>Exit / SL</Text>
            <Text style={[styles.metricValue, { color: COLORS.loss }]}>
              ₹{signal.stopLossPrice.toFixed(2)}
            </Text>
          </View>
        ) : null}

        <View style={styles.metric}>
          <Text style={styles.metricLabel}>Qty</Text>
          <Text style={styles.metricValue}>{signal.quantity}</Text>
        </View>
      </View>
    </View>
  );
};

const styles = StyleSheet.create({
  card: {
    backgroundColor: COLORS.surface,
    borderRadius: 14,
    padding: 14,
    marginVertical: 5,
    marginHorizontal: 16,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  cardExpired: {
    opacity: 0.65,
  },
  topRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: 8,
  },
  timeArea: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
  },
  timeText: {
    fontSize: 11,
    fontWeight: '700',
    color: COLORS.textMuted,
  },
  badgeGroup: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
  },
  statusPill: {
    paddingHorizontal: 8,
    paddingVertical: 2,
    borderRadius: 6,
  },
  statusText: {
    fontSize: 10,
    fontWeight: '700',
  },
  validityPill: {
    paddingHorizontal: 6,
    paddingVertical: 2,
    borderRadius: 4,
  },
  pillActive: {
    backgroundColor: COLORS.profitLight,
  },
  pillExpired: {
    backgroundColor: 'rgba(100, 116, 139, 0.15)',
  },
  validityText: {
    fontSize: 10,
    fontWeight: '700',
  },
  textActive: {
    color: COLORS.profit,
  },
  textExpired: {
    color: COLORS.textSubtle,
  },
  strategyName: {
    fontSize: 14,
    fontWeight: '700',
    color: COLORS.text,
    marginBottom: 8,
  },
  instrumentRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    marginBottom: 10,
  },
  actionTag: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 6,
  },
  tagBuy: {
    backgroundColor: COLORS.profitLight,
  },
  tagSell: {
    backgroundColor: COLORS.lossLight,
  },
  tagExit: {
    backgroundColor: COLORS.warningLight,
  },
  actionTagText: {
    fontSize: 11,
    fontWeight: '800',
  },
  textBuy: {
    color: COLORS.profit,
  },
  textSell: {
    color: COLORS.loss,
  },
  textExit: {
    color: COLORS.warning,
  },
  instrumentText: {
    fontSize: 13,
    fontWeight: '700',
    color: COLORS.text,
  },
  metricsBox: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    backgroundColor: COLORS.bg,
    padding: 10,
    borderRadius: 10,
  },
  metric: {
    alignItems: 'center',
  },
  metricLabel: {
    fontSize: 10,
    color: COLORS.textMuted,
    marginBottom: 2,
  },
  metricValue: {
    fontSize: 12,
    fontWeight: '800',
    color: COLORS.text,
  },
});
