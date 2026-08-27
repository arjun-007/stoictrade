import React from 'react';
import { View, Text, StyleSheet, Switch } from 'react-native';
import { Activity, Zap, Shield, Radar } from 'lucide-react-native';
import { COLORS } from '../lib/theme';

export interface StrategyItemData {
  id: number;
  strategyName: string;
  isEnabled: boolean;
  operatingMode: string;
  perTradeStopLossPoint: number;
  perTradeGainPoint: number;
  timeframeMinutes: number;
  trailingStopLossPoint?: number;
}

interface StrategyCardProps {
  strategy: StrategyItemData;
  onToggle: (id: number, currentValue: boolean) => void;
}

export const StrategyCard: React.FC<StrategyCardProps> = ({ strategy, onToggle }) => {
  const getModeInfo = () => {
    switch (strategy.operatingMode) {
      case 'Automatic':
        return { label: 'Auto-Execute', icon: Zap, bg: COLORS.profitLight, color: COLORS.profit };
      case 'ApprovalRequired':
        return { label: 'Approval Required', icon: Shield, bg: COLORS.warningLight, color: COLORS.warning };
      default:
        return { label: 'Signal Only', icon: Radar, bg: COLORS.purpleLight, color: COLORS.purple };
    }
  };

  const mode = getModeInfo();
  const ModeIcon = mode.icon;

  return (
    <View style={[styles.card, strategy.isEnabled && styles.cardActive]}>
      <View style={styles.topRow}>
        <View style={styles.nameArea}>
          <Text style={styles.name}>{strategy.strategyName}</Text>
          <View style={[styles.modeBadge, { backgroundColor: mode.bg }]}>
            <ModeIcon size={12} color={mode.color} />
            <Text style={[styles.modeText, { color: mode.color }]}>{mode.label}</Text>
          </View>
        </View>

        <Switch
          value={strategy.isEnabled}
          onValueChange={() => onToggle(strategy.id, strategy.isEnabled)}
          trackColor={{ false: '#334155', true: COLORS.primary }}
          thumbColor={strategy.isEnabled ? '#ffffff' : '#94a3b8'}
        />
      </View>

      <View style={styles.metricsRow}>
        <Text style={styles.paramText}>🎯 Target: <Text style={{ color: COLORS.profit, fontWeight: '700' }}>+{strategy.perTradeGainPoint} pts</Text></Text>
        <Text style={styles.paramText}>🛑 Stop-Loss: <Text style={{ color: COLORS.loss, fontWeight: '700' }}>-{strategy.perTradeStopLossPoint} pts</Text></Text>
        <Text style={styles.paramText}>⏱ {strategy.timeframeMinutes}m</Text>
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
  cardActive: {
    borderColor: 'rgba(99, 102, 241, 0.35)',
  },
  topRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: 10,
  },
  nameArea: {
    flex: 1,
    marginRight: 10,
  },
  name: {
    fontSize: 15,
    fontWeight: '800',
    color: COLORS.text,
    marginBottom: 4,
  },
  modeBadge: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    alignSelf: 'flex-start',
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 6,
  },
  modeText: {
    fontSize: 11,
    fontWeight: '700',
  },
  metricsRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    borderTopWidth: 1,
    borderTopColor: COLORS.surfaceBorder,
    paddingTop: 10,
  },
  paramText: {
    fontSize: 11,
    color: COLORS.textMuted,
  },
});
