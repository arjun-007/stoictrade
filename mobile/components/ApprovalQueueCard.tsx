import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity } from 'react-native';
import { Check, X, Bell, Clock, ShieldAlert } from 'lucide-react-native';
import * as Haptics from 'expo-haptics';
import { COLORS } from '../lib/theme';

export interface PendingSignal {
  id: string;
  signal: {
    strategyName: string;
    action: string;
    instrument: string;
    price: number;
    targetPrice?: number;
    stopLossPrice?: number;
    quantity: number;
    generatedAt: string;
  };
}

interface ApprovalQueueCardProps {
  item: PendingSignal;
  onApprove: (id: string) => void;
  onDeny: (id: string) => void;
}

export const ApprovalQueueCard: React.FC<ApprovalQueueCardProps> = ({
  item,
  onApprove,
  onDeny,
}) => {
  const { signal, id } = item;
  const isBuy = signal.action === 'BUY';

  const handleApprove = async () => {
    await Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    onApprove(id);
  };

  const handleDeny = async () => {
    await Haptics.notificationAsync(Haptics.NotificationFeedbackType.Warning);
    onDeny(id);
  };

  return (
    <View style={styles.card}>
      <View style={styles.headerRow}>
        <View style={styles.badgeContainer}>
          <View style={styles.pulseDot} />
          <Text style={styles.alertTitle}>Awaiting Squad Approval</Text>
        </View>
        <View style={styles.timerBadge}>
          <Clock size={12} color={COLORS.warning} />
          <Text style={styles.timerText}>10m TTL</Text>
        </View>
      </View>

      <Text style={styles.strategyName}>{signal.strategyName}</Text>

      <View style={styles.detailsRow}>
        <View style={[styles.actionBadge, isBuy ? styles.actionBuy : styles.actionSell]}>
          <Text style={[styles.actionText, isBuy ? styles.textBuy : styles.textSell]}>
            {signal.action}
          </Text>
        </View>
        <Text style={styles.instrumentText}>{signal.instrument}</Text>
      </View>

      <View style={styles.metricsGrid}>
        <View style={styles.metricItem}>
          <Text style={styles.metricLabel}>LTP / Price</Text>
          <Text style={styles.metricValue}>₹{signal.price.toFixed(2)}</Text>
        </View>
        <View style={styles.metricItem}>
          <Text style={styles.metricLabel}>Target</Text>
          <Text style={[styles.metricValue, { color: COLORS.profit }]}>
            ₹{(signal.targetPrice || signal.price * 1.25).toFixed(2)}
          </Text>
        </View>
        <View style={styles.metricItem}>
          <Text style={styles.metricLabel}>Exit / SL</Text>
          <Text style={[styles.metricValue, { color: COLORS.loss }]}>
            ₹{(signal.stopLossPrice || Math.max(5, signal.price * 0.85)).toFixed(2)}
          </Text>
        </View>
        <View style={styles.metricItem}>
          <Text style={styles.metricLabel}>Quantity</Text>
          <Text style={styles.metricValue}>{signal.quantity} qty</Text>
        </View>
      </View>

      <View style={styles.buttonRow}>
        <TouchableOpacity
          style={styles.denyButton}
          onPress={handleDeny}
          activeOpacity={0.8}
        >
          <X size={18} color={COLORS.loss} />
          <Text style={styles.denyText}>Deny</Text>
        </TouchableOpacity>

        <TouchableOpacity
          style={styles.approveButton}
          onPress={handleApprove}
          activeOpacity={0.8}
        >
          <Check size={18} color="#ffffff" />
          <Text style={styles.approveText}>Approve Trade</Text>
        </TouchableOpacity>
      </View>
    </View>
  );
};

const styles = StyleSheet.create({
  card: {
    backgroundColor: COLORS.surface,
    borderRadius: 16,
    padding: 16,
    marginVertical: 8,
    marginHorizontal: 16,
    borderWidth: 1.5,
    borderColor: COLORS.primary,
    shadowColor: COLORS.primary,
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 0.25,
    shadowRadius: 8,
    elevation: 5,
  },
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: 8,
  },
  badgeContainer: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
  },
  pulseDot: {
    width: 8,
    height: 8,
    borderRadius: 4,
    backgroundColor: COLORS.warning,
  },
  alertTitle: {
    fontSize: 13,
    fontWeight: '800',
    color: COLORS.warning,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
  },
  timerBadge: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    backgroundColor: COLORS.warningLight,
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 6,
  },
  timerText: {
    fontSize: 11,
    fontWeight: '700',
    color: COLORS.warning,
  },
  strategyName: {
    fontSize: 16,
    fontWeight: '800',
    color: COLORS.text,
    marginBottom: 8,
  },
  detailsRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    marginBottom: 14,
  },
  actionBadge: {
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 6,
  },
  actionBuy: {
    backgroundColor: COLORS.profitLight,
  },
  actionSell: {
    backgroundColor: COLORS.lossLight,
  },
  actionText: {
    fontSize: 12,
    fontWeight: '800',
  },
  textBuy: {
    color: COLORS.profit,
  },
  textSell: {
    color: COLORS.loss,
  },
  instrumentText: {
    fontSize: 14,
    fontWeight: '700',
    color: COLORS.text,
  },
  metricsGrid: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    backgroundColor: COLORS.bg,
    padding: 12,
    borderRadius: 12,
    marginBottom: 14,
  },
  metricItem: {
    alignItems: 'center',
  },
  metricLabel: {
    fontSize: 10,
    fontWeight: '600',
    color: COLORS.textMuted,
    marginBottom: 2,
  },
  metricValue: {
    fontSize: 13,
    fontWeight: '800',
    color: COLORS.text,
  },
  buttonRow: {
    flexDirection: 'row',
    gap: 10,
  },
  denyButton: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 6,
    backgroundColor: COLORS.lossLight,
    paddingVertical: 12,
    borderRadius: 10,
    borderWidth: 1,
    borderColor: COLORS.loss,
  },
  denyText: {
    fontSize: 14,
    fontWeight: '800',
    color: COLORS.loss,
  },
  approveButton: {
    flex: 2,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 6,
    backgroundColor: COLORS.profit,
    paddingVertical: 12,
    borderRadius: 10,
  },
  approveText: {
    fontSize: 14,
    fontWeight: '800',
    color: '#ffffff',
  },
});
