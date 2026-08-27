import React from 'react';
import { View, Text, StyleSheet, TouchableOpacity, Switch } from 'react-native';
import { Layers, CheckCircle2, Shield, Activity } from 'lucide-react-native';
import { COLORS } from '../lib/theme';

export interface StrategyGroupData {
  id: number;
  name: string;
  description: string;
  isEnabled: boolean;
  strategyIdsJson: string;
  consensusRule: string;
  minAgreeingStrategies: number;
  operatingMode: string;
  perTradeStopLossPoint: number;
  perTradeGainPoint: number;
  timeframeMinutes: number;
}

interface SquadCardProps {
  squad: StrategyGroupData;
  allStrategies: { id: number; strategyName: string }[];
  onToggle: (id: number, currentValue: boolean) => void;
}

export const SquadCard: React.FC<SquadCardProps> = ({
  squad,
  allStrategies,
  onToggle,
}) => {
  let memberIds: number[] = [];
  try {
    memberIds = JSON.parse(squad.strategyIdsJson) || [];
  } catch {}

  const memberNames = memberIds.map(
    (id) => allStrategies.find((s) => s.id === id)?.strategyName || `Strategy #${id}`
  );

  const getModeBadge = () => {
    switch (squad.operatingMode) {
      case 'Automatic':
        return { label: 'Auto-Execute', bg: COLORS.profitLight, text: COLORS.profit };
      case 'ApprovalRequired':
        return { label: 'Approval Required', bg: COLORS.warningLight, text: COLORS.warning };
      default:
        return { label: 'Signal Only', bg: COLORS.purpleLight, text: COLORS.purple };
    }
  };

  const mode = getModeBadge();

  return (
    <View style={[styles.card, squad.isEnabled && styles.cardActive]}>
      {/* Top Header */}
      <View style={styles.topRow}>
        <View style={styles.titleArea}>
          <View style={styles.iconTag}>
            <Layers size={14} color={COLORS.primary} />
            <Text style={styles.tagText}>SQUAD</Text>
          </View>
          <Text style={styles.squadName}>{squad.name}</Text>
        </View>

        <Switch
          value={squad.isEnabled}
          onValueChange={() => onToggle(squad.id, squad.isEnabled)}
          trackColor={{ false: '#334155', true: COLORS.primary }}
          thumbColor={squad.isEnabled ? '#ffffff' : '#94a3b8'}
        />
      </View>

      <Text style={styles.description} numberOfLines={2}>{squad.description}</Text>

      {/* Consensus & Mode Row */}
      <View style={styles.metaRow}>
        <View style={[styles.modeBadge, { backgroundColor: mode.bg }]}>
          <Text style={[styles.modeText, { color: mode.text }]}>{mode.label}</Text>
        </View>

        <View style={styles.consensusPill}>
          <Text style={styles.consensusText}>
            Consensus: <Text style={{ fontWeight: '800', color: COLORS.text }}>{squad.consensusRule} ({squad.minAgreeingStrategies}/{memberIds.length})</Text>
          </Text>
        </View>
      </View>

      {/* Member Strategy Badges */}
      <View style={styles.membersContainer}>
        {memberNames.map((name, idx) => (
          <View key={idx} style={styles.memberPill}>
            <CheckCircle2 size={11} color={squad.isEnabled ? COLORS.profit : COLORS.textMuted} />
            <Text style={styles.memberText} numberOfLines={1}>{name}</Text>
          </View>
        ))}
      </View>

      {/* Metrics Row */}
      <View style={styles.metricsRow}>
        <Text style={styles.paramText}>🎯 Target: <Text style={{ color: COLORS.profit, fontWeight: '700' }}>+{squad.perTradeGainPoint} pts</Text></Text>
        <Text style={styles.paramText}>🛑 Stop-Loss: <Text style={{ color: COLORS.loss, fontWeight: '700' }}>-{squad.perTradeStopLossPoint} pts</Text></Text>
        <Text style={styles.paramText}>⏱ {squad.timeframeMinutes}m</Text>
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
  cardActive: {
    borderColor: 'rgba(99, 102, 241, 0.4)',
  },
  topRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: 6,
  },
  titleArea: {
    flex: 1,
    marginRight: 10,
  },
  iconTag: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    marginBottom: 2,
  },
  tagText: {
    fontSize: 10,
    fontWeight: '800',
    color: COLORS.primary,
    letterSpacing: 0.5,
  },
  squadName: {
    fontSize: 16,
    fontWeight: '800',
    color: COLORS.text,
  },
  description: {
    fontSize: 12,
    color: COLORS.textMuted,
    lineHeight: 16,
    marginBottom: 12,
  },
  metaRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    marginBottom: 12,
  },
  modeBadge: {
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 6,
  },
  modeText: {
    fontSize: 11,
    fontWeight: '700',
  },
  consensusPill: {
    backgroundColor: COLORS.bg,
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  consensusText: {
    fontSize: 11,
    color: COLORS.textMuted,
  },
  membersContainer: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 6,
    marginBottom: 12,
  },
  memberPill: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    backgroundColor: COLORS.bg,
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 8,
    borderWidth: 1,
    borderColor: COLORS.surfaceBorder,
  },
  memberText: {
    fontSize: 11,
    fontWeight: '600',
    color: COLORS.text,
    maxWidth: 140,
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
