import React, { useState, useEffect } from 'react';
import { View, Text, StyleSheet, TouchableOpacity } from 'react-native';
import { ShieldAlert, CheckCircle2, XCircle, Clock, Layers } from 'lucide-react-native';
import * as Haptics from 'expo-haptics';
import { COLORS } from '../lib/theme';
import { apiClient } from '../lib/api';
import { formatInstrumentName } from '../lib/formatters';

export interface PendingSignalItem {
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

export const GlobalPendingApprovalsBanner: React.FC = () => {
  const [approvals, setApprovals] = useState<PendingSignalItem[]>([]);
  const [loadingAction, setLoadingAction] = useState<string | null>(null);

  const fetchApprovals = async () => {
    try {
      const res = await apiClient.get('/api/approval/pending');
      if (res.data && Array.isArray(res.data)) {
        setApprovals(res.data);
      } else {
        setApprovals([]);
      }
    } catch {
      // Ignore background poll errors
    }
  };

  useEffect(() => {
    fetchApprovals();
    const interval = setInterval(fetchApprovals, 3500);
    return () => clearInterval(interval);
  }, []);

  const handleAction = async (id: string, action: 'approve' | 'deny') => {
    setLoadingAction(`${action}_${id}`);
    if (action === 'approve') {
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    } else {
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Warning);
    }

    try {
      await apiClient.post(`/api/approval/${action}/${id}`);
      setApprovals((prev) => prev.filter((a) => a.id !== id));
    } catch (err: any) {
      console.error(`Failed to ${action} signal`, err);
    } finally {
      setLoadingAction(null);
    }
  };

  if (approvals.length === 0) return null;

  return (
    <View style={styles.container}>
      {approvals.map((app) => {
        const sig = app.signal || {};
        const isGroup = sig.strategyName?.startsWith('Group:');
        const displayInstrument = formatInstrumentName(sig.instrument || 'NIFTY');
        const tgt = sig.targetPrice && sig.targetPrice > 0 ? sig.targetPrice : (sig.price > 0 ? sig.price * 1.25 : 0);
        const sl = sig.stopLossPrice && sig.stopLossPrice > 0 ? sig.stopLossPrice : (sig.price > 0 ? Math.max(5, sig.price * 0.85) : 0);

        return (
          <View key={app.id} style={styles.card}>
            {/* Top Tag & Squad row */}
            <View style={styles.topRow}>
              <View style={styles.actionRequiredBadge}>
                <Clock size={11} color="#b45309" />
                <Text style={styles.actionRequiredText}>ACTION REQUIRED</Text>
              </View>

              {isGroup ? (
                <View style={styles.squadBadge}>
                  <Layers size={11} color={COLORS.primary} />
                  <Text style={styles.squadBadgeText}>Squad Consensus</Text>
                </View>
              ) : (
                <View style={styles.standaloneBadge}>
                  <Text style={styles.standaloneBadgeText}>Standalone</Text>
                </View>
              )}

              <Text style={styles.strategyName} numberOfLines={1}>
                {sig.strategyName}
              </Text>
            </View>

            {/* Instrument & Action details */}
            <View style={styles.detailsRow}>
              <View style={[styles.actionTag, sig.action === 'BUY' ? styles.tagBuy : styles.tagSell]}>
                <Text style={[styles.actionText, sig.action === 'BUY' ? styles.textBuy : styles.textSell]}>
                  {sig.action || 'BUY'}
                </Text>
              </View>

              <Text style={styles.instrumentText} numberOfLines={1}>
                {sig.quantity || 65} Qty · {displayInstrument}
              </Text>

              {sig.price > 0 && (
                <Text style={styles.priceText}>@ ~₹{sig.price.toFixed(2)}</Text>
              )}
            </View>

            {/* Target & SL Badges */}
            <View style={styles.badgesRow}>
              {tgt > 0 && (
                <View style={styles.targetBadge}>
                  <Text style={styles.targetBadgeText}>🎯 Target: ₹{tgt.toFixed(2)}</Text>
                </View>
              )}
              {sl > 0 && (
                <View style={styles.slBadge}>
                  <Text style={styles.slBadgeText}>🛑 Exit / SL: ₹{sl.toFixed(2)}</Text>
                </View>
              )}
            </View>

            {/* Action Buttons */}
            <View style={styles.buttonsRow}>
              <TouchableOpacity
                style={styles.denyButton}
                onPress={() => handleAction(app.id, 'deny')}
                disabled={loadingAction === `deny_${app.id}`}
                activeOpacity={0.8}
              >
                <XCircle size={15} color={COLORS.loss} />
                <Text style={styles.denyButtonText}>DENY</Text>
              </TouchableOpacity>

              <TouchableOpacity
                style={styles.approveButton}
                onPress={() => handleAction(app.id, 'approve')}
                disabled={loadingAction === `approve_${app.id}`}
                activeOpacity={0.85}
              >
                <CheckCircle2 size={15} color="#ffffff" />
                <Text style={styles.approveButtonText}>APPROVE &amp; BUY</Text>
              </TouchableOpacity>
            </View>
          </View>
        );
      })}
    </View>
  );
};

const styles = StyleSheet.create({
  container: {
    backgroundColor: 'rgba(245, 158, 11, 0.12)',
    borderBottomWidth: 1,
    borderBottomColor: 'rgba(245, 158, 11, 0.3)',
    paddingHorizontal: 12,
    paddingVertical: 8,
  },
  card: {
    backgroundColor: COLORS.surface,
    borderRadius: 12,
    padding: 12,
    marginVertical: 4,
    borderWidth: 1,
    borderColor: 'rgba(245, 158, 11, 0.4)',
    shadowColor: '#f59e0b',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.15,
    shadowRadius: 4,
    elevation: 3,
  },
  topRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    marginBottom: 8,
  },
  actionRequiredBadge: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    backgroundColor: 'rgba(245, 158, 11, 0.2)',
    paddingHorizontal: 6,
    paddingVertical: 2,
    borderRadius: 4,
    borderWidth: 0.5,
    borderColor: 'rgba(245, 158, 11, 0.4)',
  },
  actionRequiredText: {
    fontSize: 9,
    fontWeight: '800',
    color: '#d97706',
    letterSpacing: 0.5,
  },
  squadBadge: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 3,
    backgroundColor: 'rgba(99, 102, 241, 0.15)',
    paddingHorizontal: 6,
    paddingVertical: 2,
    borderRadius: 4,
  },
  squadBadgeText: {
    fontSize: 9,
    fontWeight: '700',
    color: COLORS.primary,
  },
  standaloneBadge: {
    backgroundColor: COLORS.bg,
    paddingHorizontal: 6,
    paddingVertical: 2,
    borderRadius: 4,
  },
  standaloneBadgeText: {
    fontSize: 9,
    fontWeight: '600',
    color: COLORS.textMuted,
  },
  strategyName: {
    flex: 1,
    fontSize: 10,
    fontWeight: '600',
    color: COLORS.textMuted,
  },
  detailsRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    marginBottom: 6,
  },
  actionTag: {
    paddingHorizontal: 8,
    paddingVertical: 2,
    borderRadius: 4,
  },
  tagBuy: {
    backgroundColor: COLORS.profitLight,
  },
  tagSell: {
    backgroundColor: COLORS.lossLight,
  },
  actionText: {
    fontSize: 11,
    fontWeight: '900',
  },
  textBuy: {
    color: COLORS.profit,
  },
  textSell: {
    color: COLORS.loss,
  },
  instrumentText: {
    flex: 1,
    fontSize: 13,
    fontWeight: '800',
    color: COLORS.text,
  },
  priceText: {
    fontSize: 11,
    fontWeight: '700',
    color: COLORS.textMuted,
  },
  badgesRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 6,
    marginBottom: 10,
  },
  targetBadge: {
    backgroundColor: 'rgba(16, 185, 129, 0.12)',
    paddingHorizontal: 8,
    paddingVertical: 2,
    borderRadius: 4,
    borderWidth: 0.5,
    borderColor: 'rgba(16, 185, 129, 0.3)',
  },
  targetBadgeText: {
    fontSize: 10,
    fontWeight: '700',
    color: COLORS.profit,
  },
  slBadge: {
    backgroundColor: 'rgba(239, 68, 68, 0.12)',
    paddingHorizontal: 8,
    paddingVertical: 2,
    borderRadius: 4,
    borderWidth: 0.5,
    borderColor: 'rgba(239, 68, 68, 0.3)',
  },
  slBadgeText: {
    fontSize: 10,
    fontWeight: '700',
    color: COLORS.loss,
  },
  buttonsRow: {
    flexDirection: 'row',
    gap: 8,
  },
  denyButton: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 4,
    backgroundColor: COLORS.lossLight,
    paddingVertical: 8,
    borderRadius: 8,
    borderWidth: 1,
    borderColor: 'rgba(239, 68, 68, 0.3)',
  },
  denyButtonText: {
    fontSize: 12,
    fontWeight: '800',
    color: COLORS.loss,
  },
  approveButton: {
    flex: 2,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 4,
    backgroundColor: COLORS.profit,
    paddingVertical: 8,
    borderRadius: 8,
  },
  approveButtonText: {
    fontSize: 12,
    fontWeight: '900',
    color: '#ffffff',
  },
});
